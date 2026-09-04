# config-transform

Base + Environments + Clients layered configuration resolution for multi-client,
multi-environment .NET deployments — App.config, Web.config, and appsettings.json — through
self-describing `configtransform.json` layers.

Status: **implemented, tested, and released** (pre-1.0, `-alpha` — see
[`docs/ROADMAP.md`](docs/ROADMAP.md) for exactly what that does and doesn't mean).

## Projects

- `src/ConfigTransform.Core` — shared, format-agnostic logic (`configtransform.json` parsing,
  `extends`-chain resolution, case-insensitive file resolution, layer-resolution reporting).
- `src/ConfigTransform.Xml` — XDT-based resolution for App.config/Web.config/other XML config
  files, distributed as the `configtransform-xml` dotnet tool.
- `src/ConfigTransform.Json` — `Microsoft.Extensions.Configuration`-based resolution for
  appsettings.json and other JSON config files, distributed as the `configtransform-json`
  dotnet tool.

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
