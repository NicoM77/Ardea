namespace Ardea.Host.Commands;

internal static class HelpCommand
{
    public static int Run(string command)
    {
        var known = command is "help" or "--help" or "-h";
        var output = known ? Console.Out : Console.Error;
        if (!known)
        {
            output.WriteLine($"Unknown command '{command}'.");
            output.WriteLine();
        }

        output.WriteLine("""
            Usage: ardea <command> [options]

            Roles
              api                 Serve the REST API, sign-in and the web app on port 8080
              worker              Run background jobs: sources, assessment, notifications
              migrate             Create the application role, back up, migrate the database, then exit
                --roles-only        Only create the application role (before restoring a dump)

            Helpers
              healthcheck api     Exit 0 when the API answers /health/ready
              healthcheck worker  Exit 0 when this worker's heartbeat is less than two minutes old
              init-env            Write .env and .env.app with generated secrets
                --dir <path>        Target directory (default: current directory)
              doctor              Check configuration, database, schema and the runtime image
              admin <command>     Maintenance commands, e.g. `admin reset-password <email>`; `admin` lists them
              intel renormalize --source <key>   Read a source's stored records again with the current parser
              intel recompute     Merge every entry again with the current rules
              org export <slug> [--out file]       An organisation's data as JSON (owner role, no secrets)
              org delete <slug> --confirm <slug>   Remove an organisation and all its data
              version             Print the installed version
            """);
        return known ? 0 : 64;
    }
}

internal static class VersionCommand
{
    public static int Run()
    {
        Console.WriteLine(AppVersion.Current);
        return 0;
    }
}
