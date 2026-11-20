using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.Iam.Domain;
using Ardea.Iam.Security;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Ardea.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Ardea.Iam.Tests;

public sealed class AuthenticatorReplayTests(PostgresServer server)
{
    private const string Email = "first@example.org";
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task The_code_that_turned_two_factor_on_does_not_sign_in()
    {
        var (host, clock) = await StartAsync();
        await using var _ = host;
        using var owner = host.Client();
        await SetupAsync(owner);
        var key = await EnableTotpAsync(owner, clock);
        var used = Totp.Code(key, clock.GetUtcNow());
        await owner.PostAsync("/api/v1/auth/logout");

        await owner.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        var replay = await owner.PostAsync("/api/v1/auth/login/two-factor", new { code = used, isRecoveryCode = false });

        await ExpectProblem(replay, HttpStatusCode.BadRequest, "invalid_code");
        await Expect(HttpStatusCode.Unauthorized, await owner.SessionAsync());
    }

    [Fact]
    public async Task An_older_code_within_the_drift_window_is_refused_after_a_newer_one()
    {
        var (host, clock) = await StartAsync();
        await using var _ = host;
        using var owner = host.Client();
        await SetupAsync(owner);
        var key = await EnableTotpAsync(owner, clock);
        clock.Advance(Step);
        using var phone = await SignInAsync(host, Totp.Code(key, clock.GetUtcNow() + (2 * Step)), HttpStatusCode.NoContent);

        using var later = host.Client();
        await later.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        var older = await later.PostAsync("/api/v1/auth/login/two-factor", new { code = Totp.Code(key, clock.GetUtcNow()), isRecoveryCode = false });

        await ExpectProblem(older, HttpStatusCode.BadRequest, "invalid_code");
        await Expect(HttpStatusCode.Unauthorized, await later.SessionAsync());
    }

    [Fact]
    public async Task Two_concurrent_sign_ins_with_the_same_code_let_exactly_one_in()
    {
        var (host, clock) = await StartAsync();
        await using var _ = host;
        using var owner = host.Client();
        await SetupAsync(owner);
        var key = await EnableTotpAsync(owner, clock);
        clock.Advance(Step);
        var code = Totp.Code(key, clock.GetUtcNow());
        using var laptop = host.Client();
        using var onlooker = host.Client();
        await laptop.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await onlooker.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });

        var answers = await Task.WhenAll(
            laptop.PostAsync("/api/v1/auth/login/two-factor", new { code, isRecoveryCode = false }),
            onlooker.PostAsync("/api/v1/auth/login/two-factor", new { code, isRecoveryCode = false }));

        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.NoContent);
        await ExpectProblem(Assert.Single(answers, a => a.StatusCode != HttpStatusCode.NoContent), HttpStatusCode.BadRequest, "invalid_code");
        var sessions = await Task.WhenAll(laptop.SessionAsync(), onlooker.SessionAsync());
        Assert.Single(sessions, s => s.StatusCode == HttpStatusCode.OK);
        Assert.Equal(AuthenticatorCode.StepAt(clock.GetUtcNow()), await host.ScalarAsync<long>("SELECT totp_last_step FROM iam.\"user\""));
        using var third = await SignInAsync(host, code, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_account_loaded_before_a_code_was_claimed_does_not_move_the_step_back_when_saved()
    {
        var (host, clock) = await StartAsync();
        await using var _ = host;
        using var owner = host.Client();
        await SetupAsync(owner);
        var key = await EnableTotpAsync(owner, clock);
        clock.Advance(Step);
        var code = Totp.Code(key, clock.GetUtcNow());
        var userId = await host.ScalarAsync<Guid>("SELECT id FROM iam.\"user\"");

        await using var stale = host.Services.CreateAsyncScope();
        var staleUsers = ActAs(stale, userId);
        var staleUser = (await staleUsers.FindByIdAsync(userId.ToString()))!;

        await using (var signIn = host.Services.CreateAsyncScope())
        {
            var users = ActAs(signIn, userId);
            Assert.True(await users.VerifyTwoFactorTokenAsync((await users.FindByIdAsync(userId.ToString()))!, TokenOptions.DefaultAuthenticatorProvider, code));
            await signIn.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        }

        Assert.True((await staleUsers.AccessFailedAsync(staleUser)).Succeeded);
        await stale.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);

        Assert.Equal(1, await host.ScalarAsync<int>("SELECT access_failed_count FROM iam.\"user\""));
        Assert.Equal(AuthenticatorCode.StepAt(clock.GetUtcNow()), await host.ScalarAsync<long>("SELECT totp_last_step FROM iam.\"user\""));
        using var replay = await SignInAsync(host, code, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_database_never_lets_the_used_step_go_back()
    {
        var (host, clock) = await StartAsync();
        await using var _ = host;
        using var owner = host.Client();
        await SetupAsync(owner);
        await EnableTotpAsync(owner, clock);
        var used = await host.ScalarAsync<long>("SELECT totp_last_step FROM iam.\"user\"");

        Assert.Equal(used, await host.ScalarAsync<long>("UPDATE iam.\"user\" SET totp_last_step = totp_last_step - 1 RETURNING totp_last_step"));
        Assert.Equal(used, await host.ScalarAsync<long>("UPDATE iam.\"user\" SET totp_last_step = NULL RETURNING totp_last_step"));
        Assert.Equal(used + 1, await host.ScalarAsync<long>("UPDATE iam.\"user\" SET totp_last_step = totp_last_step + 1 RETURNING totp_last_step"));
    }

    [Fact]
    public async Task A_refused_replay_counts_toward_the_lockout()
    {
        var (host, clock) = await StartAsync();
        await using var _ = host;
        using var owner = host.Client();
        await SetupAsync(owner);
        var key = await EnableTotpAsync(owner, clock);
        clock.Advance(Step);
        var code = Totp.Code(key, clock.GetUtcNow());
        using var first = await SignInAsync(host, code, HttpStatusCode.NoContent);
        Assert.Equal(0, await host.ScalarAsync<int>("SELECT access_failed_count FROM iam.\"user\""));

        using var second = await SignInAsync(host, code, HttpStatusCode.BadRequest);

        Assert.Equal(1, await host.ScalarAsync<int>("SELECT access_failed_count FROM iam.\"user\""));
    }

    [Fact]
    public async Task Turning_two_factor_off_takes_a_code_only_once()
    {
        var (host, clock) = await StartAsync();
        await using var _ = host;
        using var owner = host.Client();
        await SetupAsync(owner);
        var key = await EnableTotpAsync(owner, clock);
        clock.Advance(Step);
        var code = Totp.Code(key, clock.GetUtcNow());
        using var laptop = await SignInAsync(host, code, HttpStatusCode.NoContent);

        var replay = await laptop.SendAsync(HttpMethod.Delete, "/api/v1/account/mfa", new { password = ApiTestHost.Password, code });

        await ExpectProblem(replay, HttpStatusCode.BadRequest, "invalid_code");
        Assert.True(await host.ScalarAsync<bool>("SELECT two_factor_enabled FROM iam.\"user\""));
        Assert.Equal(1, await host.ScalarAsync<int>("SELECT access_failed_count FROM iam.\"user\""));
        clock.Advance(Step);
        await Expect(
            HttpStatusCode.NoContent,
            await laptop.SendAsync(HttpMethod.Delete, "/api/v1/account/mfa", new { password = ApiTestHost.Password, code = Totp.Code(key, clock.GetUtcNow()) }));
    }

    [Fact]
    public async Task Two_concurrent_sign_ins_with_the_same_recovery_code_let_exactly_one_in()
    {
        var (host, clock) = await StartAsync();
        await using var _ = host;
        using var owner = host.Client();
        await SetupAsync(owner);
        var recoveryCodes = new List<string>();
        await EnableTotpAsync(owner, clock, recoveryCodes);
        using var laptop = host.Client();
        using var onlooker = host.Client();
        await laptop.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await onlooker.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });

        var answers = await Task.WhenAll(
            laptop.PostAsync("/api/v1/auth/login/two-factor", new { code = recoveryCodes[0], isRecoveryCode = true }),
            onlooker.PostAsync("/api/v1/auth/login/two-factor", new { code = recoveryCodes[0], isRecoveryCode = true }));

        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.NoContent);
        await ExpectProblem(Assert.Single(answers, a => a.StatusCode != HttpStatusCode.NoContent), HttpStatusCode.BadRequest, "invalid_code");
    }

    [Fact]
    public async Task Two_concurrent_sign_ins_with_different_recovery_codes_use_up_both()
    {
        var (host, clock) = await StartAsync();
        await using var _ = host;
        using var owner = host.Client();
        await SetupAsync(owner);
        var recoveryCodes = new List<string>();
        await EnableTotpAsync(owner, clock, recoveryCodes);
        using var laptop = host.Client();
        using var phone = host.Client();
        await laptop.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await phone.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });

        var answers = await Task.WhenAll(
            laptop.PostAsync("/api/v1/auth/login/two-factor", new { code = recoveryCodes[0], isRecoveryCode = true }),
            phone.PostAsync("/api/v1/auth/login/two-factor", new { code = recoveryCodes[1], isRecoveryCode = true }));

        Assert.All(answers, a => Assert.Equal(HttpStatusCode.NoContent, a.StatusCode));
        foreach (var used in recoveryCodes.Take(2))
        {
            using var again = host.Client();
            await again.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
            await ExpectProblem(await again.PostAsync("/api/v1/auth/login/two-factor", new { code = used, isRecoveryCode = true }), HttpStatusCode.BadRequest, "invalid_code");
        }
    }

    private static UserManager<ArdeaUser> ActAs(AsyncServiceScope scope, Guid userId)
    {
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(userId);
        return scope.ServiceProvider.GetRequiredService<UserManager<ArdeaUser>>();
    }

    private async Task<(ApiTestHost Host, FakeTimeProvider Clock)> StartAsync()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = await ApiTestHost.StartAsync(server, services: services => services.AddSingleton<TimeProvider>(clock));
        return (host, clock);
    }

    private static async Task<ApiClient> SignInAsync(ApiTestHost host, string code, HttpStatusCode expected)
    {
        var client = host.Client();
        await client.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await Expect(expected, await client.PostAsync("/api/v1/auth/login/two-factor", new { code, isRecoveryCode = false }));
        return client;
    }

    private static Task<HttpResponseMessage> SetupAsync(ApiClient client) =>
        client.PostAsync("/api/v1/setup", new { setupToken = ApiTestHost.SetupTokenValue, email = Email, displayName = "First Person", password = ApiTestHost.Password });

    private static async Task<string> EnableTotpAsync(ApiClient client, FakeTimeProvider clock, List<string>? recoveryCodes = null)
    {
        var setup = await (await client.PostAsync("/api/v1/account/mfa/setup", new { password = ApiTestHost.Password })).Content.ReadFromJsonAsync<JsonElement>();
        var key = setup.GetProperty("sharedKey").GetString()!;
        var enabled = await client.PostAsync("/api/v1/account/mfa", new { code = Totp.Code(key, clock.GetUtcNow()) });
        await Expect(HttpStatusCode.OK, enabled);
        recoveryCodes?.AddRange((await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!));
        return key;
    }

    private static async Task Expect(HttpStatusCode status, HttpResponseMessage response)
    {
        if (response.StatusCode != status)
        {
            Assert.Fail($"Expected {(int)status}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task ExpectProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status}, got {(int)response.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }
}
