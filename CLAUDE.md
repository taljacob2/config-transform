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
projects — App.config, Web.config, appsettings.json, and eventually other formats — by
layering **base → Environments → Clients** overlays through self-describing `configtransform.json`
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
  `Microsoft.Extensions.Configuration`. This is proven, not just claimed: the `GenericXml` and
  `GenericJson` test fixtures use arbitrary, made-up schemas specifically to catch any
  accidental special-casing. Don't add logic that assumes a specific filename or schema.
- **No coupling to any language, ecosystem, or `TargetFramework`.** Both tools are plain
  `net8.0` executables operating on config files purely as XML/JSON content — they never
  compile against, reference, or otherwise depend on the project the config file belongs to.
  A `.NET` project on net35, net40, net45, or net472 works exactly the same as one on net48 or
  net8.0 (confirmed via `config-transform-pilot`'s `LegacyGateway.Framework`, a deliberately
  vanilla net35 project) — and the same is true for a Node.js, Angular, React, or Flutter
  project's own JSON config, since `resources[].path` is just a path (see the Self-describing
  layers bullet above). The only real constraint is the config file's *format*: XML or JSON today, not the ecosystem
  or TFM it happens to live in. Don't add anything here that assumes a specific TFM, language,
  or the consuming project's own SDK/build tooling.
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
  behave identically across XML and JSON.
- `src/ConfigTransform.Xml/`, `src/ConfigTransform.Json/` — internal merge-engine libraries, one
  per format (`XmlLayerMerger`/`XmlFieldAuthor`, `JsonLayerMerger`/`JsonFieldAuthor`), not their
  own dotnet tools.
- `src/ConfigTransform.Cli/` — the actual CLI, packaged as the `configtransform` dotnet tool.
  Registers both format engines above into Core's dispatcher; this is genuinely all it does.
- `tests/*/Fixtures/` — real-shaped fixture files per scenario: `DotNetFramework`,
  `IisWebConfig`, `GenericXml` (XML); `DotNetCore`, `GenericJson` (JSON). New merge-behavior
  test cases belong here as fixtures, exercised by data-driven tests — not as inline strings
  duplicated per test method. Full test matrix: `docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §3.
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
what the underlying engine already supported. Not yet released — still in `[Unreleased]`. See
`docs/ROADMAP.md`'s "Next up" for what's actionable now versus what needs either a solution repo
that doesn't exist yet or an owner decision.
