using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigTransform.Core;

/// <summary>
/// One entry of a layer's <c>secrets</c>: a <c>*.secret.env</c> file (docs/SECRETS_DESIGN.md), or an
/// Azure Key Vault source (docs/KEYVAULT_SECRETS_DESIGN.md). Written as a plain string —
/// <c>".configtransform/…/db.secret.env"</c>, <c>"keyvault://kv-ra-prod-ca"</c>,
/// <c>"keyvault://kv-ra-prod-ca/CFSECRET-SMTP-PASSWORD"</c> — or, to give one vault secret an explicit
/// placeholder name, as <c>{ "from": "keyvault://kv/legacy-api-key", "as": "CFSECRET_LEGACY_API_KEY" }</c>.
/// <see cref="LayerManifestLoader"/> validates which combinations are allowed.
/// </summary>
[JsonConverter(typeof(SecretsEntryConverter))]
public sealed record SecretsEntry(string Source, string? As = null)
{
    public static implicit operator SecretsEntry(string source) => new(source);

    /// <summary>How the entry reads in <c>--list</c>'s header and in error messages.</summary>
    public override string ToString() => As is null ? Source : $"{Source} as {As}";
}

/// <summary>Reads and writes a <see cref="SecretsEntry"/> as a string, or as an object only when it has an <c>as</c>.</summary>
internal sealed class SecretsEntryConverter : JsonConverter<SecretsEntry>
{
    public override SecretsEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new SecretsEntry(reader.GetString()!);

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A \"secrets\" entry must be a string, or an object with \"from\" and \"as\".");

        string? from = null;
        string? @as = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();
            switch (property)
            {
                case "from" when reader.TokenType == JsonTokenType.String:
                    from = reader.GetString();
                    break;
                case "as" when reader.TokenType == JsonTokenType.String:
                    @as = reader.GetString();
                    break;
                case "from" or "as":
                    throw new JsonException($"A \"secrets\" entry's \"{property}\" must be a string.");
                default:
                    // Worded like System.Text.Json's own unmapped-member error, so LayerManifestLoader
                    // reports it the same way as any other unknown field.
                    throw new JsonException(
                        $"The JSON property '{property}' could not be mapped to any .NET member contained in a \"secrets\" entry " +
                        "(only \"from\" and \"as\" are recognized).");
            }
        }

        return new SecretsEntry(
            from ?? throw new JsonException("A \"secrets\" entry written as an object needs \"from\"."),
            @as);
    }

    public override void Write(Utf8JsonWriter writer, SecretsEntry value, JsonSerializerOptions options)
    {
        if (value.As is null)
        {
            writer.WriteStringValue(value.Source);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("from", value.Source);
        writer.WriteString("as", value.As);
        writer.WriteEndObject();
    }
}
