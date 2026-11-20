using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Ardea.Host;
using Ardea.Host.Commands;
using Ardea.Platform.Persistence;
using Ardea.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ardea.Iam.Tests;

internal sealed class ApiTestHost : IAsyncDisposable
{
    public const string SetupTokenValue = "test-setup-token";
    public const string Password = "correct horse battery staple";

    private readonly WebApplication _app;

    private ApiTestHost(WebApplication app, DatabaseOptions database)
    {
        _app = app;
        Database = database;
    }

    public DatabaseOptions Database { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<ApiTestHost> StartAsync(PostgresServer server, Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    {
        var name = await server.CreateDatabaseAsync("iam");
        var database = new DatabaseOptions
        {
            Host = server.Host,
            Port = server.Port,
            Database = name,
            OwnerUser = PostgresServer.OwnerUser,
            OwnerPassword = PostgresServer.OwnerPassword,
            AppPassword = "app-test-password",
        };
        await using (var owner = new NpgsqlConnection(database.OwnerConnectionString("tests")))
        {
            await owner.OpenAsync();
            await new DatabaseMigrator(database, Modules.Databases, NoBackup.Instance, NullLogger<DatabaseMigrator>.Instance)
                .MigrateAsync(owner, CancellationToken.None);
        }

        var values = new Dictionary<string, string?>
        {
            ["DB_HOST"] = database.Host,
            ["DB_PORT"] = database.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DB_NAME"] = database.Database,
            ["DB_APP_PASSWORD"] = database.AppPassword,
            ["MASTER_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["PUBLIC_URL"] = "https://localhost",
            ["SETUP_TOKEN"] = SetupTokenValue,
            ["REGISTRATION"] = "open",
            ["PASSWORD_HASH_ITERATIONS"] = "1000",
            ["RATE_LIMIT:AUTH_PER_MINUTE"] = "1000",
            ["RATE_LIMIT:OUTBOUND_PER_MINUTE"] = "1000",
        };
        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        var app = ApiRole.Build([], out var check, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(values);
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
        }, services) ?? throw new InvalidOperationException(string.Join(" ", check.Errors));
        await app.StartAsync();
        return new ApiTestHost(app, database);
    }

    public ApiClient Client(string baseAddress = "https://localhost") => new(_app.GetTestServer(), baseAddress);

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.OwnerConnectionString("tests"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class NoBackup : IBackupWriter
    {
        public static readonly NoBackup Instance = new();

        public Task<string?> WriteAsync(NpgsqlConnection owner, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}

internal sealed class ApiClient : IDisposable
{
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;

    private readonly Uri _base;

    public ApiClient(TestServer server, string baseAddress)
    {
        _base = new Uri(baseAddress);
        _http = new HttpClient(new CookieHandler(_cookies, server.CreateHandler())) { BaseAddress = _base };
    }

    public CookieContainer Cookies => _cookies;

    public Task<HttpResponseMessage> SessionAsync() => _http.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));

    public Task<HttpResponseMessage> GetAsync(string path) => _http.GetAsync(new Uri(path, UriKind.Relative));

    public Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request) => _http.SendAsync(request);

    public Task<HttpResponseMessage> StreamAsync(string path, CancellationToken cancellationToken) =>
        _http.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, bool csrf = true, string? origin = null, string? fetchSite = null, CancellationToken cancellationToken = default)
    {
        if (csrf && Xsrf() is null)
        {
            (await SessionAsync()).Dispose();
        }

        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (csrf && Xsrf() is { } token)
        {
            request.Headers.Add("X-XSRF-TOKEN", token);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (fetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", fetchSite);
        }

        var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode && ChangesIdentity(path))
        {
            (await SessionAsync()).Dispose();
        }

        return response;
    }

    private static bool ChangesIdentity(string path) =>
        path.StartsWith("/api/v1/auth/", StringComparison.Ordinal) || path == "/api/v1/setup"
        || path.StartsWith("/api/v1/account/password", StringComparison.Ordinal) || path.StartsWith("/api/v1/account/mfa", StringComparison.Ordinal);

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool csrf = true, string? origin = null) =>
        SendAsync(HttpMethod.Post, path, body, csrf, origin);

    public void Dispose() => _http.Dispose();

    private string? Xsrf() => _cookies.GetCookies(_base).FirstOrDefault(c => c.Name == "XSRF-TOKEN")?.Value;

    private sealed class CookieHandler(CookieContainer cookies, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var header = cookies.GetCookieHeader(request.RequestUri!);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var cookie in setCookies)
                {
                    cookies.SetCookies(request.RequestUri!, cookie);
                }
            }

            return response;
        }
    }
}

internal static class Totp
{
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var key = Base32(base32Key.Replace(" ", string.Empty, StringComparison.Ordinal));
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in input.ToUpperInvariant().TrimEnd('='))
        {
            value = (value << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
