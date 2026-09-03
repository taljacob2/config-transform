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
/// A leading bare "set" (no dashes) is a different verb, not a flag — see
/// docs/FIELD_AUTHORING_DESIGN.md. It switches on --match/--set (each repeatable) and relaxes
/// --client/--environment to optional (they choose *which* file set writes, rather than being
/// required inputs to a resolve).
/// </summary>
public static class CliOptionsParser
{
    public static CliOptions Parse(string[] args)
    {
        var set = args.Length > 0 && args[0] == "set";
        var rest = set ? args[1..] : args;

        string? manifest = null;
        string? file = null;
        string? client = null;
        string? environment = null;
        string? output = null;
        var dryRun = false;
        var diff = false;
        var list = false;
        var match = new List<string>();
        var setFields = new List<string>();

        for (var i = 0; i < rest.Length; i++)
        {
            switch (rest[i])
            {
                case "--manifest":
                case "-m":
                    manifest = RequireValue(rest, ref i, rest[i]);
                    break;
                case "--file":
                case "-f":
                    file = RequireValue(rest, ref i, rest[i]);
                    break;
                case "--client":
                case "-c":
                    client = RequireValue(rest, ref i, rest[i]);
                    break;
                case "--environment":
                case "-e":
                    environment = RequireValue(rest, ref i, rest[i]);
                    break;
                case "--output":
                case "-o":
                    output = RequireValue(rest, ref i, rest[i]);
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
                case "--match":
                    match.Add(RequireValue(rest, ref i, rest[i]));
                    break;
                case "--set":
                    setFields.Add(RequireValue(rest, ref i, rest[i]));
                    break;
                default:
                    throw new ArgumentException($"Unrecognized argument: '{rest[i]}'.");
            }
        }

        if (set)
        {
            if (list)
                throw new ArgumentException("--list and 'set' are different modes; use one or the other.");
            if (output is not null)
                throw new ArgumentException("--output has no effect with 'set' — it writes to the file --client/--environment select, not an arbitrary path.");
            if (client is not null && environment is null)
                throw new ArgumentException("--client requires --environment with 'set' (there is no client-only overlay layer).");
            if (match.Count == 0)
                throw new ArgumentException("'set' requires at least one --match.");
            if (setFields.Count == 0)
                throw new ArgumentException("'set' requires at least one --set.");
        }
        // --list is pure introspection (what files/clients/environments does this manifest
        // have), not a resolve -- it needs none of --client/--environment/--output.
        else if (!list)
        {
            if (client is null)
                throw new ArgumentException("--client is required.");
            if (environment is null)
                throw new ArgumentException("--environment is required.");
            if (output is null && !dryRun && !diff)
                throw new ArgumentException("--output is required for a real run (omit only with --dry-run or --diff).");
        }

        return new CliOptions(manifest, file, client, environment, output, dryRun, diff, list, set, match, setFields);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value.");

        i++;
        return args[i];
    }
}
