# config-transform

Base + Environments + Clients layered configuration resolution for multi-client,
multi-environment .NET deployments — App.config, Web.config, and appsettings.json — through
self-describing `configtransform.json` layers.

Status: **implemented, tested, and released** (pre-1.0, `-alpha` — see
[`docs/ROADMAP.md`](docs/ROADMAP.md) for exactly what that does and doesn't mean).

## Quick start

```bash
dotnet new tool-manifest   # if the repo doesn't already have one
dotnet tool install --local ConfigTransform.Cli --version <latest>
dotnet tool run configtransform          # no arguments -- prints a tldr-style help page
```

Running `configtransform` with no arguments at all — or `configtransform help`/`--help`/`-h`
anytime — prints a quick-reference cheat sheet: common commands, and an easy plus a more advanced
example for each. See [`docs/GETTING_STARTED.md`](docs/GETTING_STARTED.md) for setting up a
project from scratch and [`docs/USAGE.md`](docs/USAGE.md) for the full CLI reference.

## Projects

- `src/ConfigTransform.Core` — shared, format-agnostic logic (`configtransform.json` parsing,
  `extends`-chain resolution, case-insensitive file resolution, layer-resolution reporting,
  format-engine dispatch).
- `src/ConfigTransform.Xml` — XDT-based merge engine for App.config/Web.config/other XML config
  files. An internal library, not its own dotnet tool.
- `src/ConfigTransform.Json` — `Microsoft.Extensions.Configuration`-based merge engine for
  appsettings.json and other JSON config files. An internal library, not its own dotnet tool.
- `src/ConfigTransform.Cli` — the unified CLI, distributed as the `configtransform` dotnet tool
  (`ConfigTransform.Cli` package). Dispatches each resource to the right engine above by its own
  file extension, so a mixed XML/JSON layer resolves in one call. Replaces the separate
  `configtransform-xml`/`configtransform-json` tools (`ConfigTransform.Xml`/`ConfigTransform.Json`
  packages) — every already-published version of those stays installable forever, but neither
  receives a new version.

## Docs

Start with [`CLAUDE.md`](CLAUDE.md) for a fast orientation (written for AI agents and humans
alike), then [`docs/INDEX.md`](docs/INDEX.md) for the full map — architecture, this repo's
design, the `configtransform.json` schema, CLI reference, changelog, and the documentation
policy itself. [`CONTRIBUTING.md`](CONTRIBUTING.md) covers building and testing.

## Building

```bash
dotnet restore
dotnet build
dotnet test
```
