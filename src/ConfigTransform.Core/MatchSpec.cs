namespace ConfigTransform.Core;

/// <summary>
/// One <c>--match</c>/<c>--set</c> argument, parsed per docs/FIELD_AUTHORING_DESIGN.md: splits
/// on the first <c>=</c> only (so a value containing more <c>=</c> signs, e.g.
/// <c>connectionString=Data Source=prod;User=admin</c>, is never ambiguous), or -- when there is
/// no <c>=</c> at all -- treats the whole argument as the value against
/// <paramref name="defaultAttribute"/> ("key" for --match, "value" for --set). The bare form's
/// default is only ever safe to *apply* after verifying against the real document (see
/// docs/FIELD_AUTHORING_DESIGN.md's "Defaults" section) -- this type only parses the text, it
/// does not decide whether the default was correct.
/// </summary>
public sealed record MatchSpec(string Attribute, string Value, bool WasDefaulted)
{
    public static MatchSpec Parse(string raw, string defaultAttribute)
    {
        var eq = raw.IndexOf('=');
        return eq < 0
            ? new MatchSpec(defaultAttribute, raw, WasDefaulted: true)
            : new MatchSpec(raw[..eq], raw[(eq + 1)..], WasDefaulted: false);
    }
}
