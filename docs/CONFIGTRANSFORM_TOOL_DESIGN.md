# ConfigTransform Tool Repository — Design & Test Plan

Companion document to [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) §10 (Tool distribution &
versioning). That document describes how solution repos *consume* this tool; this document
describes the tool repo's own internal structure and test strategy.

Status: this repo (`taljacob2/config-transform`) now exists, with a scaffold in place — see
[`CHANGELOG.md`](CHANGELOG.md) for exactly what's implemented so far. The detailed test matrix
below is still design-only until each fixture set is actually written.

## 1. Repository structure

```
ConfigTransform/                              (repo root)
├── ConfigTransform.sln
├── Directory.Build.props                     # shared TargetFramework, Nullable, LangVersion
├── src/
│   ├── ConfigTransform.Core/                 # shared logic, no CLI — see §2
│   │   ├── Manifest.cs
│   │   ├── FileResolver.cs                   # case-insensitive resolution, §5.4 of the spec
│   │   ├── LayerResolution.cs                # found/not-found reporting, §5.1
│   │   └── ConfigTransform.Core.csproj
│   ├── ConfigTransform.Xml/                  # CLI front-end wrapping Microsoft.Web.Xdt
│   │   ├── Program.cs
│   │   └── ConfigTransform.Xml.csproj
│   └── ConfigTransform.Json/                 # CLI front-end wrapping Microsoft.Extensions.Configuration
│       ├── Program.cs
│       └── ConfigTransform.Json.csproj
├── tests/
│   ├── ConfigTransform.Core.Tests/           # manifest parsing, resolver, reporting — format-agnostic
│   ├── ConfigTransform.Xml.Tests/
│   │   └── Fixtures/
│   │       ├── DotNetFramework/              # App.config-shaped fixtures
│   │       ├── IisWebConfig/                 # Web.config-shaped fixtures
│   │       └── GenericXml/                   # arbitrary, non-standard XML — proves no hardcoding
│   └── ConfigTransform.Json.Tests/
│       └── Fixtures/
│           ├── DotNetCore/                   # appsettings.json-shaped fixtures
│           └── GenericJson/                  # arbitrary, non-standard JSON
├── .github/
│   └── workflows/
│       ├── build.yml                         # build + test, every push/PR
│       └── publish.yml                       # dotnet pack + nuget push, on tagged release only
├── docs/
│   ├── USAGE.md                              # full CLI reference: every flag, both tools
│   ├── MANIFEST_SCHEMA.md                    # authoritative manifest.json schema reference —
│   │                                          # the tool's own input contract, kept here rather
│   │                                          # than only in a consuming repo's docs
│   └── CHANGELOG.md                          # per-version history, keyed to the SemVer policy
│                                              # in CONFIG_MANAGEMENT.md §10.8
├── CONTRIBUTING.md
└── README.md
```

## 2. Why a shared `ConfigTransform.Core`

Manifest parsing, case-insensitive file resolution, and found/not-found reporting (spec §4,
§5.1, §5.4) are identical regardless of whether the file being merged is XML or JSON — only the
actual merge engine differs (`Microsoft.Web.Xdt` vs `Microsoft.Extensions.Configuration`).
Keeping that shared logic in one library tested once, rather than duplicated (and drifting)
between `ConfigTransform.Xml` and `ConfigTransform.Json`, is the same reuse principle the rest
of this design has followed throughout — one manifest schema, one resolution rule, two thin
format-specific engines on top.

## 3. Test matrix

Core principle: the same test *behavior* (merge correctness, missing-file handling,
case-insensitivity, dry-run/diff safety) is asserted against multiple fixture sets per format —
proving the engine is genuinely generic, not secretly tied to "App.config" or "appsettings.json"
as special names. Implemented in **xUnit**, as data-driven (`[Theory]`/`[MemberData]`) tests
iterating fixture folders, not copy-pasted test methods per scenario.

### 3.1 XML — `ConfigTransform.Xml.Tests`

**DotNetFramework fixtures** — a realistic App.config shape:

```xml
<!-- Fixtures/DotNetFramework/base.config -->
<configuration>
  <appSettings>
    <add key="ApiUrl" value="https://dev.example.com" />
    <add key="Timeout" value="30" />
  </appSettings>
  <connectionStrings>
    <add name="Main" connectionString="Server=devdb;Database=App;" providerName="System.Data.SqlClient" />
  </connectionStrings>
</configuration>
```
Environment overlay changes `Timeout`; client overlay changes `ApiUrl` and the connection
string. Asserts: both layers apply in order, unrelated keys (`Timeout` when only client layer
is under test) are untouched.

**IisWebConfig fixtures** — deliberately includes Web.config-specific structures that App.config
never has, to prove the engine handles them, not just flat `appSettings`:

```xml
<!-- Fixtures/IisWebConfig/base.config -->
<configuration>
  <system.web>
    <compilation debug="true" targetFramework="4.8" />
    <customErrors mode="Off" />
  </system.web>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="HTTPS Redirect" enabled="false">
          <match url="(.*)" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
  <location path="Admin">
    <system.web>
      <authorization>
        <deny users="?" />
      </authorization>
    </system.web>
  </location>
</configuration>
```
Production overlay: `debug="false"`, `customErrors mode="RemoteOnly"`, enables the HTTPS
redirect rule. Exercises `Locator="Match(...)"` against nested and `<location>`-wrapped
elements — a real edge case flat appSettings tests never touch.

**GenericXml fixtures** — an arbitrary, made-up XML schema with no resemblance to either
App.config or Web.config, to prove there is no hidden filename- or schema-specific logic
anywhere in the resolver or the merge call. If this passes using the exact same code path as
the other two, the "format-generic" claim (spec §5.2) is actually demonstrated, not just
asserted in prose.

### 3.2 JSON — `ConfigTransform.Json.Tests`

**DotNetCore fixtures** — realistic `appsettings.json` shape with nested objects:

```json
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "ApiUrl": "https://dev.example.com",
  "Features": { "EnableX": false },
  "AllowedHosts": "*"
}
```
Includes one dedicated test for a **JSON array value** — `Microsoft.Extensions.Configuration`
does not merge arrays element-wise, it flattens them to indexed keys (`Key:0`, `Key:1`, …), so
an overlay "overriding" an array can produce a surprising result if not understood. This is a
known gotcha worth an explicit, documented test asserting the actual (if unintuitive) behavior,
rather than discovering it by surprise against a real project later.

**GenericJson fixtures** — same purpose as GenericXml: an arbitrary schema, proving no
hardcoded assumptions.

### 3.3 Cross-cutting, run against every fixture set

- **Missing overlay ≠ error; missing base = error** (spec §5.1), with the found/not-found
  report asserted in the tool's output for both cases.
- **Case-insensitive resolution** (spec §5.4): fixture pairs differing only by case
  (`App.config`/`app.config`), zero-match, and an intentionally ambiguous multi-match case,
  each asserted against the correct outcome/exception.
- **`--dry-run`/`--diff` never write to disk**: assert no file at the base path (or anywhere
  outside an explicit `--output`) is modified, across every fixture set.
- **UTF-8 BOM handling**: Visual Studio commonly saves XML/JSON with a byte-order mark — a
  realistic fixture with a BOM must round-trip correctly, not get corrupted or silently
  stripped in a way that changes the file's encoding declaration.
- **Manifest validation**: malformed JSON, a `project` path pointing at a non-existent
  `.csproj`, a `relativeToProject` pointing at a non-existent file — each producing a clear,
  specific error rather than a generic crash.

### 3.4 `ConfigTransform.Core.Tests`

Format-agnostic unit tests for the shared library in isolation — manifest parsing, the
case-insensitive resolver, and found/not-found reporting — independent of either CLI front-end,
so a regression here is caught once rather than needing to be independently rediscovered by
both the XML and JSON test suites.

### 3.5 Platform coverage

The full suite (§3.1–3.4) runs on **both Windows and Linux** CI runners — the entire
case-insensitivity design (§5.4) exists specifically because of a Windows-vs-Linux discrepancy,
so the test suite must actually exercise both, not just assert the logic looks correct on one
platform.

## 4. CI for the tool repo itself

- **`build.yml`** — push/PR: `dotnet build`, `dotnet test` (both OSes, per §3.5).
- **`publish.yml`** — triggered on a tagged release only: `dotnet pack` + `dotnet nuget push`
  to the GitHub Packages feed (spec §10.2), version taken directly from the tag, following the
  SemVer policy in spec §10.8. Tags are the bare SemVer string, **no `v` prefix** — `1.2.0`,
  `0.3.0-beta`, not `v1.2.0` — so the tag value is usable as-is wherever the version string is
  needed (the NuGet package version, `dotnet pack -p:Version=...`) without stripping a prefix
  first.

## 5. Open items

- Repo created (`taljacob2/config-transform`), scaffolded (solution, project stubs, CI
  skeletons, `Manifest` model). The fixture-set-driven test matrix in §3 is not yet
  implemented — current test coverage is scaffold-only, see `CHANGELOG.md`.
