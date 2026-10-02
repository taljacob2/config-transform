using ConfigTransform.Core;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ConfigTransform.Yaml;

/// <summary>
/// Secret substitution for YAML (docs/SECRETS_DESIGN.md): <c>{{CFSECRET_…}}</c> placeholders are
/// replaced inside scalar <i>values</i> — map values and sequence items, never map keys — and the
/// document is written back with the same layout <see cref="YamlLayerMerger"/> uses. A quoted
/// scalar keeps its quoting; a plain one that a substitution changed becomes double-quoted, since a
/// value that held a placeholder is a string and must not be misread as a boolean or number.
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
                var substituted = SecretPlaceholders.Replace(scalar.Value, resolve);
                if (substituted == scalar.Value)
                    break;
                scalar.Value = substituted;
                // A scalar that held a placeholder was a string by construction, so it must stay one.
                // Left plain, `Code: N{{CFSECRET_X}}` with X=O would come out as `Code: NO` -- a
                // boolean to YAML 1.1 readers (the "Norway problem"). Quoted, it can't be misread.
                if (scalar.Style is ScalarStyle.Plain or ScalarStyle.Any)
                    scalar.Style = ScalarStyle.DoubleQuoted;
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
