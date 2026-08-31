# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/). Versioning follows SemVer 2.0
— see this repo's design doc for the policy (a breaking change to the CLI's arguments or the
manifest schema requires a major version bump, or staying in `0.x` where any change may break).

## [Unreleased]

### Added

- `docs/SECRETS_AND_LOCAL_SETUP.md`: what a *consuming* repo needs configured — GitHub Packages
  feed auth (including the cross-repo `GITHUB_TOKEN` wrinkle) and, for repos that use it,
  git-crypt — in CI and locally, with explicit Windows/macOS/Linux commands throughout. This
  was previously only worked out ad hoc while setting up `config-transform-pilot`; now written
  down generically. `GETTING_STARTED.md` step 4 and `CONFIG_MANAGEMENT.md` §7.1/§10.4 now point
  to it; the latter's two dangling "§12" forward-references (to a section that was never
  written, from when this doc predated the tool actually existing) are fixed to point at the
  real docs instead.

### Changed

- `docs/SECRETS_AND_LOCAL_SETUP.md` §1 now generalizes to GitHub Enterprise Cloud with data
  residency (`*.ghe.com`) tenants, not just `github.com`. The feed URL isn't a simple hostname
  substitution — `github.com`'s `nuget.pkg.github.com` becomes `nuget.<subdomain>.ghe.com` on a
  `ghe.com` tenant, dropping the `.pkg.` segment, confirmed against GitHub's own docs rather than
  assumed. The full feed URL is now supplied via a new **repository variable**,
  `CONFIGTRANSFORM_PACKAGES_SOURCE` (a variable, not a secret — it's a URL, not sensitive), read
  by `nuget.config` through the same `%VAR%` expansion already used for the feed credentials.
  Deliberately no default value anywhere for this one: an earlier draft had a
  `vars.X || 'https://nuget.pkg.github.com/<owner>/index.json'` fallback, which would let a repo
  that forgets to set the variable silently restore from one specific hardcoded account's feed
  instead of failing loudly — removed in favor of requiring the variable always be set. Also adds
  a cross-host consumption caveat (Actions egress allowlists, PAT-must-be-minted-on-the-serving-
  host) for the case where the consuming repo and the packages-publishing repo live on different
  GitHub hosts entirely. `config-transform-pilot`'s `nuget.config` and
  `.github/workflows/build-transformed.yml` were updated to the new variable as a real (if
  same-host) exercise of the pattern.
- `docs/SECRETS_AND_LOCAL_SETUP.md` §1's CI example now sets `GITHUB_ACTOR`/`GITHUB_TOKEN`/
  `CONFIGTRANSFORM_PACKAGES_SOURCE` at the **job level**, not on a single step, and says so
  explicitly, plus a note that this applies to every workflow in a consuming repo that runs
  `dotnet build`/`dotnet restore`/`dotnet tool restore` on anything, not just the one invoking
  `config-transform`'s own CLI tools — `nuget.config` is resolved per-repo, and `dotnet build`'s
  implicit restore enumerates every configured source regardless of which workflow runs it.
  Found the hard way in `config-transform-pilot`: step-level scoping broke its
  `build-transformed.yml` (`NU1301` on a project with no dependency on the feed) and, once fixed
  there, broke its separate, previously-untouched `build.yml` the same way, since that workflow
  had never needed any of these env vars before `nuget.config` started referencing them
  repo-wide.

## [0.2.0-alpha] - 2026-08-31

### Changed

- **Breaking: manifest schema field rename.** `manifest.json`'s `project` field is now
  `directory`, and `files[].relativeToProject` is now `files[].relativeToDirectory` — with a
  real semantic fix alongside the rename, not just a relabel. `project` was never actually
  opened, parsed, or validated by the tool; it was only ever fed to `Path.GetDirectoryName()`
  to derive the real base directory, which meant every manifest had to name a fake `.csproj`
  path it didn't need. `directory` is now literally that base directory itself — no more
  fictional filename required. Existing manifests need `"project": "X/Y.csproj"` changed to
  `"directory": "X/Y"` (drop the fake filename) and `relativeToProject` renamed to
  `relativeToDirectory` throughout. This also makes explicit something that was already true of
  the old field but obscured by its name and `.csproj`-flavored docs: the manifest has no
  coupling to `.csproj`, .NET, or any specific `TargetFramework` — see `MANIFEST_SCHEMA.md`'s
  new "`directory` is not a `.csproj` reference" section and `CLAUDE.md`'s "Core concepts" for
  the full implication (this works for a Node.js/Angular/React/Flutter project's JSON config
  too, not just a `.csproj`-anchored one).
- `docs/CONFIG_MANAGEMENT.md` §11 and `docs/ROADMAP.md` updated with the solution-repo pilot's
  first-round results: several open items (feed auth mechanics, the GitHub Packages feed's
  existence, layering/partial-coverage behavior) are now confirmed by a real pilot run rather
  than only a design claim — see `config-transform-pilot`'s `FINDINGS.md` for the full writeup,
  including the real bug it found and got fixed (`[0.1.0-alpha2]` below).
- `CLAUDE.md`'s "Core concepts" gains a new bullet: neither tool has any `TargetFramework`
  coupling to the projects whose config files it resolves (a real concern raised — the actual
  multi-client repos this design targets may have projects on much older TFMs than net48).
  Confirmed via `config-transform-pilot`'s `LegacyGateway.Framework`, a deliberately vanilla
  net35 project whose App.config resolves identically to every other project's.

## [0.1.0-alpha2] - 2026-08-31

### Added

- `docs/GETTING_STARTED.md`: a task-oriented guide for using the tool without reading the full
  architecture first — the base→Environments→Clients layering as a Mermaid diagram, setting up
  a project from scratch, day-to-day field-adding recipes (same-for-everyone,
  per-environment, per-client), the XML-vs-JSON difference when adding a brand-new key, and a
  reasoned recommendation against building an `init` command yet.

### Changed

- The `init`-command deferral is now a tracked item in `docs/ROADMAP.md`'s "Later / not yet
  scheduled" (with an explicit trigger condition — repeated identical manual setups, not a
  fixed timeline), not just a passing note in `GETTING_STARTED.md`.
- `docs/DOCUMENTATION_POLICY.md` gains rule 8: fix drift you notice while already editing a
  file, not just what the immediate task required — with a worked example. `CLAUDE.md`'s rules
  gain a matching rule 6 pointing at it.

### Fixed

- `XmlLayerMerger.Merge` declared `encoding="utf-16"` in the merged XML's own prolog (a plain
  `StringWriter`'s default `Encoding`), while `CliRunner`'s real-run path (`--output`) persists
  that string to disk via `File.WriteAllText`, which defaults to UTF-8 — a declared-vs-actual
  encoding mismatch invisible to this project's own tests (`XDocument.Parse(string)` ignores
  the declared encoding entirely for an already-decoded .NET string) but rejected by any
  standards-compliant parser reading the file back from disk (caught via `config-transform-pilot`,
  a real `workflow_dispatch` run failing on `xml.etree.ElementTree.parse`: "encoding specified
  in XML declaration is incorrect"). Fixed with a `StringWriter` subclass that reports
  `Encoding.UTF8`, matching what actually lands on disk; a new regression test
  (`Merged_result_survives_a_real_disk_round_trip_through_a_strict_parser`) writes the merged
  result to a real temp file and reloads it with `XDocument.Load(path)` — unlike
  `XDocument.Parse(string)`, `.Load` honors the declared encoding, so it would have caught this
  before it ever shipped. `0.1.0-alpha` is affected; upgrade rather than working around it.

## [0.1.0-alpha] - 2026-08-31

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
- `CliRunner` in `ConfigTransform.Core`: the shared CLI orchestration extracted out of
  `XmlCliRunner` (now a one-line delegation) once `ConfigTransform.Json` made the near-total
  duplication worth eliminating — takes the format-specific merge function as a
  `Func<string, string?, string?, string>` delegate, so it knows nothing about XML or JSON.
- `JsonLayerMerger` in `ConfigTransform.Json`: real base → Environments → Clients merge via
  `Microsoft.Extensions.Configuration`'s `ConfigurationBuilder`, flattened back to a single JSON
  document. Handles two real correctness issues inherent to `IConfiguration`'s flat, string-only
  internal model, both documented in the class and pinned by tests: values are type-inferred
  (bool/integer/float/string, in that order) rather than round-tripping everything as quoted
  strings, and overlay arrays override by index rather than replacing the base array wholesale
  (any base-layer indices beyond what the overlay specifies survive untouched).
- `JsonCliRunner` in `ConfigTransform.Json`: `ConfigTransform.Json`'s real-run,`--dry-run`, and
  `--diff` CLI path, now fully wired end to end via the shared `CliRunner`.
- Real `DotNetCore` fixture set (base appsettings.json, an environment-wide `RetryCount`/log
  level override, a client-specific `ApiUrl`/feature-flag override, and an array field to
  exercise the index-override behavior), and `GenericJson` (an arbitrary schema unlike
  appsettings.json, proving no hidden assumptions) — both per
  `docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §3.2, with tests mirroring the XML suite's coverage
  (all layers, environment-only, real-run/dry-run/diff disk-write guarantees).
- `scripts/smoke-test-published-tool.sh`: installs the just-published `ConfigTransform.Xml`
  and `ConfigTransform.Json` packages from GitHub Packages (not the local build) via
  `dotnet tool install --local` and actually invokes each one — proving the published package
  works, not just that `dotnet build`/`dotnet test` passed. Run by `publish.yml` immediately
  after `dotnet nuget push`.
- `scripts/extract-changelog-section.sh` and a GitHub Release-creation step in `publish.yml`:
  each release's notes are extracted directly from this file's matching version section
  (Keep a Changelog format) rather than written a second time. See `docs/RELEASING.md` for the
  full release process this and the smoke test are part of.

`ConfigTransform.Xml` and `ConfigTransform.Json` are both fully implemented and at parity. This
is the first real release, cut specifically to validate the packaging/publish/install pipeline
end to end — see `docs/ROADMAP.md`.
