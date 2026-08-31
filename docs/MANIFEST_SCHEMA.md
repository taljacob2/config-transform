# Manifest schema

Authoritative reference for `manifest.json` — the tool's own input contract. One manifest per
project, at `.configtransform/<Project>/manifest.json` in a consuming repository (the
`.configtransform/` root name is itself configurable per repo; see [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md)
§10.5).

## Shape

```json
{
  "directory": "services/billing/ProjectB.Core",
  "files": [
    { "relativeToDirectory": "appsettings.json", "type": "json" }
  ]
}
```

## Fields

| Field | Required | Description |
|---|---|---|
| `directory` | yes | Repo-relative path to the directory holding the project's config file(s), wherever it actually is in the repo. No assumption about a `src/` convention or any particular directory layout. |
| `files` | yes | Array of config files belonging to this project that this tool manages. |
| `files[].relativeToDirectory` | yes | Path to the base config file, relative to `directory`. Resolved case-insensitively at runtime — this value does not need to match the real file's exact casing. |
| `files[].type` | yes | `"xml"` or `"json"` — selects which tool (`ConfigTransform.Xml` or `ConfigTransform.Json`) handles this entry. `"yaml"` and `"env"` are reserved for future use (not yet implemented — see the design doc's "future extensibility" notes). |
| `files[].name` | no | Overlay subfolder name under `.configtransform/<Project>/`. When omitted (the normal case), derived automatically from `relativeToDirectory`'s own filename — `App.config` → `App.config/`, `appsettings.json` → `appsettings.json/`. Only needed to disambiguate the rare case of two base files sharing a filename in different subdirectories of the same project, where auto-derivation would otherwise collide. |

## `directory` is not a `.csproj` reference — it's just a path

Earlier revisions of this schema called the field `project` and described it as "the path to
the `.csproj`". That was never accurate to what the tool actually does with it: `directory`'s
value is never opened, parsed, or validated as a project file of any kind — the tool only ever
takes it as-is and resolves `relativeToDirectory` against it (`CliRunner`, in
`ConfigTransform.Core`). Nothing about the manifest, the CLI, or either merge engine
(`Microsoft.Web.Xdt` for XML, `Microsoft.Extensions.Configuration` for JSON) knows or cares that
a `.csproj` exists at all.

Concretely, this means:

- **`directory` should point at the directory itself**, not at a project file inside it — e.g.
  `"services/billing/ProjectB.Core"`, not `"services/billing/ProjectB.Core/ProjectB.Core.csproj"`.
  (Earlier examples in this repo's history used the `.csproj`-suffixed form; that was cosmetic,
  never a real requirement, and the field is renamed specifically so the shape now matches what
  it means.)
- **No `TargetFramework` coupling** — a `directory` pointing at a net35, net48, or net8.0 project
  behaves identically, since the tool never touches the project file or the consuming project's
  own build output. See `CLAUDE.md`'s "Core concepts" for the fuller version of this claim and
  how it's verified.
- **No C#/.NET coupling at all, beyond the config file format.** `directory` can point at any
  directory in the repo — a Flutter package, a Node.js/Angular/React app, anything — as long as
  the config file it points at via `relativeToDirectory` is XML or JSON (the two formats this
  tool currently merges). For example, a Node.js app's custom JSON config:
  ```json
  {
    "directory": "frontend/checkout-app",
    "files": [
      { "relativeToDirectory": "src/config/app-config.json", "type": "json" }
    ]
  }
  ```
  works exactly the same way as a `.csproj`-anchored `appsettings.json` — same layering, same
  CLI, same tool. What doesn't yet work is a config file in YAML or `.env` format (Flutter's
  typical `.env`-based config, for instance) — those formats are confirmed compatible with the
  existing design without a redesign, but not yet implemented; see
  [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) §5.5.

## Pointing `directory` at the repo root itself

`directory` doesn't have to name a subdirectory — `"."` works, and resolves to the repo root
exactly like any other relative path does, for a config file that genuinely lives at the top
level of the repo rather than inside a project subfolder:

```json
{
  "directory": ".",
  "files": [
    { "relativeToDirectory": "appsettings.json", "type": "json" }
  ]
}
```

There's no separate "root" keyword or special case in the schema for this — `directory`'s value
is passed through `Path.GetFullPath` (`CliRunner`, in `ConfigTransform.Core`) exactly as written,
and `.` is just an ordinary relative path that means "here." The `.configtransform/<Name>/`
folder holding this manifest can be named anything, including `root` — that name is purely an
organizational label for humans, never parsed or given meaning by the tool (see the "not a
`.csproj` reference" section above for the same point about `directory` generally).

One real caveat: `Path.GetFullPath` resolves against the CLI process's *working directory*, not
the manifest file's own location. `"."` means "repo root" specifically because every documented
invocation (`SECRETS_AND_LOCAL_SETUP.md`, every `build-transformed.yml`-style workflow) runs
`dotnet tool run configtransform-*` from the repo root. Running the same manifest from a
different working directory would resolve `"."` to that directory instead — the same is true of
every other `directory` value, this isn't unique to `"."`, but it's easy to miss precisely
because `"."` looks like it should mean something absolute.

**Encryption at rest is unaffected by any of this.** `config-transform` never encrypts anything
itself — a consuming repo's git-crypt `.gitattributes` rule (`CONFIG_MANAGEMENT.md` §7.1) is a
single glob, `.configtransform/** filter=git-crypt diff=git-crypt`, covering the whole
`.configtransform/` tree unconditionally. It has nothing to do with what any manifest's
`directory` resolves to. So a root-pointing manifest's `Environments`/`Clients` overlays under
`.configtransform/root/appsettings.json/` (or whatever the folder is named) are encrypted
automatically, the same as every other project's — nothing extra to configure. The one thing
git-crypt does *not* encrypt is the actual base file at the real path `directory` +
`relativeToDirectory` resolves to (here, the real `./appsettings.json` at the repo root) — that's
true for every project's base file, not specific to pointing `directory` at the root.

## Example: a project with multiple config files

```json
{
  "directory": "ProjectA.Framework",
  "files": [
    { "relativeToDirectory": "App.config", "type": "xml" },
    { "relativeToDirectory": "NLog.config", "type": "xml" }
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
App.config. See [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) §5.2 for the one real caveat: on
ASP.NET Web Application projects with an existing native MSBuild Web.config transform
(`Web.Debug.config`/`Web.Release.config`), this tool's step must run *after* that native
pipeline step in the deploy workflow.
