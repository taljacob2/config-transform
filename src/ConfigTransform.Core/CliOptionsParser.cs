namespace ConfigTransform.Core;

/// <summary>
/// Parses the CLI shape shared by ConfigTransform.Xml and ConfigTransform.Json (docs/USAGE.md).
/// --dry-run and --diff parse successfully here even though neither front-end implements them
/// yet (see docs/ROADMAP.md) — a user passing them gets a specific "not yet implemented"
/// message from the front-end, not a generic "unrecognized argument" error from this parser.
/// --manifest/--file/--client/--environment/--output each also accept a short alias
/// (-m/-f/-c/-e/-o) for interactive use. --manifest/-m is optional here: omitting it is not an
/// error at parse time — <see cref="CliRunner"/> auto-discovers it (<see cref="ManifestDiscovery"/>)
/// when it's null, since that needs filesystem/working-directory access this pure parser
/// deliberately doesn't have.
/// </summary>
public static class CliOptionsParser
{
    public static CliOptions Parse(string[] args)
    {
        string? manifest = null;
        string? file = null;
        string? client = null;
        string? environment = null;
        string? output = null;
        var dryRun = false;
        var diff = false;
        var list = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--manifest":
                case "-m":
                    manifest = RequireValue(args, ref i, args[i]);
                    break;
                case "--file":
                case "-f":
                    file = RequireValue(args, ref i, args[i]);
                    break;
                case "--client":
                case "-c":
                    client = RequireValue(args, ref i, args[i]);
                    break;
                case "--environment":
                case "-e":
                    environment = RequireValue(args, ref i, args[i]);
                    break;
                case "--output":
                case "-o":
                    output = RequireValue(args, ref i, args[i]);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--diff":
                    diff = true;
                    break;
                case "--list":
                    list = true;
                    break;
                default:
                    throw new ArgumentException($"Unrecognized argument: '{args[i]}'.");
            }
        }

        // --list is pure introspection (what files/clients/environments does this manifest
        // have), not a resolve -- it needs none of --client/--environment/--output.
        if (!list)
        {
            if (client is null)
                throw new ArgumentException("--client is required.");
            if (environment is null)
                throw new ArgumentException("--environment is required.");
            if (output is null && !dryRun && !diff)
                throw new ArgumentException("--output is required for a real run (omit only with --dry-run or --diff).");
        }

        return new CliOptions(manifest, file, client, environment, output, dryRun, diff, list);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value.");

        i++;
        return args[i];
    }
}
