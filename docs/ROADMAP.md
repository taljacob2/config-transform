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

`ConfigTransform.Xml` is now fully implemented and tested end to end, including `--dry-run` and
`--diff`: scaffold, Core resolution primitives (`FileResolver`, `LayerResolution`,
`CliOptionsParser`, `ManifestLoader`, `ManifestEntrySelector`, `GitDiff`), `XmlLayerMerger`, and
`XmlCliRunner` (the full CLI orchestration, factored out of `Program.cs` so it's directly
testable) — verified against all three XML fixture sets `CONFIGTRANSFORM_TOOL_DESIGN.md` §3.1
calls for (`DotNetFramework`, `IisWebConfig`, `GenericXml`), plus dedicated tests confirming
`--dry-run`/`--diff` never write to disk anywhere, not just to the base file. See
`docs/CHANGELOG.md`'s `[Unreleased]` section for the precise, current list of what exists.

## Next up

**Slice: JSON tool.** Mirror the XML slice for `ConfigTransform.Json` —
`Microsoft.Extensions.Configuration`-based merge (base → env → client via `AddJsonFile`,
flattened back to a single JSON file), a `JsonCliRunner` mirroring `XmlCliRunner`'s shape
(reusing `GitDiff`, `ManifestLoader`, `ManifestEntrySelector`, `LayerResolution` from Core
unchanged — none of that is XML-specific), and the `DotNetCore`/`GenericJson` fixture sets from
`CONFIGTRANSFORM_TOOL_DESIGN.md` §3.2, including the documented JSON-array-merge behavior test
(arrays flatten to indexed keys rather than merging element-wise — a real gotcha worth a test
that pins the actual behavior, not the intuitive-but-wrong one).

Definition of done: `ConfigTransform.Json` reaches parity with `ConfigTransform.Xml` — real-run,
`--dry-run`, and `--diff` all working against both JSON fixture sets, with tests — and
`docs/USAGE.md`/`docs/CHANGELOG.md` are updated accordingly.

## After that, in order

1. **Real packaging verification** — confirm `dotnet pack`/`PackAsTool` actually produces
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
