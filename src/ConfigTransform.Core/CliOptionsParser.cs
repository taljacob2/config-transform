namespace ConfigTransform.Core;

/// <summary>
/// Parses the CLI shape shared by ConfigTransform.Xml and ConfigTransform.Json (docs/USAGE.md).
/// --resource/--client/--environment/--output each also accept a short alias (-r/-c/-e/-o) for
/// interactive use; --host's is -H (capital — -h is already --help, see docs/HOST_LAYER_DESIGN.md
/// decision log #2 for the accepted tradeoff). --resource/-r names one project directly by its
/// repo-root-relative path (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions" #6/#7) —
/// it's optional for a resolve/dry-run/diff/real-run (omitting it means "every resource this layer
/// touches") and for --list (omitting it lists the whole target layer instead of a reverse
/// lookup), but always required for 'set', which can only ever target one resource at a time.
/// --host/-H is a further optional third axis (docs/HOST_LAYER_DESIGN.md) — always requires both
/// --client and --environment, the same "requires the level above it" rule --client already
/// follows for --environment, and is never valid with --list --resource's reverse lookup.
/// A leading bare "set" (no dashes) is a different verb, not a flag — see
/// docs/FIELD_AUTHORING_DESIGN.md. It switches on --match/--set (each repeatable) and relaxes
/// --client/--environment to optional (they choose *which* layer set writes, rather than being
/// required inputs to a resolve).
/// A leading bare "init" (no dashes) is likewise a different verb, not a flag — see
/// docs/INIT_COMMAND_DESIGN.md. Its own <c>--environment</c>/<c>--client</c>/<c>--resource</c>
/// flags are repeatable (declaring several new layers/resources, not targeting one existing one)
/// and land in <see cref="CliOptions.InitEnvironments"/>/<see cref="CliOptions.InitClients"/>/
/// <see cref="CliOptions.InitResources"/>/<see cref="CliOptions.InitHosts"/> instead of the
/// singular <see cref="CliOptions.Client"/>/<see cref="CliOptions.Environment"/>/
/// <see cref="CliOptions.Resource"/>/<see cref="CliOptions.Host"/> every other mode uses.
/// No arguments at all, or a bare "help"/"--help"/"-h" in flag position anywhere in the
/// arguments (never mistaken for a value some other flag is consuming, e.g. `--set value=-h`,
/// and independent of the leading "set"/"init" verb — `configtransform set help` wins just like
/// `configtransform set --help` does) always wins and short-circuits every other check — help is
/// the default when there's nothing else to go on, not an error.
/// </summary>
public static class CliOptionsParser
{
    private static readonly CliOptions HelpOptions = new(
        null, null, null, null, null, DryRun: false, Diff: false, List: false, Set: false, Help: true, [], [],
        Init: false, [], [], [], [], null, Yes: false, NoScan: false, Template: false);

    // Every token the switch below recognizes as a flag (or the bare "help" verb it also
    // accepts) — used only to power the "did you mean" suggestion on an unrecognized argument,
    // so it deliberately excludes "set"/"init" (recognized positionally, before this switch ever
    // runs, not as a flag typo).
    private static readonly string[] KnownFlags =
    [
        "--help", "-h", "help", "--resource", "-r", "--client", "-c", "--environment", "-e",
        "--host", "-H", "--output", "-o", "--dry-run", "--diff", "--list", "--match", "--set",
        "--scan-root", "--yes", "--no-scan", "--template"
    ];

    private const int MaxSuggestionDistance = 2;

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
            return HelpOptions;

        var set = args[0] == "set";
        var init = args[0] == "init";
        var rest = set || init ? args[1..] : args;

        string? resource = null;
        string? client = null;
        string? environment = null;
        string? host = null;
        string? output = null;
        var dryRun = false;
        var diff = false;
        var list = false;
        var match = new List<string>();
        var setFields = new List<string>();
        var initEnvironments = new List<string>();
        var initClients = new List<string>();
        var initResources = new List<string>();
        var initHosts = new List<string>();
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
                case "help":
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
                case "--host":
                case "-H":
                    if (init)
                        initHosts.Add(RequireValue(rest, ref i, rest[i]));
                    else
                        host = RequireValue(rest, ref i, rest[i]);
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
                    var suggestion = FindClosestFlag(rest[i]);
                    throw new ArgumentException(suggestion is null
                        ? $"Unrecognized argument: '{rest[i]}'.\nTry: configtransform --help to see every valid flag."
                        : $"Unrecognized argument: '{rest[i]}'.\nTry: did you mean {suggestion}?");
            }
        }

        if (set)
        {
            if (list)
                throw new ArgumentException("--list and 'set' are different modes; use one or the other.\nTry: configtransform --list ... or configtransform set ..., not both in one call.");
            if (output is not null)
                throw new ArgumentException("--output has no effect with 'set' — it writes to the file --client/--environment select, not an arbitrary path.\nTry: drop --output; 'set' always writes back into the layer named by --client/--environment.");
            if (client is not null && environment is null)
                throw new ArgumentException("--client requires --environment with 'set' (there is no client-only layer).\nTry: add --environment <E>, e.g. --client Acme --environment Production.");
            if (host is not null && (client is null || environment is null))
                throw new ArgumentException("--host requires --client and --environment with 'set'.\nTry: add --client <C> --environment <E>, e.g. --host <H> --client Acme --environment Production.");
            if (resource is null)
                throw new ArgumentException("'set' requires --resource.\nTry: configtransform set --resource <path> --client <C> --environment <E> --match <k>=<v> --set <k>=<v>.");
            if (match.Count == 0)
                throw new ArgumentException("'set' requires at least one --match.\nTry: add --match <field>=<value> (or bare --match <field> to match any value).");
            if (setFields.Count == 0)
                throw new ArgumentException("'set' requires at least one --set.\nTry: add --set <field>=<value>.");
        }
        else if (init)
        {
            if (diff)
                throw new ArgumentException("--diff is not valid with 'init'.\nTry: drop --diff; 'init' scaffolds new layers, it doesn't merge or preview one.");
            if (list)
                throw new ArgumentException("--list is not valid with 'init'.\nTry: drop --list; run configtransform --list separately once the tree exists.");
            if (output is not null)
                throw new ArgumentException("--output is not valid with 'init'.\nTry: drop --output; 'init' writes new configtransform.json layers, not a merged resource.");
            if (match.Count > 0)
                throw new ArgumentException("--match is not valid with 'init'.\nTry: drop --match; that's a 'set' flag, not an 'init' one.");
            if (setFields.Count > 0)
                throw new ArgumentException("--set is not valid with 'init'.\nTry: drop --set; that's a 'set' flag, not an 'init' one.");

            if (template)
            {
                if (scanRoot is not null || initEnvironments.Count > 0 || initClients.Count > 0 ||
                    initResources.Count > 0 || initHosts.Count > 0 || noScan || yes)
                    throw new ArgumentException("--template is mutually exclusive with every other 'init' flag.\nTry: configtransform init --template on its own, or drop --template to scaffold a custom tree.");
            }
            else
            {
                if (noScan && initResources.Count == 0)
                    throw new ArgumentException("--no-scan requires at least one --resource — nothing to list otherwise.\nTry: add --resource <path> (repeatable), or drop --no-scan to let init scan the repo instead.");
                if (initClients.Count > 0 && initEnvironments.Count == 0)
                    throw new ArgumentException("--client requires --environment with 'init' (there is no client-only layer).\nTry: add --environment <E>, e.g. --environment Production --client Acme.");
                if (initHosts.Count > 0 && (initClients.Count == 0 || initEnvironments.Count == 0))
                    throw new ArgumentException("--host requires --client and --environment with 'init' (there is no host-only or host-without-client layer).\nTry: add --client <C> --environment <E>, e.g. --host <H> --client Acme --environment Production.");
            }
        }
        else if (list)
        {
            if (resource is null && environment is null)
                throw new ArgumentException("--list requires --resource, or --environment (optionally with --client).\nTry: configtransform --list --environment <E> [--client <C>], or configtransform --list --resource <path>.");
            if (resource is not null && (client is not null || environment is not null || host is not null))
                throw new ArgumentException("--list --resource is a tree-wide reverse lookup; it doesn't take --client/--environment/--host.\nTry: drop --client/--environment/--host to keep --resource, or drop --resource and use --client/--environment/--host instead.");
            if (client is not null && environment is null)
                throw new ArgumentException("--client requires --environment with --list (there is no client-only layer).\nTry: add --environment <E>, e.g. --list --client Acme --environment Production.");
            if (host is not null && (client is null || environment is null))
                throw new ArgumentException("--host requires --client and --environment with --list.\nTry: add --client <C> --environment <E>, e.g. --list --host <H> --client Acme --environment Production.");
        }
        else
        {
            if (client is not null && environment is null)
                throw new ArgumentException("--client requires --environment (there is no client-only layer).\nTry: add --environment <E>, e.g. --client Acme --environment Production.");
            if (host is not null && (client is null || environment is null))
                throw new ArgumentException("--host requires --client and --environment.\nTry: add --client <C> --environment <E>, e.g. --host <H> --client Acme --environment Production.");
            if (output is null && !dryRun && !diff)
                throw new ArgumentException("--output is required for a real run (omit only with --dry-run or --diff).\nTry: add --output <path>, or pass --dry-run/--diff to preview instead of writing.");
        }

        return new CliOptions(
            resource, client, environment, host, output, dryRun, diff, list, set, Help: false, match, setFields,
            init, initEnvironments, initClients, initResources, initHosts, scanRoot, yes, noScan, template);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value.\nTry: {flag} <value>.");

        i++;
        return args[i];
    }

    private static string? FindClosestFlag(string token)
    {
        string? closest = null;
        var closestDistance = MaxSuggestionDistance + 1;

        foreach (var flag in KnownFlags)
        {
            var distance = LevenshteinDistance(token, flag);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = flag;
            }
        }

        return closestDistance <= MaxSuggestionDistance ? closest : null;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var distances = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            distances[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            distances[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var substitutionCost = a[i - 1] == b[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + substitutionCost);
            }
        }

        return distances[a.Length, b.Length];
    }
}
