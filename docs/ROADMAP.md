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

**The solution-repo pilot (`config-transform-pilot`) is now underway and already earned its
keep**: a real `build-transformed.yml` `workflow_dispatch` run against it caught a genuine bug
this repo's own test suite never could — `XmlLayerMerger.Merge` declared `encoding="utf-16"` in
the merged XML while the file actually lands on disk as UTF-8 (`CliRunner`'s `File.WriteAllText`
default), because this project's own tests only ever re-parse the merged *string* in memory
(`XDocument.Parse`, which ignores the declared encoding) rather than round-tripping through a
real file and a standards-compliant parser the way a real consumer does. Fixed, with a
regression test that does the real round-trip — see `docs/CHANGELOG.md`'s `[0.1.0-alpha2]`
section. **`0.1.0-alpha` is affected; anything consuming it should upgrade to `0.1.0-alpha2`
once that tag is pushed and `publish.yml` completes**, rather than working around the bug
downstream.

One operational note worth carrying forward: this session's GitHub credentials can push
branches but not tags (a real `403`, confirmed via verbose tracing, not a bug) — cutting the
`0.1.0-alpha` tag required the repo owner to push it manually, and `0.1.0-alpha2` needs the
same. Expect the same for any future release tag.

## Next up

Nothing is actionable purely within this repo right now — every remaining item below either
needs a solution repo that doesn't exist yet, or a decision only the repo owner can make. Not
a "next slice" in the same sense as the ones so far; pick from below (or something new) when
ready, rather than assuming the next item in this list is the default next step.

- **Solution-repo pilot, in progress** — `config-transform-pilot` (synthetic, three projects at
  varying nesting depth, one per config format) is live and already found and fixed one real
  bug (see "Current state" above). Continue exercising it — more client/environment
  combinations via `build-transformed.yml`, and the eventual findings writeup in that repo — to
  see what else the design's assumptions miss against something closer to a real solution than
  this repo's own fixtures. A pilot against the *actual* employer-owned multi-client repo this
  design targets still needs a separate session in that organization's own Claude Code
  environment — this repo's own conversations can't touch that repo directly.
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
