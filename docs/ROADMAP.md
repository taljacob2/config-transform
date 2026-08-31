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

**Both `ConfigTransform.Xml` and `ConfigTransform.Json` are now fully implemented and tested
end to end**, including `--dry-run` and `--diff` for both. Shared orchestration
(`CliRunner` in Core, taking the format-specific merge function as a delegate — extracted once
the JSON tool made the near-total duplication with `XmlCliRunner` worth eliminating) plus the
Core resolution primitives (`FileResolver`, `LayerResolution`, `CliOptionsParser`,
`ManifestLoader`, `ManifestEntrySelector`, `GitDiff`) back both tools. Verified against all XML
fixture sets (`DotNetFramework`, `IisWebConfig`, `GenericXml`) and both JSON fixture sets
(`DotNetCore`, `GenericJson`) `CONFIGTRANSFORM_TOOL_DESIGN.md` §3 calls for, including a pinned
test for the JSON array-overrides-by-index (not wholesale) behavior and one for JSON
type-preservation (bool/number survive round-tripping through `IConfiguration`'s
string-only internal model, rather than becoming quoted strings). See `docs/CHANGELOG.md`'s
`[Unreleased]` section for the precise, current list of what exists.

## Next up

**Slice: real packaging verification.** Confirm `dotnet pack`/`PackAsTool` actually produce
installable tools — not just that they build. Cut a real first tagged pre-release (e.g.
`0.1.0-alpha`, no `v` prefix per `docs/CONFIG_MANAGEMENT.md` §10.8) to validate `publish.yml`
end-to-end against the GitHub Packages feed, then actually install the published tool locally
(`dotnet tool install --local`) and run it against a manifest, confirming the whole distribution
path — not just the build — works as designed.

Definition of done: a real tag is pushed, `publish.yml` succeeds, the package appears in GitHub
Packages, and `dotnet tool install` + a real invocation of the installed tool succeeds locally.
`docs/CHANGELOG.md` gets a new entry recording the first real version.

## Later / not yet scheduled

Tracked in more detail in `docs/CONFIG_MANAGEMENT.md` §11 and
`docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §5 — pulled up here only as a pointer, not duplicated:

- First real solution-repo pilot (no solution repo exists yet).
- Deployment transport mechanism (self-hosted runner vs. WinRM vs. Octopus Deploy) — not this
  repo's concern directly, but blocks the consuming architecture's `build-transformed.yml`.
- git-crypt key rotation trigger — deferred by design, not blocking.
- YAML/`.env` format support — confirmed compatible with the existing design, not needed yet.
