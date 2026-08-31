# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/). Versioning follows SemVer 2.0
— see this repo's design doc for the policy (a breaking change to the CLI's arguments or the
manifest schema requires a major version bump, or staying in `0.x` where any change may break).

## [Unreleased]

### Added

- Repository scaffold: solution structure, `ConfigTransform.Core`/`.Xml`/`.Json` project
  stubs, corresponding test projects (xUnit), `build.yml` (test on push/PR, matrix across
  Windows and Linux) and `publish.yml` (pack + push to GitHub Packages on a tagged release).
- `Manifest`/`ManifestFileEntry` data model in `ConfigTransform.Core`, matching the documented
  schema (`docs/MANIFEST_SCHEMA.md`), including automatic overlay-folder-name derivation.
- `CLAUDE.md` (repo root) as the AI-agent/fast-orientation entry point, plus `docs/INDEX.md`
  (documentation map) and `docs/DOCUMENTATION_POLICY.md` (the "document as you go, for both
  human and AI readers" policy this repo follows).
- `docs/CONFIG_MANAGEMENT.md` mirrored into this repo alongside `docs/CONFIGTRANSFORM_TOOL_DESIGN.md`
  (moved here from the repo root), so the full architectural context is available in one place.
- `docs/ROADMAP.md` as the durable, cross-session source of truth for what's planned and
  what's next, plus explicit numbered rules at the top of `CLAUDE.md` requiring it be read
  before starting work and kept updated as work progresses — see `docs/DOCUMENTATION_POLICY.md`
  rule 7.
- `FileResolver` in `ConfigTransform.Core`: case-insensitive file lookup
  (`TryResolveCaseInsensitive`, `ResolveCaseInsensitiveRequired`), with tests covering exact
  match, differently-cased match, no match, ambiguous match (Linux-only, via
  `Xunit.SkippableFact` — see the test's own comment for why), and a missing directory.
- `LayerResolution` in `ConfigTransform.Core`: resolves base + Environments + Clients for one
  (project, file, client, environment) combination and produces an explicit found/not-found
  report per layer. Missing overlays are reported but not fatal; a missing base file throws
  `FileNotFoundException`. Fully tested, including that the overlay file name's extension is
  derived from the base file's own extension.

No CLI argument parsing or actual XDT/JSON merge logic yet — that's the next roadmap slice.
