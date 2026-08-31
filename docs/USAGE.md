# CLI usage

Status: **stub.** `ConfigTransform.Xml` and `ConfigTransform.Json` are scaffolded but not yet
implemented — this document will carry the real, verified CLI reference once that lands.

Planned shape, both tools share the same flags (see the design doc's transform tool CLI
section for the full rationale):

```
--manifest <path to manifest.json>       required
--file <base filename, e.g. App.config>  required if the manifest has more than one file entry
--client <ClientName>                    required
--environment <EnvironmentName>          required
--output <path>                          real runs only
--dry-run                                print the fully merged result to stdout; nothing written
--diff                                   print a unified diff (base vs. merged) via `git diff --no-index`
```

`--dry-run` and `--diff` never write to the base file's own location under any input.
