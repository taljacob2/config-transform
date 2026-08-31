# config-transform

Base + Environments + Clients layered configuration resolution for multi-client,
multi-environment .NET deployments — App.config, Web.config, and appsettings.json, all
through the same manifest-driven model.

Status: **scaffold only.** Solution structure, project stubs, and CI workflow skeletons are in
place; merge logic is not yet implemented.

## Projects

- `src/ConfigTransform.Core` — shared, format-agnostic logic (manifest parsing,
  case-insensitive file resolution, layer-resolution reporting).
- `src/ConfigTransform.Xml` — XDT-based resolution for App.config/Web.config/other XML config
  files, distributed as the `configtransform-xml` dotnet tool.
- `src/ConfigTransform.Json` — `Microsoft.Extensions.Configuration`-based resolution for
  appsettings.json and other JSON config files, distributed as the `configtransform-json`
  dotnet tool.

## Docs

Start with [`CLAUDE.md`](CLAUDE.md) for a fast orientation (written for AI agents and humans
alike), then [`docs/INDEX.md`](docs/INDEX.md) for the full map — architecture, this repo's
design, manifest schema, CLI reference, changelog, and the documentation policy itself.
[`CONTRIBUTING.md`](CONTRIBUTING.md) covers building and testing.

## Building

```bash
dotnet restore
dotnet build
dotnet test
```
