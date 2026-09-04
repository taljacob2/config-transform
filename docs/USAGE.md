# CLI usage

`ConfigTransform.Xml` and `ConfigTransform.Json` share the same CLI shape for resolving/
previewing/writing a merged result, built around self-describing `configtransform.json` layers
(`docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`) — one per layer directory under `.configtransform/`,
addressed by `--client`/`--environment`, spanning every resource (project config file) that layer
touches. Each tool processes only the resources in its own format; a mixed-format layer is fine,
each tool just skips what isn't its format (see "Multi-resource: omitting `--resource`" below).
The `set` verb (below) is implemented for both too, though what it actually supports differs by
format for reasons that come from the format itself, not an arbitrary gap.

```
--resource, -r <repo-root-relative path>   optional for a resolve/--list — omit for every resource the layer touches; required for `set`
--client, -c <ClientName>                  required for a resolve; optional for --list/set
--environment, -e <EnvironmentName>        required for a resolve; optional for --list (with --resource) and set
--output, -o <path>                        required for a real run (omit only with --dry-run/--diff) — a file with --resource, a directory without it
--dry-run                                  print the fully merged result to stdout; nothing written to disk
--diff                                     print a unified diff (unpatched vs. merged) via `git diff --no-index`; nothing written to disk
--list                                     show a layer's resources (--client/--environment), or a tree-wide reverse lookup (--resource) — see below
```

Every flag that takes a value also accepts the short form shown above (`-r`, `-c`, `-e`, `-o`) —
meant for typing a command out by hand; scripts and CI can keep using the long forms for
readability in a pipeline log. Both forms can be mixed freely in the same invocation.

Every path — `--resource`'s value, and everything a `configtransform.json` itself declares
(`extends`, `resources[].path`, `resources[].patch`) — is **repo-root-relative**, resolved
against the current working directory. Run the tool from the repository root, the same way CI
does.

## Resolving `--client`/`--environment` to a layer

- Neither given → the base file itself, no layer at all (only meaningful for `set`; every other
  command requires both).
- `--environment` only → `.configtransform/Environments/<Environment>/configtransform.json`.
- Both given → `.configtransform/Clients/<Client>/<Environment>/configtransform.json`, which
  typically (not necessarily) `extends` the matching Environment layer.

None of this is discovery or guessing — given `--client`/`--environment`, the path is fully
determined. A target layer that doesn't exist on disk (or whose `extends` chain hits a layer that
doesn't exist) is never fatal for a resolve — it just means nothing more is configured from that
point on, the same tolerance a missing overlay always had (`CONFIG_MANAGEMENT.md` §5.1). One real
consequence of the new tree, named explicitly in the design doc's "accepted cost" section: a
Client layer that inherits an Environment layer's content but adds nothing of its own must still
exist on disk (even with an empty `resources: []`, just declaring `extends`) — a Client layer file
that's missing *entirely* does not implicitly fall through to the Environment layer the way a
missing overlay *file* did under the old fixed rule.

## Single resource vs. every resource

`--resource <path>` (repeatable? no — exactly one) names a project directly, by the same
repo-root-relative path its `configtransform.json` entries use everywhere else. Omitting it
processes **every resource the resolved layer's chain touches, in this tool's own format**, in one
invocation:

- `--dry-run`/`--diff` print each resource's own result, labeled `=== <path> ===`.
- A real run requires `--output <directory>` (not a file) and writes one file per resource, each
  resource's own repo-root-relative path mirrored under that directory.
- A resource whose extension isn't this tool's format is skipped with a note on stderr (e.g.
  "Skipped 1 resource(s) not in this tool's format (.json); run the matching tool for those.") —
  not an error, and not silently dropped. True single-binary dispatch across both formats in one
  call is a separate, not-yet-implemented pass (`docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`'s "Open
  items for implementation").

## `--list`

Two modes:

- **Given `--client`/`--environment`** (client optional, environment required): shows that
  layer's own `extends` and every resource it touches — each one labeled `patched here: <patch>`
  (plus `also patched in: <layer>` for every other layer in the chain that also patches it),
  `not patched here — inherited from <layer>`, or `not patched anywhere — using the base file
  directly`.
- **Given `--resource` instead** (no `--client`/`--environment`): a tree-wide reverse lookup —
  every `configtransform.json` anywhere under `.configtransform/` that patches this one resource,
  each with its own patch file and `extends` (if any). This closes a real ergonomic gap the new
  tree creates: under the old fixed layout, "every overlay for App.config" was one directory
  listing; under this design the same question means searching every `configtransform.json` in
  the tree, which is exactly what this mode does for you.

## Examples

All examples below assume the tree `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`'s own worked example
builds — `OrderProcessor.Framework/App.config` (XML) and `BillingApi.Core/appsettings.json`
(JSON), both patched for `Acme`/`Production`, run from the repo root.

```bash
# Preview what Acme gets for OrderProcessor.Framework/App.config in Production
dotnet run --project src/ConfigTransform.Xml -- \
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production --dry-run

# See exactly what Acme's overrides change vs. the unpatched chain
dotnet run --project src/ConfigTransform.Xml -- \
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production --diff

# Real run, one resource, as CI invokes it
dotnet run --project src/ConfigTransform.Xml -- \
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production --output publish/App.config

# Every XML resource this layer touches, one call, --output as a directory
dotnet run --project src/ConfigTransform.Xml -- \
  --client Acme --environment Production --output publish/

# ConfigTransform.Json — identical shape, a JSON project instead
dotnet run --project src/ConfigTransform.Json -- \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production --diff

# --list for one layer — resources, extends, and what's patched here vs. inherited
dotnet run --project src/ConfigTransform.Xml -- \
  --list --client Acme --environment Production

# --list --resource — reverse lookup: every layer in the tree that patches this one project
dotnet run --project src/ConfigTransform.Xml -- \
  --list --resource OrderProcessor.Framework/App.config

# Short flags, for typing out by hand
dotnet run --project src/ConfigTransform.Json -- -r BillingApi.Core/appsettings.json -c Acme -e Production --diff
```

On every single-resource run, the tool prints an explicit found/not-found line for the base file
and each layer in the chain (`CONFIG_MANAGEMENT.md` §5.1) before doing anything else. A missing
base file is a fatal error; a layer that doesn't list the resource, or lists it with no `patch`,
is reported but not fatal. A `patch` that *is* declared but whose file doesn't exist on disk is a
different case — an explicit, broken reference — and is always an error, the same way a missing
base file is.

`--dry-run` and `--diff` never write to the base file's own location, or anywhere else on
disk — verified directly by `XmlCliRunnerTests`/`JsonCliRunnerTests`, not just by code
inspection. `--diff` prints `(no changes)` rather than an empty diff when the resolved chain has
nothing to apply.

## `set` — author an overlay field

Writes a field directly — no hand-written XDT for XML, no hand-edited nested JSON for JSON — see
`docs/FIELD_AUTHORING_DESIGN.md` for the full design and why it works this way. The flag shape is
identical for both tools:

```
set --resource, -r <repo-root-relative path>   required — names the project directly
    --client, -c <ClientName>                  optional — with --environment, writes the Client layer
    --environment, -e <EnvName>                optional — writes the Environment layer (no --client), or required alongside --client
    --match <attr>=<value>                     repeatable — identifies the target; bare <value> (no "=") defaults to key=<value>
    --set <attr>=<value>                       repeatable — the field(s) to write; bare <value> defaults to value=<value>
    --dry-run                                  print what would be written; nothing written to disk
```

No `--client`/`--environment` at all writes the base file directly (a change meant for everyone);
`--client` requires `--environment` (there's no client-only layer). A real write auto-prints the
effective `--diff` afterward — the same trust-check `--diff` gives elsewhere, without it being a
separate step to remember.

**When the target layer's `configtransform.json` doesn't exist yet, `set` creates it** — for a
Client-layer write, defaulting its `extends` to the matching Environment layer's path even if that
file doesn't exist on disk yet either (a missing `extends` target is "nothing to inherit," not an
error). **When the resource isn't listed there yet**, `set` appends a `resources` entry pointing
at a newly-authored patch file, named `patch-<resource path, "/" replaced with "-">.<xml|json>`
(the patch extension always reflects the *transform's own* format — `.xml` for an XDT transform
regardless of the base resource's own extension, `.json` for JSON) sitting alongside the
`configtransform.json` that references it. **When it's already listed with a `patch`**,
re-running `set` for the same `--match` updates that existing patch file in place rather than
duplicating it or creating a second one — idempotent, same as before.

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
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production --match ApiUrl --set https://acme.example.com

# XML, connectionStrings — one identity attribute (name), two value attributes at once.
dotnet run --project src/ConfigTransform.Xml -- set \
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production \
  --match name=Prod --set connectionString="Data Source=prod;..." --set providerName=System.Data.SqlClient

# XML, no --client/--environment: edits the base file directly, no xdt: anything.
dotnet run --project src/ConfigTransform.Xml -- set \
  --resource OrderProcessor.Framework/App.config \
  --match key=ApiUrl --set value=https://new-default.example.com

# JSON — a nested key, ':'-separated (matches Microsoft.Extensions.Configuration's own
# flattening convention, and ASP.NET Core's own command-line config override syntax) — not '.',
# since dots commonly appear literally in real setting names ("api.timeout.ms"-style).
dotnet run --project src/ConfigTransform.Json -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
  --match key=Logging:LogLevel:Default --set value=Warning

# JSON — creating a brand-new key: works the same as updating one (no Insert-style gap for JSON).
dotnet run --project src/ConfigTransform.Json -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production --match key=Features:EnableBeta --set value=true

# JSON — a key that itself contains a literal ':' (rare, but real): --match literal-key=...
# instead of key=..., so it's matched as one property name, not split into path segments.
dotnet run --project src/ConfigTransform.Json -- set \
  --resource BillingApi.Core/appsettings.json \
  --match literal-key=Logging:LogLevel:Default --set value=Warning

# JSON — array of objects: --match key=<array> locates the array, any further --match
# <field>=<value> (not key=/literal-key=) becomes an $elemMatch condition on the item.
dotnet run --project src/ConfigTransform.Json -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
  --match key=ConnectionStrings --match name=Prod --set connectionString="Data Source=new;..."

# JSON — compound conditions (more than one field needed to identify the item uniquely).
dotnet run --project src/ConfigTransform.Json -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
  --match key=Rules --match role=Admin --match env=Production --set enabled=true

# JSON — a second call against the same array, different conditions, same overlay file: appends
# a second $elemMatch patch rather than colliding with the first (see FIELD_AUTHORING_DESIGN.md).
dotnet run --project src/ConfigTransform.Json -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
  --match key=Rules --match role=Viewer --set enabled=true

# JSON — no match found: creates a new item instead of erroring (an upsert, Mongo's own term for
# the same idea) -- the new item's identity comes from the --match conditions themselves.
dotnet run --project src/ConfigTransform.Json -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
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
`src/ConfigTransform.Json/JsonElemMatchResolver.cs`; shared target-layer resolution in
`src/ConfigTransform.Core/SetTargetResolver.cs`; orchestration in each tool's own `CliRunner.cs`.

## What "merge" means

**XML**: base file, loaded once. Every patch in the resolved chain, in order (outermost/base-most
first), applied via `Microsoft.Web.Xdt` to that same document. See
`src/ConfigTransform.Xml/XmlLayerMerger.cs`.

**JSON**: base + every patch in the resolved chain, in order, loaded as layered sources via
`Microsoft.Extensions.Configuration`'s own `ConfigurationBuilder`, then flattened back to a
single JSON document. Two things worth knowing, both inherent to how `IConfiguration` works,
documented in full in `src/ConfigTransform.Json/JsonLayerMerger.cs`:
- An overlay array does not replace the base array wholesale — it overrides by index, so any
  base-layer indices beyond what the overlay specifies survive untouched.
- Types (bool/number/string) are inferred from the flattened value to avoid turning
  `"enabled": false` into `"enabled": "false"`.
- A patch containing a `set`-written (or hand-written) `$elemMatch` array-of-objects patch (see
  the `set` section above) is resolved to a real position and rewritten *before* it reaches
  `Microsoft.Extensions.Configuration` — a pre-processing pass (`JsonElemMatchResolver.Rewrite`)
  that only runs when a patch in the chain actually contains one, resolving each patch's
  `$elemMatch` entries against the *accumulated* merge of every prior patch (not the base alone);
  every other merge takes the original, unmodified code path.

For `--diff`, the same base file is also rendered with *no* patches applied (through the identical
merge code path, to avoid spurious serialization-only differences) and the two are compared via
`GitDiff` (`ConfigTransform.Core`). See `src/ConfigTransform.Xml/XmlCliRunner.cs`,
`src/ConfigTransform.Json/JsonCliRunner.cs`, and the shared orchestration in
`src/ConfigTransform.Core/CliRunner.cs`.
