# Roadmap

## Why this document exists

An AI agent's conversation context does not persist across sessions — when a session ends,
gets compacted, or a different session picks up this repo cold, nothing not written down here
survives. This document is the single source of truth for **what's planned and what's next**,
so any session (or any human contributor) can resume exactly where the last one left off,
without needing prior context. `docs/CHANGELOG.md` records what's *done*; this document records
what's *next* — read this one first when starting work here.

**Rule for maintaining it:** update this file in the same change as any work that completes a
slice, changes priority, or adds a new one — before ending a session in which progress was
made. An out-of-date roadmap is worse than none, because it's actively misleading. See
`docs/DOCUMENTATION_POLICY.md`.

## Current state

**`ConfigTransform.Xml` and `ConfigTransform.Json` are fully implemented, tested, and
released.** `0.1.0-alpha` (tag, no `v` prefix) is live: `publish.yml` ran end to end for the
first time — build, test, pack, push to GitHub Packages, `scripts/smoke-test-published-tool.sh`
(installed both packages from the real feed and invoked them, not just from the local build),
and GitHub Release creation with notes extracted from `docs/CHANGELOG.md` — all succeeded on
the first real attempt. Release: https://github.com/taljacob2/config-transform/releases/tag/0.1.0-alpha

Shared orchestration (`CliRunner` in Core, taking the format-specific merge function as a
delegate) plus the Core resolution primitives (`FileResolver`, `LayerResolution`,
`CliOptionsParser`, `ManifestLoader`, `ManifestEntrySelector`, `GitDiff`) back both tools,
verified against all XML fixture sets (`DotNetFramework`, `IisWebConfig`, `GenericXml`) and
both JSON fixture sets (`DotNetCore`, `GenericJson`) `CONFIGTRANSFORM_TOOL_DESIGN.md` §3 calls
for. See `docs/CHANGELOG.md`'s `[0.1.0-alpha]` section for the precise, full list of what
exists.

**The solution-repo pilot (`config-transform-pilot`) has completed its first round and already
earned its keep**: a real `build-transformed.yml` `workflow_dispatch` run against it caught a
genuine bug this repo's own test suite never could — `XmlLayerMerger.Merge` declared
`encoding="utf-16"` in the merged XML while the file actually lands on disk as UTF-8
(`CliRunner`'s `File.WriteAllText` default), because this project's own tests only ever
re-parse the merged *string* in memory (`XDocument.Parse`, which ignores the declared encoding)
rather than round-tripping through a real file and a standards-compliant parser the way a real
consumer does. Fixed, with a regression test that does the real round-trip — see
`docs/CHANGELOG.md`'s `[0.1.0-alpha2]` section. `0.1.0-alpha2` is live (tag pushed, `publish.yml`
succeeded), the pilot re-pinned to it, and `build-transformed.yml` has since run successfully
end to end for three real client/environment combinations, confirming the fix and the rest of
the design's core claims. Full writeup: `config-transform-pilot`'s `FINDINGS.md`.

**`0.2.0-alpha` is a breaking manifest-schema change**: `manifest.json`'s `project` field is
renamed to `directory` (and `relativeToProject` to `relativeToDirectory`), with a real semantic
fix alongside the rename — `directory` is now literally the base directory itself, not a fake
`.csproj` path fed to `Path.GetDirectoryName()`. This also makes explicit something that was
already true but obscured by the old field's name: the manifest has no coupling to `.csproj`,
.NET, or any `TargetFramework` at all — see `docs/CHANGELOG.md`'s `[0.2.0-alpha]` section and
`docs/MANIFEST_SCHEMA.md` for the full implication (a Node.js/Angular/React/Flutter project's
JSON config works identically to a `.csproj`-anchored one). Every manifest in this repo and in
`config-transform-pilot` needs updating to the new field names before pinning this version.

**`0.3.0-alpha` is live**: `ManifestLoader.Load` now recognizes git-crypt's own encrypted-file
magic header and fails with an actionable "run `git-crypt unlock`" message instead of a raw,
confusing JSON parse error when a manifest under a `.configtransform/**` tree hasn't been
unlocked yet — see `docs/CHANGELOG.md`'s `[0.3.0-alpha]` section. Also generalizes GitHub
Packages feed setup to `*.ghe.com` tenants and documents pointing a manifest's `directory` at a
repo's own root (`"."`) — both doc-only, folded into this release rather than published
separately. `config-transform-pilot` needs its `.config/dotnet-tools.json` re-pinned to
`0.3.0-alpha` (currently `0.2.0-alpha`) to actually pick up the new error message locally.

**`0.4.0-alpha` is live**: adds `--list`, a new flag on both tools that prints a manifest's file
entries and which `Environments`/`Clients` overlays actually exist on disk, needing only
`--manifest` — no `--client`/`--environment`/`--output` — so a user doesn't have to already know
what a manifest has overlays for just to find out. Implemented once in `ConfigTransform.Core`
(`ManifestLister`), shared by both tools. See `docs/CHANGELOG.md`'s `[0.4.0-alpha]` section and
`docs/USAGE.md`. Also folds in the `USAGE.md` `--file`-omission example and the git-crypt
base64-vs-raw-key documentation fix from this same session, rather than publishing those
separately. `config-transform-pilot` needs its `.config/dotnet-tools.json` re-pinned to
`0.4.0-alpha` (currently `0.3.0-alpha`) to actually pick up `--list` locally.

**Added `docs/ONBOARDING.md`**: a strict, linear, copy-paste checklist for a developer joining a
repo that already uses `config-transform` — distinct from `SECRETS_AND_LOCAL_SETUP.md`'s
comprehensive reference and from `GETTING_STARTED.md`'s new-project/day-to-day usage. Prompted by
a product-brainstorming session about making the tool accessible to "developers who don't have
time to learn it": since the packages feed is intentionally private (not a wall to remove), the
highest-leverage move for that audience is minimizing friction for people already granted access,
not changing the distribution model. Every troubleshooting entry in it is a real incident this
project actually hit, not a hypothetical one. See `docs/CHANGELOG.md`'s `[0.5.0-alpha]` section.
Not yet validated against a real second developer's onboarding — that's the natural next test,
whenever one is available, with the doc itself as the artifact to watch them use.

**Added manifest auto-discovery and short flag aliases** (`--manifest`/`-m` optional when the
current directory has exactly one `.configtransform/*/manifest.json`; `-m`/`-f`/`-c`/`-e`/`-o`
short forms for `--manifest`/`--file`/`--client`/`--environment`/`--output`), directly
prompted by the same accessibility brainstorming session that produced `docs/ONBOARDING.md`
above — see `docs/CHANGELOG.md`'s `[0.5.0-alpha]` section for the full writeup and
`docs/USAGE.md`'s "Manifest auto-discovery" section for the user-facing behavior.

**`0.5.0-alpha` is live**: the repo owner pushed the tag, `publish.yml` succeeded (build, test,
pack, push to GitHub Packages, smoke-test, GitHub Release —
https://github.com/taljacob2/config-transform/releases/tag/0.5.0-alpha), and
`config-transform-pilot`'s `.config/dotnet-tools.json` has been re-pinned from `0.4.0-alpha` to
`0.5.0-alpha` accordingly (its `README.md`'s "not yet verified" caveat is resolved too — see
that repo's own history).

**Drift found while doing that re-pin, flagged rather than silently fixed**: a `0.4.1` tag
exists on GitHub (non-prerelease, published 2026-09-01) between `0.4.0-alpha` and `0.5.0-alpha`
that this document and `docs/CHANGELOG.md` never recorded. It points at the exact same commit as
`0.4.0-alpha`'s successor docs commit (`b4087d7`, "Add docs/ONBOARDING.md") — so its actual tool
code is identical to `0.4.0-alpha`'s, just re-tagged non-alpha. Because `docs/RELEASING.md`'s
process wasn't followed for it, `docs/CHANGELOG.md` has no `## [0.4.1]` section, so its GitHub
Release notes (extracted via `scripts/extract-changelog-section.sh`) are empty. Not fixed here —
this looks like a deliberate manual action by the repo owner outside the documented process
(notably: dropping `-alpha` for what's functionally `0.4.0`'s code, the same question raised and
declined for `0.5.0` in the conversation that produced this section), not an accident to
casually undo. Left for the owner to reconcile: either backfill a `## [0.4.1]` CHANGELOG.md
section for consistency, or explain the intent here so a future session doesn't re-trip on it.

**Added `docs/FIELD_AUTHORING_DESIGN.md`**: a completed design for a `set` command that authors
an overlay field's `SetAttributes`/`Insert`/base-edit operation mechanically instead of by hand
— see that document for the full `--match`/`--set` model and the "verify against the real
document, refuse only when creating something brand new with nothing to check against" rule that
resolves every ambiguity case it hit. This is a deliberate, named exception to the validation
gate the `init`/TUI/GUI entries below are held to — see the design doc's own "Why this exists,
and why now" section for the reasoning, rather than repeating it here.

**Implemented `set` for `ConfigTransform.Xml`** — the "update an existing element" case in full:
base file, Environment overlay, and Client overlay targets; verified `key`/`value` defaults;
ambiguous/not-found errors (the not-found path suggests the real attribute name when a bare
`--match` guessed wrong); idempotent re-runs update the same overlay entry rather than
duplicating it; a real write auto-prints the effective `--diff`. `Insert` (a genuinely brand-new
element) is **not implemented** — see the design doc's "Open items" for exactly why it needs a
real design decision (parent-location information `--match`/`--set` don't carry) rather than a
quick add. 31 new tests (`XmlFieldAuthorTests`, `XmlSetCommandCliTests`, `CliOptionsParserTests`
additions, `MatchSpecTests`).

**Implemented `set` for `ConfigTransform.Json`** — a single key path, `:`-separated
(`--match key=Logging:LogLevel:Default`), covering both updating an existing key *and* creating
a brand-new one: JSON has no `Insert`-style gap the way XML does, since any layer can already
introduce a key with no special syntax, so `set` just writes it either way. A genuine
nested-path-vs-literal-key collision (rare) refuses and offers `--match literal-key=...` instead.
**Matching an item inside an array of objects is not implemented — a real design gap found
during this implementation, not anticipated by the original design doc**: `Microsoft.Extensions.
Configuration`'s JSON provider merges arrays by index, not by matching a field's value the way
XDT's `Locator` does for XML, so the array-of-objects `--match name=Prod`-style disambiguation
`docs/FIELD_AUTHORING_DESIGN.md` originally described can't actually be resolved that way against
this repo's real merge engine — `set` rejects more than one `--match` rather than silently doing
the wrong thing. Target-file resolution (base/Environment/Client) extracted to
`ConfigTransform.Core`'s new `SetTargetResolver`, shared with XML (refactor-only, verified with
XML's existing tests before adding JSON's). 33 new tests (`JsonFieldAuthorTests`,
`JsonSetCommandCliTests`), including a regression test for a real bug caught during manual
smoke-testing: a base-target write was dropping the rest of the document instead of updating it
in place. See `docs/CHANGELOG.md`'s `[0.6.0-alpha]` section and `docs/USAGE.md`'s `set` section
for the full reference on both.

**Closed JSON's array-of-objects gap**, the one flagged as a real, previously-undesigned problem
above: `set` now matches or creates an item inside a JSON array of objects via a `$elemMatch`
overlay syntax (MongoDB's own operator name, a known convention rather than an invented one).
`--match key=<array>` locates the array, as many further `--match <field>=<value>` as needed
become the match conditions, an upsert creates a new item when nothing matches. The persisted
overlay never contains an array index anywhere — a hard requirement from the design conversation
— which means resolution has to happen fresh at real merge time, progressively per layer
(Environment resolves against base; Client resolves against base+Environment-merged), not just
once when `set` writes the file: `JsonLayerMerger.Merge` gained a pre-processing pass
(`JsonElemMatchResolver`, new) that rewrites `$elemMatch` patches into a real position before
handing a layer to `Microsoft.Extensions.Configuration`, and falls back to the original,
unmodified merge code path whenever neither overlay layer uses `$elemMatch` at all — every
previously-shipped merge behavior is unchanged. See `docs/FIELD_AUTHORING_DESIGN.md`'s "JSON /
YAML" section and decision log for the full mechanism and the alternatives it ruled out
(index/position addressing, a bare-object-only overlay shape), and `docs/USAGE.md`'s `set`
section for worked examples. 41 new tests: `JsonElemMatchResolverTests` (new, 17), fixture-backed
`JsonLayerMergerElemMatchTests`/`JsonLayerMergerGenericJsonElemMatchTests` (new, 13 combined), and
additions to `JsonFieldAuthorTests`/`JsonSetCommandCliTests` (11 combined, net of two tests that
pinned the old "rejected outright" behavior and were rewritten to match the new one).

**`0.6.0-alpha` is live**: the repo owner tagged it directly from `main` (everything above since
`0.5.0-alpha` — both `set` implementations and the `$elemMatch` gap closure), without first
following `docs/RELEASING.md`'s step 1 (moving `docs/CHANGELOG.md`'s `[Unreleased]` content into
a versioned section before tagging) — the same kind of drift already flagged for `0.4.1` above,
here reconciled directly since the intent was unambiguous (the tag points at the exact commit
that content was merged at): `docs/CHANGELOG.md` now has a proper `## [0.6.0-alpha]` section
covering it, backfilled after the fact rather than left undocumented.

**Added `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`, now fully decided** — replace `manifest.json`
and the fixed base→Environments→Clients rule (`CONFIG_MANAGEMENT.md` §9) with a Kustomize-style
self-describing `configtransform.json` per layer directory, declaring `resources` (each pairing a
project's real repo-root-relative path with its own optional `patch`, same convention) plus an
optional `extends` naming the layer to inherit from. Raised directly by the repo owner, and every
design question it originally opened is now settled: JSON, not YAML (no new dependency for a
config file this tool doesn't merge); one file spans every project/format a client×environment
touches, accepting that `ConfigTransform.Xml`/`ConfigTransform.Json` likely unify into one CLI
dispatcher as a first-class, separately-scoped consequence; each resource carries its own patch
directly rather than two lists cross-referenced by convention, which needed the new `extends`
field (flagged as new, not silently folded in) to stay unambiguous when a layer inherits from
another spanning multiple projects; every path — `extends`, `path`, `patch` alike — is
repo-root-relative uniformly, no same-directory exception for `patch` (two real inconsistencies
in the first pass, caught by the repo owner and corrected in favor of one predictable rule over a
little repetition); `manifest.json` is fully replaced, a clean break, no coexistence period; and
`set` gains a new `--resource <path>` targeting flag plus rules for creating/updating a layer's
`resources` entries and patch files as needed, while its actual field-authoring logic
(`XmlFieldAuthor`/`JsonFieldAuthor`, `$elemMatch`, verified defaults) stays untouched. Nothing
design-level remains open — see "Next up" below for what's left, which is implementation
planning, not more design.

One operational note worth carrying forward: this session's GitHub credentials can push
branches but not tags (a real `403`, confirmed via verbose tracing, not a bug) — cutting the
`0.1.0-alpha`, `0.1.0-alpha2`, `0.2.0-alpha`, `0.3.0-alpha`, `0.4.0-alpha`, and `0.5.0-alpha`
tags all required the repo owner to push them manually (`0.4.1` too, going by its publish date,
though not part of this session's own release work). Expect the same for any future release
tag.

**Self-describing overlays (`configtransform.json`) implemented** — `docs/SELF_DESCRIBING_
OVERLAYS_DESIGN.md`'s fully-decided design is now real code, not just a plan. `manifest.json` and
`ManifestLoader`/`ManifestDiscovery`/`ManifestEntrySelector`/`ManifestLister`/`LayerResolution`
are deleted, replaced by `LayerManifest`/`LayerManifestLoader`/`LayerPathResolver`/`LayerChain`/
`LayerLister` (`ConfigTransform.Core`). `XmlLayerMerger`/`JsonLayerMerger` now take an
arbitrary-length ordered patch chain instead of a fixed base+environment+client two-slot
signature — `JsonLayerMerger`'s `$elemMatch` progressive resolution folds over the whole chain
(verified with a genuine 3-deep chain test, not just the old 2-hop case). `--manifest`/`--file`
are gone; `--resource <repo-root-relative path>` is the tool-wide targeting flag everywhere
(`--dry-run`/`--diff`/a real run/`--list`/`set`), and omitting it processes every resource the
resolved layer touches, in this tool's own format, in one call (mixed-format layers are fine — the
other tool's resources are skipped with a stderr note, not silently dropped or an error; true
single-binary dispatch across formats is the separately-scoped CLI-unification pass below, still
not started). `set` creates a missing `configtransform.json` on first write, defaulting a Client
layer's `extends` to the matching Environment layer even if that file doesn't exist yet — its
actual field-authoring logic (`XmlFieldAuthor`/`JsonFieldAuthor`, `$elemMatch`) is untouched, as
designed. A real bug caught only by manual smoke-testing (not the unit suite): `set` was writing
an *absolute* path into a newly-created layer's `extends` field instead of repo-root-relative,
violating the design's own "every path is repo-root-relative, no exceptions" rule — fixed, with
the fix's regression coverage tightened from a loose substring check to exact-value assertions so
the same class of bug can't silently pass again. All fixture trees (`DotNetFramework`,
`GenericXml`, `IisWebConfig`, `DotNetCore` (+`ElemMatch`), `GenericJson` (+`ElemMatch`)) migrated
to the new `.configtransform/Environments/<Env>/`+`.configtransform/Clients/<Client>/<Env>/` shape;
`TempCliWorkspace` (both test projects) rebuilt around a synthetic repo root. Docs rewritten in the
same change: `CLAUDE.md`, `MANIFEST_SCHEMA.md` (content now describes `configtransform.json`, kept
its filename), `GETTING_STARTED.md`, `ONBOARDING.md`, `USAGE.md`, `CONFIG_MANAGEMENT.md` §3/§4/
§5.1/§9, and `docs/INDEX.md`. 177 tests passing solution-wide.

**Versioned as `0.7.0-alpha`, a breaking pre-1.0 change** — `manifest.json` support and
`--manifest`/`--file` are removed outright, no coexistence period, so this needs its own version
bump before merging to `main`. Follows this repo's own precedent for a breaking pre-1.0 change
(`0.2.0-alpha`'s manifest-schema rename) rather than jumping to `1.0.0`: `CONFIG_MANAGEMENT.md`
§10.8 ties dropping `-alpha` to real-content validation, not to how large a breaking change is,
and that gate hasn't moved — confirmed directly with the repo owner rather than assumed.
`docs/CHANGELOG.md`'s `[Unreleased]` content moved into a `## [0.7.0-alpha]` section as part of
this same change (`docs/RELEASING.md` step 1) — the owner still needs to tag and push it
(`git push origin 0.7.0-alpha`) once this PR merges to `main`, same as every prior release.

**`0.7.0-alpha`'s own release shipped with a broken release-verification gate, corrected as
`0.7.0-alpha2`** — a real gap in the sweep above: every doc and the CLI itself moved off
`--manifest`, but `scripts/smoke-test-published-tool.sh` was missed. `publish.yml` pushes
packages to GitHub Packages *before* running that script and gates GitHub Release creation on it
passing, so `0.7.0-alpha`'s packages went live but its smoke-test step failed
(`Error: Unrecognized argument: '--manifest'.`) and no GitHub Release was created for it. Per
`docs/RELEASING.md`'s own documented recovery policy (packages already pushed can never be
un-published or overwritten; re-attempt as a new tag, precedent `0.1.0-alpha`→`0.1.0-alpha2`),
fixed by rewriting the script around `configtransform.json`/`--resource` (now asserting the
merged value in the output, not just exit code 0 — verified locally against real builds of both
tools before tagging) and cutting `0.7.0-alpha2`, a release-process-only correction with no
`src/` changes. See `docs/CHANGELOG.md`'s `[0.7.0-alpha2]` entry.

**CLI unification implemented — `ConfigTransform.Xml`/`ConfigTransform.Json` merged into one
`ConfigTransform.Cli` dispatcher (`configtransform`), versioned `0.8.0-alpha`.** The one piece of
`docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`'s "Settled decisions" #2 deliberately left out of the
`0.7.0-alpha` implementation, now done: every resource a resolved layer touches, across every
registered format, resolves in one call — a mixed XML/JSON layer no longer needs two tool
invocations, and the old "skip the other format with a stderr note" interim behavior is gone
(that skip now only fires for a genuinely unregistered extension, e.g. a future YAML resource,
and is still reported, never silently dropped). New Core types `FormatEngine`/
`FormatEngineRegistry` and a new `SetRunner` (the unified `set` orchestration, replacing each
tool's own near-duplicate `RunSet`) do the dispatching by resource extension;
`XmlLayerMerger`/`JsonLayerMerger`/`XmlFieldAuthor`/`JsonFieldAuthor` are unchanged internal
engines, exactly as the design doc anticipated. `ConfigTransform.Xml`/`ConfigTransform.Json` are
now internal libraries (`IsPackable=false`), no longer their own NuGet packages — every version
already published under those IDs stays installable forever, but neither gets a new version.
Verified via a real `dotnet pack`/local-tool-install rehearsal (the exact gate `0.7.0-alpha`
skipped, which cost a broken release) and a manual smoke test of the actual new capability: one
`--dry-run` call with `--resource` omitted resolving both an XML and a JSON resource with zero
stderr output. 179 tests passing solution-wide (a 37-test `ConfigTransform.Cli.Tests` project
replaces the migrated `*CliRunnerTests`/`*SetCommandCliTests`, plus new coverage — mixed-format
single-call resolution, `set` dispatching by extension within one shared layer, the
unregistered-extension error/skip cases — that no test process could previously reach, since the
two-tool split meant no single test could touch both formats at once). See
`docs/CHANGELOG.md`'s `[0.8.0-alpha]` entry.

**`help` command added, and the default when the tool is run with no arguments at all** — a new
`HelpPrinter` (`ConfigTransform.Core`) prints a tldr-style page (a `USAGE` line, a
`COMMON COMMANDS` quick-reference table, and an easy + a more advanced "tldr" example for every
command), reachable via no arguments, a leading bare `help`, or `--help`/`-h` anywhere in flag
position — all of which win over every other flag, including what would otherwise be a
validation error. Supported extensions are read from the real `FormatEngineRegistry`, not
hardcoded, so the page can't drift from what the binary actually handles. 200 tests passing
solution-wide. Folded into the `[0.8.0-alpha]` CHANGELOG section (rather than its own version)
since `ConfigTransform.Cli` had no published version yet to be additive *relative to* — one clean
first release covers both the CLI unification and this. See `docs/CHANGELOG.md`'s `[0.8.0-alpha]`
entry.

**`0.8.0-alpha` is live and `config-transform-pilot` has been migrated onto it.** The owner
tagged and pushed `0.8.0-alpha`; `publish.yml` ran green end to end, including the
mixed-format-single-call smoke test (`ConfigTransform.Cli` release:
`https://github.com/taljacob2/config-transform-pilot`'s
`docs/CHANGELOG.md`/GitHub Releases in `config-transform` have the details). The pilot's own
`.config/dotnet-tools.json` is re-pinned to `ConfigTransform.Cli 0.8.0-alpha`,
`build-transformed.yml`'s five invocations now call the unified `configtransform`, and the
"Demonstrate multi-resource mode" step collapsed from two per-format calls to one
`--resource`-omitted call — the actual headline capability, validated against real
multi-project content, not just this repo's own synthetic smoke test. Verified via three real
`workflow_dispatch` runs, not just reasoning about the change: a golden-output diff against a
freshly-triggered `0.7.0-alpha2` baseline run (all four resolved resources byte-for-byte
identical, `--list` output identical, the only difference being multi-resource mode's stderr
skip notes dropping from 2 to 0), plus an `Initech`/`Staging` negative test confirming
"missing overlay ≠ error" and the known multi-resource-mode/`LegacyGateway` asymmetry both hold
unchanged. See the pilot's own `FINDINGS.md` "Migrating to the unified CLI (0.8.0-alpha)"
section and `config-transform-pilot#2` (merged) for the full writeup.

**`init` command implemented** — `docs/INIT_COMMAND_DESIGN.md`'s design is fully built: an
interactive form (plain sequential prompts, no TUI), a flag-driven quiet mode safe for CI, and
`init --template` (a bare switch, one fixed Production/Test x Client-A/Client-B starter tree whose
demo resource's `message` names its own layer at every override — immediately runnable via
`--dry-run`/`--diff` right after `init --template`, no other setup). New `InitScanner`/
`InitPlanner`/`InitTemplate`/`InitRunner` in `ConfigTransform.Core`; `CliRunner.Run` gained
`stdin`/`interactiveAllowed` parameters (mirroring how `stdout`/`stderr` are already injected) so
the whole wizard is testable with no real terminal. Along the way, fixed a real, previously-silent
patch-filename stutter in `SetTargetResolver`'s naming rule (`PatchFileNaming`, shared by `set`
and `init --template`) — see `docs/CHANGELOG.md`'s `[Unreleased]` entry for both.

**`--client` no longer required for a plain resolve** — `CliOptionsParser`'s default branch used
to demand both `--client`/`--environment` unconditionally, stricter than `--list`/`set` (both
already allowed `--environment` alone or neither) and stricter than the underlying engine needed.
`init`'s own design doc smoke-tested this exact gap and worked around it by documenting a
correction instead of fixing it; a real user then hit the identical error against the published
tool (`--environment ... --resource ...` with no `--client`) and asked why. Fixed properly:
`--client` now requires `--environment` (no client-only layer), nothing else does — uniform
across every mode. `docs/INIT_COMMAND_DESIGN.md`'s original three-invocation demo (base-only,
Environment-only, full chain) is restored, no correction needed anymore. 264 tests passing
solution-wide. Versioned as `0.9.0-alpha` (see below).

**Clearer chain output for `--list` and the single-resource resolution report** — reported
against the published tool by a real user working against `config-transform-pilot`, worked out
interactively into an agreed format. `--list` now walks the resolved chain in real application
order (`base` first, then every layer outermost-first, connected by `↓`) instead of showing the
target layer first and ancestors after; wording is uniformly `patched in`/`not patched in`
everywhere (no more `patched here`/`also patched in`/`inherited from`/`using the base file
directly`). The resolution report printed before every single-resource `--dry-run`/`--diff`/real
run got the same base→arrow→layer shape, with a two-line entry per layer (label, then an indented
`patched in: <path>`/`not patched in` detail line) since patch paths are too long to trail on the
label line, every path shown repo-relative and never omitted, and a blank line now separates that
report from the merged content/diff that follows. New `LayerChain.ChainStep`/
`ResolvedResource.Steps` back the new display; `ResolvedResource.Report` is unchanged. 269 tests
passing solution-wide. Versioned as `0.11.0-alpha` — see the drift note right below for why it
isn't `0.10.0-alpha`.

**`0.9.0-alpha` is live; `0.10.0-alpha` is a wasted, identical re-tag; `0.11.0-alpha` is the real
next version to pin to.** The owner tagged and pushed both `0.9.0-alpha` and `0.10.0-alpha`
against the exact same commit (`de7e8c2`, the `--client`-optionality fix above) — 14 minutes
apart, before this session's `--list`/resolution-report readability PR (`#16`) had merged to
`main` — without first following `docs/RELEASING.md`'s step 1 either time, the same kind of drift
already flagged for `0.4.1`/`0.6.0-alpha`. Both tags' `publish.yml` runs succeeded, so both are
real, installable packages, but `0.10.0-alpha` has no code changes over `0.9.0-alpha` — a spent
version number. Since a tag can't be moved once its packages are pushed (`docs/RELEASING.md`'s
own recovery policy), the fix is a fresh tag: `docs/CHANGELOG.md` now has proper `## [0.9.0-alpha]`
(the init command + `--client` fix), `## [0.10.0-alpha]` (backfilled drift note, points at the
same commit as `0.9.0-alpha`), and `## [0.11.0-alpha]` (the `--list`/resolution-report work,
`#16`) sections. The owner has since tagged and pushed `0.11.0-alpha` from `main` and its
`publish.yml` ran green end to end; `config-transform-pilot` is re-pinned to it (see that repo's
`FINDINGS.md`/`README.md`), so this drift is fully closed — `0.10.0-alpha` remains a wasted, never
-to-be-used tag, documented rather than removed since tags are immutable once published.

**Two more CLI usability issues, reported against the published tool by the same real user**:
bare `help` only short-circuited as `args[0]`, so a trailing `help` after other flags (e.g.
`configtransform -e Production -r App.config help`) fell through to `Unrecognized argument:
'help'.` instead of printing help, even though `--help`/`-h` already worked from any position —
fixed by adding `help` alongside `--help`/`-h` in `CliOptionsParser`'s switch, so all three now
behave identically regardless of position. Separately, `dotnet tool run configtransform ...
--help` doesn't reach `configtransform` at all — `dotnet tool run` intercepts `--help`/`-h` as
its own option before forwarding anything to the tool, which is a `dotnet` CLI parsing behavior
outside this tool's control; documented in `docs/USAGE.md`'s "Getting help" section along with
the `--` separator workaround (`dotnet tool run configtransform -- --help`). Given that
`--help` is easy to miss in practice, every CLI validation error now also gets a one-line `Try:`
example specific to that mistake (e.g. missing `--output` on a real run suggests adding
`--output <path>` or using `--dry-run`/`--diff`), so the fix is visible right where the user hit
the problem, not just behind a flag they may not reach for. An unrecognized flag close to a known
one (edit distance ≤2, e.g. `--otuput`, `--lsit`, `--dif`) now gets a specific
`Try: did you mean --output?` instead of that generic hint — plain Levenshtein distance against a
small hand-maintained list of the flags the switch recognizes, no new dependency. 277 tests
passing solution-wide. Versioned as `0.12.0-alpha` — but the owner tagged and pushed it against
the #18 merge commit *before* the CHANGELOG-versioning PR (#19) had merged, so `publish.yml`'s
release-notes step found no `## [0.12.0-alpha]` section yet and created a GitHub Release with an
empty body (the package itself published and smoke-tested fine — see `docs/CHANGELOG.md`'s
`[0.12.0-alpha]` entry for the full drift note; the release notes still need a manual paste from
that entry, the one part of this drift that isn't yet closed). `config-transform-pilot` has been
re-pinned to `0.12.0-alpha` and re-verified via a real dispatch (`config-transform-pilot#4`,
merged).

**`init`'s scan no longer suggests universal .NET/NuGet tooling manifests as candidate
resources** — reported against the published tool: a plain `configtransform init` run in this
very repo surfaced `.config/dotnet-tools.json` and `nuget.config` on the checklist alongside real
application config. `InitScanner` gained a second, narrow named-exclude list (alongside its
existing directory excludes) for exactly these two filenames, since neither is ever a legitimate
per-client/per-environment resource in any repo — unlike something merely config-*shaped*
(`tsconfig.json`, a stray `package.json`), which is deliberately still left to the checklist; see
`docs/INIT_COMMAND_DESIGN.md`'s "Scanning: directory filters, not content filters" for why this
doesn't reopen that broader rule. 280 tests passing solution-wide. Not yet tagged/released.

**A clear error instead of a raw `IOException` when `--output` collides with an existing file
in multi-resource mode** — also reported against the published tool, from the same session as
the `init` scan fix above: omitting `--resource` treats `--output` as a directory (one file per
resource), and a bare filename that happened to already exist there — most naturally, a layer
whose only resource shares that exact name — used to fail with an unhelpful, OS-worded exception
instead of a real error. `RunEveryResource` now checks up front and fails with a message plus a
`Try:` hint pointing at `--resource`. A tempting alternative — auto-detect "only one resource
found" and silently write to that file instead — was considered and rejected: it would make the
same command's meaning depend on how many resources happen to sit in the layer *right now*, so
adding a second resource to that layer later (a routine change) would silently change what an
existing, working command does. 281 tests passing solution-wide. Not yet tagged/released.

## Next up

One item below is now actionable purely within this repo (see the first bullet); every other
remaining item still either needs a solution repo that doesn't exist yet, or a decision only the
repo owner can make. Not a "next slice" in the same sense as the ones before this section; pick
from below (or something new) when ready, rather than assuming the next item in this list is the
default next step.

- **Finish `set`** — XML's "update an existing element" case, JSON's single-key-path case, and
  JSON's array-of-objects matching (`$elemMatch`) all shipped (see "Current state" above); two
  gaps remain, both scoped to XML, both real design questions rather than unimplemented happy
  paths, and both actionable now without a solution repo or an owner decision:
  1. **XML's `Insert` case** (a genuinely brand-new element) — needs an actual design decision
     first (how the parent location/tag name gets specified — a new flag, XPath, something
     else), not just an implementation pass. See `docs/FIELD_AUTHORING_DESIGN.md`'s "Open items"
     for why this is a real gap, not a checkbox.
  2. **XML's array-of-objects matching** — scoped out alongside `Insert` above (same underlying
     reason: nothing to derive a brand-new array item's shape from on create); *matching an
     existing* array item is mechanically answerable the same way an XML element match already
     is, so this could in principle be implemented independently of `Insert` — not done only for
     lack of time, not a design blocker. See `docs/FIELD_AUTHORING_DESIGN.md`'s "Open items".
- **`docs/MANIFEST_SCHEMA.md`'s filename vs. its content** — now describes the
  `configtransform.json` schema in full (the self-describing-overlays implementation above), but
  kept its old filename to avoid a large cross-reference rename across `docs/`. Worth revisiting
  as a pure rename (e.g. `LAYER_SCHEMA.md`) if the mismatch causes real confusion — not urgent,
  purely cosmetic.
- **Solution-repo pilot, first round complete** — `config-transform-pilot` (synthetic, three
  projects at varying nesting depth, one per config format) validated the core design claims
  end to end and found/fixed one real bug (see "Current state" above and the pilot's
  `FINDINGS.md`). What that pilot deliberately couldn't validate, since it's synthetic: a real
  inventory against actual solution-repo content, the deployment transport mechanism, key
  rotation, per-client key splitting, YAML/`.env` formats. A pilot against the *actual*
  employer-owned multi-client repo this design targets still needs a separate session in that
  organization's own Claude Code environment — this repo's own conversations can't touch that
  repo directly. **Migration off `manifest.json` complete as of `0.7.0-alpha2`**: contrary to
  the earlier assumption above, a session with access to both repos could drive this directly
  (`git mv` preserves a git-crypt-encrypted patch file's ciphertext unchanged across a rename,
  since the filter only runs at checkout/smudge time — the tree relocation needed no decryption;
  only the 8 new `configtransform.json` layer files, which carry no secrets, needed authoring
  from scratch). Merged in `config-transform-pilot#1`. The golden-output diff, `--list` sanity
  checks, and `workflow_dispatch` CI runs called for by the migration plan still need a session
  with the repo's real git-crypt key to actually run (this repo's own sessions never have it) —
  see `config-transform-pilot`'s own `FINDINGS.md` for the worked migration, what it found, and
  what's still unexercised.
- **Deployment transport mechanism** (self-hosted runner vs. WinRM vs. Octopus Deploy) — not
  this repo's concern directly, but blocks the consuming architecture's
  `build-transformed.yml`. `CONFIG_MANAGEMENT.md` §8.3.
- **git-crypt key rotation trigger** — deferred by design, not blocking.
- **YAML/`.env` format support** — confirmed compatible with the existing design without a
  redesign, see `docs/CONFIG_MANAGEMENT.md` §5.5. Not needed yet.
- **A real (non-`-alpha`) `1.0.0` release** — once the solution-repo pilot validates the design
  against real content, worth promoting out of pre-release.
- **A TUI (`configtransform-tui`) and/or a cross-platform GUI (`configtransform-gui`)** —
  investigated, not started. Two separate blockers, not one:
  1. There's no CLI-level field-authoring feature to build a UI around yet. Today every overlay
     is hand-written XDT or JSON — nothing computes or writes one. That's a real feature in its
     own right before any UI wraps it, and XML is the harder half: "add/set a field" isn't one
     operation, it branches three ways depending on intent — add a genuinely new key to the
     *base* file (applies to everyone, the normal case), `SetAttributes`+`Locator="Match(key)"`
     an overlay to override an existing key for one environment/client, or `Transform="Insert"`
     an overlay for the unusual case of a client-only field that exists nowhere else (see
     `docs/GETTING_STARTED.md`'s "One real difference between XML and JSON when the key is
     brand new"). JSON's version is simpler — any layer can introduce a new key with no special
     syntax — but the command still has to know which of the three XML cases it's in, which
     needs the base document's real shape, not just a key/value pair. **This half is now mostly
     built**: `SetAttributes` (update an existing key/attribute) is implemented for
     `ConfigTransform.Xml`, and JSON's `set` covers update, create, and array-of-objects matching
     (`$elemMatch`) — see "Current state" above. `Insert` (the client-only-field case named above,
     XML-specific by nature) and XML's own array-of-objects matching are not — see
     `docs/FIELD_AUTHORING_DESIGN.md` and this section's first "Next up" bullet.
  2. Same validation gap that deferred `init`, more so: designing a UI's workflows now would be
     guessing at real usage patterns from one synthetic pilot, not real per-repo variation.
     `--diff`/`--dry-run` already cover "see the merged result easily" without either UI.

  **Trigger to actually pick this up:** a CLI-level field-authoring command exists, is validated
  against real content, and people using it still hit friction that `--list`-style introspection
  or better docs don't solve — not a fixed timeline.
