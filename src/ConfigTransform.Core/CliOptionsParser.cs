namespace ConfigTransform.Core;

/// <summary>
/// Parses the CLI shape shared by ConfigTransform.Xml and ConfigTransform.Json (docs/USAGE.md).
/// --resource/--client/--environment/--output each also accept a short alias (-r/-c/-e/-o) for
/// interactive use. --resource/-r names one project directly by its repo-root-relative path
/// (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions" #6/#7) — it's optional for a
/// resolve/dry-run/diff/real-run (omitting it means "every resource this layer touches") and for
/// --list (omitting it lists the whole target layer instead of a reverse lookup), but always
/// required for 'set', which can only ever target one resource at a time.
/// A leading bare "set" (no dashes) is a different verb, not a flag — see
/// docs/FIELD_AUTHORING_DESIGN.md. It switches on --match/--set (each repeatable) and relaxes
/// --client/--environment to optional (they choose *which* layer set writes, rather than being
/// required inputs to a resolve).
/// </summary>
public static class CliOptionsParser
{
    public static CliOptions Parse(string[] args)
    {
        var set = args.Length > 0 && args[0] == "set";
        var rest = set ? args[1..] : args;

        string? resource = null;
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
                case "--resource":
                case "-r":
                    resource = RequireValue(rest, ref i, rest[i]);
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
                throw new ArgumentException("--client requires --environment with 'set' (there is no client-only layer).");
            if (resource is null)
                throw new ArgumentException("'set' requires --resource.");
            if (match.Count == 0)
                throw new ArgumentException("'set' requires at least one --match.");
            if (setFields.Count == 0)
                throw new ArgumentException("'set' requires at least one --set.");
        }
        else if (list)
        {
            if (resource is null && environment is null)
                throw new ArgumentException("--list requires --resource, or --environment (optionally with --client).");
            if (resource is not null && (client is not null || environment is not null))
                throw new ArgumentException("--list --resource is a tree-wide reverse lookup; it doesn't take --client/--environment.");
            if (client is not null && environment is null)
                throw new ArgumentException("--client requires --environment with --list (there is no client-only layer).");
        }
        else
        {
            if (client is null)
                throw new ArgumentException("--client is required.");
            if (environment is null)
                throw new ArgumentException("--environment is required.");
            if (output is null && !dryRun && !diff)
                throw new ArgumentException("--output is required for a real run (omit only with --dry-run or --diff).");
        }

        return new CliOptions(resource, client, environment, output, dryRun, diff, list, set, match, setFields);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value.");

        i++;
        return args[i];
    }
}
