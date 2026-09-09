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
    public void Tag_only_match_targets_a_singleton_element_with_no_identifying_attribute()
    {
        const string baseXml = """
            <configuration>
              <system.web>
                <customErrors mode="Off" />
              </system.web>
            </configuration>
            """;

        var result = XmlFieldAuthor.Author(
            baseXml, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("tag", "customErrors", WasDefaulted: false)],
            setFields: [new MatchSpec("mode", "RemoteOnly", WasDefaulted: false)]);

        Assert.Contains("xdt:Transform=\"SetAttributes\"", result);
        Assert.Contains("mode=\"RemoteOnly\"", result);
        // Real XDT's own default-match behavior for a singleton element: no Locator at all.
        Assert.DoesNotContain("xdt:Locator", result);
        // "tag" is a coordinate, never a real attribute -- must never be written to the overlay.
        Assert.DoesNotContain("tag=", result);
    }

    [Fact]
    public void Tag_only_match_re_run_updates_the_existing_overlay_entry_in_place()
    {
        const string baseXml = """
            <configuration>
              <system.web>
                <customErrors mode="Off" />
              </system.web>
            </configuration>
            """;

        var first = XmlFieldAuthor.Author(
            baseXml, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("tag", "customErrors", WasDefaulted: false)],
            setFields: [new MatchSpec("mode", "RemoteOnly", WasDefaulted: false)]);

        var second = XmlFieldAuthor.Author(
            baseXml, existingTargetXml: first, isBaseTarget: false,
            matches: [new MatchSpec("tag", "customErrors", WasDefaulted: false)],
            setFields: [new MatchSpec("mode", "On", WasDefaulted: false)]);

        Assert.Contains("mode=\"On\"", second);
        Assert.DoesNotContain("RemoteOnly", second);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(second, "<customErrors"));
    }

    [Fact]
    public void Tag_combined_with_an_attribute_match_still_produces_a_Locator_for_the_attribute_only()
    {
        var result = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches:
            [
                new MatchSpec("tag", "add", WasDefaulted: false),
                new MatchSpec("key", "ApiUrl", WasDefaulted: false)
            ],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        Assert.Contains("xdt:Locator=\"Match(key)\"", result);
        Assert.DoesNotContain("tag=", result);
    }

    [Fact]
    public void Tag_only_match_is_ambiguous_when_more_than_one_sibling_shares_the_tag()
    {
        const string baseXml = """
            <configuration>
              <appSettings>
                <add key="ApiUrl" value="https://a.example.com" />
                <add key="OtherKey" value="unrelated" />
              </appSettings>
            </configuration>
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => XmlFieldAuthor.Author(
            baseXml, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("tag", "add", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("More than one element matches", ex.Message);
    }

    [Fact]
    public void A_single_attribute_match_is_ambiguous_among_repeated_siblings_sharing_it()
    {
        // Two real siblings sharing one attribute value but differing on another -- the actual
        // "array of objects" shape this repo's own docs describe (docs/ROADMAP.md's "Finish
        // set"), unlike Ambiguous_match_throws_and_lists_every_candidate above, which uses two
        // *unrelated* elements in different parents that happen to share a key.
        const string baseXml = """
            <configuration>
              <system.webServer>
                <rewrite>
                  <rules>
                    <rule name="Redirect" enabled="false" stopProcessing="true" />
                    <rule name="Redirect" enabled="false" stopProcessing="false" />
                  </rules>
                </rewrite>
              </system.webServer>
            </configuration>
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => XmlFieldAuthor.Author(
            baseXml, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("name", "Redirect", WasDefaulted: false)],
            setFields: [new MatchSpec("enabled", "true", WasDefaulted: false)]));

        Assert.Contains("More than one element matches", ex.Message);
        Assert.Contains("stopProcessing=\"true\"", ex.Message);
        Assert.Contains("stopProcessing=\"false\"", ex.Message);
    }

    [Fact]
    public void A_compound_match_disambiguates_one_item_among_repeated_siblings()
    {
        const string baseXml = """
            <configuration>
              <system.webServer>
                <rewrite>
                  <rules>
                    <rule name="Redirect" enabled="false" stopProcessing="true" />
                    <rule name="Redirect" enabled="false" stopProcessing="false" />
                  </rules>
                </rewrite>
              </system.webServer>
            </configuration>
            """;

        var result = XmlFieldAuthor.Author(
            baseXml, existingTargetXml: null, isBaseTarget: false,
            matches:
            [
                new MatchSpec("name", "Redirect", WasDefaulted: false),
                new MatchSpec("stopProcessing", "true", WasDefaulted: false)
            ],
            setFields: [new MatchSpec("enabled", "true", WasDefaulted: false)]);

        Assert.Contains("xdt:Locator=\"Match(name,stopProcessing)\"", result);
        Assert.Contains("stopProcessing=\"true\"", result);
        Assert.Contains("enabled=\"true\"", result);
        // The overlay carries only the one matched rule's identity, not both siblings'.
        Assert.DoesNotContain("stopProcessing=\"false\"", result);
    }

    [Fact]
    public void Re_running_a_compound_match_among_repeated_siblings_updates_the_same_item_in_place()
    {
        const string baseXml = """
            <configuration>
              <system.webServer>
                <rewrite>
                  <rules>
                    <rule name="Redirect" enabled="false" stopProcessing="true" />
                    <rule name="Redirect" enabled="false" stopProcessing="false" />
                  </rules>
                </rewrite>
              </system.webServer>
            </configuration>
            """;

        var matches = new MatchSpec[]
        {
            new("name", "Redirect", WasDefaulted: false),
            new("stopProcessing", "true", WasDefaulted: false)
        };

        var first = XmlFieldAuthor.Author(
            baseXml, existingTargetXml: null, isBaseTarget: false,
            matches: matches, setFields: [new MatchSpec("enabled", "true", WasDefaulted: false)]);

        var second = XmlFieldAuthor.Author(
            baseXml, existingTargetXml: first, isBaseTarget: false,
            matches: matches, setFields: [new MatchSpec("enabled", "false", WasDefaulted: false)]);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(second, "<rule "));
        Assert.Contains("enabled=\"false\"", second);
    }

    [Fact]
    public void Not_found_without_parent_mentions_match_parent_as_the_way_to_enable_Insert()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "BrandNewKey", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("--match parent=", ex.Message);
    }

    [Fact]
    public void Insert_with_parent_but_no_tag_coordinate_refuses_with_a_clear_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: [new MatchSpec("parent", "appSettings", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("--match tag=<NewElementName>", ex.Message);
        Assert.Contains("parent=appSettings", ex.Message);
    }

    [Fact]
    public void Insert_into_an_existing_parent_container_writes_no_Locator_and_only_the_new_element_gets_Insert()
    {
        // Uses a tag name ("customSetting") that doesn't collide with any real element elsewhere
        // in DotNetFrameworkBase (which has two <add> elements in different containers), so a
        // bare parent+tag match genuinely finds zero real candidates -- no Locator needed.
        var result = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches:
            [
                new MatchSpec("parent", "appSettings", WasDefaulted: false),
                new MatchSpec("tag", "customSetting", WasDefaulted: false)
            ],
            setFields:
            [
                new MatchSpec("key", "NewKey", WasDefaulted: false),
                new MatchSpec("value", "new-value", WasDefaulted: false)
            ]);

        Assert.Contains("xdt:Transform=\"Insert\"", result);
        Assert.DoesNotContain("xdt:Locator", result);
        Assert.Contains("key=\"NewKey\"", result);
        Assert.Contains("value=\"new-value\"", result);

        var appSettingsIndex = result.IndexOf("<appSettings>", StringComparison.Ordinal);
        var newElementIndex = result.IndexOf("key=\"NewKey\"", StringComparison.Ordinal);
        Assert.True(appSettingsIndex >= 0 && appSettingsIndex < newElementIndex,
            "the new <customSetting> element should be nested inside <appSettings>, matching the base document's real structure");
    }

    [Fact]
    public void Insert_into_a_parent_container_that_does_not_exist_yet_marks_only_the_shallowest_new_ancestor()
    {
        // Verified empirically against real Microsoft.Web.Xdt before writing this: Insert on the
        // shallowest missing ancestor copies its whole subtree (attributes and descendants) as a
        // unit -- everything nested inside it, including the new leaf element itself, must carry
        // no xdt:Transform of its own.
        var result = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches:
            [
                new MatchSpec("parent", "system.webServer/rewrite/rules", WasDefaulted: false),
                new MatchSpec("tag", "rule", WasDefaulted: false)
            ],
            setFields:
            [
                new MatchSpec("name", "NewRule", WasDefaulted: false),
                new MatchSpec("enabled", "true", WasDefaulted: false)
            ]);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, "xdt:Transform=\"Insert\""));
        Assert.Contains("<system.webServer xdt:Transform=\"Insert\">", result);
        Assert.Contains("<rewrite>", result);
        Assert.Contains("<rules>", result);
        Assert.Contains("name=\"NewRule\"", result);
        Assert.Contains("enabled=\"true\"", result);
    }

    [Fact]
    public void Re_running_the_same_Insert_call_updates_the_previously_inserted_element_in_place()
    {
        var matches = new MatchSpec[]
        {
            new("parent", "appSettings", WasDefaulted: false),
            new("tag", "customSetting", WasDefaulted: false)
        };

        var first = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches: matches,
            setFields:
            [
                new MatchSpec("key", "NewKey", WasDefaulted: false),
                new MatchSpec("value", "v1", WasDefaulted: false)
            ]);

        var second = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: first, isBaseTarget: false,
            matches: matches,
            setFields:
            [
                new MatchSpec("key", "NewKey", WasDefaulted: false),
                new MatchSpec("value", "v2", WasDefaulted: false)
            ]);

        Assert.Contains("value=\"v2\"", second);
        Assert.DoesNotContain("v1", second);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(second, "<customSetting "));
    }

    [Fact]
    public void A_real_matching_element_always_wins_over_parent_even_when_parent_is_also_given()
    {
        // A real match should never be second-guessed by a --match parent= hint -- Insert is only
        // ever consulted when nothing real matches at all.
        var result = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: false,
            matches:
            [
                new MatchSpec("parent", "appSettings", WasDefaulted: false),
                new MatchSpec("key", "ApiUrl", WasDefaulted: false)
            ],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        Assert.Contains("xdt:Transform=\"SetAttributes\"", result);
        Assert.DoesNotContain("xdt:Transform=\"Insert\"", result);
        Assert.Contains("xdt:Locator=\"Match(key)\"", result);
    }

    [Fact]
    public void Base_target_Insert_edits_the_real_document_directly_with_no_xdt_anything()
    {
        var result = XmlFieldAuthor.Author(
            DotNetFrameworkBase, existingTargetXml: null, isBaseTarget: true,
            matches:
            [
                new MatchSpec("parent", "appSettings", WasDefaulted: false),
                new MatchSpec("tag", "customSetting", WasDefaulted: false)
            ],
            setFields:
            [
                new MatchSpec("key", "NewKey", WasDefaulted: false),
                new MatchSpec("value", "new-value", WasDefaulted: false)
            ]);

        Assert.Contains("key=\"NewKey\"", result);
        Assert.Contains("value=\"new-value\"", result);
        Assert.DoesNotContain("xdt:", result);
        Assert.DoesNotContain("xmlns:xdt", result);
        // The original ApiUrl entry survives -- this is a real edit of the base document, not a
        // wholesale replacement.
        Assert.Contains("ApiUrl", result);
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
