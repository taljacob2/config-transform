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

Scaffold stage, plus Core resolution primitives: solution/project structure, CI workflow
skeletons, the `Manifest` data model, `FileResolver` (case-insensitive resolution), and
`LayerResolution` (found/not-found reporting) are all implemented and tested. See
`docs/CHANGELOG.md`'s `[Unreleased]` section for the precise, current list of what exists.

## Next up

**Slice: XML merge engine, end-to-end vertical slice.** Wire real merge logic into
`ConfigTransform.Xml` (`Microsoft.Web.Xdt`, base → env → client via `LayerResolution`),
implement CLI argument parsing (`--manifest`, `--file`, `--client`, `--environment`,
`--output`), and get the `DotNetFramework` fixture set (write real fixture files, replacing the
`.gitkeep` placeholder) passing end-to-end. This is the slice that proves the whole shape
actually works, not just compiles.

Definition of done: `dotnet run --project src/ConfigTransform.Xml -- --manifest ... --file
App.config --client ClientA --environment Production --output ...` produces a correctly merged
file against the `DotNetFramework` fixtures, with a passing test asserting it, and
`docs/CHANGELOG.md` gets a new entry.

## After that, in order

1. **Expand XML fixture coverage** — `IisWebConfig` and `GenericXml` fixtures + tests, per
   `CONFIGTRANSFORM_TOOL_DESIGN.md` §3.1.
2. **`--dry-run` and `--diff`** — per `CONFIG_MANAGEMENT.md` §6, with tests asserting no file
   outside an explicit `--output` is ever written.
3. **JSON tool** — mirror the XML slice for `ConfigTransform.Json`
   (`Microsoft.Extensions.Configuration`-based merge), `DotNetCore` and `GenericJson`
   fixtures, including the documented JSON-array-merge behavior test
   (`CONFIGTRANSFORM_TOOL_DESIGN.md` §3.2).
4. **Real packaging verification** — confirm `dotnet pack`/`PackAsTool` actually produces
   installable tools; cut a real first tagged pre-release (e.g. `0.1.0-alpha`, no `v` prefix)
   to validate `publish.yml` end-to-end against the GitHub Packages feed.
5. **`docs/USAGE.md`** — replace the stub with the real, verified CLI reference once the CLI
   exists.

## Later / not yet scheduled

Tracked in more detail in `docs/CONFIG_MANAGEMENT.md` §11 and
`docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §5 — pulled up here only as a pointer, not duplicated:

- First real solution-repo pilot (no solution repo exists yet).
- Deployment transport mechanism (self-hosted runner vs. WinRM vs. Octopus Deploy) — not this
  repo's concern directly, but blocks the consuming architecture's `build-transformed.yml`.
- git-crypt key rotation trigger — deferred by design, not blocking.
- YAML/`.env` format support — confirmed compatible with the existing design, not needed yet.
