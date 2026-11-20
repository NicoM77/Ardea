using Ardea.Host.Commands;

var command = args.Length > 0 ? args[0] : "help";
var rest = args.Length > 1 ? args[1..] : [];

return command switch
{
    "api" => await ApiRole.RunAsync(rest),
    "worker" => await WorkerRole.RunAsync(rest),
    "migrate" => await MigrateCommand.RunAsync(rest),
    "healthcheck" => await HealthcheckCommand.RunAsync(rest),
    "init-env" => InitEnvCommand.Run(rest),
    "doctor" => await DoctorCommand.RunAsync(),
    "admin" => await AdminCommand.RunAsync(rest),
    _ when rest.Length > 0 && AdminCommand.GroupPrefixes.Contains(command) => await AdminCommand.RunAsync([command + "-" + rest[0], .. rest[1..]]),
    "org" => await OrgCommand.RunAsync(rest),
    "seed-demo" => await SeedDemoCommand.RunAsync(rest),
    "version" or "--version" => VersionCommand.Run(),
    _ => HelpCommand.Run(command),
};
