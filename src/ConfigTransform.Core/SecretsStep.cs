namespace ConfigTransform.Core;

/// <summary>
/// What the resolve flow does with secrets (docs/SECRETS_DESIGN.md), shared by
/// <see cref="CliRunner"/>'s single-resource and every-resource paths: the secrets section of the
/// resolution report, substitution for <c>--reveal-secrets</c> previews, and the strict
/// substitution a real run requires. Never prints or puts a secret's value in an error message.
/// </summary>
public static class SecretsStep
{
    /// <summary>The report's secrets section: each placeholder name in <paramref name="merged"/>, its state, and where its value comes from. Prints nothing when there are no placeholders.</summary>
    public static void PrintReport(TextWriter stdout, string merged, SecretSet secrets, string indent = "    ")
    {
        var statuses = SecretPlaceholders.Names(merged).Select(secrets.Lookup).ToList();
        if (statuses.Count == 0)
            return;

        stdout.WriteLine($"{indent}secrets");
        foreach (var line in FormatStatuses(statuses))
            stdout.WriteLine($"{indent}  {line}");
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
        var unresolved = SecretPlaceholders.Names(merged)
            .Select(secrets.Lookup)
            .Where(status => status.State != SecretState.Resolved)
            .ToList();

        if (unresolved.Count > 0)
            throw new InvalidOperationException(
                $"'{resourcePath}' uses secrets that can't be resolved, so nothing was written:\n" +
                string.Join("\n", FormatStatuses(unresolved).Select(line => $"  {line}")) +
                "\nTry: add the missing names to a *.secret.env file a layer in this chain lists under \"secrets\", " +
                "run git-crypt unlock for locked files, or set the environment variable of the same name.");

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

        return substitute(content, name => secrets.Lookup(name) is { State: SecretState.Resolved } status ? status.Value : null);
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
