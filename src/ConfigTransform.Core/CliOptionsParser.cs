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
/// A leading bare "init" (no dashes) is likewise a different verb, not a flag — see
/// docs/INIT_COMMAND_DESIGN.md. Its own <c>--environment</c>/<c>--client</c>/<c>--resource</c>
/// flags are repeatable (declaring several new layers/resources, not targeting one existing one)
/// and land in <see cref="CliOptions.InitEnvironments"/>/<see cref="CliOptions.InitClients"/>/
/// <see cref="CliOptions.InitResources"/> instead of the singular <see cref="CliOptions.Client"/>/
/// <see cref="CliOptions.Environment"/>/<see cref="CliOptions.Resource"/> every other mode uses.
/// No arguments at all, a leading bare "help", or "--help"/"-h" in flag position anywhere in the
/// arguments (never mistaken for a value some other flag is consuming, e.g. `--set value=-h`)
/// always wins and short-circuits every other check — help is the default when there's nothing
/// else to go on, not an error.
/// </summary>
public static class CliOptionsParser
{
    private static readonly CliOptions HelpOptions = new(
        null, null, null, null, DryRun: false, Diff: false, List: false, Set: false, Help: true, [], [],
        Init: false, [], [], [], null, Yes: false, NoScan: false, Template: false);

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] == "help")
            return HelpOptions;

        var set = args[0] == "set";
        var init = args[0] == "init";
        var rest = set || init ? args[1..] : args;

        string? resource = null;
        string? client = null;
        string? environment = null;
        string? output = null;
        var dryRun = false;
        var diff = false;
        var list = false;
        var match = new List<string>();
        var setFields = new List<string>();
        var initEnvironments = new List<string>();
        var initClients = new List<string>();
        var initResources = new List<string>();
        string? scanRoot = null;
        var yes = false;
        var noScan = false;
        var template = false;

        for (var i = 0; i < rest.Length; i++)
        {
            switch (rest[i])
            {
                case "--help":
                case "-h":
                    return HelpOptions;
                case "--resource":
                case "-r":
                    if (init)
                        initResources.Add(RequireValue(rest, ref i, rest[i]));
                    else
                        resource = RequireValue(rest, ref i, rest[i]);
                    break;
                case "--client":
                case "-c":
                    if (init)
                        initClients.Add(RequireValue(rest, ref i, rest[i]));
                    else
                        client = RequireValue(rest, ref i, rest[i]);
                    break;
                case "--environment":
                case "-e":
                    if (init)
                        initEnvironments.Add(RequireValue(rest, ref i, rest[i]));
                    else
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
                case "--scan-root":
                    scanRoot = RequireValue(rest, ref i, rest[i]);
                    break;
                case "--yes":
                    yes = true;
                    break;
                case "--no-scan":
                    noScan = true;
                    break;
                case "--template":
                    template = true;
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
        else if (init)
        {
            if (diff)
                throw new ArgumentException("--diff is not valid with 'init'.");
            if (list)
                throw new ArgumentException("--list is not valid with 'init'.");
            if (output is not null)
                throw new ArgumentException("--output is not valid with 'init'.");
            if (match.Count > 0)
                throw new ArgumentException("--match is not valid with 'init'.");
            if (setFields.Count > 0)
                throw new ArgumentException("--set is not valid with 'init'.");

            if (template)
            {
                if (scanRoot is not null || initEnvironments.Count > 0 || initClients.Count > 0 ||
                    initResources.Count > 0 || noScan || yes)
                    throw new ArgumentException("--template is mutually exclusive with every other 'init' flag.");
            }
            else
            {
                if (noScan && initResources.Count == 0)
                    throw new ArgumentException("--no-scan requires at least one --resource — nothing to list otherwise.");
                if (initClients.Count > 0 && initEnvironments.Count == 0)
                    throw new ArgumentException("--client requires --environment with 'init' (there is no client-only layer).");
            }
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

        return new CliOptions(
            resource, client, environment, output, dryRun, diff, list, set, Help: false, match, setFields,
            init, initEnvironments, initClients, initResources, scanRoot, yes, noScan, template);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value.");

        i++;
        return args[i];
    }
}
