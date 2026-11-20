using System.Globalization;
using Ardea.Platform.Persistence;
using Npgsql;

namespace Ardea.Host.Commands;

internal static class DoctorCommand
{
    public static async Task<int> RunAsync()
    {
        var configuration = HostConfiguration.Load();
        var results = new List<(string Check, bool Ok, string Detail)> { VersionResult() };

        var config = HostConfiguration.CheckApplication(configuration);
        results.Add(("configuration", config.IsValid, config.IsValid ? "complete" : string.Join(" ", config.Errors)));
        results.AddRange(RuntimeChecks(configuration["SCRATCH_DIR"] ?? "/scratch"));

        if (!string.IsNullOrEmpty(configuration[DatabaseOptions.AppPasswordKey]))
        {
            results.AddRange(await DatabaseChecksAsync(DatabaseOptions.FromConfiguration(configuration)));
        }

        var services = await DoctorServiceChecks.RunAsync(configuration, DoctorServiceChecks.TryConnectAsync, SourceHosts(), CancellationToken.None);
        results.AddRange(services.Where(r => r.Level != DoctorLevel.Warn).Select(r => (r.Check, r.Level == DoctorLevel.Ok, r.Detail)));

        foreach (var result in results)
        {
            Console.WriteLine(Line(result));
        }

        foreach (var warning in services.Where(r => r.Level == DoctorLevel.Warn))
        {
            Console.WriteLine($"warn {warning.Check,-14} {warning.Detail}");
        }

        return results.All(r => r.Ok) ? 0 : 1;
    }

    internal static (string Check, bool Ok, string Detail) VersionResult() => ("version", true, AppVersion.Current);

    internal static string Line((string Check, bool Ok, string Detail) result) =>
        $"{(result.Ok ? "ok  " : "FAIL")} {result.Check,-14} {result.Detail}";

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> SourceHosts()
    {
        var hosts = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var type in SourceAdapters.Discover().Where(t => t.GetConstructor(Type.EmptyTypes) is not null))
        {
            switch (Activator.CreateInstance(type))
            {
                case Ardea.Intel.Sources.Abstractions.ISourceProvider provider:
                    hosts[provider.Descriptor.Key] = provider.Descriptor.AllowedHosts;
                    break;
                case Ardea.Intel.Sources.Abstractions.ISignalProvider signal:
                    hosts[signal.Descriptor.Key] = signal.Descriptor.AllowedHosts;
                    break;
            }
        }

        return hosts;
    }

    internal static IEnumerable<(string, bool, string)> RuntimeChecks(string scratch)
    {
        (string, bool, string) Try(string name, Func<string> probe)
        {
            try
            {
                return (name, true, probe());
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or CultureNotFoundException or InvalidTimeZoneException or IOException or UnauthorizedAccessException)
            {
                return (name, false, ex.Message);
            }
        }

        yield return Try("time zones", () =>
        {
            var zurich = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");
            var summer = TimeZoneInfo.ConvertTime(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), zurich);
            return summer.Offset == TimeSpan.FromHours(2)
                ? "Europe/Zurich resolves with daylight saving"
                : throw new InvalidTimeZoneException($"Europe/Zurich has offset {summer.Offset} in July");
        });
        yield return Try("culture", () =>
        {
            var german = CultureInfo.GetCultureInfo("de-CH", predefinedOnly: true);
            var text = 1234.5m.ToString("N1", german);
            return text.Contains('’', StringComparison.Ordinal) || text.Contains('\'', StringComparison.Ordinal)
                ? $"ICU present (de-CH formats {text})"
                : throw new CultureNotFoundException($"de-CH formats 1234.5 as {text}; ICU data looks incomplete");
        });
        yield return Try("temp", () =>
        {
            var file = Path.Combine(Path.GetTempPath(), $"ardea-doctor-{Environment.ProcessId}");
            File.WriteAllText(file, "ok");
            File.Delete(file);
            return $"{Path.GetTempPath()} writable";
        });
        if (!IsMountPoint(scratch))
        {
            yield return ("scratch", true, $"{scratch} not mounted (only the worker needs it)");
        }
        else
        {
            yield return Try("scratch", () =>
            {
                var file = Path.Combine(scratch, $".ardea-doctor-{Environment.ProcessId}");
                File.WriteAllText(file, "ok");
                File.Delete(file);
                var free = new DriveInfo(scratch).AvailableFreeSpace / (1024 * 1024 * 1024.0);
                return $"{scratch} writable, {free:F1} GB free";
            });
        }
    }

    private static bool IsMountPoint(string path)
    {
        const string mountInfo = "/proc/self/mountinfo";
        if (!File.Exists(mountInfo))
        {
            return Directory.Exists(path);
        }

        var target = Path.TrimEndingDirectorySeparator(path);
        return File.ReadLines(mountInfo).Any(line => line.Split(' ') is { Length: > 4 } fields && fields[4] == target);
    }

    private static async Task<IEnumerable<(string, bool, string)>> DatabaseChecksAsync(DatabaseOptions options)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var dataSource = NpgsqlDataSource.Create(options.AppConnectionString("ardea-doctor"));
        try
        {
            await using var command = dataSource.CreateCommand("SELECT current_setting('server_version')");
            var version = (string)(await command.ExecuteScalarAsync(cancellation.Token))!;
            var behind = await SchemaGate.FindBehindAsync(dataSource, options, cancellation.Token);
            return
            [
                ("database", true, $"PostgreSQL {version} as {options.AppUser}@{options.Host}:{options.Port}/{options.Database}"),
                ("schema", behind.Count == 0, behind.Count == 0 ? "up to date" : $"behind in {string.Join(", ", behind)}; run `ardea migrate`"),
            ];
        }
        catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or TimeoutException)
        {
            return [("database", false, ex.Message)];
        }
    }
}
