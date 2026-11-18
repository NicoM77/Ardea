namespace Ardea.Platform.Modules;

/// <summary>
/// A maintenance command of a module, run as <c>ardea admin &lt;name&gt; …</c> (master plan 12.8).
/// It runs in its own scope with the application role and the tenant-free unit of work; it writes
/// what it does to standard output and returns the process exit code.
/// </summary>
public sealed record MaintenanceCommand(string Name, string Usage, string Description, Func<IServiceProvider, string[], CancellationToken, Task<int>> RunAsync);
