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
    "version" or "--version" => VersionCommand.Run(),
    _ => HelpCommand.Run(command),
};
