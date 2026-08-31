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

Scaffold stage: solution/project structure, CI workflow skeletons, and the `Manifest` data
model are implemented and tested. See `docs/CHANGELOG.md`'s `[Unreleased]` section for the
precise, current list of what exists.

## Next up

**Slice: Core resolution primitives.** Implement in `src/ConfigTransform.Core/`:

- `FileResolver` — case-insensitive file resolution (`CONFIG_MANAGEMENT.md` §5.4). Real unit
  tests in `tests/ConfigTransform.Core.Tests/`, using temp directories, not fixtures: exact
  match, differently-cased match, zero matches (throws), ambiguous multiple matches (throws).
- `LayerResolution` — the found/not-found reporting rule (`CONFIG_MANAGEMENT.md` §5.1): missing
  overlay is reported but not fatal; missing base file is fatal. Real unit tests for both
  outcomes.

Definition of done: both classes exist in Core with tests green, and `docs/CHANGELOG.md` gets
a new entry.

## After that, in order

1. **XML merge engine, end-to-end vertical slice.** Wire real merge logic into
   `ConfigTransform.Xml` (`Microsoft.Web.Xdt`, base → env → client), implement CLI argument
   parsing (`--manifest`, `--file`, `--client`, `--environment`, `--output`), and get the
   `DotNetFramework` fixture set (write real fixture files, replacing the `.gitkeep`
   placeholder) passing end-to-end. This is the slice that proves the whole shape actually
   works, not just compiles.
2. **Expand XML fixture coverage** — `IisWebConfig` and `GenericXml` fixtures + tests, per
   `CONFIGTRANSFORM_TOOL_DESIGN.md` §3.1.
3. **`--dry-run` and `--diff`** — per `CONFIG_MANAGEMENT.md` §6, with tests asserting no file
   outside an explicit `--output` is ever written.
4. **JSON tool** — mirror steps 1–3 for `ConfigTransform.Json`
   (`Microsoft.Extensions.Configuration`-based merge), `DotNetCore` and `GenericJson`
   fixtures, including the documented JSON-array-merge behavior test
   (`CONFIGTRANSFORM_TOOL_DESIGN.md` §3.2).
5. **Real packaging verification** — confirm `dotnet pack`/`PackAsTool` actually produces
   installable tools; cut a real first tagged pre-release (e.g. `0.1.0-alpha`, no `v` prefix)
   to validate `publish.yml` end-to-end against the GitHub Packages feed.
6. **`docs/USAGE.md`** — replace the stub with the real, verified CLI reference once the CLI
   exists.

## Later / not yet scheduled

Tracked in more detail in `docs/CONFIG_MANAGEMENT.md` §11 and
`docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §5 — pulled up here only as a pointer, not duplicated:

- First real solution-repo pilot (no solution repo exists yet).
- Deployment transport mechanism (self-hosted runner vs. WinRM vs. Octopus Deploy) — not this
  repo's concern directly, but blocks the consuming architecture's `build-transformed.yml`.
- git-crypt key rotation trigger — deferred by design, not blocking.
- YAML/`.env` format support — confirmed compatible with the existing design, not needed yet.
