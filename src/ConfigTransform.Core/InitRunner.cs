namespace ConfigTransform.Core;

/// <summary>
/// The `init` verb's orchestration (docs/INIT_COMMAND_DESIGN.md) — scaffolds
/// `.configtransform/Environments/&lt;Env&gt;/` and `.configtransform/Clients/&lt;Client&gt;/&lt;Env&gt;/`
/// trees, either via a plain sequential <see cref="Console.ReadLine"/>-style form (no TUI), a
/// fully flag-driven quiet mode safe for CI, or the fixed <c>--template</c> starter tree. Two hard
/// modes chosen up front by flag presence plus whether the caller says stdin is a real terminal
/// (<paramref name="interactiveAllowed"/> below) — never a partial blend of flags and prompts
/// ("Command shape" in the design doc). <paramref name="stdin"/>/<paramref name="interactiveAllowed"/>
/// are supplied by the caller (the real CLI entry point passes <see cref="Console.In"/> and
/// <c>!Console.IsInputRedirected</c>) rather than read from <see cref="Console"/> directly here, so
/// the whole wizard is exactly as testable as any other stdin-based test — no real terminal needed.
/// </summary>
public static class InitRunner
{
    public static void Run(
        CliOptions options, string root, FormatEngineRegistry engines,
        TextWriter stdout, TextReader stdin, bool interactiveAllowed)
    {
        if (options.Template)
        {
            RunTemplate(root, options.DryRun, stdout);
            return;
        }

        var flagsGiven = options.InitEnvironments.Count > 0 || options.InitClients.Count > 0 ||
            options.InitResources.Count > 0 || options.Yes || options.NoScan;
        var interactive = !flagsGiven && interactiveAllowed;

        IReadOnlyList<string> resources;
        IReadOnlyList<string> environments;
        IReadOnlyList<string> clients;

        if (interactive)
        {
            (resources, environments, clients) = RunInteractive(options, root, engines, stdout, stdin);
        }
        else
        {
            resources = ResolveQuietResources(options, root, engines, interactiveAllowed, stdin, stdout);
            environments = options.InitEnvironments;
            clients = options.InitClients;

            if (environments.Count == 0)
            {
                throw new InvalidOperationException(
                    "init needs --environment (or --template) when not running in an interactive terminal.");
            }
        }

        ValidateNoCaseCollision(root, environments, clients);
        ValidateResourcesExist(root, resources);

        var files = InitPlanner.BuildPlan(root, resources, environments, clients);
        WriteFiles(files, options.DryRun, stdout);
    }

    private static void RunTemplate(string root, bool dryRun, TextWriter stdout)
    {
        var resourceFullPath = Path.Combine(root, InitTemplate.ResourcePath);
        if (File.Exists(resourceFullPath))
        {
            var existing = File.ReadAllText(resourceFullPath);
            if (!string.Equals(existing, InitTemplate.BaseContent, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"init --template: '{InitTemplate.ResourcePath}' already exists with different " +
                    "content — refusing to overwrite it.");
            }
        }

        WriteFiles(InitTemplate.BuildPlan(root), dryRun, stdout);
    }

    private static (IReadOnlyList<string> Resources, IReadOnlyList<string> Environments, IReadOnlyList<string> Clients)
        RunInteractive(CliOptions options, string root, FormatEngineRegistry engines, TextWriter stdout, TextReader stdin)
    {
        var candidates = ScanCandidates(options, root, engines);
        var resources = PromptResourceChecklist(candidates, stdin, stdout);

        IReadOnlyList<string> environments;
        while (true)
        {
            stdout.WriteLine("Environments (comma-separated, e.g. Production,Test):");
            environments = SplitCsv(ReadLineOrFail(stdin, "Environments", "--environment"));
            if (environments.Count > 0)
                break;

            stdout.WriteLine("At least one environment is required.");
        }

        stdout.WriteLine("Clients (comma-separated, or blank for none yet):");
        var clients = SplitCsv(ReadLineOrFail(stdin, "Clients", "--client"));

        return (resources, environments, clients);
    }

    private static IReadOnlyList<string> ResolveQuietResources(
        CliOptions options, string root, FormatEngineRegistry engines,
        bool interactiveAllowed, TextReader stdin, TextWriter stdout)
    {
        if (options.InitResources.Count > 0)
            return options.InitResources;

        var candidates = ScanCandidates(options, root, engines);
        if (candidates.Count == 0)
            return [];

        if (options.Yes)
            return candidates;

        if (interactiveAllowed)
            return PromptResourceChecklist(candidates, stdin, stdout);

        throw new InvalidOperationException(
            $"init found {candidates.Count} candidate resource(s); pass --yes to accept them all, " +
            "or --resource to name them explicitly (not running in an interactive terminal).");
    }

    private static IReadOnlyList<string> ScanCandidates(CliOptions options, string root, FormatEngineRegistry engines)
    {
        var scanRoot = options.ScanRoot is not null ? Path.GetFullPath(options.ScanRoot, root) : root;
        return InitScanner.Scan(root, scanRoot, engines);
    }

    private static IReadOnlyList<string> PromptResourceChecklist(
        IReadOnlyList<string> candidates, TextReader stdin, TextWriter stdout)
    {
        if (candidates.Count == 0)
            return [];

        stdout.WriteLine("Found the following candidate resources:");
        for (var i = 0; i < candidates.Count; i++)
            stdout.WriteLine($"  {i + 1}. {candidates[i]}");

        while (true)
        {
            stdout.WriteLine("Select which of these are resources to manage (e.g. \"1,3,5\", \"all\", \"none\"):");
            var line = ReadLineOrFail(stdin, "resource selection", "--resource/--yes").Trim();

            if (string.Equals(line, "all", StringComparison.OrdinalIgnoreCase))
                return candidates;
            if (string.Equals(line, "none", StringComparison.OrdinalIgnoreCase))
                return [];
            if (line.Length == 0)
            {
                stdout.WriteLine("Enter indices (e.g. \"1,3,5\"), \"all\", or \"none\".");
                continue;
            }

            var tokens = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var selected = new List<string>();
            var badToken = tokens.FirstOrDefault(t => !int.TryParse(t, out var index) || index < 1 || index > candidates.Count);

            if (badToken is not null)
            {
                stdout.WriteLine($"'{badToken}' is not a valid choice (expected 1-{candidates.Count}, \"all\", or \"none\"); try again.");
                continue;
            }

            foreach (var token in tokens)
                selected.Add(candidates[int.Parse(token) - 1]);

            return selected.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private static string ReadLineOrFail(TextReader stdin, string question, string suggestedFlag)
    {
        var line = stdin.ReadLine();
        if (line is null)
        {
            throw new InvalidOperationException(
                $"init: no answer given for \"{question}\" (unexpected end of input) — pass {suggestedFlag} " +
                "instead when not running in an interactive terminal.");
        }

        return line;
    }

    private static IReadOnlyList<string> SplitCsv(string line) =>
        line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void ValidateResourcesExist(string root, IReadOnlyList<string> resources)
    {
        foreach (var resource in resources)
        {
            var fullPath = Path.GetFullPath(resource, root);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"init: resource '{resource}' does not exist at '{fullPath}' — init never creates " +
                    "a resource file outside --template mode.");
            }
        }
    }

    /// <summary>
    /// Case-insensitive collision hazard already named for resource files by
    /// <see cref="FileResolver"/>/CLAUDE.md — a directory name is exactly as exposed to it (CI
    /// runners are typically Linux/case-sensitive, local dev typically Windows/case-insensitive).
    /// </summary>
    private static void ValidateNoCaseCollision(string root, IReadOnlyList<string> environments, IReadOnlyList<string> clients)
    {
        CheckCollisions(Path.Combine(root, ".configtransform", "Environments"), environments, "environment");
        CheckCollisions(Path.Combine(root, ".configtransform", "Clients"), clients, "client");
    }

    private static void CheckCollisions(string parentDir, IReadOnlyList<string> names, string kind)
    {
        if (!Directory.Exists(parentDir))
            return;

        var existing = Directory.EnumerateDirectories(parentDir).Select(Path.GetFileName).ToList();

        foreach (var name in names)
        {
            var collision = existing.FirstOrDefault(e =>
                string.Equals(e, name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(e, name, StringComparison.Ordinal));

            if (collision is not null)
            {
                throw new InvalidOperationException(
                    $"init: {kind} '{name}' collides case-insensitively with the existing '{collision}' — " +
                    "pick a name that doesn't differ only by case.");
            }
        }
    }

    private static void WriteFiles(IReadOnlyList<InitFile> files, bool dryRun, TextWriter stdout)
    {
        if (files.Count == 0)
        {
            stdout.WriteLine("Nothing to write (no resources/environments given).");
            return;
        }

        stdout.WriteLine(dryRun ? "Would write:" : "Writing:");
        foreach (var file in files)
            stdout.WriteLine($"  {file.RepoRelativePath}");

        if (dryRun)
            return;

        foreach (var file in files)
        {
            var dir = Path.GetDirectoryName(file.FullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(file.FullPath, file.Content);
        }

        stdout.WriteLine($"Wrote {files.Count} file(s).");
    }
}
