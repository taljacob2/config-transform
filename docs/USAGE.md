# CLI usage

Both `ConfigTransform.Xml` and `ConfigTransform.Json` share the same CLI shape. As of this
writing, `ConfigTransform.Xml`'s real-run path (below) is implemented and tested;
`ConfigTransform.Json` is still scaffold-only, and `--dry-run`/`--diff` are not yet implemented
in either tool — see `docs/ROADMAP.md`.

```
--manifest <path to manifest.json>       required
--file <base filename, e.g. App.config>  required if the manifest has more than one file entry
--client <ClientName>                    required
--environment <EnvironmentName>          required
--output <path>                          required for a real run (omit only with --dry-run/--diff)
--dry-run                                not yet implemented — parses, then errors clearly
--diff                                   not yet implemented — parses, then errors clearly
```

`--manifest` and the manifest's own `project` field (`docs/MANIFEST_SCHEMA.md`) are both
resolved relative to the current working directory — run the tool from the repository root, the
same way CI does.

## Example (`ConfigTransform.Xml`)

```bash
dotnet run --project src/ConfigTransform.Xml -- \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --file App.config --client ClientA --environment Production \
  --output publish/App.config
```

On success, the tool prints an explicit found/not-found line for each layer (base, environment
overlay, client overlay — CONFIG_MANAGEMENT.md §5.1), then writes the merged result to
`--output`. A missing base file is a fatal error; a missing overlay is reported but not fatal —
it just means that layer had no override to apply.

## What "merge" means for XML

Base file, loaded once. Environment overlay applied (if found) via `Microsoft.Web.Xdt`. Client
overlay applied (if found) on top of that, same document. Result is serialized and written to
`--output`. See `src/ConfigTransform.Xml/XmlLayerMerger.cs`.
