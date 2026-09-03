using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Xml.Tests;

/// <summary>
/// Direct tests of the "set" command's decision logic (docs/FIELD_AUTHORING_DESIGN.md), against
/// in-memory XML strings -- fast, and isolates the actual authoring rules from
/// XmlCliRunnerTests' end-to-end file-resolution concerns.
/// </summary>
public class XmlFieldAuthorTests
{
    private const string DotNetFrameworkBase = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <appSettings>
            <add key="ApiUrl" value="https://dev.example.com" />
          </appSettings>
          <connectionStrings>
            <add name="Prod" connectionString="Data Source=old;" providerName="System.Data.SqlClient" />
          </connectionStrings>
        </configuration>
        """;

    [Fact]
    public void Creates_a_new_overlay_with_SetAttributes_for_a_single_attribute_element()
    {
        var result = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        Assert.Contains("xmlns:xdt=\"http://schemas.microsoft.com/XML-Document-Transform\"", result);
        Assert.Contains("key=\"ApiUrl\"", result);
        Assert.Contains("value=\"https://new.example.com\"", result);
        Assert.Contains("xdt:Transform=\"SetAttributes\"", result);
        Assert.Contains("xdt:Locator=\"Match(key)\"", result);
    }

    [Fact]
    public void Supports_multiple_set_fields_on_one_element()
    {
        var result = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("name", "Prod", WasDefaulted: false)],
            setFields:
            [
                new MatchSpec("connectionString", "Data Source=new;", WasDefaulted: false),
                new MatchSpec("providerName", "System.Data.SqlClient", WasDefaulted: false)
            ]);

        Assert.Contains("name=\"Prod\"", result);
        Assert.Contains("connectionString=\"Data Source=new;\"", result);
        Assert.Contains("providerName=\"System.Data.SqlClient\"", result);
        Assert.Contains("xdt:Locator=\"Match(name)\"", result);
    }

    [Fact]
    public void Compound_match_produces_a_comma_joined_Locator()
    {
        const string baseXml = """
            <configuration>
              <roles>
                <role name="Admin" env="Production" enabled="false" />
              </roles>
            </configuration>
            """;

        var result = XmlFieldAuthor.Author(
            baseXml, existingTargetXml: null, isBaseTarget: false,
            matches:
            [
                new MatchSpec("name", "Admin", WasDefaulted: false),
                new MatchSpec("env", "Production", WasDefaulted: false)
            ],
            setFields: [new MatchSpec("enabled", "true", WasDefaulted: false)]);

        Assert.Contains("xdt:Locator=\"Match(name,env)\"", result);
        Assert.Contains("enabled=\"true\"", result);
    }

    [Fact]
    public void Reuses_the_correct_nested_parent_path_from_the_base_document()
    {
        var result = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        var appSettingsIndex = result.IndexOf("<appSettings>", StringComparison.Ordinal);
        var addIndex = result.IndexOf("<add", StringComparison.Ordinal);
        Assert.True(appSettingsIndex >= 0 && appSettingsIndex < addIndex,
            "the <add> element should be nested inside <appSettings>, matching the base document's own structure");
    }

    [Fact]
    public void Re_running_set_for_the_same_field_updates_the_existing_overlay_entry_in_place_not_a_duplicate()
    {
        var first = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://v1.example.com", WasDefaulted: false)]);

        var second = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: first, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://v2.example.com", WasDefaulted: false)]);

        Assert.Contains("https://v2.example.com", second);
        Assert.DoesNotContain("https://v1.example.com", second);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(second, "<add "));
    }

    [Fact]
    public void Base_target_edits_the_matched_element_directly_with_no_xdt_anything()
    {
        var result = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: true,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://everyone.example.com", WasDefaulted: false)]);

        Assert.Contains("value=\"https://everyone.example.com\"", result);
        Assert.DoesNotContain("xdt:", result);
        Assert.DoesNotContain("xmlns:xdt", result);
    }

    [Fact]
    public void Not_found_throws_and_names_Insert_as_not_yet_supported()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "BrandNewKey", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("No element found", ex.Message);
        Assert.Contains("does not yet support creating a brand-new element", ex.Message);
    }

    [Fact]
    public void Not_found_with_a_defaulted_bare_match_suggests_the_real_attribute_name()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Prod", WasDefaulted: true)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: true)]));

        Assert.Contains("did you mean", ex.Message);
        Assert.Contains("--match name=Prod", ex.Message);
    }

    [Fact]
    public void Not_found_with_an_explicit_match_does_not_guess_a_suggestion()
    {
        // Same scenario as the defaulted case above, but the user explicitly typed "key=Prod" --
        // docs/FIELD_AUTHORING_DESIGN.md: an explicit --match that doesn't match anything gets a
        // plain not-found message, not a guessed correction.
        var ex = Assert.Throws<InvalidOperationException>(() => XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Prod", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.DoesNotContain("did you mean", ex.Message);
    }

    [Fact]
    public void Ambiguous_match_throws_and_lists_every_candidate()
    {
        const string baseXml = """
            <configuration>
              <appSettings>
                <add key="ApiUrl" value="https://a.example.com" />
              </appSettings>
              <featureFlags>
                <add key="ApiUrl" value="unrelated-but-same-key" />
              </featureFlags>
            </configuration>
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => XmlFieldAuthor.Author(
            baseXml, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("More than one element matches", ex.Message);
        Assert.Contains("https://a.example.com", ex.Message);
        Assert.Contains("unrelated-but-same-key", ex.Message);
    }

    [Fact]
    public void GenericXml_arbitrary_schema_works_with_no_special_casing()
    {
        // Proves this isn't secretly tied to "key"/"value"/appSettings — CLAUDE.md's
        // format-generic principle, same one the GenericXml merge fixtures exist to prove.
        const string genericXml = """
            <root>
              <widgets>
                <widget code="W1" label="Old label" tier="basic" />
              </widgets>
            </root>
            """;

        var result = XmlFieldAuthor.Author(
            genericXml, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("code", "W1", WasDefaulted: false)],
            setFields: [new MatchSpec("tier", "premium", WasDefaulted: false)]);

        Assert.Contains("code=\"W1\"", result);
        Assert.Contains("tier=\"premium\"", result);
        Assert.Contains("xdt:Locator=\"Match(code)\"", result);

        var widgetsIndex = result.IndexOf("<widgets>", StringComparison.Ordinal);
        var widgetIndex = result.IndexOf("<widget ", StringComparison.Ordinal);
        Assert.True(widgetsIndex >= 0 && widgetsIndex < widgetIndex);
    }

    [Fact]
    public void Adding_to_an_existing_overlay_file_preserves_its_other_content()
    {
        var withApiUrl = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        var withBoth = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: withApiUrl, isBaseTarget: false,
            matches: [new MatchSpec("name", "Prod", WasDefaulted: false)],
            setFields: [new MatchSpec("connectionString", "Data Source=new;", WasDefaulted: false)]);

        Assert.Contains("ApiUrl", withBoth);
        Assert.Contains("https://new.example.com", withBoth);
        Assert.Contains("Data Source=new;", withBoth);
    }
}
