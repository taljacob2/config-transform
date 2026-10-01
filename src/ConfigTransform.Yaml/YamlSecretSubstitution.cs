using ConfigTransform.Core;
using YamlDotNet.RepresentationModel;

namespace ConfigTransform.Yaml;

/// <summary>
/// Secret substitution for YAML (docs/SECRETS_DESIGN.md): <c>{{CFSECRET_…}}</c> placeholders are
/// replaced inside scalar <i>values</i> — map values and sequence items, never map keys — and the
/// document is written back with the same layout <see cref="YamlLayerMerger"/> uses. Each scalar
/// keeps its style; YamlDotNet's emitter quotes a plain scalar whose new value can't stay plain.
/// </summary>
public static class YamlSecretSubstitution
{
    public static string Substitute(string content, Func<string, string?> resolve)
    {
        if (!SecretPlaceholders.ContainsMarker(content))
            return content;

        var root = YamlLayerMerger.Load(content, "merged output");
        if (root is null)
            return content;

        var layout = YamlLayerMerger.DetectLayout(root);
        Visit(root, resolve);
        return TextLayout.Of(content).Apply(YamlLayerMerger.Save(root, layout));
    }

    private static void Visit(YamlNode node, Func<string, string?> resolve)
    {
        switch (node)
        {
            case YamlScalarNode scalar when scalar.Value is not null:
                scalar.Value = SecretPlaceholders.Replace(scalar.Value, resolve);
                break;
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children)
                    Visit(item, resolve);
                break;
            case YamlMappingNode map:
                foreach (var (_, value) in map.Children)
                    Visit(value, resolve);
                break;
        }
    }
}
