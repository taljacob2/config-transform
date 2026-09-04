namespace ConfigTransform.Core;

/// <summary>
/// Every format the CLI dispatcher can handle, keyed by extension. Dispatch is per resource, by
/// its own extension (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions" #2/#7) — a
/// resource whose extension no registered engine claims is never silently dropped, only reported
/// (see <see cref="CliRunner"/>'s use of <see cref="Require"/> vs. <see cref="Find"/>).
/// </summary>
public sealed class FormatEngineRegistry
{
    public IReadOnlyList<FormatEngine> Engines { get; }

    public string SupportedExtensions => string.Join(", ", Engines.SelectMany(e => e.Extensions));

    public FormatEngineRegistry(IReadOnlyList<FormatEngine> engines)
    {
        var duplicate = engines
            .SelectMany(e => e.Extensions, (engine, ext) => (engine, ext))
            .GroupBy(x => x.ext, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException(
                $"Extension '{duplicate.Key}' is claimed by more than one format engine: " +
                string.Join(", ", duplicate.Select(x => x.engine.DisplayName)) + ".");

        Engines = engines;
    }

    public FormatEngine? Find(string resourcePath) =>
        Engines.FirstOrDefault(e => e.Handles(resourcePath));

    /// <exception cref="ArgumentException">No registered engine handles this resource's extension.</exception>
    public FormatEngine Require(string resourcePath) =>
        Find(resourcePath) ?? throw new ArgumentException(
            $"'{resourcePath}' has an extension no format engine handles (supported: {SupportedExtensions}).");
}
