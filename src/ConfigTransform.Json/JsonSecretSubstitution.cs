using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigTransform.Core;

namespace ConfigTransform.Json;

/// <summary>
/// Secret substitution for JSON (docs/SECRETS_DESIGN.md): <c>{{CFSECRET_…}}</c> placeholders are
/// replaced inside string values only — never in property names — and the document is written back
/// with the same options and layout <see cref="JsonLayerMerger"/> uses, so a value containing
/// <c>"</c> or <c>\</c> is escaped by the JSON writer instead of breaking the file. A whole-value
/// placeholder stays a JSON string.
/// </summary>
public static class JsonSecretSubstitution
{
    public static string Substitute(string content, Func<string, string?> resolve)
    {
        if (!SecretPlaceholders.ContainsMarker(content))
            return content;

        var document = Visit(JsonNode.Parse(content), resolve);
        return TextLayout.Of(content).Apply(document?.ToJsonString(JsonWriteOptions.Indented) ?? "null");
    }

    private static JsonNode? Visit(JsonNode? node, Func<string, string?> resolve)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kvp => kvp.Key).ToList())
                {
                    var current = obj[key];
                    var replaced = Visit(current, resolve);
                    if (!ReferenceEquals(replaced, current))
                        obj[key] = replaced;
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var current = array[i];
                    var replaced = Visit(current, resolve);
                    if (!ReferenceEquals(replaced, current))
                        array[i] = replaced;
                }
                return array;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                var substituted = SecretPlaceholders.Replace(text, resolve);
                return substituted == text ? value : JsonValue.Create(substituted);
            default:
                return node;
        }
    }
}
