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

No merge logic, CLI argument parsing, or case-insensitive file resolution yet — scaffold only.
