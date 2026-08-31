# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/). Versioning follows SemVer 2.0
— see this repo's design doc for the policy (a breaking change to the CLI's arguments or the
manifest schema requires a major version bump, or staying in `0.x` where any change may break).

## [Unreleased]

### Added

- Repository scaffold: solution structure, `ConfigTransform.Core`/`.Xml`/`.Json` project
  stubs, corresponding test projects (xUnit), `build.yml` (test on push/PR, matrix across
  Windows and Linux) and `publish.yml` (pack + push to GitHub Packages on a tagged release).
- `Manifest`/`ManifestFileEntry` data model in `ConfigTransform.Core`, matching the documented
  schema (`docs/MANIFEST_SCHEMA.md`), including automatic overlay-folder-name derivation.
- `CLAUDE.md` (repo root) as the AI-agent/fast-orientation entry point, plus `docs/INDEX.md`
  (documentation map) and `docs/DOCUMENTATION_POLICY.md` (the "document as you go, for both
  human and AI readers" policy this repo follows).
- `docs/CONFIG_MANAGEMENT.md` mirrored into this repo alongside `docs/CONFIGTRANSFORM_TOOL_DESIGN.md`
  (moved here from the repo root), so the full architectural context is available in one place.
- `docs/ROADMAP.md` as the durable, cross-session source of truth for what's planned and
  what's next, plus explicit numbered rules at the top of `CLAUDE.md` requiring it be read
  before starting work and kept updated as work progresses — see `docs/DOCUMENTATION_POLICY.md`
  rule 7.
- `FileResolver` in `ConfigTransform.Core`: case-insensitive file lookup
  (`TryResolveCaseInsensitive`, `ResolveCaseInsensitiveRequired`), with tests covering exact
  match, differently-cased match, no match, ambiguous match (Linux-only, via
  `Xunit.SkippableFact` — see the test's own comment for why), and a missing directory.
- `LayerResolution` in `ConfigTransform.Core`: resolves base + Environments + Clients for one
  (project, file, client, environment) combination and produces an explicit found/not-found
  report per layer. Missing overlays are reported but not fatal; a missing base file throws
  `FileNotFoundException`. Fully tested, including that the overlay file name's extension is
  derived from the base file's own extension.
- `CliOptionsParser`/`CliOptions` in `ConfigTransform.Core`: parses the CLI shape shared by
  both tools (`docs/USAGE.md`); `--dry-run`/`--diff` parse successfully but the front-ends
  reject them with an explicit "not yet implemented" message.
- `ManifestLoader` and `ManifestEntrySelector` in `ConfigTransform.Core`: load and validate a
  manifest file, and select the targeted file entry (by exact path or by the derived/explicit
  overlay folder name), each with clear errors for the invalid cases.
- `XmlLayerMerger` in `ConfigTransform.Xml`: real base → Environments → Clients merge via
  `Microsoft.Web.Xdt`. `ConfigTransform.Xml`'s real-run CLI path is now fully wired end to end.
- Real `DotNetFramework` fixture set (base App.config, an environment-wide `Timeout` override,
  a client-specific `ApiUrl`/connection-string override), replacing the placeholder, with tests
  covering all three layers present, no overlays present, and environment-only (no client
  override) — verifying the layering order and the "missing overlay ≠ error" rule together,
  end to end, not just each piece in isolation.

- Real `IisWebConfig` fixture set (system.web/compilation, customErrors, system.webServer
  rewrite rules, and a `<location path="Admin">`-wrapped authorization section), with tests
  confirming `Locator="Match(...)"` and `Transform="Insert"` work correctly through nested and
  location-wrapped elements, not just flat `appSettings` — and that a client-less environment
  layer leaves the location-wrapped section untouched.
- Real `GenericXml` fixture set: an arbitrary, made-up schema (`Endpoints`/`FeatureFlags`) with
  a `.xml` extension rather than `.config`, proving `XmlLayerMerger` and `LayerResolution` have
  no hidden App.config/Web.config-specific assumptions — including that the overlay file name's
  extension is genuinely derived from the base file, not hardcoded to `.config`.

- `GitDiff` in `ConfigTransform.Core`: renders a unified diff between two strings via
  `git diff --no-index` and throwaway temp files, cleaned up immediately after. Format-agnostic
  and reusable — not XML-specific — for when `ConfigTransform.Json` needs the same capability.
- `XmlCliRunner` in `ConfigTransform.Xml`: the CLI orchestration factored out of `Program.cs`
  (now a two-line wrapper) so the full flow — including `--dry-run` and `--diff` — is directly
  testable without spawning a subprocess.
- `--dry-run` and `--diff` are now fully implemented for `ConfigTransform.Xml`. `--diff`
  compares the base file rendered with no overlays against the fully merged result (through the
  identical `XmlLayerMerger` code path on both sides, avoiding spurious serialization-only
  differences) and prints `(no changes)` rather than an empty diff when neither layer overrides
  anything. Tests confirm both flags never write to disk anywhere — not just "not to the base
  file" — across a snapshot of the entire test workspace before and after.

`ConfigTransform.Json` is not yet implemented — see `docs/ROADMAP.md`.
