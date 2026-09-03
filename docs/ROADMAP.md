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
in place. See `docs/CHANGELOG.md`'s `[Unreleased]` section and `docs/USAGE.md`'s `set` section
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

**Added `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`** — a proposal, not a completed design (unlike
`FIELD_AUTHORING_DESIGN.md` at this same stage): replace `manifest.json` and the fixed
base→Environments→Clients rule (`CONFIG_MANAGEMENT.md` §9) with a Kustomize-style self-describing
`configtransform.json` per layer directory, declaring `resources` (each pairing a project's real
path with its own optional `patch` directly) plus an optional `extends` naming the layer to
inherit from. Raised directly by the repo owner. Three of the document's original open questions
are now settled by the repo owner: file format is JSON, not YAML (no new dependency for a config
file this tool doesn't merge); one file spans every project/format a client×environment touches,
not just one project, accepting that `ConfigTransform.Xml`/`ConfigTransform.Json` likely unify
into one CLI dispatcher as a first-class, separately-scoped consequence; and each resource carries
its own patch directly rather than two lists cross-referenced by convention — which needed the new
`extends` field, introduced (and flagged as new, not silently folded in) to keep that patch
unambiguous when one layer inherits from another that itself spans multiple projects. Still open:
path convention, whether `manifest.json`'s directory-indirection is worth losing, and what `set`
needs to do differently — see "Next up" below.

One operational note worth carrying forward: this session's GitHub credentials can push
branches but not tags (a real `403`, confirmed via verbose tracing, not a bug) — cutting the
`0.1.0-alpha`, `0.1.0-alpha2`, `0.2.0-alpha`, `0.3.0-alpha`, `0.4.0-alpha`, and `0.5.0-alpha`
tags all required the repo owner to push them manually (`0.4.1` too, going by its publish date,
though not part of this session's own release work). Expect the same for any future release
tag.

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
- **Self-describing overlays (`configtransform.json`)** — needs further repo-owner decisions, not
  a solution repo: `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md` lays out replacing `manifest.json`
  and the fixed base→Environments→Clients rule with a Kustomize-style self-describing manifest per
  layer directory. File format (JSON), scope (one file spans every project/format a
  client×environment touches, meaning `ConfigTransform.Xml`/`ConfigTransform.Json` likely unify
  into one CLI entry point), and patch-to-resource matching (each resource pairs its own `path`
  with an optional `patch` directly, via a new `extends` field that separates layer inheritance
  from what one layer itself adds) are now decided; still open: path convention, whether losing
  `manifest.json`'s directory-indirection is worth it, and what `set` needs to do differently.
  This is a bigger, more foundational change than `set`'s remaining gaps above — it touches
  `Manifest`/`ManifestLoader`/`ManifestDiscovery`/`ManifestEntrySelector`/`LayerResolution`/
  `SetTargetResolver` and both tools' `CliRunner`, not one command, plus the CLI-unification
  consequence as its own separately-scoped piece of work. Resolve the remaining open questions in
  the design doc first; do not start implementation against it as written.
- **Solution-repo pilot, first round complete** — `config-transform-pilot` (synthetic, three
  projects at varying nesting depth, one per config format) validated the core design claims
  end to end and found/fixed one real bug (see "Current state" above and the pilot's
  `FINDINGS.md`). What that pilot deliberately couldn't validate, since it's synthetic: a real
  inventory against actual solution-repo content, the deployment transport mechanism, key
  rotation, per-client key splitting, YAML/`.env` formats. A pilot against the *actual*
  employer-owned multi-client repo this design targets still needs a separate session in that
  organization's own Claude Code environment — this repo's own conversations can't touch that
  repo directly.
- **Deployment transport mechanism** (self-hosted runner vs. WinRM vs. Octopus Deploy) — not
  this repo's concern directly, but blocks the consuming architecture's
  `build-transformed.yml`. `CONFIG_MANAGEMENT.md` §8.3.
- **git-crypt key rotation trigger** — deferred by design, not blocking.
- **YAML/`.env` format support** — confirmed compatible with the existing design without a
  redesign, see `docs/CONFIG_MANAGEMENT.md` §5.5. Not needed yet.
- **A real (non-`-alpha`) `1.0.0` release** — once the solution-repo pilot validates the design
  against real content, worth promoting out of pre-release.
- **A `configtransform init` command** — investigate once a few solution repos have actually
  gone through the manual setup in `docs/GETTING_STARTED.md` ("Setting up a project from
  scratch"). Deliberately not built now: the manual setup is small, and no real repo has
  validated the design yet, so an `init` command today would risk baking in wrong defaults
  (folder names, file-type detection, "typical" manifest shape). **Trigger to actually pick
  this up:** the same setup steps get repeated identically, with no real per-repo variation,
  across multiple onboardings — that repetition is the signal the automation would earn its
  complexity, not a fixed timeline. See `docs/GETTING_STARTED.md`'s "Should there be an `init`
  command?" section for the full reasoning.
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
