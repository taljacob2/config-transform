# CLI usage

`ConfigTransform.Xml` and `ConfigTransform.Json` share the same CLI shape for resolving/
previewing/writing a merged result — both fully implemented, including `--dry-run` and `--diff`.
The `set` verb (below) is implemented for both too, though what it actually supports differs by
format for reasons that come from the format itself, not an arbitrary gap.

```
--manifest, -m <path to manifest.json>       optional — auto-discovered if omitted, see below
--file, -f <base filename, e.g. App.config>  required if the manifest has more than one file entry
--client, -c <ClientName>                    required
--environment, -e <EnvironmentName>          required
--output, -o <path>                          required for a real run (omit only with --dry-run/--diff)
--dry-run                                    print the fully merged result to stdout; nothing written to disk
--diff                                       print a unified diff (base vs. merged) via `git diff --no-index`; nothing written to disk
--list                                       print the manifest's file entries and which Environments/Clients overlays actually exist on disk; needs only --manifest (and optionally --file to filter to one entry) — no --client/--environment/--output
```

Every flag that takes a value also accepts the short form shown above (`-m`, `-f`, `-c`, `-e`,
`-o`) — meant for typing a command out by hand; scripts and CI can keep using the long forms for
readability in a pipeline log. Both forms can be mixed freely in the same invocation.

`--manifest`/`-m` and the manifest's own `directory` field (`docs/MANIFEST_SCHEMA.md`) are both
resolved relative to the current working directory — run the tool from the repository root, the
same way CI does.

## Manifest auto-discovery

`--manifest`/`-m` can be omitted: if the current directory has a `.configtransform/` folder
containing exactly one `*/manifest.json` — the layout `docs/GETTING_STARTED.md` sets up for a
repo with a single project under management — that's the one used. This never guesses between
multiple candidates: with zero or more than one `.configtransform/*/manifest.json` found, the
tool fails with an error naming what it found (or didn't), and `--manifest`/`-m` has to be given
explicitly. Implemented in `ConfigTransform.Core`'s `ManifestDiscovery`, invoked from
`CliRunner` before anything else runs — every flag downstream (`--list`, `--diff`, a real run)
benefits from it equally, since it's the same manifest-path resolution step for all of them.

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

# --file is only required above because ProjectA.Framework's manifest (see
# MANIFEST_SCHEMA.md's own multi-file example) declares two files, App.config and
# NLog.config. ProjectB.Core has exactly one file entry, so --file can be dropped
# entirely — ManifestEntrySelector auto-selects the only entry:
dotnet run --project src/ConfigTransform.Json -- \
  --manifest .configtransform/ProjectB.Core/manifest.json \
  --client ClientA --environment Production --diff

# --list — what clients/environments does this manifest actually have overlays for?
# No --client/--environment/--output needed; --file narrows to one entry if the
# manifest declares more than one (omit it to see every entry).
dotnet run --project src/ConfigTransform.Xml -- \
  --manifest .configtransform/ProjectA.Framework/manifest.json --list

# Short flags + manifest auto-discovery, for typing out by hand — equivalent to the
# ProjectB.Core --diff example above, assuming it's the only project under .configtransform/:
dotnet run --project src/ConfigTransform.Json -- -c ClientA -e Production --diff
```

On every run, the tool prints an explicit found/not-found line for each layer (base,
environment overlay, client overlay — CONFIG_MANAGEMENT.md §5.1) before doing anything else. A
missing base file is a fatal error; a missing overlay is reported but not fatal — it just means
that layer had no override to apply.

`--dry-run` and `--diff` never write to the base file's own location, or anywhere else on
disk — verified directly by `XmlCliRunnerTests`/`JsonCliRunnerTests`, not just by code
inspection. `--diff` prints `(no changes)` rather than an empty diff when neither layer has an
override for the requested client/environment.

## `set` — author an overlay field

Writes a field directly — no hand-written XDT for XML, no hand-edited nested JSON for JSON — see
`docs/FIELD_AUTHORING_DESIGN.md` for the full design and why it works this way. The flag shape is
identical for both tools:

```
set --manifest, -m <path>            optional — same auto-discovery as above
    --file, -f <name>                same as above
    --client, -c <ClientName>        optional — with --environment, writes the Client overlay
    --environment, -e <EnvName>      optional — writes the Environment overlay (no --client), or required alongside --client
    --match <attr>=<value>           repeatable — identifies the target; bare <value> (no "=") defaults to key=<value>
    --set <attr>=<value>             repeatable — the field(s) to write; bare <value> defaults to value=<value>
    --dry-run                        print what would be written; nothing written to disk
```

No `--client`/`--environment` at all writes the base file directly (a change meant for
everyone); `--client` requires `--environment` (there's no client-only layer). A real write
auto-prints the effective `--diff` afterward — the same trust-check `--diff` gives elsewhere,
without it being a separate step to remember. Re-running `set` for the same `--match` against an
overlay that already wrote it updates that entry in place rather than duplicating it.

**What's actually supported differs by format, in ways that come from the format itself, not an
arbitrary implementation gap** — see `docs/FIELD_AUTHORING_DESIGN.md`'s "Open items" for the
full reasoning behind each:

- **`ConfigTransform.Xml`**: covers updating a field that already exists somewhere in the
  resolved document — the common case (overriding an existing value for one environment/client).
  Creating a genuinely new element (`Insert`) is not implemented: `--match`/`--set` don't carry
  the new element's tag name or parent location, and there's nothing in an existing document to
  derive them from for a true insert, so `set` refuses rather than guessing.
- **`ConfigTransform.Json`**: covers a single key path (nested or top-level) — both updating an
  existing key *and* creating a brand-new one, since JSON has no XDT-style Transform/Locator
  distinction to make (any layer can introduce a key; `set` just writes it) — **and matching or
  creating an item inside an array of objects**, via a `$elemMatch`-style overlay (below).
  `Microsoft.Extensions.Configuration`'s JSON provider merges arrays purely by index, with no
  native concept of matching a field's value the way XDT's `Locator` does for XML, so this isn't a
  direct port of XML's mechanism — see `docs/FIELD_AUTHORING_DESIGN.md`'s "JSON / YAML" section
  and decision log for the full reasoning.

```bash
# XML, appSettings — the simple case: one identity attribute, one value attribute.
# Bare --match/--set default to key=/value=, verified against the real file before being used.
dotnet run --project src/ConfigTransform.Xml -- set \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --client ClientA --environment Production --match ApiUrl --set https://clienta.example.com

# XML, connectionStrings — one identity attribute (name), two value attributes at once.
dotnet run --project src/ConfigTransform.Xml -- set \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --client ClientA --environment Production \
  --match name=Prod --set connectionString="Data Source=prod;..." --set providerName=System.Data.SqlClient

# XML, no --client/--environment: edits the base file directly, no xdt: anything.
dotnet run --project src/ConfigTransform.Xml -- set \
  --manifest .configtransform/ProjectA.Framework/manifest.json \
  --match key=ApiUrl --set value=https://new-default.example.com

# JSON — a nested key, ':'-separated (matches Microsoft.Extensions.Configuration's own
# flattening convention, and ASP.NET Core's own command-line config override syntax) — not '.',
# since dots commonly appear literally in real setting names ("api.timeout.ms"-style).
dotnet run --project src/ConfigTransform.Json -- set \
  --manifest .configtransform/ProjectB.Core/manifest.json \
  --client ClientA --environment Production \
  --match key=Logging:LogLevel:Default --set value=Warning

# JSON — creating a brand-new key: works the same as updating one (no Insert-style gap for JSON).
dotnet run --project src/ConfigTransform.Json -- set \
  --manifest .configtransform/ProjectB.Core/manifest.json \
  --client ClientA --environment Production --match key=Features:EnableBeta --set value=true

# JSON — a key that itself contains a literal ':' (rare, but real): --match literal-key=...
# instead of key=..., so it's matched as one property name, not split into path segments.
dotnet run --project src/ConfigTransform.Json -- set \
  --manifest .configtransform/ProjectB.Core/manifest.json \
  --match literal-key=Logging:LogLevel:Default --set value=Warning

# JSON — array of objects: --match key=<array> locates the array, any further --match
# <field>=<value> (not key=/literal-key=) becomes an $elemMatch condition on the item.
dotnet run --project src/ConfigTransform.Json -- set \
  --manifest .configtransform/ProjectB.Core/manifest.json \
  --client ClientA --environment Production \
  --match key=ConnectionStrings --match name=Prod --set connectionString="Data Source=new;..."

# JSON — compound conditions (more than one field needed to identify the item uniquely).
dotnet run --project src/ConfigTransform.Json -- set \
  --manifest .configtransform/ProjectB.Core/manifest.json \
  --client ClientA --environment Production \
  --match key=Rules --match role=Admin --match env=Production --set enabled=true

# JSON — a second call against the same array, different conditions, same overlay file: appends
# a second $elemMatch patch rather than colliding with the first (see FIELD_AUTHORING_DESIGN.md).
dotnet run --project src/ConfigTransform.Json -- set \
  --manifest .configtransform/ProjectB.Core/manifest.json \
  --client ClientA --environment Production \
  --match key=Rules --match role=Viewer --set enabled=true

# JSON — no match found: creates a new item instead of erroring (an upsert, Mongo's own term for
# the same idea) -- the new item's identity comes from the --match conditions themselves.
dotnet run --project src/ConfigTransform.Json -- set \
  --manifest .configtransform/ProjectB.Core/manifest.json \
  --client ClientA --environment Production \
  --match key=Rules --match role=Auditor --set enabled=true
```

Zero matching fields fails rather than guessing: for XML, with a suggested
`--match <realattr>=<value>` when a bare `--match` found the value under a different attribute
name instead; for JSON's plain-field path, a brand-new key just gets created (see above) —
there's no "not found" case there *unless* it's a genuine nested-path-vs-literal-key collision
(`--match key=Logging:LogLevel:Default` when the document has both a nested `Logging.LogLevel.
Default` *and* a literal top-level key spelled exactly `"Logging:LogLevel:Default"`), in which
case `set` refuses and shows both `--match key=...`/`--match literal-key=...` forms rather than
guessing. More than one matching XML element, or more than one JSON array item matching an
`$elemMatch` patch's conditions, fails and lists every candidate, asking for another `--match` to
narrow it down. Implemented in `src/ConfigTransform.Xml/XmlFieldAuthor.cs`,
`src/ConfigTransform.Json/JsonFieldAuthor.cs`, and (JSON array-of-objects resolution specifically,
shared between `set`'s eager check and `JsonLayerMerger`'s authoritative merge-time resolution)
`src/ConfigTransform.Json/JsonElemMatchResolver.cs`; shared target-file resolution in
`src/ConfigTransform.Core/SetTargetResolver.cs`; orchestration in each tool's own `CliRunner.cs`.

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
- An overlay layer containing a `set`-written (or hand-written) `$elemMatch` array-of-objects
  patch (see the `set` section above) is resolved to a real position and rewritten *before* it
  reaches `Microsoft.Extensions.Configuration` — a pre-processing pass
  (`JsonElemMatchResolver.Rewrite`) that only runs on a layer actually containing one; every other
  merge takes the original, unmodified code path.

For `--diff` in both tools, the same base file is also rendered with *no* overlays applied
(through the identical merge code path, to avoid spurious serialization-only differences) and
the two are compared via `GitDiff` (`ConfigTransform.Core`). See
`src/ConfigTransform.Xml/XmlCliRunner.cs`, `src/ConfigTransform.Json/JsonCliRunner.cs`, and the
shared orchestration in `src/ConfigTransform.Core/CliRunner.cs`.
