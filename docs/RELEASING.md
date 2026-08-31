# Releasing

`docs/CHANGELOG.md` doubles as the source of GitHub Release notes — each version's section
(Keep a Changelog format) is extracted verbatim and used as that release's notes, so release
notes are written once, not twice.

## Steps

1. Move `docs/CHANGELOG.md`'s `## [Unreleased]` section content into a new
   `## [VERSION] - DATE` section, and add a fresh, empty `## [Unreleased]` above it. Commit
   this to `main` before tagging — the tagged commit must already contain the version's
   section, since `scripts/extract-changelog-section.sh` reads it from the repo at that commit.
2. Tag the commit with the bare SemVer version, **no `v` prefix** (`0.1.0-alpha`, `1.2.0`, ...)
   — see `CONFIG_MANAGEMENT.md` §10.8 for the full versioning policy.
3. Push the tag: `git push origin <version>`. This triggers `.github/workflows/publish.yml`,
   which:
   - builds, tests, and packs both tools with `-p:Version=<version>`;
   - pushes both `.nupkg`s to GitHub Packages;
   - runs `scripts/smoke-test-published-tool.sh`, which actually installs the just-published
     packages from the feed (not from the local build) via `dotnet tool install --local` and
     invokes each one — proving the *published package* works, not just that the source
     compiles and its own test suite passes;
   - creates a GitHub Release (marked pre-release automatically when the version contains a
     `-` pre-release identifier) with notes from `scripts/extract-changelog-section.sh`.

## If it fails partway through

A failure before "Push to GitHub Packages" is a normal fix-forward: fix the issue, commit, and
push a new tag.

A failure **after** the packages are already pushed (e.g. the smoke test itself fails) is
different: the published package version cannot be un-published or overwritten. Cut a new
version instead (e.g. `0.1.0-alpha2`) with the fix — never try to reuse the same version tag for
a corrected republish.
