# CLI usage

`ConfigTransform.Xml` and `ConfigTransform.Json` share the same CLI shape, and both are now
fully implemented, including `--dry-run` and `--diff`.

```
--manifest <path to manifest.json>       required
--file <base filename, e.g. App.config>  required if the manifest has more than one file entry
--client <ClientName>                    required
--environment <EnvironmentName>          required
--output <path>                          required for a real run (omit only with --dry-run/--diff)
--dry-run                                print the fully merged result to stdout; nothing written to disk
--diff                                   print a unified diff (base vs. merged) via `git diff --no-index`; nothing written to disk
```

`--manifest` and the manifest's own `directory` field (`docs/MANIFEST_SCHEMA.md`) are both
resolved relative to the current working directory — run the tool from the repository root, the
same way CI does.

## Examples

```bash
# ConfigTransform.Xml — preview what ClientA gets in Production, without touching any file
dotnet run --project src/ConfigTransform.Xml -- \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --file App.config --client ClientA --environment Production --dry-run

# ConfigTransform.Xml — see exactly what ClientA's overrides change vs. the untouched base
dotnet run --project src/ConfigTransform.Xml -- \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --file App.config --client ClientA --environment Production --diff

# ConfigTransform.Xml — real run, as CI invokes it
dotnet run --project src/ConfigTransform.Xml -- \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --file App.config --client ClientA --environment Production \
  --output publish/App.config

# ConfigTransform.Json — identical shape, a JSON project instead
dotnet run --project src/ConfigTransform.Json -- \
  --manifest .configtransform/ProjectB.Core/manifest.json \
  --file appsettings.json --client ClientA --environment Production \
  --output publish/appsettings.json
```

On every run, the tool prints an explicit found/not-found line for each layer (base,
environment overlay, client overlay — CONFIG_MANAGEMENT.md §5.1) before doing anything else. A
missing base file is a fatal error; a missing overlay is reported but not fatal — it just means
that layer had no override to apply.

`--dry-run` and `--diff` never write to the base file's own location, or anywhere else on
disk — verified directly by `XmlCliRunnerTests`/`JsonCliRunnerTests`, not just by code
inspection. `--diff` prints `(no changes)` rather than an empty diff when neither layer has an
override for the requested client/environment.

## What "merge" means

**XML**: base file, loaded once. Environment overlay applied (if found) via `Microsoft.Web.Xdt`.
Client overlay applied (if found) on top of that, same document. See
`src/ConfigTransform.Xml/XmlLayerMerger.cs`.

**JSON**: base + environment overlay + client overlay loaded as layered sources via
`Microsoft.Extensions.Configuration`'s own `ConfigurationBuilder`, then flattened back to a
single JSON document. Two things worth knowing, both inherent to how `IConfiguration` works,
documented in full in `src/ConfigTransform.Json/JsonLayerMerger.cs`:
- An overlay array does not replace the base array wholesale — it overrides by index, so any
  base-layer indices beyond what the overlay specifies survive untouched.
- Types (bool/number/string) are inferred from the flattened value to avoid turning
  `"enabled": false` into `"enabled": "false"`.

For `--diff` in both tools, the same base file is also rendered with *no* overlays applied
(through the identical merge code path, to avoid spurious serialization-only differences) and
the two are compared via `GitDiff` (`ConfigTransform.Core`). See
`src/ConfigTransform.Xml/XmlCliRunner.cs`, `src/ConfigTransform.Json/JsonCliRunner.cs`, and the
shared orchestration in `src/ConfigTransform.Core/CliRunner.cs`.
