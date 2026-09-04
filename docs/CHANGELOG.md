# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/). Versioning follows SemVer 2.0
— see this repo's design doc for the policy (a breaking change to the CLI's arguments or the
manifest schema requires a major version bump, or staying in `0.x` where any change may break).

## [Unreleased]

## [0.7.0-alpha] - 2026-09-04

Breaking, following this repo's own precedent for a pre-1.0 breaking change (`0.2.0-alpha`'s
manifest-schema rename): a MINOR bump, not a jump to `1.0.0` — see `CONFIG_MANAGEMENT.md` §10.8,
unchanged by this release. `1.0.0` stays reserved for real-content validation, not for the size
of a breaking change; this redesign hasn't cleared that bar any more than `0.6.0-alpha` had.

### Changed

- **Breaking: `manifest.json` and `--manifest`/`--file` are gone, replaced by self-describing
  `configtransform.json` layers.** `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`'s fully-decided
  design (see the entry below) is now real code. One `configtransform.json` per layer directory
  (`.configtransform/Environments/<Env>/` and `.configtransform/Clients/<Client>/<Env>/`)
  declares an optional `extends` and a `resources[]` list, each entry pairing a project's
  repo-root-relative path with its own optional `patch` — no separate project-declaration file;
  `resources[].path` points straight at the real config file.
  `Manifest`/`ManifestLoader`/`ManifestDiscovery`/`ManifestEntrySelector`/`ManifestLister`/
  `LayerResolution` (`ConfigTransform.Core`) are deleted, replaced by `LayerManifest`/
  `LayerManifestLoader`/`LayerPathResolver`/`LayerChain`/`LayerLister`. `XmlLayerMerger`/
  `JsonLayerMerger.Merge` take an arbitrary-length ordered patch chain instead of a fixed
  base+environment+client two-slot signature; `JsonLayerMerger`'s `$elemMatch` progressive
  resolution now folds over the whole chain (verified with a genuine 3-deep chain test, not just
  the old 2-hop case).
  **CLI**: `--manifest`/`--file` are gone; `--resource <repo-root-relative path>` is the tool-wide
  targeting flag for a resolve/`--dry-run`/`--diff`/a real run/`--list`/`set`. Omitting it
  processes every resource the resolved layer touches, in that tool's own format, in one call — a
  real run then requires `--output <directory>` and writes one file per resource; a resource in
  the other tool's format is skipped with a stderr note, not an error or silent drop (true
  single-binary dispatch across formats is a separate, not-yet-started pass). `--list` shows one
  layer's resources and `extends` (or, given `--resource` instead, a tree-wide reverse lookup —
  every layer that patches one project, closing a real ergonomic gap the new tree creates).
  **`set`**: now targets a resource by its own path; creates a missing `configtransform.json` on
  first write, defaulting a Client layer's `extends` to the matching Environment layer even if
  that file doesn't exist yet (a missing `extends` target is "nothing to inherit," not an error) —
  its actual field-authoring logic is untouched. A real bug caught only by manual smoke-testing,
  not the unit suite: `set` was writing an *absolute* path into a newly-created layer's `extends`
  field instead of repo-root-relative, violating the design's own "every path is repo-root-relative,
  no exceptions" rule — fixed, with the regression coverage tightened from a loose substring check
  to exact-value assertions.
  **Migration**: every consuming repo's `.configtransform/<Project>/manifest.json` +
  `Environments/`/`Clients/` tree needs converting to the new
  `.configtransform/Environments/<Env>/configtransform.json` +
  `.configtransform/Clients/<Client>/<Env>/configtransform.json` shape (`docs/MANIFEST_SCHEMA.md`
  has the full field reference and worked example) — no coexistence period, no automated
  migration tool (no real solution repo has adopted the old schema in production yet, so there's
  no live migration to script for). Every CI/CD invocation using `--manifest`/`--file` needs
  updating to `--resource`.
  All fixture trees migrated to the new tree shape; `TempCliWorkspace` (both test projects)
  rebuilt around a synthetic repo root; `CLAUDE.md`/`MANIFEST_SCHEMA.md`/`GETTING_STARTED.md`/
  `ONBOARDING.md`/`USAGE.md`/`CONFIG_MANAGEMENT.md` §3/§4/§5.1/§9 rewritten in the same change.
  CLI unification (`ConfigTransform.Xml`/`ConfigTransform.Json` merging into one dispatcher)
  remains the one deliberately deferred piece — see `docs/ROADMAP.md`'s "Next up".

### Added

- `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`: **now fully decided** — replaces `manifest.json` and
  the fixed base→Environments→Clients rule with a Kustomize-style self-describing
  `configtransform.json` per layer directory, raised directly by the repo owner. Every design
  question it originally opened is settled: file format (JSON, not YAML — no new dependency for a
  config file this tool doesn't merge); scope (one file spans every project/format a
  client×environment touches, not just one project — accepting `ConfigTransform.Xml`/
  `ConfigTransform.Json` likely unifying into one CLI dispatcher as a first-class consequence);
  patch-to-resource matching (each `resources` entry pairs its own `path` with an optional `patch`
  field directly, requested by the repo owner over the document's own earlier filename-convention
  proposal — needed a new `extends` field to keep that unambiguous when a layer inherits from
  another multi-project layer); path convention (`extends`, `path`, and `patch` all
  repo-root-relative, uniformly — the document's first pass special-cased `patch` as a
  same-directory filename and gave `extends` a different anchor than `path`, both real
  inconsistencies caught and corrected, prioritizing one predictable rule over the repetition it
  costs); `manifest.json`'s fate (fully replaced, a clean break, no coexistence period); and
  `set`'s redesign (a new `--resource <path>` targeting flag, rules for creating/updating a
  layer's `resources` entries and patch files, while the actual field-authoring logic —
  `XmlFieldAuthor`/`JsonFieldAuthor`, `$elemMatch`, verified defaults — stays untouched). See
  `docs/ROADMAP.md`'s "Next up" for what's left, which is implementation planning, not more design.

## [0.6.0-alpha] - 2026-09-03

### Added

- **`set` support for JSON array-of-objects matching, via a `$elemMatch` overlay syntax** —
  closes the gap flagged below as "not implemented". `--match key=<array>` locates the array, one
  or more further `--match <field>=<value>` (any attribute other than `key`/`literal-key`) become
  match conditions; no match found creates a new item instead of erroring (an upsert, combining
  the conditions themselves with whatever `--set` wrote as the new item's fields); more than one
  match is a hard error listing every candidate, same posture as XML's ambiguous-element case.
  The persisted overlay never contains an array index anywhere, including in the file itself — a
  named, deliberate requirement — expressed instead as a **list** of `$elemMatch` patches under
  the array's key (a list even for one condition set, so a second `set` call against the same
  array in the same overlay file, different conditions, appends a second patch rather than
  colliding with the first; the same conditions re-run updates that patch in place). Because
  nothing in the file names a position, resolving one has to happen fresh at real merge time, not
  only when `set` writes the file — a hand-written `$elemMatch` overlay must merge correctly too,
  and layering is progressive (an Environment-layer patch resolves against the base array; a
  Client-layer patch against the base+Environment-*merged* array, mirroring how `XmlLayerMerger`
  applies the Client transform to the already-Environment-transformed document). New
  `src/ConfigTransform.Json/JsonElemMatchResolver.cs` implements the shared resolution logic (used
  by both `set`'s eager, non-authoritative set-time check and `JsonLayerMerger`'s authoritative
  merge-time resolution); `JsonLayerMerger.Merge` gained a pre-processing pass that rewrites
  `$elemMatch` patches into a real position (a `JsonObject` keyed by numeric-string index, proven
  to flatten identically to a real array element at that index — not a `JsonArray` literal, which
  can't address one index without placeholder nulls at the others that would themselves clobber
  base-layer values) before a layer reaches `Microsoft.Extensions.Configuration`, and falls back
  to the original, unmodified merge implementation whenever neither overlay layer uses
  `$elemMatch` at all. 41 new tests (`JsonElemMatchResolverTests`,
  `JsonLayerMergerElemMatchTests`/`JsonLayerMergerGenericJsonElemMatchTests`, and additions to
  `JsonFieldAuthorTests`/`JsonSetCommandCliTests`). See `docs/FIELD_AUTHORING_DESIGN.md`'s "JSON /
  YAML" section and decision log for the full design, and `docs/USAGE.md`'s `set` section for
  worked examples.

- **`set` command on `ConfigTransform.Json`**: authors a nested key's value directly — no
  XDT-style Transform/Locator concept for JSON, so both updating an existing key *and* creating a
  brand-new one are the same operation (unlike XML's `Insert` gap below). `--match key=<path>`
  (`:`-separated, matching `Microsoft.Extensions.Configuration`'s own flattening convention and
  ASP.NET Core's command-line config override syntax — not `.`, since dots commonly appear
  literally in real setting names) or `--match literal-key=<name>` for a key that itself contains
  a literal `:`. **Matching an item inside an array of objects** was not implemented at the time
  this entry was first written — discovered during implementation, not part of the original
  design: `Microsoft.Extensions.Configuration`'s JSON provider merges arrays purely by index, not
  by matching a field's value the way XDT's `Locator` does for XML, so `--match name=Prod`-style
  disambiguation (as `docs/FIELD_AUTHORING_DESIGN.md`'s original array-of-objects section
  describes) couldn't be resolved the same way. **Now closed** — see the entry above. A genuine
  nested-path-vs-literal-key collision refuses and shows both `--match key=...`/
  `--match literal-key=...` forms — reachable in practice only via a base-target write against a
  hand-edited file, since `Microsoft.Extensions.Configuration.Json` itself already refuses to load
  a file shaped that way for any Environment/Client-target write (which merges through it), making
  its own load failure the actual defense there. Implemented in
  `src/ConfigTransform.Json/JsonFieldAuthor.cs`; target-file resolution shared with XML via a new
  `src/ConfigTransform.Core/SetTargetResolver.cs` (extracted from `XmlCliRunner`'s original inline
  version, refactor-only, no behavior change). See `docs/USAGE.md`'s `set` section for the full
  reference and worked examples.

- **`set` command on `ConfigTransform.Xml`**: authors an overlay field's
  `xdt:Transform="SetAttributes"` — or edits the base file directly — by checking the real,
  resolved document instead of it being hand-written, per `docs/FIELD_AUTHORING_DESIGN.md`.
  `--match <attr>=<value>` (repeatable, identifies the target; bare `<value>` defaults to
  `key=<value>`) and `--set <attr>=<value>` (repeatable, the field(s) written; bare `<value>`
  defaults to `value=<value>`) — both defaults only ever applied after verifying against the
  document, never guessed on brand-new content. Covers updating a field that already exists
  anywhere in the resolved document (the common case — overriding an existing value for one
  environment/client, or the shared default in the base file); creating a genuinely new element
  (`Insert`) is **not yet implemented** — there's nothing in an empty document to derive its
  parent location from, and `set` refuses rather than guessing, with a suggested
  `--match <realattr>=<value>` when a bare `--match` found the value under a different attribute
  instead of guessing wrong. A real write auto-prints the effective `--diff` afterward.
  Implemented in `src/ConfigTransform.Xml/XmlFieldAuthor.cs` (the matching/authoring logic) and
  `src/ConfigTransform.Xml/XmlCliRunner.cs` (orchestration); shared `--match`/`--set` argument
  parsing (`MatchSpec`) lives in `ConfigTransform.Core`, also reused by JSON's `set` above. See
  `docs/USAGE.md`'s `set` section for the full flag reference and worked examples.

- `docs/FIELD_AUTHORING_DESIGN.md`: a completed design (not yet implemented) for a `set` command
  that authors an overlay field's `SetAttributes`/`Insert`/base-edit operation mechanically
  instead of by hand, removing the silent-failure risk of a hand-picked `Locator` matching
  nothing. Covers the `--match`/`--set` model (repeatable, same shape across XML/JSON/YAML/
  `.env`), per-format matching rules, verified-vs-unverifiable default handling, and the single
  "verify against the real document, refuse only when creating something brand new" rule that
  resolved every ambiguity case raised during design. A deliberate, named exception to the
  `init`/TUI/GUI validation gate in `docs/ROADMAP.md` — see that document's own reasoning for
  why this specific piece doesn't need real-content validation to design correctly.

## [0.5.0-alpha] - 2026-09-02

### Added

- **Manifest auto-discovery and short flag aliases**, on both `ConfigTransform.Xml` and
  `ConfigTransform.Json`: `--manifest`/`-m` is now optional — when omitted, `ManifestDiscovery`
  (new, in `ConfigTransform.Core`) looks for exactly one `.configtransform/*/manifest.json`
  under the current directory and uses it, failing with an actionable error naming every
  candidate it found (or that none exist) rather than ever guessing between more than one. Every
  value-taking flag also gained a short alias — `-m`/`-f`/`-c`/`-e`/`-o` for
  `--manifest`/`--file`/`--client`/`--environment`/`--output` — for less typing on an
  interactive command; the long forms are unchanged and still what CI should keep using for a
  readable pipeline log. Prompted directly by a product-brainstorming session on the tool's
  accessibility: the ergonomics gap wasn't the base→Environments→Clients model, which is simple
  to explain, but that every invocation demanded five fully-spelled flags with no defaults. This
  is the first of that session's non-breaking, no-guessing-added ideas (a repo-local wrapper
  script naming its own manifest path is the other, left to individual solution repos rather
  than built here). `CliRunner.Run` (and both front-ends' `Run`) also gained an optional
  `workingDirectory` parameter (defaulting to the real process CWD) purely as a test seam for
  auto-discovery, with no effect on `Program.cs`'s existing call sites.
- `docs/ONBOARDING.md`: a strict, linear, copy-paste checklist for a developer joining a repo
  that already uses `config-transform` — install prerequisites, get a PAT, set env vars, unlock
  git-crypt, restore the tool, run a first `--list`/`--diff`. Complements
  `SECRETS_AND_LOCAL_SETUP.md`'s comprehensive reference (which explains every edge case and the
  *why*) with a fast path for developers who just want a working setup with no time to learn the
  tool first. The troubleshooting table is pulled directly from real incidents hit during this
  project's own pilot testing, not hypothetical ones: the locked-manifest error (`[0.3.0-alpha]`
  above), the base64-vs-raw git-crypt key mixup (also this changelog, `[0.4.0-alpha]`), the
  wrong-working-directory `Manifest not found` error (`config-transform-pilot`'s `FINDINGS.md`),
  and `NU1301` from env vars not set in the current shell/CI step
  (`SECRETS_AND_LOCAL_SETUP.md` §1).

## [0.4.1] - 2026-09-01

Published directly by the repo owner as a test of the release/publish pipeline, not through
`docs/RELEASING.md`'s documented process (this section is backfilled after the fact, which is
why it wasn't already here). Points at the same commit as `0.4.0-alpha`'s immediate docs
follow-up (`b4087d7`, "Add docs/ONBOARDING.md") — so its actual `ConfigTransform.Xml`/
`ConfigTransform.Json` code is identical to `0.4.0-alpha`'s. No functional changes. Also the
first release in this repo's history published as non-prerelease (no `-alpha`/`-beta` suffix) —
not a deliberate graduation out of pre-release status; see `docs/CONFIG_MANAGEMENT.md` §10.8 for
why `0.5.0-alpha` kept the suffix instead. Not recommended for use — pin to `0.5.0-alpha` or
later.

## [0.4.0-alpha] - 2026-08-31

### Added

- **`--list`**, a new flag on both `ConfigTransform.Xml` and `ConfigTransform.Json`: prints a
  manifest's file entries and, for each, which `Environments`/`Clients` overlays actually exist
  on disk — pure introspection, needing only `--manifest` (`--file` optionally narrows to one
  entry; omitted, every entry is listed, unlike a real run where an ambiguous manifest without
  `--file` is an error). Implemented once in `ConfigTransform.Core` (`ManifestLister`) and shared
  by both tools, same as the rest of `CliRunner`. Addresses "I have to remember/browse the
  manifest schema to know what clients or environments exist" — the cheapest of the friendlier-
  UX ideas raised (`ROADMAP.md`'s deferred TUI/GUI entry references this directly) before
  reaching for anything bigger.
- `docs/USAGE.md`: a second `ConfigTransform.Json` example showing `--file` dropped entirely for
  `ProjectB.Core` (a single-file manifest), alongside the existing examples — those correctly
  keep `--file App.config` since `ProjectA.Framework` is `MANIFEST_SCHEMA.md`'s own multi-file
  example (`App.config` + `NLog.config`), where `--file` is genuinely required.
- `docs/SECRETS_AND_LOCAL_SETUP.md` §2: warns explicitly against saving the base64-encoded git-crypt
  key as the local keyfile instead of decoding it back to binary first — the two forms are easy to
  mix up, and doing so fails `git-crypt unlock` with `not a valid git-crypt key file` rather than
  anything that names the actual mistake. Hit for real while setting up `config-transform-pilot`.

## [0.3.0-alpha] - 2026-08-31

### Fixed

- `ManifestLoader.Load` now detects a still-git-crypt-locked manifest by its magic header (NUL +
  `GITCRYPT` + NUL) and raises an actionable error naming the fix (`git-crypt unlock`, and where
  to find the key) instead of the confusing raw JSON parse failure
  (`'0x00' is an invalid start of a value`) a locked file's ciphertext used to produce. Found via
  `config-transform-pilot`'s real local-dev usage — running the CLI against a manifest under a
  `.configtransform/**` tree that hadn't been unlocked yet gave that opaque error with no hint
  the file was actually encrypted, not malformed.

### Added

- `docs/MANIFEST_SCHEMA.md`: documents pointing a manifest's `directory` at the repo root itself
  (`"."`) for a config file that lives at the top level of a repo rather than inside a project
  subfolder — no schema change, since `directory` was already just a plain relative path; this
  writes down that `"."` is a valid, ordinary value for it, notes the working-directory-relative
  caveat that makes `"."` mean "repo root" specifically, and clarifies that git-crypt's
  `.configtransform/**` encryption scope (`CONFIG_MANAGEMENT.md` §7.1) is unconditional and
  independent of what `directory` resolves to — a root-pointing manifest's overlays are
  encrypted automatically like any other project's.

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
