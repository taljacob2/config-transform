# Manifest schema

Authoritative reference for `manifest.json` — the tool's own input contract. One manifest per
project, at `.configtransform/<Project>/manifest.json` in a consuming repository (the
`.configtransform/` root name is itself configurable per repo; see the consuming repo's own
`CONFIG_MANAGEMENT.md` §10.5).

## Shape

```json
{
  "project": "services/billing/ProjectB.Core/ProjectB.Core.csproj",
  "files": [
    { "relativeToProject": "appsettings.json", "type": "json" }
  ]
}
```

## Fields

| Field | Required | Description |
|---|---|---|
| `project` | yes | Repo-relative path to the project's `.csproj`, wherever it actually is in the repo. No assumption about a `src/` convention or any particular directory layout. |
| `files` | yes | Array of config files belonging to this project that this tool manages. |
| `files[].relativeToProject` | yes | Path to the base config file, relative to the `.csproj`'s own directory. Resolved case-insensitively at runtime — this value does not need to match the real file's exact casing. |
| `files[].type` | yes | `"xml"` or `"json"` — selects which tool (`ConfigTransform.Xml` or `ConfigTransform.Json`) handles this entry. `"yaml"` and `"env"` are reserved for future use (not yet implemented — see the design doc's "future extensibility" notes). |
| `files[].name` | no | Overlay subfolder name under `.configtransform/<Project>/`. When omitted (the normal case), derived automatically from `relativeToProject`'s own filename — `App.config` → `App.config/`, `appsettings.json` → `appsettings.json/`. Only needed to disambiguate the rare case of two base files sharing a filename in different subdirectories of the same project, where auto-derivation would otherwise collide. |

## Example: a project with multiple config files

```json
{
  "project": "ProjectA.Framework/ProjectA.Framework.csproj",
  "files": [
    { "relativeToProject": "App.config", "type": "xml" },
    { "relativeToProject": "NLog.config", "type": "xml" }
  ]
}
```

Produces the overlay tree:

```
.configtransform/ProjectA.Framework/
  manifest.json
  App.config/
    Environments/
    Clients/
  NLog.config/
    Environments/
    Clients/
```

## Web.config

No special handling — Web.config is XML, resolved through the same `"type": "xml"` engine as
App.config. See the consuming repo's `CONFIG_MANAGEMENT.md` §5.2 for the one real caveat: on
ASP.NET Web Application projects with an existing native MSBuild Web.config transform
(`Web.Debug.config`/`Web.Release.config`), this tool's step must run *after* that native
pipeline step in the deploy workflow.
