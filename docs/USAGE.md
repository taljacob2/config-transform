# CLI usage

`configtransform` (the `ConfigTransform.Cli` dotnet tool) resolves/previews/writes a merged
result, built around self-describing `configtransform.json` layers
(`docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`) — one per layer directory under `.configtransform/`,
addressed by `--client`/`--environment`, spanning every resource (project config file) that layer
touches, in **any** registered format, in one call. Each resource is dispatched to the right merge
engine by its own file extension — `.config`/`.xml` via `Microsoft.Web.Xdt`, `.json` via
`Microsoft.Extensions.Configuration`, `.env` via a dependency-free flat `KEY=VALUE` merge
(`ConfigTransform.Env`), `.yaml`/`.yml` via `NetEscapades.Configuration.Yaml`/`YamlDotNet`
(`ConfigTransform.Yaml`) — so a mixed-format layer resolves with no skipping and no separate tool
invocation per format; a resource whose extension no registered engine handles is reported, not
silently dropped (see "Single resource vs. every resource" below). The `set` verb
(below) works the same way, dispatching by the *target* resource's own extension; what it actually
supports differs by format for reasons that come from the format itself, not an arbitrary gap.

```
--resource, -r <repo-root-relative path>   optional for a resolve/--list — omit for every resource the layer touches; required for `set`
--client, -c <ClientName>                  optional everywhere — requires --environment (no client-only layer); see "Resolving" below
--environment, -e <EnvironmentName>        optional everywhere — see "Resolving" below
--output, -o <path>                        required for a real run (omit only with --dry-run/--diff) — a file with --resource, a directory without it
--dry-run                                  print the fully merged result to stdout; nothing written to disk
--diff                                     print a unified diff (unpatched vs. merged) via `git diff --no-index`; nothing written to disk
--list                                     show a layer's resources (--client/--environment), or a tree-wide reverse lookup (--resource) — see below
help, --help, -h                           print the help page (see "Getting help" below) — also the default with no arguments at all
init                                       scaffold a .configtransform/ tree — a different verb, see "init" below
```

Every flag that takes a value also accepts the short form shown above (`-r`, `-c`, `-e`, `-o`) —
meant for typing a command out by hand; scripts and CI can keep using the long forms for
readability in a pipeline log. Both forms can be mixed freely in the same invocation.

Every path — `--resource`'s value, and everything a `configtransform.json` itself declares
(`extends`, `resources[].path`, `resources[].patch`) — is **repo-root-relative**, resolved
against the current working directory. Run the tool from the repository root, the same way CI
does.

## Getting help

Running `configtransform` with **no arguments at all** prints a help page and exits 0 — it's the
default, not an error, specifically so a new user's first, uninformed invocation actually teaches
them something instead of just failing. The same page is reachable anytime via a bare `help`, or
`--help`/`-h`, added anywhere in an otherwise-normal invocation — not just leading; `configtransform
-e Production -r App.config help` shows help exactly like `configtransform help` does (all three
forms are recognized only in flag position, so a value some other flag is consuming that happens
to equal `help` or `-h` is never mistaken for the flag) — and it wins over every other flag,
including what would otherwise be a validation error (`configtransform --client Acme --help` shows
help, not "--environment is required.").

The page itself is a short, tldr-style cheat sheet, not the full reference this document is —
a `USAGE` summary, a `COMMON COMMANDS` quick-reference table, and, for every command, one easy
example plus one more advanced example (a mixed-format single-call resolve, a `--list --resource`
reverse lookup, a `set` with a compound `$elemMatch` condition). The list of supported resource
extensions it prints is read from the tool's own real, registered format engines, not a separate
hardcoded copy — it can't drift from what the binary actually handles.

Every validation error the tool prints (missing `--output`, `--client` without `--environment`,
an unrecognized flag, and so on) is followed by a second `Try:` line with a concrete corrected
example for that specific mistake, rather than pointing you at the full help page — e.g. omitting
`--output` on a real run prints:

```
Error: --output is required for a real run (omit only with --dry-run or --diff).
Try: add --output <path>, or pass --dry-run/--diff to preview instead of writing.
```

An unrecognized flag gets the same treatment, but as a spelling suggestion when one fits: a typo
within edit distance 2 of a known flag (e.g. `--otuput`, `--lsit`, `--dif`) prints
`Try: did you mean --output?` instead of the generic hint; anything farther off falls back to
`Try: configtransform --help to see every valid flag.`

**Running via `dotnet tool run` swallows `--help`/`-h` before it reaches `configtransform`.**
`dotnet tool run <name> [<toolArguments>...] [options]` treats `--help`/`-h`/`-?` as its *own*
option (you'll see `dotnet`'s "Run a local tool" help instead of ours) — this is a `dotnet` CLI
parsing behavior, not something `configtransform` can intercept. Use the `--` separator to force
everything after it to be forwarded as tool arguments instead:
`dotnet tool run configtransform -- -e Production -r App.config --help`. The bare `help` verb
isn't affected by this — `dotnet tool run configtransform -- -e Production -r App.config help`
and even without the `--` separator both reach `configtransform` and print its help normally,
since `dotnet tool run` doesn't treat a bare `help` token as one of its own options.

## Resolving `--client`/`--environment` to a layer

Both are optional, uniformly across every command (a resolve/dry-run/diff/real-run, `--list`,
`set`) — `--client` without `--environment` is the only combination that's ever an error (there's
no client-only layer):

- Neither given → the base file itself, no layer at all — its own real, meaningful case (e.g.
  `configtransform --resource App.config --dry-run` shows the file completely unpatched), not
  just an internal detail `set` happens to use.
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
processes **every resource the resolved layer's chain touches, across every registered format**,
in one invocation:

- `--dry-run`/`--diff` print each resource's own result, labeled `=== <path> ===`.
- A real run requires `--output <directory>` (not a file) and writes one file per resource, each
  resource's own repo-root-relative path mirrored under that directory. If `--output` already
  exists as a plain file — most naturally when the layer has only one resource and it happens to
  share that exact name — the run fails fast with `Error: --output '<path>' already exists as a
  file, but --resource was omitted...` rather than a raw filesystem exception; the fix is either
  `--resource <path>` (to target and overwrite that one file directly) or a different/empty
  `--output` directory. This is deliberately not auto-detected from "only one resource found" —
  that would make the same command's behavior depend on how many resources happen to be in the
  layer at the time, silently changing the day a second resource is added.
- A resource whose extension no registered format engine handles is skipped with a note on stderr
  (e.g. "Skipped 1 resource(s) with no registered format handler; supported formats: .config,
  .xml, .json, .env, .yaml, .yml.") — not an error, and not silently dropped. This is the only remaining skip case:
  every currently-supported format resolves in the same call, with no note at all, which is the
  actual capability CLI unification delivers over the old two-tool split.
- If `--environment`/`--client` names a layer with no `configtransform.json` at all (most often a
  typo), the run still isn't an error — a missing target layer is tolerated the same as any other
  missing overlay — but the message names the exact path it looked for instead of the generic
  "no resources" note: `(no configtransform.json found for --environment 'test' -- expected at
  '.configtransform/Environments/test/configtransform.json'. Try: check the spelling, or run
  'configtransform init' to scaffold it.)`. The generic `(no resources with a registered format
  handler at this layer)` message is reserved for a layer that genuinely exists but declares no
  resources, or whose resources' extensions have no registered engine.

Naming that one exact resource with `--resource` instead is always an error if no engine handles
its extension (rather than a stderr note) — you named that exact file, so silently producing
nothing would be worse than failing loudly.

## `--list`

Two modes:

- **Given `--client`/`--environment`** (client optional, environment required): shows that
  layer's own `extends` and every resource it touches, as its full chain in real application
  order — `base` first, then every layer outermost-first, each one either `patched in` or
  `not patched in`, connected by `↓`:
  ```
    OrderProcessor.Framework/App.config
      base                                                             (always applied)
        ↓
      .configtransform/Environments/Production/configtransform.json    patched in
        ↓
      .configtransform/Clients/Acme/Production/configtransform.json    patched in
  ```
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
dotnet run --project src/ConfigTransform.Cli -- \
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production --dry-run

# See exactly what Acme's overrides change vs. the unpatched chain
dotnet run --project src/ConfigTransform.Cli -- \
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production --diff

# Real run, one resource, as CI invokes it
dotnet run --project src/ConfigTransform.Cli -- \
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production --output publish/App.config

# BillingApi.Core/appsettings.json — same tool, dispatched to the JSON engine by its extension
dotnet run --project src/ConfigTransform.Cli -- \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production --diff

# Every resource this layer touches, ANY format, one call, --output as a directory --
# the real capability CLI unification delivers over the old per-format tool split
dotnet run --project src/ConfigTransform.Cli -- \
  --client Acme --environment Production --output publish/

# --list for one layer — resources, extends, and the full base->Environment->Client chain
dotnet run --project src/ConfigTransform.Cli -- \
  --list --client Acme --environment Production

# --list --resource — reverse lookup: every layer in the tree that patches this one project
dotnet run --project src/ConfigTransform.Cli -- \
  --list --resource OrderProcessor.Framework/App.config

# Short flags, for typing out by hand
dotnet run --project src/ConfigTransform.Cli -- -r BillingApi.Core/appsettings.json -c Acme -e Production --diff
```

On every single-resource run, the tool prints an explicit found/not-found line for the base file
and each layer in the chain (`CONFIG_MANAGEMENT.md` §5.1) before doing anything else. A missing
base file is a fatal error; a layer that doesn't list the resource, or lists it with no `patch`,
is reported but not fatal. A `patch` that *is* declared but whose file doesn't exist on disk is a
different case — an explicit, broken reference — and is always an error, the same way a missing
base file is.

`--dry-run` and `--diff` never write to the base file's own location, or anywhere else on
disk — verified directly by `ConfigTransform.Cli.Tests`' `CliRunnerTests`, not just by code
inspection. `--diff` prints `(no changes)` rather than an empty diff when the resolved chain has
nothing to apply.

## `init` — scaffold a tree

Creates `.configtransform/Environments/<Env>/` and `.configtransform/Clients/<Client>/<Env>/`
layers directly, instead of the first one coming into existence only as a side effect of a first
`set` — see `docs/INIT_COMMAND_DESIGN.md` for the full design and rationale. A different verb,
like `set` — `configtransform init ...`, not a flag on the resolve/list command:

```
init --environment, -e <EnvName>           repeatable — every environment to create
     --client, -c <ClientName>             repeatable — every client to create (requires --environment)
     --resource, -r <path>                 repeatable — explicit resources; skips scanning entirely if given
     --scan-root <dir>                     where to scan for candidate resources (default: repo root)
     --yes                                 accept every scanned candidate without asking
     --no-scan                             don't scan — requires at least one --resource
     --template                            the one canned starter tree — a bare switch, mutually exclusive with every flag above
     --dry-run                             print what would be written; nothing written to disk
```

**Two modes, chosen up front, never a blend of flags and prompts:**

- **Interactive** — no `init`-specific flag given at all, and stdin is a real terminal. A plain
  sequential form (`Console.ReadLine()`, no TUI): scans for candidate resources and asks which to
  manage (by index, `all`, or `none`), then which environments (required, comma-separated), then
  which clients (optional, comma-separated) — then echoes the exact file list and writes it.
- **Quiet** — any `init`-specific flag given, or stdin isn't a terminal (CI-safe by default: it
  never blocks on a prompt it can't get an answer to). `--environment` becomes required in this
  mode (there's no one left to ask); everything else comes from flags. If no `--resource` was
  given and the repo scan finds candidates but stdin *is* a real terminal, it still asks the
  resource checklist specifically — only the environment/client questions are skipped because
  flags already answered them.

**Scanning** walks the repo (or `--scan-root`) for every file whose extension a registered format
engine handles, excluding only `.git/`, `.configtransform/`, `bin/`, `obj/`, `node_modules/` at
any depth — never a filename/content heuristic (this tool never assumes what "looks like" config,
the same stance `CLAUDE.md`'s core concepts take everywhere else). The checklist (or `--yes`) is
where a human decides which candidates are real resources.

**Idempotent**: re-running against a tree `init` or `set` already touched merges in what's new (a
new client, a newly-added resource) without touching an existing `patch`/`extends` reference —
the same convention `set` already established.

```bash
# Try it immediately: a canned Production/Test x Client-A/Client-B tree, one demo resource whose
# "message" is overridden at every layer, naming that layer -- runnable with no other setup.
dotnet run --project src/ConfigTransform.Cli -- init --template

dotnet run --project src/ConfigTransform.Cli -- \
  --client Client-A --environment Production --resource configtransform-template.json --diff

# Quiet/CI-safe: scaffold Production+Test for Acme, scanning the repo for candidates and
# accepting every one found (skip --yes to get an interactive checklist instead, in a real terminal)
dotnet run --project src/ConfigTransform.Cli -- \
  init --environment Production --environment Test --client Acme --yes

# Quiet, explicit resources -- no scanning at all
dotnet run --project src/ConfigTransform.Cli -- \
  init --environment Production --client Acme \
  --resource OrderProcessor.Framework/App.config --resource BillingApi.Core/appsettings.json

# Preview without writing
dotnet run --project src/ConfigTransform.Cli -- init --template --dry-run
```

## `set` — author an overlay field

Writes a field directly — no hand-written XDT for XML, no hand-edited nested JSON for JSON — see
`docs/FIELD_AUTHORING_DESIGN.md` for the full design and why it works this way. The flag shape is
the same regardless of the target resource's format — `set` dispatches to the right authoring
engine by `--resource`'s own extension, the same way every other command dispatches its merge:

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
at a newly-authored patch file, named `patch-<resource path, "/" replaced with "-">.<xml|json|env|yaml>`
(the patch extension always reflects the *transform's own* format — `.xml` for an XDT transform
regardless of the base resource's own extension, `.json` for JSON, `.env` for `.env`, `.yaml` for
YAML regardless of whether the resource itself is `.yaml` or `.yml`) sitting alongside the
`configtransform.json` that references it — a single shared `configtransform.json` can (and
routinely will) end up listing an `.xml`-patched, a `.json`-patched, an `.env`-patched, and a
`.yaml`-patched resource side by side, each authored by its own engine, entirely independently.
**When it's already listed with a
`patch`**, re-running `set` for the same `--match` updates that existing patch file in place
rather than duplicating it or creating a second one — idempotent, same as before.

**What's actually supported differs by format, in ways that come from the format itself, not an
arbitrary implementation gap** — see `docs/FIELD_AUTHORING_DESIGN.md`'s "Open items" for the
full reasoning behind each:

- **XML** (`.config`/`.xml` resources): covers updating a field that already exists somewhere in
  the resolved document — the common case (overriding an existing value for one
  environment/client). Creating a genuinely new element (`Insert`) is not implemented:
  `--match`/`--set` don't carry the new element's tag name or parent location, and there's nothing
  in an existing document to derive them from for a true insert, so `set` refuses rather than
  guessing. For a singleton element with no identifying attribute at all (`customErrors`,
  `compilation`, `httpRuntime`...), `--match tag=<ElementName>` matches by element name alone and
  writes no `xdt:Locator` at all — mirroring real XDT's own default-match behavior for exactly
  this case. `tag` is a reserved coordinate, not a real attribute.
- **JSON** (`.json` resources): covers a single key path (nested or top-level) — both updating an
  existing key *and* creating a brand-new one, since JSON has no XDT-style Transform/Locator
  distinction to make (any layer can introduce a key; `set` just writes it) — **and matching or
  creating an item inside an array of objects**, via a `$elemMatch`-style overlay (below).
  `Microsoft.Extensions.Configuration`'s JSON provider merges arrays purely by index, with no
  native concept of matching a field's value the way XDT's `Locator` does for XML, so this isn't a
  direct port of XML's mechanism — see `docs/FIELD_AUTHORING_DESIGN.md`'s "JSON / YAML" section
  and decision log for the full reasoning.
- **`.env`** (`.env` resources): the simplest of the four — a `.env` file is always flat, so
  there's no nested-path disambiguation to make (unlike JSON) and no update-vs-insert branch
  (unlike XML). `--match key=<NAME>` (bare shorthand defaults to `key=`) identifies the variable,
  `--set value=<value>` (bare shorthand defaults to `value=`) is its new value; the key is
  validated against the real POSIX env-var-name grammar before writing.
- **YAML** (`.yaml`/`.yml` resources): covers a single key path (nested or top-level) — both
  updating an existing key and creating a brand-new one — the same model as JSON's own plain-field
  case, since YAML shares JSON's exact `:`-separated nesting and the same `key=`/`literal-key=`
  disambiguation for a literal key that happens to contain a colon. **Matching an item inside an
  array of objects is not implemented for YAML** — a `--match` with more than one coordinate
  refuses with a clear "not yet supported" message rather than guessing, the same posture XML
  takes for its own unimplemented array-of-objects matching; see
  `docs/FIELD_AUTHORING_DESIGN.md`'s "JSON / YAML" section for why this is scoped out of YAML's
  first version specifically.

```bash
# XML, appSettings — the simple case: one identity attribute, one value attribute.
# Bare --match/--set default to key=/value=, verified against the real file before being used.
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production --match ApiUrl --set https://acme.example.com

# XML, connectionStrings — one identity attribute (name), two value attributes at once.
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource OrderProcessor.Framework/App.config \
  --client Acme --environment Production \
  --match name=Prod --set connectionString="Data Source=prod;..." --set providerName=System.Data.SqlClient

# XML, no --client/--environment: edits the base file directly, no xdt: anything.
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource OrderProcessor.Framework/App.config \
  --match key=ApiUrl --set value=https://new-default.example.com

# XML, customErrors — a singleton element with no identifying attribute at all: --match tag=...
# matches by element name alone, writing no xdt:Locator (mirrors real XDT's own default match).
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource OrderProcessor.Framework/Web.config \
  --environment Production --match tag=customErrors --set mode=RemoteOnly

# JSON — a nested key, ':'-separated (matches Microsoft.Extensions.Configuration's own
# flattening convention, and ASP.NET Core's own command-line config override syntax) — not '.',
# since dots commonly appear literally in real setting names ("api.timeout.ms"-style).
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
  --match key=Logging:LogLevel:Default --set value=Warning

# JSON — creating a brand-new key: works the same as updating one (no Insert-style gap for JSON).
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production --match key=Features:EnableBeta --set value=true

# JSON — a key that itself contains a literal ':' (rare, but real): --match literal-key=...
# instead of key=..., so it's matched as one property name, not split into path segments.
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource BillingApi.Core/appsettings.json \
  --match literal-key=Logging:LogLevel:Default --set value=Warning

# JSON — array of objects: --match key=<array> locates the array, any further --match
# <field>=<value> (not key=/literal-key=) becomes an $elemMatch condition on the item.
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
  --match key=ConnectionStrings --match name=Prod --set connectionString="Data Source=new;..."

# JSON — compound conditions (more than one field needed to identify the item uniquely).
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
  --match key=Rules --match role=Admin --match env=Production --set enabled=true

# JSON — a second call against the same array, different conditions, same overlay file: appends
# a second $elemMatch patch rather than colliding with the first (see FIELD_AUTHORING_DESIGN.md).
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
  --match key=Rules --match role=Viewer --set enabled=true

# JSON — no match found: creates a new item instead of erroring (an upsert, Mongo's own term for
# the same idea) -- the new item's identity comes from the --match conditions themselves.
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource BillingApi.Core/appsettings.json \
  --client Acme --environment Production \
  --match key=Rules --match role=Auditor --set enabled=true

# .env -- one identity, one value, same shape as XML's simple case. Bare --match/--set default
# to key=/value= here too.
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource OrderProcessor.Framework/.env \
  --client Acme --environment Production --match API_URL --set https://acme.example.com

# YAML -- a nested key, same ':'-separated model as JSON (see above); updating and creating both
# go through the same path, no Insert-style gap for YAML either.
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource NotificationWorker/settings.yaml \
  --client Acme --environment Production \
  --match key=Logging:LogLevel:Default --set value=Warning

# YAML -- an array of objects: refused as not yet supported, rather than guessed at.
dotnet run --project src/ConfigTransform.Cli -- set \
  --resource NotificationWorker/settings.yaml \
  --client Acme --environment Production \
  --match key=Rules --match role=Admin --set enabled=true
# Error: matching an item inside a YAML array of objects is not yet supported...
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
`src/ConfigTransform.Core/SetTargetResolver.cs`; the dispatcher orchestration that picks between
them by extension is `src/ConfigTransform.Core/SetRunner.cs`, with the actual format→engine
registration in `src/ConfigTransform.Cli/FormatEngines.cs`.

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

**Dispatch, for both the read path and `set`**: `ConfigTransform.Cli/FormatEngines.cs` registers
one `FormatEngine` per format (extensions owned, merge function, field-author function, patch-file
extension) into a `FormatEngineRegistry`; `ConfigTransform.Core/CliRunner.cs` picks the matching
engine for a given resource by its own file extension, for both a single `--resource` and the
omitted-`--resource` case (routing each resource in the union independently — see "Single resource
vs. every resource" above). `ConfigTransform.Core/SetRunner.cs` does the same for `set`. Neither
`CliRunner` nor `SetRunner` knows anything about XML or JSON specifically — `XmlLayerMerger`/
`JsonLayerMerger`/`XmlFieldAuthor`/`JsonFieldAuthor` are unchanged internal engines, exactly as
before unification; only how they're selected and invoked moved into one shared dispatcher.

For `--diff`, the same base file is also rendered with *no* patches applied (through the identical
merge code path, to avoid spurious serialization-only differences) and the two are compared via
`GitDiff` (`ConfigTransform.Core`), which shells out to `git diff --no-index` against two
throwaway temp files. `GitDiff.Render` strips git's own 4-line file-identity header (`diff --git
a/... b/...`, `index ...`, `--- a/...`, `+++ b/...`) from the output before returning it — those
`a`/`b` paths are always OS temp file paths, meaningless to the end user and not real file
identity, unlike the resource path the CLI already shows above the diff (`Resolving '<path>'` for
one resource, `=== <path> ===` for every resource).
