# Layer schema (`configtransform.json`)

Authoritative field-by-field reference for `configtransform.json` — the self-describing overlay
file this tool reads (`docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md` has the full design and rationale;
this document is the schema reference, kept in sync with it). One per **layer directory** under
`.configtransform/` (the `.configtransform/` root name is itself configurable per repo; see
[`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) §10.5) — `.configtransform/Environments/<Env>/
configtransform.json` and `.configtransform/Clients/<Client>/<Env>/configtransform.json`.

This filename predates the design it now describes — `manifest.json` (one per project, a
`directory` + `files[]` declaration) has been fully replaced, not kept alongside; there's nothing
called a "manifest" left in the tool's own vocabulary.

## Shape

```json
{
  "extends": ".configtransform/Environments/Production/configtransform.json",
  "resources": [
    { "path": "OrderProcessor.Framework/App.config", "patch": ".configtransform/Clients/Acme/Production/patch-OrderProcessor.Framework-App.config.xml" }
  ]
}
```

## Fields

| Field | Required | Description |
|---|---|---|
| `extends` | no | The layer to start from — another `configtransform.json`'s own fully-resolved output becomes this layer's starting point instead of the raw base files. An Environment layer has no `extends`; a Client layer typically `extends` the matching `Environments/<Env>/configtransform.json`, though nothing enforces that beyond `set`'s own default when it creates a new Client layer. Applies to the whole file, not per-resource. |
| `resources` | yes | What **this layer itself** adds on top of `extends` (or the raw base files, with no `extends`) — one entry per project this layer actually touches. Can be empty (`[]`) — a layer that only exists to declare `extends`, with nothing of its own to add, is a real and expected shape (see "The accepted cost" below). |
| `resources[].path` | yes | The project's real config file, **repo-root-relative** — the same identity at every layer, `extends` or not. This is what a resource is addressed by everywhere: `--resource`, `--list --resource`'s reverse lookup, every `resources[]` entry across the whole tree. |
| `resources[].patch` | no | The overlay file that overrides this specific resource at this layer — a real overlay file, same format/semantics as this tool has always used (an XDT transform for XML, plain or `$elemMatch`-bearing JSON for JSON). **Repo-root-relative**, the same convention as `extends`/`path`, even though in practice it almost always lives in the same directory as the `configtransform.json` referencing it — see "Path convention" below. Omitted entirely for a resource this layer doesn't touch; it just passes through untouched (from `extends`, or from the raw base file if there's no `extends`). |

## Path convention: everything is repo-root-relative, no exceptions

`extends`, `resources[].path`, and `resources[].patch` are **all** resolved against the repo
root (the tool's working directory) — not against the `configtransform.json` file's own
directory. This is a deliberate, explicit design decision
(`docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`'s "Settled decisions" #4): a `patch` field almost
always names a file sitting right next to the `configtransform.json` referencing it, and it would
be shorter to write as just a filename — that was rejected specifically because "some fields work
one way, others work another" is a real cognitive tax on whoever reads or writes the file, more
costly than the repetition it would have saved. This still avoids the long, fragile `../../../../`
chains a file-relative convention (Kustomize's own) accumulates in a deep tree — repo-root-relative
gets full consistency *and* short paths.

## Which engine handles a resource

Inferred from `resources[].path`'s file extension — `.config`/`.xml` → the XML engine
(`ConfigTransform.Xml`), `.json` → the JSON engine (`ConfigTransform.Json`) — never a declared
field. Consistent with this tool's existing stance that format is the only real constraint, never
redundantly declared (`CLAUDE.md`'s "Core concepts"). A `configtransform.json` can freely mix
resources of both formats in one file — the unified `configtransform` CLI (`ConfigTransform.Cli`)
dispatches each one to the right engine and resolves the whole layer in a single call; a resource
whose extension no registered engine handles is reported and skipped, never silently dropped (see
`USAGE.md`'s "Single resource vs. every resource" section).

## Full worked example: two projects, one client

```
.configtransform/
  Environments/
    Production/
      configtransform.json
      patch-OrderProcessor.Framework-App.config.xml
      patch-BillingApi.Core-appsettings.json.json
  Clients/
    Acme/
      Production/
        configtransform.json
        patch-OrderProcessor.Framework-App.config.xml   # only Acme overrides this in Production
```

```json
// .configtransform/Environments/Production/configtransform.json
{
  "resources": [
    { "path": "OrderProcessor.Framework/App.config", "patch": ".configtransform/Environments/Production/patch-OrderProcessor.Framework-App.config.xml" },
    { "path": "BillingApi.Core/appsettings.json", "patch": ".configtransform/Environments/Production/patch-BillingApi.Core-appsettings.json.json" }
  ]
}
```

```json
// .configtransform/Clients/Acme/Production/configtransform.json
{
  "extends": ".configtransform/Environments/Production/configtransform.json",
  "resources": [
    { "path": "OrderProcessor.Framework/App.config", "patch": ".configtransform/Clients/Acme/Production/patch-OrderProcessor.Framework-App.config.xml" }
  ]
}
```

`BillingApi.Core/appsettings.json` isn't named anywhere in Acme's file — it passes through with
just the Environment layer's own change, exactly like "missing overlay ≠ error"
(`CONFIG_MANAGEMENT.md` §5.1) — nothing about this design changes that rule.

## The accepted cost: "no override" is no longer free

Named explicitly in `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`, since it's the direct trade for
the discoverability win this design brings: under the old `manifest.json`-based fixed rule, a
client with no override for some environment was pure silence — no file, nothing to write,
nothing to read, and the Environment layer's content still applied automatically. Under this
design, a Client layer that should inherit the Environment layer's content but has nothing of its
own to add still needs its own `configtransform.json` on disk — even one whose `resources` list is
empty, just declaring `extends` — because something has to say the chain exists and where it
starts. A Client layer file that's missing *entirely* falls through to nothing (the raw base
file), not to the Environment layer, even if the Environment layer itself has real content for
that resource. `set` handles the common case of this automatically (creating that file, with the
right `extends`, the first time it writes anything for a client/environment combination), but a
client that genuinely has zero overrides of its own and was never `set` against still needs this
file created by hand (or scripted) if it should inherit the Environment layer.

## No coupling to any language, ecosystem, or `TargetFramework`

Nothing in this schema — or anywhere else in this tool — assumes a `.csproj`, a specific
`TargetFramework`, or even a .NET project. `resources[].path` is genuinely just a path to a real
XML or JSON file; the tool never opens, parses, or validates anything about the project that file
belongs to (`CliRunner`/`LayerChain`, in `ConfigTransform.Core`). A `.NET` project on net35 works
exactly the same as one on net8.0 — and the same is true for a Node.js, Angular, React, or Flutter
project's own JSON config, since `resources[].path` can point anywhere in the repo. The only real
constraint is the config file's *format*: XML or JSON today, not the ecosystem or TFM it happens
to live in. See `CLAUDE.md`'s "Core concepts" for the fuller version of this claim.

## Web.config

No special handling — Web.config is XML, resolved through the same engine as App.config. See
[`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) §5.2 for the one real caveat: on ASP.NET Web
Application projects with an existing native MSBuild Web.config transform
(`Web.Debug.config`/`Web.Release.config`), this tool's step must run *after* that native pipeline
step in the deploy workflow.
