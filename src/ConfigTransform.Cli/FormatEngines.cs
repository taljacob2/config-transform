using ConfigTransform.Core;

namespace ConfigTransform.Cli;

/// <summary>
/// The dispatcher's real, production format registry — every format `configtransform` handles,
/// registered once here. Deliberately uses fully-qualified <c>ConfigTransform.Xml.…</c>/
/// <c>ConfigTransform.Json.…</c> names rather than `using` directives for either namespace:
/// inside `namespace ConfigTransform.Cli`, a `using ConfigTransform.Xml;`/`using
/// ConfigTransform.Json;` would make the bare identifiers `Xml`/`Json` resolve to those
/// namespaces, shadowing `System.Xml`/`System.Text.Json` for any later edit to this file.
/// <c>ConfigTransform.Env</c>/<c>ConfigTransform.Yaml</c> have no such collision (no bare
/// `System.Env`/`System.Yaml` namespace exists) but are fully-qualified here too, purely for
/// consistency with the other lines.
/// </summary>
public static class FormatEngines
{
    public static readonly FormatEngineRegistry All = new(
    [
        new FormatEngine("XML", [".config", ".xml"], "xml",
            ConfigTransform.Xml.XmlLayerMerger.Merge, ConfigTransform.Xml.XmlFieldAuthor.Author),
        new FormatEngine("JSON", [".json"], "json",
            ConfigTransform.Json.JsonLayerMerger.Merge, ConfigTransform.Json.JsonFieldAuthor.Author),
        new FormatEngine("ENV", [".env"], "env",
            ConfigTransform.Env.EnvLayerMerger.Merge, ConfigTransform.Env.EnvFieldAuthor.Author),
        new FormatEngine("YAML", [".yaml", ".yml"], "yaml",
            ConfigTransform.Yaml.YamlLayerMerger.Merge, ConfigTransform.Yaml.YamlFieldAuthor.Author),
    ]);
}
