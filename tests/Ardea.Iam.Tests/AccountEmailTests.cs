using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.Iam.Features;
using Ardea.Platform.Email;
using Ardea.Platform.Jobs;
using Ardea.TestSupport;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Iam.Tests;

/// <summary>Task 0.9: reset and confirmation by e-mail, and the behaviour without SMTP (master plan 12.4).</summary>
public sealed class AccountEmailTests(PostgresServer server)
{
    private const string Email = "first@example.org";

    [Fact]
    public async Task Reset_link_from_the_mail_sets_a_new_password_and_ends_every_session()
    {
        var mail = new CapturingEmailSender();
        await using var host = await ApiTestHost.StartAsync(server, services: s => s.AddScoped<IEmailSender>(_ => mail));
        using var laptop = host.Client();
        await SetupAsync(laptop);
        using var stranger = host.Client();

        Assert.Equal(HttpStatusCode.NoContent, (await stranger.PostAsync("/api/v1/auth/password/forgot", new { email = Email })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await stranger.PostAsync("/api/v1/auth/password/forgot", new { email = Email })).StatusCode);
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job WHERE kind = 'iam.send-password-reset' AND state = 'pending'"));

        await RunJobAsync(host, new SendPasswordResetEmail(await UserIdAsync(host)));
        var link = Link(Assert.Single(mail.Sent));
        Assert.Equal("/reset-password", link.AbsolutePath);
        var query = QueryHelpers.ParseQuery(link.Query);

        var reset = await stranger.PostAsync("/api/v1/auth/password/reset", new { email = query["email"].ToString(), token = query["token"].ToString(), newPassword = "a brand new passphrase" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.SessionAsync()).StatusCode);
        var login = await stranger.PostAsync("/api/v1/auth/login", new { email = Email, password = "a brand new passphrase" });
        Assert.Equal("signedIn", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var reuse = await stranger.PostAsync("/api/v1/auth/password/reset", new { email = Email, token = query["token"].ToString(), newPassword = "yet another passphrase" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task Without_smtp_forgot_password_answers_the_same_and_sends_nothing()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);

        var known = await browser.PostAsync("/api/v1/auth/password/forgot", new { email = Email });
        var unknown = await browser.PostAsync("/api/v1/auth/password/forgot", new { email = "nobody@example.org" });

        Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM platform.job"));
    }

    [Fact]
    public async Task With_smtp_registration_waits_for_the_confirmation_link()
    {
        var mail = new CapturingEmailSender();
        await using var host = await ApiTestHost.StartAsync(server, services: s => s.AddScoped<IEmailSender>(_ => mail));
        using var browser = host.Client();

        await browser.PostAsync("/api/v1/auth/register", new { email = "new@example.org", displayName = "New Person", password = ApiTestHost.Password });
        Assert.False(await host.ScalarAsync<bool>("SELECT email_confirmed FROM iam.\"user\" WHERE email = 'new@example.org'"));

        var userId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE email = 'new@example.org'");
        await RunJobAsync(host, new SendEmailConfirmation(userId));
        var query = QueryHelpers.ParseQuery(Link(Assert.Single(mail.Sent)).Query);
        var confirmed = await browser.PostAsync("/api/v1/auth/email/confirm", new { userId = Guid.Parse(query["userId"].ToString()), token = query["token"].ToString() });

        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.True(await host.ScalarAsync<bool>("SELECT email_confirmed FROM iam.\"user\" WHERE email = 'new@example.org'"));
    }

    [Fact]
    public async Task A_confirmation_link_confirms_only_its_own_account()
    {
        var mail = new CapturingEmailSender();
        await using var host = await ApiTestHost.StartAsync(server, services: s => s.AddScoped<IEmailSender>(_ => mail));
        using var browser = host.Client();
        using var other = host.Client();
        await browser.PostAsync("/api/v1/auth/register", new { email = "new@example.org", displayName = "New Person", password = ApiTestHost.Password });
        await other.PostAsync("/api/v1/auth/register", new { email = "other@example.org", displayName = "Other Person", password = ApiTestHost.Password });
        var userId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE email = 'new@example.org'");
        var otherId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\" WHERE email = 'other@example.org'");
        await RunJobAsync(host, new SendEmailConfirmation(userId));
        var token = QueryHelpers.ParseQuery(Link(Assert.Single(mail.Sent)).Query)["token"].ToString();

        foreach (var (user, value) in new[] { (otherId, token), (userId, "forged"), (Guid.CreateVersion7(), token) })
        {
            var response = await other.PostAsync("/api/v1/auth/email/confirm", new { userId = user, token = value });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_token", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.\"user\" WHERE email_confirmed"));
    }

    [Fact]
    public async Task Registering_a_taken_address_mails_its_owner_instead_of_telling_the_stranger()
    {
        var mail = new CapturingEmailSender();
        await using var host = await ApiTestHost.StartAsync(server, services: s => s.AddScoped<IEmailSender>(_ => mail));
        using var owner = host.Client();
        await SetupAsync(owner);
        using var stranger = host.Client();

        var response = await stranger.PostAsync("/api/v1/auth/register", new { email = Email, displayName = "Stranger", password = "the strangers own phrase" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.SessionAsync()).StatusCode);
        await RunJobAsync(host, new SendAlreadyRegisteredNotice(await UserIdAsync(host)));
        Assert.Equal(Email, Assert.Single(mail.Sent).To);
    }

    [Fact]
    public async Task Operator_restores_access_with_a_generated_password()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        using var attacker = host.Client();
        for (var i = 0; i < 5; i++)
        {
            await attacker.PostAsync("/api/v1/auth/login", new { email = Email, password = $"wrong guess number {i}" });
        }

        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        int exit;
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            exit = await ResetPasswordCommand.Definition.RunAsync(scope.ServiceProvider, [Email], CancellationToken.None);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(0, exit);
        var password = output.ToString().Split('\n')[0].Split(": ")[1].Trim();
        Assert.Matches("^([a-z2-9]{5}-){4}[a-z2-9]{5}$", password);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.SessionAsync()).StatusCode);
        var login = await browser.PostAsync("/api/v1/auth/login", new { email = Email, password });
        Assert.Equal("signedIn", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    private static Task<HttpResponseMessage> SetupAsync(ApiClient client) =>
        client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = Email, displayName = "First Person", password = ApiTestHost.Password });

    private static Task<Guid> UserIdAsync(ApiTestHost host) => host.ScalarAsync<Guid>($"SELECT id FROM iam.\"user\" WHERE email = '{Email}'");

    private static async Task RunJobAsync<TJob>(ApiTestHost host, TJob job)
        where TJob : IJob
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<AccountEmailHandlers>();
        await ((IJobHandler<TJob>)(object)handler).HandleAsync(job, new JobContext(Guid.CreateVersion7(), TJob.Kind, 1, null), CancellationToken.None);
    }

    private static Uri Link(EmailMessage message)
    {
        var start = message.Text.IndexOf("https://", StringComparison.Ordinal);
        var end = message.Text.IndexOf('\n', start);
        return new Uri(message.Text[start..end]);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            lock (Sent)
            {
                Sent.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
