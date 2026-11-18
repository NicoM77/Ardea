using Ardea.Host.Commands;

// One program, several roles (master plan 3.1, 12.3): the image's entry point is `ardea <command>`.
var command = args.Length > 0 ? args[0] : "help";
var rest = args.Length > 1 ? args[1..] : [];

return command switch
{
    "api" => await ApiRole.RunAsync(rest),
    "worker" => await WorkerRole.RunAsync(rest),
    "migrate" => await MigrateCommand.RunAsync(rest),
    "healthcheck" => await HealthcheckCommand.RunAsync(rest),
    "init-env" => InitEnvCommand.Run(rest),
    "doctor" => await DoctorCommand.RunAsync(rest),
    "admin" => await AdminCommand.RunAsync(rest),
    // `ardea intel renormalize …` is the admin command `intel-renormalize` (master plan 12.8 names it so).
    "intel" when rest.Length > 0 => await AdminCommand.RunAsync(["intel-" + rest[0], .. rest[1..]]),
    "version" or "--version" => VersionCommand.Run(),
    _ => HelpCommand.Run(command),
};
