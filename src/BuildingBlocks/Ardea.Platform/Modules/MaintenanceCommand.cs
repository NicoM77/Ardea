namespace Ardea.Platform.Modules;

public sealed record MaintenanceCommand(string Name, string Usage, string Description, Func<IServiceProvider, string[], CancellationToken, Task<int>> RunAsync);
