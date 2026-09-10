# CLAUDE.md

Orientation for any AI agent (or human moving fast) working in this repo. Kept short and
high-signal on purpose — for depth, follow the links into `docs/`, starting with
[`docs/INDEX.md`](docs/INDEX.md). If the question is just "how do I use this tool," skip
straight to [`docs/GETTING_STARTED.md`](docs/GETTING_STARTED.md) instead of reading further
here.

## Rules — read first, every session

1. **Read [`docs/ROADMAP.md`](docs/ROADMAP.md) before deciding what to do.** It's the single
   source of truth for what's planned and what's next — your conversation context does not
   persist across sessions, but this file does. Don't reconstruct a plan from guesswork or
   from a conversation you can't see; read it here.
2. **Update `docs/ROADMAP.md` in the same change as any work that completes a slice, shifts
   priority, or adds a new one** — before ending a session in which progress was made. An
   out-of-date roadmap actively misleads the next session; treat keeping it current as part of
   the work, not cleanup.
3. **Document as you go, in the same change as the code** — new design decisions, non-obvious
   constraints, rejected alternatives worth remembering go into `docs/`, not a follow-up.
   `docs/DOCUMENTATION_POLICY.md` has the full rationale and rules; this file and the roadmap
   both follow it.
4. **Add a `docs/CHANGELOG.md` entry for merged changes.**
5. **If it isn't written down in this repo, treat it as lost.** No session should assume
   another session's unwritten context will still be available.
6. **Fix drift you notice, not just what the task required.** When you're editing a file for
   one reason, check the rest of it for staleness — a claim that's no longer true, a status
   that's outdated — and fix that too, in the same change. Don't leave known-stale content
   behind because it wasn't the reason you opened the file. See
   `docs/DOCUMENTATION_POLICY.md` rule 8 for why this matters more than it might seem.

## What this is

`config-transform` resolves per-client, per-environment configuration overrides for .NET
projects — App.config, Web.config, appsettings.json, `.env`, YAML, and eventually other
formats — by layering **base → Environments → Clients** overlays through self-describing `configtransform.json`
layers, each declaring what it extends and which resources it patches. It's one piece of a larger
architecture; the full "why" lives in
[`docs/CONFIG_MANAGEMENT.md`](docs/CONFIG_MANAGEMENT.md) — read that before assuming something
here is accidental rather than deliberate.

## Core concepts — read before touching code

- **Self-describing layers** ([`docs/MANIFEST_SCHEMA.md`](docs/MANIFEST_SCHEMA.md), implemented in
  `src/ConfigTransform.Core/LayerManifest.cs`/`LayerChain.cs`): one `configtransform.json` per
  layer directory under `.configtransform/` — `.configtransform/Environments/<Env>/` and
  `.configtransform/Clients/<Client>/<Env>/` — not one manifest per project. Each declares an
  optional `extends` (the layer it inherits from) and a `resources[]` list, each entry pairing a
  project's real, repo-root-relative `path` with its own optional `patch`. There is no separate
  project-declaration file — `resources[].path` points straight at the real config file. Don't
  add code that assumes a `.csproj` or any particular directory layout — `path` is genuinely just
  a path; the tool never opens or validates anything about the project it belongs to, only
  resolves it against the repo root (`LayerChain`).
- **Layering via `extends`, not a fixed rule**: an Environment layer has no `extends`; a Client
  layer typically `extends` the matching Environment layer, but this is a declared reference in
  the file itself (`LayerChain.Build` walks it), not a hardcoded base→Environments→Clients rule
  baked into the tool the way it used to be (see `docs/CONFIG_MANAGEMENT.md` §9 and
  `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md` for that history and the full design). A chain can be
  deeper than the traditional two hops — `LayerChain`/the merge engines never assume a fixed
  depth.
- **Format-generic by design.** `ConfigTransform.Xml` (via `Microsoft.Web.Xdt`) treats
  App.config, Web.config, NLog.config, or any other XML file identically — there is no
  App.config-specific logic anywhere in it. `ConfigTransform.Json` is the same for JSON via
  `Microsoft.Extensions.Configuration`, `ConfigTransform.Env` the same for flat `KEY=VALUE`
  `.env` files with no NuGet dependency at all, and `ConfigTransform.Yaml` the same for YAML via
  `NetEscapades.Configuration.Yaml`/`YamlDotNet` (same architecture as JSON, no code shared with
  it — see Repo structure below). This is proven, not just claimed: the `GenericXml`,
  `GenericJson`, `GenericEnv`, and `GenericYaml` test fixtures use arbitrary, made-up
  schemas/key-names specifically to catch any accidental special-casing. Don't add logic that
  assumes a specific filename or schema.
- **No coupling to any language, ecosystem, or `TargetFramework`.** All four engines are plain
  `net8.0` libraries operating on config files purely as XML/JSON/`.env`/YAML content — they never
  compile against, reference, or otherwise depend on the project the config file belongs to.
  A `.NET` project on net35, net40, net45, or net472 works exactly the same as one on net48 or
  net8.0 (confirmed via `config-transform-pilot`'s `LegacyGateway.Framework`, a deliberately
  vanilla net35 project) — and the same is true for a Node.js, Angular, React, or Flutter
  project's own JSON, `.env`, or YAML config file, since `resources[].path` is just a path (see
  the Self-describing layers bullet above). The only real constraint is the config file's
  *format*: XML, JSON, `.env`, or YAML today, not the ecosystem or TFM it happens to live in.
  Don't add anything here that assumes a specific TFM, language, or the consuming project's own
  SDK/build tooling.
- **Case-insensitive file resolution** (`FileResolver`, in Core). Exists because CI
  runners are typically Linux (case-sensitive) while local dev is typically Windows
  (case-insensitive) — a hazard that can pass locally and fail silently or loudly in CI. Full
  incident this prevents: `docs/CONFIG_MANAGEMENT.md` §5.4.
- **Missing overlay ≠ error; missing base file = error.** A client with no override for some
  environment is the normal case, not a bug — don't "fix" a missing-overlay path into an
  error. See `docs/CONFIG_MANAGEMENT.md` §5.1 for the reporting rule that goes with this
  (found/not-found is always reported, but absence at the overlay layer is never fatal).

## Repo structure — where to look

- `src/ConfigTransform.Core/` — shared, format-agnostic logic (`configtransform.json` parsing,
  `extends`-chain resolution, file resolution, layer-resolution reporting, `set` orchestration,
  and `FormatEngine`/`FormatEngineRegistry` dispatch). Change here first for anything that should
  behave identically across all formats.
- `src/ConfigTransform.Xml/`, `src/ConfigTransform.Json/`, `src/ConfigTransform.Env/`,
  `src/ConfigTransform.Yaml/` — internal merge-engine libraries, one per format
  (`XmlLayerMerger`/`XmlFieldAuthor`, `JsonLayerMerger`/`JsonFieldAuthor`,
  `EnvLayerMerger`/`EnvFieldAuthor`, `YamlLayerMerger`/`YamlFieldAuthor`), not their own dotnet
  tools, and never sharing code with each other even where conceptually similar (YAML and JSON
  share an architecture, not an implementation).
- `src/ConfigTransform.Cli/` — the actual CLI, packaged as the `configtransform` dotnet tool.
  Registers all four format engines above into Core's dispatcher; this is genuinely all it does.
- `tests/*/Fixtures/` — real-shaped fixture files per scenario: `DotNetFramework`,
  `IisWebConfig`, `GenericXml` (XML); `DotNetCore`, `GenericJson` (JSON); `GenericEnv` (`.env`);
  `DotNetCore`, `GenericYaml` (YAML). New merge-behavior test cases belong here as fixtures,
  exercised by data-driven tests — not as inline strings duplicated per test method. Full test
  matrix: `docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §3.
- `docs/` — see [`docs/INDEX.md`](docs/INDEX.md) for the full map.
  `docs/CONFIG_MANAGEMENT.md` carries the "why" behind almost every non-obvious decision in
  this codebase.

## Technical conventions

- Every merge-behavior change needs fixture-backed tests across every applicable scenario
  category, not just one (`docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §3).
- Full SemVer, tags with **no `v` prefix** (`1.2.0`, not `v1.2.0`) —
  `docs/CONFIG_MANAGEMENT.md` §10.8.

## Status and what's next

See [`docs/ROADMAP.md`](docs/ROADMAP.md) for the current plan (this is the doc rule #1 above
points at) and [`docs/CHANGELOG.md`](docs/CHANGELOG.md) for exactly what's implemented so far.
Short version as of the last update here: `manifest.json` and the fixed base→Environments→
Clients rule are gone — replaced by self-describing `configtransform.json` layers
(`docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`, fully implemented). The CLI is a single unified tool,
`configtransform` (`ConfigTransform.Cli`) — `ConfigTransform.Xml`/`ConfigTransform.Json` are now
internal merge-engine libraries, not separate dotnet tools; every already-published version of
those two tools stays installable forever, but neither gets a new one. `--resource` is the
tool-wide targeting flag; omitting it processes every resource a layer touches, across every
registered format, in one call — a mixed XML/JSON layer resolves with no skipping at all; a
resource whose extension no format engine handles is reported on stderr and skipped, never
silently dropped. `--list` shows one layer's resources (or, given `--resource` instead, a
tree-wide reverse lookup). No arguments at all (or `help`/`--help`/`-h` anytime) prints a
tldr-style help page — common commands plus an easy and an advanced example each — via the new
`HelpPrinter`.

`set` (docs/FIELD_AUTHORING_DESIGN.md) targets a resource by its own repo-root-relative path,
dispatching to the right engine by that path's extension, and creates a missing
`configtransform.json` layer (with the right `extends`) on first write: XML authors an overlay
field's `SetAttributes` operation mechanically (updating an existing key/attribute; creating a
brand-new one, `Insert`, is not implemented), JSON writes a nested key directly (covers both
updating and creating) and also matches or creates an item inside an array of objects via a
`$elemMatch` overlay syntax (`JsonElemMatchResolver`) — the one real design gap found during
implementation, for JSON, is closed; XML's own array-of-objects matching remains open, alongside
`Insert`. `0.8.0-alpha` is tagged and published, and `config-transform-pilot` has been migrated
onto it (unified `configtransform` command, golden-output-verified against a real pre-migration
baseline — see that repo's `FINDINGS.md` and `config-transform-pilot#2`, merged). A new `init`
command (`docs/INIT_COMMAND_DESIGN.md`) scaffolds a `.configtransform/` tree directly — an
interactive form (no TUI), a flag-driven quiet mode safe for CI, and a bare `init --template`
starter tree that's immediately runnable (every layer's demo resource names itself in its
override). Along the way, fixed a real patch-filename stutter shared with `set`
(`PatchFileNaming`), and — reported independently by a real user against the published tool —
fixed `--client`/`--environment` to be optional everywhere, uniformly (`--client` requires
`--environment`; neither is otherwise required), matching what `--list`/`set` already allowed and
what the underlying engine already supported. `--list` and the single-resource resolution report
(printed before every `--dry-run`/`--diff`/real run) were also reworked for readability, reported
by a real user against the published tool: both now show the resolved chain in real application
order (`base` first, then every layer outermost-first, connected by `↓`) with uniform `patched
in`/`not patched in` wording, the resolution report's paths are always repo-relative, and a blank
line separates that report from the merged content/diff that follows. The `--client` fix and
`init` command are tagged as `0.9.0-alpha`; the readability rework is `0.11.0-alpha` (`0.10.0-alpha`
is a wasted duplicate tag of `0.9.0-alpha` — see `docs/ROADMAP.md`'s "Current state" for the full
drift note), tagged and published, with `config-transform-pilot` re-pinned to it. Two more
real-user-reported usability fixes have since landed, versioned as `0.12.0-alpha` and published —
but tagged against the #18 merge commit before the CHANGELOG-versioning PR (#19) had merged, so
the GitHub Release has an empty body (package itself is real and correct; see
`docs/CHANGELOG.md`'s `[0.12.0-alpha]` entry for the drift note and manual fix): bare `help` now
short-circuits from any argument position, not just as the very first argument (matching
`--help`/`-h`, which already did); every CLI validation error now ends with a one-line `Try:`
example specific to that mistake, since `dotnet tool run configtransform ... --help` never
actually reaches `configtransform` (`dotnet tool run` intercepts it as its own option — see
`docs/USAGE.md`'s "Getting help" section for the `--` workaround); and an unrecognized flag close
to a known one (edit distance ≤2) now gets a specific `Try: did you mean --output?` instead of
the generic hint, via a small hand-maintained Levenshtein-distance check against the flags the
switch recognizes. `config-transform-pilot` is re-pinned to `0.12.0-alpha`. A further real-user
report — `init`'s scan surfacing `.config/dotnet-tools.json`/`nuget.config` as candidate
resources — is fixed too: `InitScanner` now excludes both by exact filename, a narrow named
exception alongside its existing directory excludes (see `docs/INIT_COMMAND_DESIGN.md`'s
"Scanning: directory filters, not content filters" for why this doesn't reopen the broader
no-filename-heuristics rule). A third: omitting `--resource` treats `--output` as a directory, so
an existing file at that path (most naturally, a layer whose only resource shares its exact name)
used to fail with a raw, OS-worded `IOException`; `RunEveryResource` now checks up front and
fails with a real error plus a `Try: add --resource ...` hint — deliberately not an
auto-detect-the-single-resource shortcut, since that would make behavior depend on how many
resources happen to be in the layer right now. A fourth: `--diff`'s output no longer leaks git's
own file-identity header lines (`diff --git a/... b/...`, `index ...`, `--- a/...`, `+++ b/...`)
that named the underlying OS temp files `GitDiff` diffs against — meaningless given the CLI
already shows the real resource path above the diff; `GitDiff.Render` strips exactly those 4
lines now. All three are versioned as `0.13.0-alpha` (CHANGELOG moved out of `[Unreleased]` before
tagging this time, per `docs/RELEASING.md` step 1 — the `0.12.0-alpha` empty-release-notes drift
above is exactly the mistake this avoids), tagged, pushed, and published clean — real, complete
GitHub Release notes this time, no manual patching needed — and `config-transform-pilot` is
re-pinned to `0.13.0-alpha`, verified against real CI. A fifth real-user report has since landed,
not yet tagged: omitting `--resource` against a nonexistent `--environment`/`--client` (a typo,
most likely) printed the generic `(no resources with a registered format handler at this layer)`
— worded as if the layer existed but its resources' formats were unsupported. `RunEveryResource`
now tells that case apart from a layer that genuinely exists but declares no resources: when the
target layer file itself is missing, it names the exact path it looked for and suggests
`configtransform init`, while the original message is unchanged for the cases it actually
describes. A sixth: `set` now supports matching an XML element by tag name alone
(`--match tag=customErrors`), for singleton elements with no identifying attribute at all —
mirrors real XDT's own default-match-by-name idiom (no `xdt:Locator` at all) for exactly that
case, via a new reserved `tag` `--match` coordinate parallel to JSON's existing `key`/
`literal-key`. A third format has since landed: `.env` support (`ConfigTransform.Env`,
registered as a third `FormatEngine` with zero orchestration changes needed — the real proof the
dispatcher generalizes past two engines) — needs no NuGet package at all, merges as a flat
`KEY→VALUE` override/append (simpler than JSON, no nesting or arrays to disambiguate), and `set`
is implemented as the simplest of the four formats' field authors
(`--match key=<NAME> --set value=<value>`). See `docs/CONFIG_MANAGEMENT.md` §5.5 for the `.env`
grammar this tool deliberately picked (there's no formal spec). `0.15.0-alpha` is tagged and
published, and `config-transform-pilot` is re-pinned to it with a new `.env`-based
`NotificationWorker` pilot project added and verified via real CI. A fourth format has since
landed: YAML support (`ConfigTransform.Yaml`, registered as a fourth `FormatEngine` with zero
orchestration changes needed — the dispatcher generalizing to a fourth engine, not just three) —
reuses JSON's flatten-and-merge *architecture* (`Microsoft.Extensions.Configuration`) via
`NetEscapades.Configuration.Yaml`'s `AddYamlFile` (read) and `YamlDotNet`'s `ISerializer` (write,
needed directly since NetEscapades only reads), sharing no code with `ConfigTransform.Json` per
this repo's per-format independent-library convention. Merge semantics (array-override-by-index,
empty-container-round-trips-as-absent) are inherited from `IConfiguration`'s own flattening,
identically to JSON; one real, documented limitation is that YAML is case-sensitive but
`IConfiguration` isn't, so sibling keys differing only in case throw at parse time. `set` covers
the plain-field path only (update/create a key, same `:`-separated model as JSON's own
plain-field case) — matching an item inside a YAML array of objects is **not** implemented,
refused with a "not yet supported" message, the same posture XML's own unimplemented
array-of-objects matching already takes; porting `JsonElemMatchResolver` to YAML is real,
separable work, deliberately deferred (mirrors how JSON's own `$elemMatch` landed after JSON's
first `set`). See `docs/CONFIG_MANAGEMENT.md` §5.6 for the full merge semantics and dependency
reasoning. Merged as `taljacob2/config-transform#29` and versioned as `0.16.0-alpha`
(`docs/CHANGELOG.md` section moved out of `[Unreleased]` in the same change, per
`docs/RELEASING.md` step 1); re-pinning `config-transform-pilot` and adding a YAML-based pilot
project is a separate follow-up once this ships and `publish.yml` runs green, same sequencing as
`.env`'s own pilot work. Three more closures have since landed as three independent PRs. First,
XML's array-of-objects matching — matching an *existing* item among repeated siblings — turned
out to already be fully implemented (`FindMatchingElements` already ANDs an arbitrary number of
`--match` coordinates and writes a comma-joined `Locator`), closed purely with a new fixture and
tests proving it, zero production code (merged `taljacob2/config-transform#31`). Second, a `.env`
cleanup pass found zero functional bugs and closed real test-coverage gaps plus a documentation
gap (case-sensitivity across layers, now stated as deliberate in `docs/CONFIG_MANAGEMENT.md`
§5.5) and a stale doc-comment drift in `FormatEngine.cs` (merged
`taljacob2/config-transform#32`). Third, XML's `Insert` case (a genuinely brand-new element) —
the one real remaining design gap — is now closed too, via a new reserved
`parent=<ancestor/tag/path>` `--match` coordinate always paired with `tag=`, since Insert has no
real element to read a tag/location from the way an update does. `Microsoft.Web.Xdt`'s actual
`Insert` behavior was verified empirically before writing any production code: it needs no
`Locator`, doesn't auto-create missing ancestor containers, and marking only the shallowest
missing ancestor with `Insert` inserts its whole subtree as one unit — which is what
`XmlFieldAuthor` now does (`FindOrCreateOverlayPath`, walking the overlay and the real resolved
document in lockstep). See `docs/FIELD_AUTHORING_DESIGN.md`'s "Reserved coordinate: `parent=`"
for the full design. Versioned as `0.17.0-alpha`. A further real-user report has since landed:
`--list` used to render the resolved chain differently from the single-resource resolution report
(`--dry-run`/`--diff`/a real run) — a bare `patched in` with no patch path, versus the report's
`patched in: <path>`. `LayerLister.ListLayer` now reuses `LayerChain.ResolveResource` and a new
shared `LayerChain.PrintChain` (also used by the report), so both commands render the exact same
chain the exact same way; a deliberate side effect is `--list` now also throws when a layer
declares a `patch` that doesn't exist on disk, matching every other mode's existing behavior for a
broken patch reference. Versioned as `0.18.0-alpha`. A third, optional layer axis has since landed:
`--host`/`-H` (`docs/HOST_LAYER_DESIGN.md`), one more `extends` hop under Client/Environment
(`.configtransform/Clients/<C>/<E>/Hosts/<H>/configtransform.json`) for load-balanced Production
servers that need genuinely different config from each other — rejects a hyphenated
`Client-Host`-naming workaround a real user was using in favor of reusing the self-describing-
layers chaining as-is (`LayerChain`, `--list --resource`'s reverse lookup, and every format
engine's `Merge` needed zero changes, verified against the real code). Applies uniformly
everywhere `--client`/`--environment` already do — a plain resolve, `--dry-run`/`--diff`,
`--list`, `set` (defaulting a new Host layer's `extends` to its Client/Environment layer), and
`init` (a repeatable `--host` flag/prompt, cross-multiplied with every client × environment pair
the same way clients already cross-multiply with environments). Versioned as `0.19.0-alpha`.
`init --template`'s own `--host`-aware variant is a deliberately separate, deferred follow-up —
see `docs/ROADMAP.md`'s "Next up" for what's actionable now versus what needs either a solution
repo that doesn't exist yet or an owner decision — YAML's own array-of-objects matching is the
other remaining `set` gap.
