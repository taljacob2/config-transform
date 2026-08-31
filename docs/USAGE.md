# CLI usage

Both `ConfigTransform.Xml` and `ConfigTransform.Json` share the same CLI shape. As of this
writing, `ConfigTransform.Xml` — including `--dry-run` and `--diff` — is fully implemented and
tested; `ConfigTransform.Json` is still scaffold-only. See `docs/ROADMAP.md`.

```
--manifest <path to manifest.json>       required
--file <base filename, e.g. App.config>  required if the manifest has more than one file entry
--client <ClientName>                    required
--environment <EnvironmentName>          required
--output <path>                          required for a real run (omit only with --dry-run/--diff)
--dry-run                                print the fully merged result to stdout; nothing written to disk
--diff                                   print a unified diff (base vs. merged) via `git diff --no-index`; nothing written to disk
```

`--manifest` and the manifest's own `project` field (`docs/MANIFEST_SCHEMA.md`) are both
resolved relative to the current working directory — run the tool from the repository root, the
same way CI does.

## Examples (`ConfigTransform.Xml`)

```bash
# Preview what ClientA actually gets in Production, without touching any file
dotnet run --project src/ConfigTransform.Xml -- \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --file App.config --client ClientA --environment Production --dry-run

# See exactly what ClientA's overrides change vs. the untouched base
dotnet run --project src/ConfigTransform.Xml -- \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --file App.config --client ClientA --environment Production --diff

# Real run, as CI invokes it
dotnet run --project src/ConfigTransform.Xml -- \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --file App.config --client ClientA --environment Production \
  --output publish/App.config
```

On every run, the tool prints an explicit found/not-found line for each layer (base,
environment overlay, client overlay — CONFIG_MANAGEMENT.md §5.1) before doing anything else. A
missing base file is a fatal error; a missing overlay is reported but not fatal — it just means
that layer had no override to apply.

`--dry-run` and `--diff` never write to the base file's own location, or anywhere else on
disk — verified directly by `XmlCliRunnerTests`, not just by code inspection. `--diff` prints
`(no changes)` rather than an empty diff when neither layer has an override for the requested
client/environment.

## What "merge" means for XML

Base file, loaded once. Environment overlay applied (if found) via `Microsoft.Web.Xdt`. Client
overlay applied (if found) on top of that, same document. For a real run, the result is written
to `--output`; for `--diff`, the same base file is also rendered with *no* overlays applied
(through the identical code path, to avoid spurious serialization-only differences) and the two
are compared via `GitDiff` (`ConfigTransform.Core`). See
`src/ConfigTransform.Xml/XmlLayerMerger.cs` and `src/ConfigTransform.Xml/XmlCliRunner.cs`.
