# Contributing

## Building

```bash
dotnet restore
dotnet build
dotnet test
```

## Design docs

Start with [`CLAUDE.md`](CLAUDE.md), then [`docs/INDEX.md`](docs/INDEX.md) for the full map,
including [`docs/CONFIGTRANSFORM_TOOL_DESIGN.md`](docs/CONFIGTRANSFORM_TOOL_DESIGN.md) (this
repo's own structure and test plan) and [`docs/CONFIG_MANAGEMENT.md`](docs/CONFIG_MANAGEMENT.md)
(the overall architecture this tool is one piece of).

Documentation is updated in the same change as the code — see
[`docs/DOCUMENTATION_POLICY.md`](docs/DOCUMENTATION_POLICY.md).

Changes to the CLI's arguments or the manifest schema (`docs/MANIFEST_SCHEMA.md`) are breaking
changes — see `docs/CHANGELOG.md` and the SemVer policy before making one.

## Tests

Every new merge-behavior scenario should be added as a fixture under the relevant test
project's `Fixtures/` folder (`DotNetFramework`, `IisWebConfig`, `GenericXml` for XML;
`DotNetCore`, `GenericJson` for JSON) and exercised via the shared, data-driven test pattern —
not a one-off copy-pasted test method. See `docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §3 for the
full test matrix this is built around.

## Releasing

See [`docs/RELEASING.md`](docs/RELEASING.md).
