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

Scaffold, Core resolution primitives, and a working end-to-end `ConfigTransform.Xml` real-run
path are all implemented and tested: solution/project structure, CI workflow skeletons, the
`Manifest` data model, `FileResolver`, `LayerResolution`, CLI argument parsing
(`CliOptionsParser`), manifest loading and entry selection, and `XmlLayerMerger` (base → env →
client via `Microsoft.Web.Xdt`), verified against the real `DotNetFramework` fixture set. See
`docs/CHANGELOG.md`'s `[Unreleased]` section for the precise, current list of what exists.

## Next up

**Slice: expand XML fixture coverage.** Add `IisWebConfig` and `GenericXml` fixtures + tests,
per `CONFIGTRANSFORM_TOOL_DESIGN.md` §3.1 — `IisWebConfig` specifically needs nested/
`<location>`-wrapped structures (not just flat `appSettings`) to genuinely exercise
`Locator="Match(...)"` beyond what `DotNetFramework` already covers; `GenericXml` needs an
arbitrary, made-up schema to prove `XmlLayerMerger` has no hidden App.config-specific
assumptions (it doesn't, by construction, but this is the fixture set that actually
demonstrates it).

Definition of done: both fixture sets exist with real content, both have passing
`XmlLayerMergerTests`-style tests, and `docs/CHANGELOG.md` gets a new entry.

## After that, in order

1. **`--dry-run` and `--diff`** — per `CONFIG_MANAGEMENT.md` §6, with tests asserting no file
   outside an explicit `--output` is ever written.
2. **JSON tool** — mirror the XML slice for `ConfigTransform.Json`
   (`Microsoft.Extensions.Configuration`-based merge), `DotNetCore` and `GenericJson`
   fixtures, including the documented JSON-array-merge behavior test
   (`CONFIGTRANSFORM_TOOL_DESIGN.md` §3.2).
3. **Real packaging verification** — confirm `dotnet pack`/`PackAsTool` actually produces
   installable tools; cut a real first tagged pre-release (e.g. `0.1.0-alpha`, no `v` prefix)
   to validate `publish.yml` end-to-end against the GitHub Packages feed.

## Later / not yet scheduled

Tracked in more detail in `docs/CONFIG_MANAGEMENT.md` §11 and
`docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §5 — pulled up here only as a pointer, not duplicated:

- First real solution-repo pilot (no solution repo exists yet).
- Deployment transport mechanism (self-hosted runner vs. WinRM vs. Octopus Deploy) — not this
  repo's concern directly, but blocks the consuming architecture's `build-transformed.yml`.
- git-crypt key rotation trigger — deferred by design, not blocking.
- YAML/`.env` format support — confirmed compatible with the existing design, not needed yet.
