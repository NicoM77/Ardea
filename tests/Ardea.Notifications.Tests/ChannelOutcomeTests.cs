using System.Net;
using System.Security.Cryptography;
using Ardea.Iam.Contracts;
using Ardea.Notifications.Delivering;
using Ardea.Notifications.Evaluation;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Rules;
using Ardea.Platform.Email;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ardea.Notifications.Tests;

public sealed class ChannelOutcomeTests
{
    private static readonly Uri PublicUrl = new("https://ardea.test/");

    [Theory]
    [InlineData(200, "Sent")]
    [InlineData(204, "Sent")]
    [InlineData(301, "Permanent")]
    [InlineData(302, "Permanent")]
    [InlineData(307, "Permanent")]
    [InlineData(400, "Permanent")]
    [InlineData(404, "Permanent")]
    [InlineData(410, "Permanent")]
    [InlineData(413, "Permanent")]
    [InlineData(408, "Transient")]
    [InlineData(429, "Transient")]
    [InlineData(500, "Transient")]
    [InlineData(503, "Transient")]
    public async Task A_webhook_answer_is_sent_final_or_worth_another_attempt(int status, string outcome)
    {
        using var http = new StubHttp((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));

        var result = await Webhook(http).PostAsync(new Uri("https://hooks.example.org/x"), "whsec_test", "{}", Guid.CreateVersion7(), allowInternal: false, CancellationToken.None);

        Assert.Equal(Enum.Parse<SendOutcome>(outcome), result.Outcome);
        Assert.Equal(status.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Status);
        if (status is >= 300 and < 400)
        {
            Assert.Equal($"The endpoint answered {status}; redirects are not followed.", result.Error);
        }
    }

    [Fact]
    public async Task A_webhook_failure_names_its_kind_and_the_jobs_own_timeout_is_not_an_answer()
    {
        async Task<SendResult> FailingWith(Exception exception) =>
            await Webhook(new StubHttp((_, _) => throw exception)).PostAsync(new Uri("https://hooks.example.org/x"), "whsec_test", "{}", Guid.CreateVersion7(), false, CancellationToken.None);

        var blocked = await FailingWith(new HttpRequestException("Connection failed", new HttpRequestException("hooks.example.org resolves to no address Ardea may connect to.")));
        Assert.Equal((SendOutcome.Permanent, "blocked"), (blocked.Outcome, blocked.Status));
        var unreachable = await FailingWith(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (hooks.example.org:443)"));
        Assert.Equal((SendOutcome.Transient, "unreachable"), (unreachable.Outcome, unreachable.Status));
        Assert.Equal("The endpoint could not be reached: ConnectionError.", unreachable.Error);
        var slow = await FailingWith(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));
        Assert.Equal((SendOutcome.Transient, "timeout"), (slow.Outcome, slow.Status));
        var odd = await FailingWith(new InvalidOperationException("secret detail"));
        Assert.Equal((SendOutcome.Transient, "error"), (odd.Outcome, odd.Status));
        Assert.DoesNotContain("secret detail", odd.Error, StringComparison.Ordinal);

        using var job = new CancellationTokenSource();
        await job.CancelAsync();
        using var waiting = new StubHttp((_, token) => Task.FromCanceled<HttpResponseMessage>(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Webhook(waiting).PostAsync(new Uri("https://hooks.example.org/x"), "whsec_test", "{}", Guid.CreateVersion7(), false, job.Token));
    }

    [Fact]
    public async Task A_webhook_without_a_readable_address_or_secret_is_not_tried()
    {
        using var http = new StubHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await Webhook(http).SendAsync(Message(Channels.Webhook) with { WebhookUrl = new Uri("https://hooks.example.org/x") }, CancellationToken.None);

        Assert.Equal(SendOutcome.Permanent, result.Outcome);
        Assert.Equal(0, http.Calls);
    }

    [Theory]
    [InlineData("ftp://hooks.example.org/x")]
    [InlineData("https://user:password@hooks.example.org/x")]
    [InlineData("https://hooks.example.org/x#fragment")]
    [InlineData("http://0.0.0.0/x")]
    [InlineData("http://224.0.0.1/x")]
    [InlineData("http://[::ffff:127.0.0.1]/x")]
    [InlineData("http://[64:ff9b::a9fe:a9fe]/x")]
    public async Task A_webhook_address_that_can_never_be_called_is_refused_when_saved(string url) =>
        Assert.NotNull(await WebhookChannel.CheckAddressAsync(new Uri(url), ["10.0.0.1"], CancellationToken.None));

    [Fact]
    public async Task A_released_host_may_be_internal_and_a_public_address_needs_no_release()
    {
        Assert.Null(await WebhookChannel.CheckAddressAsync(new Uri("https://203.0.113.7/hook"), [], CancellationToken.None));
        Assert.Null(await WebhookChannel.CheckAddressAsync(new Uri("http://[fd00::7]:8080/hook"), ["FD00::7"], CancellationToken.None));
        Assert.NotNull(await WebhookChannel.CheckAddressAsync(new Uri("http://[fd00::7]:8080/hook"), [], CancellationToken.None));
    }

    [Fact]
    public async Task An_email_is_sent_suppressed_without_smtp_or_not_tried_without_an_address()
    {
        var sender = new StubSender();
        var channel = new EmailChannel(sender);

        Assert.Equal(SendOutcome.Sent, (await channel.SendAsync(Message(Channels.Email), CancellationToken.None)).Outcome);
        Assert.Equal("member@example.org", Assert.Single(sender.Sent).To);

        var nobody = await channel.SendAsync(Message(Channels.Email) with { Recipient = new Recipient("user:x", RecipientKinds.User, Guid.CreateVersion7(), null, "Nobody") }, CancellationToken.None);
        Assert.Equal(SendOutcome.Permanent, nobody.Outcome);

        sender.Configured = false;
        var off = await channel.SendAsync(Message(Channels.Email), CancellationToken.None);
        Assert.Equal((SendOutcome.Suppressed, "SMTP is not configured."), (off.Outcome, off.Error));
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task Only_a_final_refusal_of_the_recipient_ends_the_retries_and_no_reply_of_the_mail_server_reaches_the_log()
    {
        var sender = new StubSender();
        var channel = new EmailChannel(sender);
        async Task<SendResult> FailingWith(Exception exception)
        {
            sender.Failure = exception;
            return await channel.SendAsync(Message(Channels.Email), CancellationToken.None);
        }

        var unknown = await FailingWith(new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "5.1.1 User unknown"));
        Assert.Equal(SendOutcome.Permanent, unknown.Outcome);
        Assert.StartsWith("The mail server refused the recipient: 550", unknown.Error, StringComparison.Ordinal);

        Assert.Equal(SendOutcome.Transient, (await FailingWith(new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxBusy, "4.2.0 Try later"))).Outcome);
        Assert.Equal(SendOutcome.Transient, (await FailingWith(new SmtpCommandException(SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable, "5.7.1 Relay denied"))).Outcome);
        var down = await FailingWith(new SmtpProtocolException("relay.internal.example said: auth failed for ardea@relay"));
        Assert.Equal(SendOutcome.Transient, down.Outcome);
        Assert.StartsWith("SmtpProtocolException:", down.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("relay.internal", down.Error, StringComparison.Ordinal);

        using var job = new CancellationTokenSource();
        await job.CancelAsync();
        sender.Failure = new OperationCanceledException(job.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.SendAsync(Message(Channels.Email), job.Token));
    }

    [Theory]
    [InlineData(201, "Sent")]
    [InlineData(404, "Gone")]
    [InlineData(410, "Gone")]
    [InlineData(401, "Rejected")]
    [InlineData(403, "Rejected")]
    [InlineData(400, "Refused")]
    [InlineData(413, "Refused")]
    [InlineData(408, "Unavailable")]
    [InlineData(429, "Unavailable")]
    [InlineData(500, "Unavailable")]
    [InlineData(503, "Unavailable")]
    public async Task A_push_service_answer_decides_about_the_device(int status, string outcome)
    {
        using var http = new StubHttp((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var device = Device();

        var result = await Push(http).SendAsync(device, "{}"u8.ToArray(), "normal", null, CancellationToken.None);

        Assert.Equal(new PushDeviceResult(device.Id, Enum.Parse<PushDeviceOutcome>(outcome), status.ToString(System.Globalization.CultureInfo.InvariantCulture)), result);
    }

    [Fact]
    public async Task A_push_failure_is_an_outcome_of_its_device_never_an_exception()
    {
        async Task<PushDeviceResult> FailingWith(Exception exception) =>
            await Push(new StubHttp((_, _) => throw exception)).SendAsync(Device(), "{}"u8.ToArray(), "normal", null, CancellationToken.None);

        Assert.Equal((PushDeviceOutcome.Gone, "blocked"), Pair(await FailingWith(new HttpRequestException("fcm.googleapis.com resolves to no address Ardea may connect to."))));
        Assert.Equal((PushDeviceOutcome.Unavailable, "unreachable"), Pair(await FailingWith(new HttpRequestException(HttpRequestError.NameResolutionError))));
        Assert.Equal((PushDeviceOutcome.Unavailable, "timeout"), Pair(await FailingWith(new TaskCanceledException())));
        Assert.Equal((PushDeviceOutcome.Unavailable, "error"), Pair(await FailingWith(new InvalidOperationException())));

        using var http = new StubHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)));
        var broken = Device() with { Auth = "short" };
        Assert.Equal((PushDeviceOutcome.Gone, "invalid"), Pair(await Push(http).SendAsync(broken, "{}"u8.ToArray(), "normal", null, CancellationToken.None)));
        Assert.Equal((PushDeviceOutcome.Gone, "invalid"), Pair(await Push(http).SendAsync(Device() with { Endpoint = "not a url" }, "{}"u8.ToArray(), "normal", null, CancellationToken.None)));
        Assert.Equal(0, http.Calls);

        static (PushDeviceOutcome, string) Pair(PushDeviceResult result) => (result.Outcome, result.Status);
    }

    [Fact]
    public async Task A_push_message_goes_to_every_device_with_high_urgency_only_at_level_5_and_without_devices_is_final()
    {
        var urgencies = new List<string>();
        using var http = new StubHttp((request, _) =>
        {
            lock (urgencies)
            {
                urgencies.Add(request.Headers.GetValues("Urgency").Single());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        });
        var push = Push(http);

        var critical = await push.SendAsync(Message(Channels.Push, level: 5) with { PushDevices = [Device(), Device()] }, CancellationToken.None);
        var high = await push.SendAsync(Message(Channels.Push, level: 4) with { PushDevices = [Device()] }, CancellationToken.None);
        var none = await push.SendAsync(Message(Channels.Push), CancellationToken.None);

        Assert.Equal((SendOutcome.Sent, 2), (critical.Outcome, critical.Devices!.Count));
        Assert.Equal(SendOutcome.Sent, high.Outcome);
        Assert.Equal(["high", "high", "normal"], urgencies);
        Assert.Equal(SendOutcome.Permanent, none.Outcome);
    }

    [Fact]
    public void A_push_message_is_sent_when_one_device_took_it_retried_while_one_may_still_and_final_otherwise()
    {
        PushDeviceResult Of(PushDeviceOutcome outcome, string status) => new(Guid.CreateVersion7(), outcome, status);

        Assert.Equal(SendOutcome.Sent, WebPushChannel.Summarize([Of(PushDeviceOutcome.Gone, "410"), Of(PushDeviceOutcome.Sent, "201")]).Outcome);
        var retry = WebPushChannel.Summarize([Of(PushDeviceOutcome.Gone, "410"), Of(PushDeviceOutcome.Unavailable, "429")]);
        Assert.Equal((SendOutcome.Transient, "No device accepted the message; the push services answered 410, 429."), (retry.Outcome, retry.Error));
        var final = WebPushChannel.Summarize([Of(PushDeviceOutcome.Refused, "413")]);
        Assert.Equal((SendOutcome.Permanent, "The push service answered 413."), (final.Outcome, final.Error));
        Assert.Equal(SendOutcome.Permanent, WebPushChannel.Summarize([Of(PushDeviceOutcome.Rejected, "403"), Of(PushDeviceOutcome.Gone, "404")]).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://user:pw@fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://fcm.googleapis.com/fcm/send/x#y")]
    [InlineData("https://push.evil.example/x")]
    [InlineData("https://fcm.googleapis.com.evil.example/x")]
    [InlineData("https://notapple.com/x")]
    public void A_subscription_is_taken_only_for_an_https_address_of_a_known_push_service(string? endpoint) =>
        Assert.NotNull(WebPushChannel.CheckEndpoint(endpoint, Configuration(new())));

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/x")]
    [InlineData("https://web.push.apple.com/x")]
    [InlineData("https://api.push.apple.com/x")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=x")]
    [InlineData("https://push.example.org/x")]
    public void The_browser_makers_push_services_and_the_operators_own_are_known(string endpoint) =>
        Assert.Null(WebPushChannel.CheckEndpoint(endpoint, Configuration(new() { ["PUSH_SERVICE_HOSTS"] = "push.example.org" })));

    [Fact]
    public void The_vapid_variables_are_set_together_as_one_pair_with_a_contact_the_push_services_accept()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = WebEncoders.Base64UrlEncode(WebPushEncryption.PublicKey(key));
        var privateKey = WebEncoders.Base64UrlEncode(key.ExportParameters(true).D!);

        Assert.Empty(PushConfiguration.Errors(Configuration(new())));
        Assert.Empty(PushConfiguration.Errors(Configuration(new() { ["VAPID:PUBLIC_KEY"] = publicKey, ["VAPID:PRIVATE_KEY"] = privateKey, ["VAPID:SUBJECT"] = "mailto:ops@example.org" })));
        Assert.Single(PushConfiguration.Errors(Configuration(new() { ["VAPID:PUBLIC_KEY"] = publicKey })));
        Assert.Single(PushConfiguration.Errors(Configuration(new() { ["VAPID:SUBJECT"] = "ops@example.org" })));
        Assert.Single(PushConfiguration.Errors(Configuration(new() { ["VAPID:PUBLIC_KEY"] = "AAAA", ["VAPID:PRIVATE_KEY"] = privateKey })));
        Assert.Single(PushConfiguration.Errors(Configuration(new() { ["VAPID:PUBLIC_KEY"] = publicKey, ["VAPID:PRIVATE_KEY"] = "!!not base64!!" })));
        var mismatch = PushConfiguration.Errors(Configuration(new() { ["VAPID:PUBLIC_KEY"] = publicKey, ["VAPID:PRIVATE_KEY"] = WebEncoders.Base64UrlEncode(other.ExportParameters(true).D!) }));
        Assert.Contains("ARDEA_VAPID__PRIVATE_KEY", Assert.Single(mismatch), StringComparison.Ordinal);
        Assert.DoesNotContain(privateKey, string.Concat(mismatch), StringComparison.Ordinal);
    }

    private static WebhookChannel Webhook(StubHttp http) => new(http, TimeProvider.System);

    private static WebPushChannel Push(StubHttp http)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var configuration = Configuration(new()
        {
            ["VAPID:PUBLIC_KEY"] = WebEncoders.Base64UrlEncode(WebPushEncryption.PublicKey(key)),
            ["VAPID:PRIVATE_KEY"] = WebEncoders.Base64UrlEncode(key.ExportParameters(true).D!),
            ["VAPID:SUBJECT"] = "mailto:ops@example.org",
        });
        return new WebPushChannel(http, new VapidKeyStore(null!, null!, configuration), new PushOptions(), TimeProvider.System, NullLogger<WebPushChannel>.Instance);
    }

    private static PushDevice Device()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return new PushDevice(
            Guid.CreateVersion7(), $"https://fcm.googleapis.com/fcm/send/{Guid.CreateVersion7()}",
            WebEncoders.Base64UrlEncode(WebPushEncryption.PublicKey(key)), WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16)));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(values) { ["PUBLIC_URL"] = PublicUrl.AbsoluteUri }).Build();

    private static Outgoing Message(string channel, int level = 4) => new()
    {
        Channel = channel,
        Recipient = new Recipient("user:member", RecipientKinds.User, Guid.CreateVersion7(), "member@example.org", "Mia Member"),
        Unsubscribe = new Uri(PublicUrl, "o/acme/settings/notifications"),
        Content = new MessageContent
        {
            DeliveryId = Guid.CreateVersion7(),
            Kind = "immediate",
            Organization = new OrganizationProfile(Guid.CreateVersion7(), "en", "none") { Name = "Acme", Slug = "acme" },
            Locale = "en",
            PublicUrl = PublicUrl,
            Items =
            [
                new MessageItem
                {
                    EventId = Guid.CreateVersion7(),
                    Event = NotifyEvents.WarningPublished,
                    At = DateTimeOffset.UnixEpoch,
                    Level = level,
                    Warning = new WarningFacts(Guid.CreateVersion7(), "NCSC-2026-0001", "Phishing wave", null),
                },
            ],
        },
    };

    private sealed class StubHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler, IHttpClientFactory
    {
        private int _calls;

        public int Calls => _calls;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return answer(request, cancellationToken);
        }
    }

    private sealed class StubSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public bool Configured { get; set; } = true;

        public Exception? Failure { get; set; }

        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) => Task.FromResult(Configured);

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (Failure is { } failure)
            {
                throw failure;
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
