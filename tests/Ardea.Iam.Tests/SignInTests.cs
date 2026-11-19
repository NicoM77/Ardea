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

        // The next step's code: the one that turned two-factor on is used up (issue #85).
        var code = Totp.Code(key, DateTimeOffset.UtcNow.AddSeconds(30));
        await Expect(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/login/two-factor", new { code, isRecoveryCode = false }));
        Assert.True((await SessionAsync(browser)).GetProperty("user").GetProperty("twoFactorEnabled").GetBoolean());
        Assert.False((await (await browser.GetAsync("/api/v1/setup/status")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("setupRequired").GetBoolean());
    }

    [Fact]
    public async Task An_authenticator_code_signs_in_only_once()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = host.Client();
        await SetupAsync(owner);
        var key = await EnableTotpAsync(owner);
        // The code of the next time step: still accepted, and not the one that turned two-factor on.
        var code = Totp.Code(key, DateTimeOffset.UtcNow.AddSeconds(30));
        using var laptop = host.Client();
        await laptop.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await Expect(HttpStatusCode.NoContent, await laptop.PostAsync("/api/v1/auth/login/two-factor", new { code, isRecoveryCode = false }));

        // Someone who watched the code being typed and knows the password.
        using var onlooker = host.Client();
        await onlooker.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        var replay = await onlooker.PostAsync("/api/v1/auth/login/two-factor", new { code, isRecoveryCode = false });

        await ExpectProblem(replay, HttpStatusCode.BadRequest, "invalid_code");
        await Expect(HttpStatusCode.Unauthorized, await onlooker.SessionAsync());
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
    public async Task Read_only_lookups_before_sign_in_get_a_looser_limit_than_attempts()
    {
        await using var host = await ApiTestHost.StartAsync(server, new() { ["RATE_LIMIT:AUTH_PER_MINUTE"] = "3" });
        using var browser = host.Client();

        // The web app reads the setup state on every load; a few reloads must not end in 429.
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/setup/status")).StatusCode);
        }

        // Still bounded: six times the attempt limit.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            statuses.Add((await browser.GetAsync("/api/v1/setup/status")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task Authenticator_key_is_stored_encrypted()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        var setup = await (await browser.PostAsync("/api/v1/account/mfa/setup", new { password = ApiTestHost.Password })).Content.ReadFromJsonAsync<JsonElement>();
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
    public async Task Open_registration_creates_an_account_and_does_not_reveal_a_taken_address()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();

        await Expect(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/register", new { email = "new@example.org", displayName = "New", password = ApiTestHost.Password }));
        Assert.False((await SessionAsync(browser)).GetProperty("user").GetProperty("isInstanceAdmin").GetBoolean());

        using var other = host.Client();
        await Expect(HttpStatusCode.NoContent, await other.PostAsync("/api/v1/auth/register", new { email = "new@example.org", displayName = "Other", password = "the other persons phrase" }));
        await Expect(HttpStatusCode.Unauthorized, await other.SessionAsync());
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM iam.\"user\" WHERE email = 'new@example.org'"));
    }

    [Fact]
    public async Task Two_factor_setup_needs_the_password_and_a_wrong_one_counts_toward_lockout()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var thief = host.Client();
        await SetupAsync(thief);

        await ExpectProblem(await thief.PostAsync("/api/v1/account/mfa/setup", new { password = "a guessed password" }), HttpStatusCode.BadRequest, "invalid_password");
        await ExpectProblem(await thief.PostAsync("/api/v1/account/mfa/setup", new { }), HttpStatusCode.BadRequest, "invalid_password");
        Assert.Equal(2, await host.ScalarAsync<int>("SELECT access_failed_count FROM iam.\"user\""));
    }

    [Fact]
    public async Task Turning_on_two_factor_and_signing_out_everywhere_end_the_other_sessions()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var laptop = host.Client();
        await SetupAsync(laptop);
        using var phone = host.Client();
        await phone.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });

        await EnableTotpAsync(laptop);
        await Expect(HttpStatusCode.Unauthorized, await phone.SessionAsync());
        await Expect(HttpStatusCode.OK, await laptop.SessionAsync());

        using var tablet = host.Client();
        await tablet.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        await tablet.PostAsync("/api/v1/auth/login/two-factor", new { code = await NextCodeAsync(host), isRecoveryCode = false });
        await Expect(HttpStatusCode.OK, await tablet.SessionAsync());
        await Expect(HttpStatusCode.NoContent, await laptop.SendAsync(HttpMethod.Delete, "/api/v1/account/sessions"));
        await Expect(HttpStatusCode.Unauthorized, await tablet.SessionAsync());
        await Expect(HttpStatusCode.OK, await laptop.SessionAsync());
    }

    [Fact]
    public async Task Sign_in_from_a_cross_site_page_is_refused()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        await browser.PostAsync("/api/v1/auth/logout");

        var response = await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password }, fetchSite: "cross-site");

        await ExpectProblem(response, HttpStatusCode.Forbidden, "csrf_failed");
    }

    [Fact]
    public async Task Https_instance_refuses_plain_http_api_calls_with_a_clear_code()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var plain = host.Client("http://localhost");

        await ExpectProblem(await plain.GetAsync("/api/v1/setup/status"), HttpStatusCode.Forbidden, "https_required");
    }

    [Fact]
    public async Task Development_instance_on_plain_http_signs_in_with_unprefixed_cookies()
    {
        await using var host = await ApiTestHost.StartAsync(server, new() { ["PUBLIC_URL"] = "http://localhost" });
        using var browser = host.Client("http://localhost");

        await Expect(HttpStatusCode.NoContent, await SetupAsync(browser));
        await Expect(HttpStatusCode.OK, await browser.SessionAsync());
        var names = browser.Cookies.GetCookies(new Uri("http://localhost")).Select(c => c.Name).ToList();
        Assert.Contains("ardea", names);
        Assert.DoesNotContain(names, n => n.StartsWith("__Host-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Api_answers_are_never_cached()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();

        var response = await browser.SessionAsync();

        Assert.True(response.Headers.CacheControl?.NoStore, response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("2001:db8:1:2:aaaa::1", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff::9", "2001:db8:1:2::/64")]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    public void Rate_limit_treats_an_ipv6_slash_64_as_one_client(string address, string key)
    {
        Assert.Equal(key, IamModule.ClientKey(System.Net.IPAddress.Parse(address)));
    }

    [Fact]
    public async Task Second_step_without_the_password_step_is_refused()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var owner = host.Client();
        await SetupAsync(owner);
        await EnableTotpAsync(owner);
        using var stranger = host.Client();

        var response = await stranger.PostAsync("/api/v1/auth/login/two-factor", new { code = "123456", isRecoveryCode = false });

        await ExpectProblem(response, HttpStatusCode.Unauthorized, "two_factor_expired");
        await Expect(HttpStatusCode.Unauthorized, await stranger.SessionAsync());
    }

    [Fact]
    public async Task Wrong_authenticator_codes_count_toward_the_lockout()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        var key = await EnableTotpAsync(browser);
        await browser.PostAsync("/api/v1/auth/logout");
        await browser.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });

        var attempts = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            attempts.Add(await browser.PostAsync("/api/v1/auth/login/two-factor", new { code = WrongCode(key), isRecoveryCode = false }));
        }

        await ExpectProblem(attempts[0], HttpStatusCode.BadRequest, "invalid_code");
        var correct = await browser.PostAsync("/api/v1/auth/login/two-factor", new { code = Totp.Code(key), isRecoveryCode = false });
        await ExpectProblem(correct, HttpStatusCode.Unauthorized, "invalid_credentials");
        await Expect(HttpStatusCode.Unauthorized, await browser.SessionAsync());
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_a_wrong_one_counts_toward_lockout()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var thief = host.Client();
        await SetupAsync(thief);

        var wrong = await thief.PostAsync("/api/v1/account/password", new { currentPassword = "a guessed password", newPassword = "the thiefs new passphrase" });
        var weak = await thief.PostAsync("/api/v1/account/password", new { currentPassword = ApiTestHost.Password, newPassword = "password1234" });

        await ExpectProblem(wrong, HttpStatusCode.BadRequest, "validation_failed");
        await ExpectProblem(weak, HttpStatusCode.BadRequest, "weak_password");
        Assert.Equal(1, await host.ScalarAsync<int>("SELECT access_failed_count FROM iam.\"user\""));
        using var owner = host.Client();
        var login = await owner.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        Assert.Equal("signedIn", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Enabling_two_factor_needs_a_valid_code_and_an_active_authenticator_cannot_be_replaced()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        var setup = await (await browser.PostAsync("/api/v1/account/mfa/setup", new { password = ApiTestHost.Password })).Content.ReadFromJsonAsync<JsonElement>();
        var key = setup.GetProperty("sharedKey").GetString()!;

        await ExpectProblem(await browser.PostAsync("/api/v1/account/mfa", new { code = WrongCode(key) }), HttpStatusCode.BadRequest, "invalid_code");
        Assert.False((await SessionAsync(browser)).GetProperty("user").GetProperty("twoFactorEnabled").GetBoolean());

        await Expect(HttpStatusCode.OK, await browser.PostAsync("/api/v1/account/mfa", new { code = Totp.Code(key) }));
        var again = await browser.PostAsync("/api/v1/account/mfa/setup", new { password = ApiTestHost.Password });

        await ExpectProblem(again, HttpStatusCode.Conflict, "mfa_enabled");
        Assert.Equal(key.Replace(" ", string.Empty, StringComparison.Ordinal), await StoredKeyAsync(host), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Turning_off_two_factor_needs_the_password_and_a_code()
    {
        await using var host = await ApiTestHost.StartAsync(server);
        using var browser = host.Client();
        await SetupAsync(browser);
        var key = await EnableTotpAsync(browser, out var recoveryCodes);

        await ExpectProblem(
            await browser.SendAsync(HttpMethod.Delete, "/api/v1/account/mfa", new { password = "a guessed password", code = Totp.Code(key) }), HttpStatusCode.BadRequest, "invalid_password");
        await ExpectProblem(
            await browser.SendAsync(HttpMethod.Delete, "/api/v1/account/mfa", new { password = ApiTestHost.Password, code = WrongCode(key) }), HttpStatusCode.BadRequest, "invalid_code");
        Assert.True((await SessionAsync(browser)).GetProperty("user").GetProperty("twoFactorEnabled").GetBoolean());

        await Expect(HttpStatusCode.NoContent, await browser.SendAsync(HttpMethod.Delete, "/api/v1/account/mfa", new { password = ApiTestHost.Password, code = recoveryCodes[0] }));
        await browser.PostAsync("/api/v1/auth/logout");
        var login = await browser.PostAsync("/api/v1/auth/login", new { email = Email, password = ApiTestHost.Password });
        Assert.Equal("signedIn", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Without_a_configured_setup_token_the_instance_makes_one_that_ends_with_the_setup()
    {
        await using var host = await ApiTestHost.StartAsync(server, new() { ["SETUP_TOKEN"] = string.Empty });
        using var browser = host.Client();
        var token = await StoredSetupTokenAsync(host);
        Assert.False(string.IsNullOrEmpty(token));

        await ExpectProblem(await SetupAsync(browser), HttpStatusCode.Forbidden, "invalid_setup_token");
        await Expect(HttpStatusCode.NoContent, await SetupAsync(browser, token: token!));

        Assert.Null(await StoredSetupTokenAsync(host));
    }

    /// <summary>A six-digit code that the key does not produce around now.</summary>
    private static string WrongCode(string key)
    {
        var valid = Enumerable.Range(-3, 7).Select(step => Totp.Code(key, DateTimeOffset.UtcNow.AddSeconds(step * 30))).ToHashSet();
        return Enumerable.Range(0, 10).Select(i => $"{i}{i}{i}{i}{i}{i}").First(code => !valid.Contains(code));
    }

    private static async Task<string?> StoredSetupTokenAsync(ApiTestHost host)
    {
        await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(host.Services);
        var settings = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Platform.Settings.IInstanceSettings>(scope.ServiceProvider);
        return (await settings.GetAsync(Security.SetupToken.Setting, CancellationToken.None)).Value;
    }

    /// <summary>
    /// The code of the stored authenticator key for the next time step, as the user's app shows it 30 seconds
    /// on: the current step's code turned two-factor on and is used up.
    /// </summary>
    private static async Task<string> NextCodeAsync(ApiTestHost host) => Totp.Code(await StoredKeyAsync(host), DateTimeOffset.UtcNow.AddSeconds(30));

    /// <summary>The stored authenticator key of the account.</summary>
    private static async Task<string> StoredKeyAsync(ApiTestHost host)
    {
        await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(host.Services);
        // User data is visible only to its own account (row-level security), so the lookup acts for it.
        var userId = await host.ScalarAsync<Guid>($"SELECT id FROM iam.\"user\" WHERE normalized_email = '{Email.ToUpperInvariant()}'");
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Platform.Tenancy.TenantContext>(scope.ServiceProvider).SetUser(userId);
        var users = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Domain.ArdeaUser>>(scope.ServiceProvider);
        var user = await users.FindByIdAsync(userId.ToString());
        return (await users.GetAuthenticatorKeyAsync(user!))!;
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
            var setup = await (await client.PostAsync("/api/v1/account/mfa/setup", new { password = ApiTestHost.Password })).Content.ReadFromJsonAsync<JsonElement>();
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
