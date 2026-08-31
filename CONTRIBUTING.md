# Contributing

## Building

```bash
dotnet restore
dotnet build
dotnet test
```

## Design docs

- [`CONFIGTRANSFORM_TOOL_DESIGN.md`](CONFIGTRANSFORM_TOOL_DESIGN.md) — this repo's own
  structure and test plan.
- `CONFIG_MANAGEMENT.md` — the overall multi-client/multi-environment configuration
  architecture (encryption, CI/CD, deployment) this tool is one piece of. Lives in the
  consuming solution repo(s), not here.

Changes to the CLI's arguments or the manifest schema (`docs/MANIFEST_SCHEMA.md`) are breaking
changes — see `docs/CHANGELOG.md` and the SemVer policy before making one.

## Tests

Every new merge-behavior scenario should be added as a fixture under the relevant test
project's `Fixtures/` folder (`DotNetFramework`, `IisWebConfig`, `GenericXml` for XML;
`DotNetCore`, `GenericJson` for JSON) and exercised via the shared, data-driven test pattern —
not a one-off copy-pasted test method. See `CONFIGTRANSFORM_TOOL_DESIGN.md` §3 for the full
test matrix this is built around.
