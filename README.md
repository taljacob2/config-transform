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

- [`CONFIGTRANSFORM_TOOL_DESIGN.md`](CONFIGTRANSFORM_TOOL_DESIGN.md) — this repo's structure
  and full test plan.
- [`docs/MANIFEST_SCHEMA.md`](docs/MANIFEST_SCHEMA.md) — the `manifest.json` schema reference.
- [`docs/USAGE.md`](docs/USAGE.md) — CLI reference (stub until the CLI is implemented).
- [`docs/CHANGELOG.md`](docs/CHANGELOG.md) — version history.
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — building and testing.

## Building

```bash
dotnet restore
dotnet build
dotnet test
```
