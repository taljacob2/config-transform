namespace ConfigTransform.Core;

/// <summary>One secret name in use, with every file (repo-relative) that writes its placeholder.</summary>
public sealed record SecretUse(string Name, IReadOnlyList<string> UsedIn);

/// <summary>
/// What the resolve flow does with secrets (docs/SECRETS_DESIGN.md), shared by
/// <see cref="CliRunner"/>'s single-resource and every-resource paths: the secrets section of the
/// resolution report, substitution for <c>--reveal-secrets</c> previews, and the strict
/// substitution a real run requires. Never prints or puts a secret's value in an error message.
/// </summary>
public static class SecretsStep
{
    /// <summary>
    /// The report's secrets tree for one resource: every placeholder name in <paramref name="merged"/>
    /// -- what will actually be substituted -- with the files of its chain that write it. Prints
    /// nothing, and returns false, when there are no placeholders.
    /// </summary>
    public static bool PrintReport(
        TextWriter stdout, string root, ResolvedResource resolved, string merged, SecretSet secrets,
        string indent = "    ", bool blankLineBefore = false)
    {
        // The files first, so a placeholder that isn't upper snake case is reported in the file that writes it.
        var usedIn = FindUses(root, [resolved]).ToDictionary(use => use.Name, use => use.UsedIn, StringComparer.Ordinal);
        var names = SecretPlaceholders.Names(merged, LayerChain.ToRepoRelative(root, resolved.BasePath));
        if (names.Count == 0)
            return false;

        if (blankLineBefore)
            stdout.WriteLine();
        PrintTree(stdout, names.Select(name => new SecretUse(name, usedIn.GetValueOrDefault(name) ?? [])).ToList(), secrets, indent);
        return true;
    }

    /// <summary>
    /// Every placeholder name written in <paramref name="resources"/>' base files and patches, in
    /// order of first appearance, with the files that write it. Scans the files as written, merging
    /// nothing -- so a placeholder a later patch overwrites is still found (docs/SECRETS_DESIGN.md
    /// decision #27).
    /// </summary>
    public static IReadOnlyList<SecretUse> FindUses(string root, IEnumerable<ResolvedResource> resources)
    {
        var order = new List<string>();
        var files = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var resolved in resources)
        {
            foreach (var file in resolved.PatchPathsInOrder.Prepend(resolved.BasePath))
            {
                var display = LayerChain.ToRepoRelative(root, file);
                foreach (var name in SecretPlaceholders.Names(File.ReadAllText(file), display))
                {
                    if (!files.TryGetValue(name, out var usedIn))
                    {
                        files[name] = usedIn = [];
                        order.Add(name);
                    }

                    if (!usedIn.Contains(display))
                        usedIn.Add(display);
                }
            }
        }

        return order.Select(name => new SecretUse(name, files[name])).ToList();
    }

    /// <summary>
    /// One tree per secret (docs/SECRETS_DESIGN.md, "What each mode does"), read like a resource's
    /// chain: its state, where it's used, then every step its value could come from -- each layer,
    /// outermost first, then the environment variable -- as `patched in:` / `not patched in`.
    /// Names files and variables, never a value.
    /// </summary>
    public static void PrintTree(TextWriter stdout, IReadOnlyList<SecretUse> uses, SecretSet secrets, string indent)
    {
        stdout.WriteLine($"{indent}secrets");
        var nameWidth = uses.Max(use => use.Name.Length);

        for (var i = 0; i < uses.Count; i++)
        {
            if (i > 0)
                stdout.WriteLine();

            var (name, usedIn) = uses[i];
            var state = secrets.Lookup(name).State switch
            {
                SecretState.Resolved => "resolved",
                SecretState.Missing => "MISSING",
                _ => "unknown",
            };
            stdout.WriteLine($"{indent}  {name.PadRight(nameWidth)}   {state}");
            foreach (var file in usedIn)
                stdout.WriteLine($"{indent}    used in: {file}");

            var trace = secrets.Trace(name);
            foreach (var step in trace.Layers)
            {
                stdout.WriteLine($"{indent}    {step.Layer}");
                foreach (var source in step.PatchedIn)
                    stdout.WriteLine($"{indent}      patched in: {source}");
                foreach (var unreadable in step.Unknown)
                    stdout.WriteLine($"{indent}      unknown: {unreadable}");
                if (step.PatchedIn.Count == 0 && step.Unknown.Count == 0)
                    stdout.WriteLine(step.Notes.Count == 0
                        ? $"{indent}      not patched in"
                        : $"{indent}      not patched in ({string.Join("; ", step.Notes)})");
                stdout.WriteLine($"{indent}      ↓");
            }

            stdout.WriteLine($"{indent}    environment variable");
            stdout.WriteLine(trace.Variable switch
            {
                SecretVariableState.Set => $"{indent}      patched in: ${name}",
                SecretVariableState.Empty => $"{indent}      not patched in (${name} is set but empty, which counts as unset)",
                _ => $"{indent}      not patched in",
            });
        }
    }

    /// <summary>For a preview: substitutes resolved secrets only when <paramref name="reveal"/>; otherwise returns <paramref name="content"/> as is.</summary>
    public static string ForPreview(FormatEngine engine, string content, SecretSet secrets, bool reveal) =>
        reveal ? Substitute(engine, content, secrets) : content;

    /// <summary>
    /// For a real run: every placeholder must resolve, and none may survive substitution (one in a key
    /// or comment, or a malformed one). Throws before anything is written, naming each problem but
    /// never a value.
    /// </summary>
    public static string ForRealRun(FormatEngine engine, string resourcePath, string merged, SecretSet secrets)
    {
        var unresolved = SecretPlaceholders.Names(merged, resourcePath)
            .Select(secrets.Lookup)
            .Where(status => status.State != SecretState.Resolved)
            .ToList();

        if (unresolved.Count > 0)
            throw new InvalidOperationException(
                $"'{resourcePath}' uses secrets that can't be resolved, so nothing was written:\n" +
                string.Join("\n", FormatStatuses(unresolved).Select(line => $"  {line}")) +
                "\nTry: add the missing names to a source a layer in this chain lists under \"secrets\" (a *.secret.env file " +
                "or a Key Vault), make unreadable sources readable (git-crypt unlock, az login, or access to the vault), " +
                "or set the environment variable of the same name.");

        var substituted = Substitute(engine, merged, secrets);

        if (SecretPlaceholders.ContainsMarker(substituted))
            throw new InvalidOperationException(
                $"'{resourcePath}' still contains '{SecretPlaceholders.Marker}' after secrets were substituted, so nothing " +
                "was written. Placeholders are only substituted inside values -- this one is in a key or a comment, or " +
                $"is malformed (a name is '{SecretPlaceholders.Prefix}' followed by letters, digits and '_', inside '{{{{' and '}}}}').");

        return substituted;
    }

    private static string Substitute(FormatEngine engine, string content, SecretSet secrets)
    {
        if (!SecretPlaceholders.ContainsMarker(content))
            return content;

        var substitute = engine.SubstituteSecrets
            ?? throw new InvalidOperationException($"The {engine.DisplayName} format engine doesn't support secrets placeholders.");

        return substitute(content, secrets.ValueOf);
    }

    private static IEnumerable<string> FormatStatuses(IReadOnlyList<SecretStatus> statuses)
    {
        var nameWidth = statuses.Max(s => s.Name.Length);
        foreach (var status in statuses)
        {
            var (state, detail) = status.State switch
            {
                SecretState.Resolved => ("resolved", status.Source!),
                SecretState.Missing => ("MISSING", "no secrets file in this chain defines it"),
                _ => ("unknown", status.Source!),
            };
            yield return $"{status.Name.PadRight(nameWidth)}   {state,-8}   {detail}";
        }
    }
}
