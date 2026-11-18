using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ardea.TestSupport;

namespace Ardea.Iam.Tests;

/// <summary>Sign-in after master plan 9.1 and 9.3; the first core flow of the plan, at API level.</summary>
public sealed class SignInTests(PostgresServer server)
{
    private const string Email = "first@example.org";

    [Fact]
    public async Task First_user_sets_up_with_the_token_and_signs_in_with_password_and_totp()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();

        Assert.True((await (await browser.GetAsync("/api/v1/setup/status")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("setupRequired").GetBoolean());
        await Expect(HttpStatusCode.NoContent, await SetupAsync(browser));
        var session = await SessionAsync(browser);
        Assert.True(session.GetProperty("user").GetProperty("isInstanceAdmin").GetBoolean());

        var key = await EnableTotpAsync(browser);
        await Expect(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/logout"));
        await Expect(HttpStatusCode.Unauthorized, await browser.SessionAsync());

        var login = await browser.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        Assert.Equal("twoFactorRequired", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await Expect(HttpStatusCode.Unauthorized, await browser.SessionAsync());

        await Expect(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/login/two-factor", new { code = Totp.Code(key), isRecoveryCode = false }));
        Assert.True((await SessionAsync(browser)).GetProperty("user").GetProperty("twoFactorEnabled").GetBoolean());
        Assert.False((await (await browser.GetAsync("/api/v1/setup/status")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("setupRequired").GetBoolean());
    }

    [Fact]
    public async Task Recovery_code_works_once_instead_of_the_authenticator()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        var key = await EnableTotpAsync(browser, out var recoveryCodes);
        _ = key;
        await browser.PostAsync("/api/v1/auth/logout");

        await browser.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await Expect(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/login/two-factor", new { code = recoveryCodes[0], isRecoveryCode = true }));
        await browser.PostAsync("/api/v1/auth/logout");

        await browser.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        var reuse = await browser.PostAsync("/api/v1/auth/login/two-factor", new { code = recoveryCodes[0], isRecoveryCode = true });
        await ExpectProblem(reuse, HttpStatusCode.BadRequest, "invalid_code");
    }

    [Fact]
    public async Task Wrong_token_is_refused_and_a_second_setup_is_impossible()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();

        await ExpectProblem(await SetupAsync(browser, token: "guessed"), HttpStatusCode.Forbidden, "invalid_setup_token");
        await Expect(HttpStatusCode.NoContent, await SetupAsync(browser));
        using var intruder = host.Client();
        await ExpectProblem(await SetupAsync(intruder, email: "intruder@example.org"), HttpStatusCode.Conflict, "setup_completed");
    }

    [Fact]
    public async Task Unknown_account_and_wrong_password_get_the_same_answer()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        using var stranger = host.Client();

        var wrongPassword = await stranger.PostAsync("/api/v1/auth/login", new { email = Email, password = "not the right password" });
        var unknown = await stranger.PostAsync("/api/v1/auth/login", new { email = "nobody@example.org", password = "not the right password" });

        Assert.Equal(wrongPassword.StatusCode, unknown.StatusCode);
        Assert.Equal(Code(await wrongPassword.Content.ReadFromJsonAsync<JsonElement>()), Code(await unknown.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_five_failed_attempts()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        using var attacker = host.Client();

        for (var i = 0; i < 5; i++)
        {
            await attacker.PostAsync("/api/v1/auth/login", new { email = Email, password = $"guess number {i} of many" });
        }

        var correct = await attacker.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await ExpectProblem(correct, HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Theory]
    [InlineData("short pass")]
    [InlineData("password1234")]
    [InlineData("first@example.org")]
    public async Task Short_common_or_obvious_passwords_are_rejected(string password)
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();

        var response = await SetupAsync(browser, password: password);

        await ExpectProblem(response, HttpStatusCode.BadRequest, "weak_password");
    }

    [Fact]
    public async Task Revoked_session_and_password_change_end_other_sessions()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var laptop = host.Client();
        await SetupAsync(laptop);
        using var phone = host.Client();
        await phone.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await Expect(HttpStatusCode.OK, await phone.SessionAsync());

        var sessions = await (await laptop.GetAsync("/api/v1/account/sessions")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, sessions.GetArrayLength());
        var phoneSession = sessions.EnumerateArray().Single(s => !s.GetProperty("current").GetBoolean()).GetProperty("id").GetGuid();
        await Expect(HttpStatusCode.NoContent, await laptop.SendAsync(HttpMethod.Delete, $"/api/v1/account/sessions/{phoneSession}"));
        await Expect(HttpStatusCode.Unauthorized, await phone.SessionAsync());

        using var tablet = host.Client();
        await tablet.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await Expect(HttpStatusCode.NoContent, await laptop.PostAsync("/api/v1/account/password", new { currentPassword = ApiTestHost.Password, newPassword = "an entirely new passphrase" }));
        await Expect(HttpStatusCode.Unauthorized, await tablet.SessionAsync());
        await Expect(HttpStatusCode.OK, await laptop.SessionAsync());
    }

    [Fact]
    public async Task Changing_request_without_csrf_token_or_from_another_origin_is_refused()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);

        await ExpectProblem(await browser.PostAsync("/api/v1/auth/logout", csrf: false), HttpStatusCode.Forbidden, "csrf_failed");
        await ExpectProblem(await browser.PostAsync("/api/v1/auth/logout", origin: "https://evil.example"), HttpStatusCode.Forbidden, "csrf_failed");
        await Expect(HttpStatusCode.OK, await browser.SessionAsync());
    }

    [Fact]
    public async Task Signed_out_api_answers_401_without_redirect()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();

        var response = await browser.GetAsync("/api/v1/account/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Sign_in_attempts_are_rate_limited_per_address()
    {
        await using var host = await ApiTestHost.StartAsync(server, new() { ["RATE_LIMIT:AUTH_PER_MINUTE"] = "3" });
        using var browser = host.Client();

        HttpResponseMessage last = null!;
        for (var i = 0; i < 4; i++)
        {
            last = await browser.PostAsync("/api/v1/auth/login", new { email = Email, password = "whatever it may be" });
        }

        await ExpectProblem(last, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.True(last.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Authenticator_key_is_stored_encrypted()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        var setup = await (await browser.PostAsync("/api/v1/account/mfa/setup")).Content.ReadFromJsonAsync<JsonElement>();
        var key = setup.GetProperty("sharedKey").GetString()!.Replace(" ", string.Empty, StringComparison.Ordinal);

        var stored = await host.ScalarAsync<string>("SELECT value FROM iam.user_token WHERE name = 'AuthenticatorKey'");

        Assert.DoesNotContain(key, stored, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("invite", HttpStatusCode.BadRequest, "invalid_invitation")]
    [InlineData("closed", HttpStatusCode.Forbidden, "registration_closed")]
    public async Task Registration_follows_the_instance_mode(string mode, HttpStatusCode status, string code)
    {
        await using var host = await ApiTestHost.StartAsync(server, new() { ["REGISTRATION"] = mode });
        using var browser = host.Client();

        var response = await browser.PostAsync("/api/v1/auth/register", new { email = "new@example.org", displayName = "New", password = ApiTestHost.Password });

        await ExpectProblem(response, status, code);
    }

    [Fact]
    public async Task Open_registration_creates_a_signed_in_account_and_rejects_a_taken_address()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();

        await Expect(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/register", new { email = "new@example.org", displayName = "New", password = ApiTestHost.Password }));
        Assert.False((await SessionAsync(browser)).GetProperty("user").GetProperty("isInstanceAdmin").GetBoolean());
        using var other = host.Client();
        await ExpectProblem(
            await other.PostAsync("/api/v1/auth/register", new { email = "new@example.org", displayName = "Other", password = ApiTestHost.Password }),
            HttpStatusCode.Conflict, "email_taken");
    }

    private static Task<HttpResponseMessage> SetupAsync(ApiClient client, string token = ApiTestHost.SetupTokenValue, string email = Email, string password = ApiTestHost.Password) =>
        client.PostAsync("/api/v1/setup", new { setupToken = token, email, displayName = "First Person", password });

    private static async Task<JsonElement> SessionAsync(ApiClient client)
    {
        var response = await client.SessionAsync();
        await Expect(HttpStatusCode.OK, response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<string> EnableTotpAsync(ApiClient client) => EnableTotpAsync(client, out _);

    private static Task<string> EnableTotpAsync(ApiClient client, out List<string> recoveryCodes)
    {
        var codes = new List<string>();
        recoveryCodes = codes;
        return Enable();

        async Task<string> Enable()
        {
            var setup = await (await client.PostAsync("/api/v1/account/mfa/setup")).Content.ReadFromJsonAsync<JsonElement>();
            var key = setup.GetProperty("sharedKey").GetString()!;
            Assert.StartsWith("otpauth://totp/Ardea:", setup.GetProperty("authenticatorUri").GetString(), StringComparison.Ordinal);
            var enabled = await client.PostAsync("/api/v1/account/mfa", new { code = Totp.Code(key) });
            await Expect(HttpStatusCode.OK, enabled);
            codes.AddRange((await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!));
            Assert.Equal(10, codes.Count);
            return key;
        }
    }

    private static string? Code(JsonElement problem) => problem.TryGetProperty("code", out var code) ? code.GetString() : null;

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
        Assert.Equal(code, Code(JsonDocument.Parse(body).RootElement));
    }
}
