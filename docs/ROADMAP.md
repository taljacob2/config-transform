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

**`0.5.0-alpha` is cut but not yet published**: `docs/CHANGELOG.md` has the `[0.5.0-alpha]`
section (both items above) committed to `main`, ready for `scripts/extract-changelog-section.sh`
to pick up as release notes once tagged. The tag itself (`0.5.0-alpha`, no `v` prefix) still
needs the repo owner to push it — see the operational note below — which then triggers
`publish.yml`. Once that succeeds, `config-transform-pilot`'s `.config/dotnet-tools.json` needs
re-pinning from `0.4.0-alpha` to `0.5.0-alpha` to actually pick up auto-discovery/short flags
there.

One operational note worth carrying forward: this session's GitHub credentials can push
branches but not tags (a real `403`, confirmed via verbose tracing, not a bug) — cutting the
`0.1.0-alpha`, `0.1.0-alpha2`, `0.2.0-alpha`, `0.3.0-alpha`, and `0.4.0-alpha` tags all required
the repo owner to push them manually. Expect the same for `0.5.0-alpha` and any future release
tag.

## Next up

Nothing is actionable purely within this repo right now — every remaining item below either
needs a solution repo that doesn't exist yet, or a decision only the repo owner can make. Not
a "next slice" in the same sense as the ones so far; pick from below (or something new) when
ready, rather than assuming the next item in this list is the default next step.

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
     needs the base document's real shape, not just a key/value pair.
  2. Same validation gap that deferred `init`, more so: designing a UI's workflows now would be
     guessing at real usage patterns from one synthetic pilot, not real per-repo variation.
     `--diff`/`--dry-run` already cover "see the merged result easily" without either UI.

  **Trigger to actually pick this up:** a CLI-level field-authoring command exists, is validated
  against real content, and people using it still hit friction that `--list`-style introspection
  or better docs don't solve — not a fixed timeline.
