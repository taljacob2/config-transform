# Self-describing overlays (`configtransform.json`) — design

**Status: proposed, not implemented, partially decided.** This document exists to get the open
questions on the table before any code changes, the same way `FIELD_AUTHORING_DESIGN.md` did for
`set`. Three of the questions below are now settled by the repo owner — **file format is JSON**
(`configtransform.json`, not `.yaml`); **scope is one file per client×environment, spanning
every project/format that client+environment touches** (meaning `ConfigTransform.Xml`/
`ConfigTransform.Json` unifying into one CLI dispatcher is an accepted, first-class consequence,
not a side effect to avoid); and **each resource carries its own patch directly, as a field on
the same entry**, not two separate lists cross-referenced by convention. The remaining questions
below are still open — this is not a completed design waiting on an implementation pass yet. Do
not implement against this document until every open decision below is resolved.

## Origin and problem statement

Raised directly by the repo owner, comparing this tool's manifest+fixed-layering model against
Kustomize's self-describing `kustomization.yaml` overlays (a real production tree was reviewed
for contrast). The core objection to the current design: `docs/CONFIG_MANAGEMENT.md` §9 already
argues the fixed base→Environments→Clients rule is *simpler* than Kustomize's self-describing
overlays, because this tool has no selection/composition decision to make (always exactly one
chain, per file) — but the repo owner's position, after that argument was made, is that
self-describing overlays are still easier to understand and add to, independent of the
file-count/boilerplate argument. That's a legitimate, different axis (authoring ergonomics, not
line-count accounting) and is taken at face value here, not re-litigated.

**What actually changes vs. what doesn't**: this proposal is scoped to *composition and
declaration* — which files exist, how they're addressed, how the tool finds them. It is **not**
a proposal to change how any individual overlay file is written or merged: XDT's
`Transform`/`Locator` semantics for XML, `Microsoft.Extensions.Configuration`'s merge behavior
and the `$elemMatch` mechanism for JSON (`docs/FIELD_AUTHORING_DESIGN.md`) are all unchanged. A
"patch" file under this design is byte-for-byte the same kind of file an `Environments/`/
`Clients/` overlay file is today. This keeps the blast radius contained to
`ConfigTransform.Core`'s manifest/discovery/layer-resolution layer and each tool's CLI
orchestration — not the merge engines.

## Proposed shape

One `configtransform.json` per **layer directory** — where "layer directory" replaces both
`manifest.json` (today, one per project) and each individual `Environments/<Env>.<ext>` /
`Clients/<Client>/<Env>.<ext>` overlay file (today, one per project × file × environment/client).
Decided: **one file spans every project and format that client+environment touches** (below,
"Settled decisions" #2) — a layer directory declares what it composes as a single list, each
entry pairing a resource with its own patch directly. Two fields, deliberately separated:

```json
{
  "extends": "../../../Environments/Production/configtransform.json",
  "resources": [
    { "path": "OrderProcessor.Framework/App.config", "patch": "patch-OrderProcessor.Framework-App.config.xml" }
  ]
}
```

- `extends` (optional): the layer to start from — another `configtransform.json`, whose own
  fully-resolved output becomes this layer's starting point instead of the raw base files. This
  is what replaces the fixed base→Environments→Clients rule: an Environment layer has no
  `extends` (it starts from base files directly); a Client layer typically `extends` the matching
  `Environments/<Env>/configtransform.json` — but doesn't have to, for a project with nothing
  shared across clients in that environment (see "Environment layer stays optional, per project"
  below). Applies to the whole file, not per-resource — see the note below on why that doesn't
  lose per-project granularity.
- `resources`: what **this layer itself** adds on top of `extends` (or on top of the raw base
  files, with no `extends`) — one entry per project this layer actually touches, each pairing a
  `path` with its own optional `patch` directly, no separate list to cross-reference. `path` is
  always the real base file's own **repo-root-relative path** (see "Path convention" below) —
  the *same* identity at every layer, `extends` or not, which is what makes each patch
  unambiguous: a project keeps the same path as its identity no matter how many layers deep it's
  being patched, and resolving `extends` first means a Client layer's `resources` only ever needs
  to name the projects *it itself* changes, exactly mirroring "no override = no overlay file"
  today.
- `patch` (optional, per resource entry): the overlay file that overrides this specific resource
  at this layer — a real overlay file, same format/semantics as today's `Environments/`/
  `Clients/` files (XDT transform for XML, plain or `$elemMatch`-bearing JSON for JSON). Omitted
  entirely for a resource this layer doesn't touch — it just passes through untouched (from
  `extends`, or from the raw base file if there's no `extends`).

**Why `extends` is separate from `resources`, not `resources` pointing at another
`configtransform.json` directly** (the shape this document originally proposed, before the repo
owner asked for each resource to carry its own patch explicitly): once a resource entry can pair
a path with a patch directly, a `resources` entry pointing at *another* `configtransform.json`
that itself composes multiple projects has nowhere unambiguous to put that one patch — which of
the composed file's several projects would it apply to? Separating "where do I start from"
(`extends`, one per file, resolved first) from "what do I personally add" (`resources`, each
entry self-identifying by the project's own real path) removes that ambiguity entirely, and keeps
the "each resource has its own patch, right on it" property the repo owner actually asked for.

Full worked example — same two-project, one-client scenario as `docs/MANIFEST_SCHEMA.md`'s own
"multiple config files" example, laid out under this design:

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
        patch-OrderProcessor.Framework-App.config.xml     # only if Acme overrides this in Production
```

```json
// Environments/Production/configtransform.json
{
  "resources": [
    { "path": "OrderProcessor.Framework/App.config", "patch": "patch-OrderProcessor.Framework-App.config.xml" },
    { "path": "BillingApi.Core/appsettings.json", "patch": "patch-BillingApi.Core-appsettings.json.json" }
  ]
}
```

```json
// Clients/Acme/Production/configtransform.json
{
  "extends": "../../../Environments/Production/configtransform.json",
  "resources": [
    { "path": "OrderProcessor.Framework/App.config", "patch": "patch-OrderProcessor.Framework-App.config.xml" }
  ]
}
```

`BillingApi.Core/appsettings.json` isn't named anywhere in Acme's file — it passes through with
just the Environment layer's own change, exactly like today's "missing overlay ≠ error" rule
(`CONFIG_MANAGEMENT.md` §5.1) — nothing here changes that. Note `path` in Acme's file is the
*same* `OrderProcessor.Framework/App.config` identity used in the Environment layer, not a
reference into the composed file — `extends` already established that this layer starts from
Environment's output, so `resources` here only needs to say which project it's further patching.

## Settled decisions

Confirmed directly by the repo owner — treat these as fixed, not open to silent re-litigation.

1. **File format: JSON, not YAML** (`configtransform.json`). This tool has zero YAML parsing
   anywhere today (`System.Text.Json` covers everything, including the current `manifest.json`)
   — YAML here would have meant a new dependency (`YamlDotNet` or similar) purely for tooling
   metadata, not for any config file this tool actually merges (`CONFIG_MANAGEMENT.md` §5.5
   already treats YAML-as-a-*merged-format* as a distinct, separately-still-not-needed question —
   this would have been YAML parsing for an unrelated reason, the manifest shape). The "looks
   like `kustomization.yaml`" motivation was real but aesthetic once the shape itself is adopted —
   the extension doesn't change what problem this design solves.
2. **Scope: one file per client×environment, spanning every project and format it touches** — not
   scoped to one project the way `manifest.json` is today. Direct consequence, accepted as
   first-class rather than a side effect: something has to dispatch each resource/patch to the
   right merge engine by file extension, since `ConfigTransform.Xml`/`ConfigTransform.Json` are
   separate tools today, each assuming everything it touches is its own format — meaning the two
   likely unify into one CLI dispatcher (`XmlLayerMerger`/`JsonLayerMerger` stay as internal
   engines either way). This is a bigger, separately-scoped piece of work from the rest of this
   design and needs its own implementation pass.
3. **Which patch targets which resource: each resource entry carries its own `patch` field
   directly** — not two separate `resources`/`patches` lists cross-referenced by convention (this
   document's own earlier proposal, before this was raised). Requested directly by the repo
   owner: a resource's patch (if any) should be unambiguous by construction, not inferred from a
   filename pattern. Implementing this cleanly for a resource that inherits from a *prior* layer
   (Client extending Environment) required splitting that inheritance into its own field —
   `extends` — separate from `resources`; see "Proposed shape" above for why a `resources` entry
   directly pointing at another (multi-project) `configtransform.json` couldn't otherwise say
   which of that file's several projects one `patch` field belonged to. `extends` is a genuinely
   new mechanism this document introduces beyond what was literally asked for, flagged as such
   rather than folded in silently — Kustomize's own apiVersion/kind/name matching and positional
   (array-index) pairing were both considered for the matching problem itself and rejected: the
   former assumes a structured identity XML/JSON config files don't have, the latter breaks as
   soon as one resource in a multi-resource layer has no patch at all (a normal, expected case).

## Open design questions (not yet decided)

Still genuinely open — laid out with a recommendation each, but none should be read as final.

### 1. Path convention: repo-root-relative, or relative to the `configtransform.json` file?

Real Kustomize trees accumulate long `../../../../` chains exactly like the repo owner's own
example did — a well-known, frequently-complained-about Kustomize pain point, not a hypothetical
one. **Recommendation: repo-root-relative** (`OrderProcessor.Framework/App.config`, not
`../../../../OrderProcessor.Framework/App.config`) for a resource pointing at a real base file —
consistent with how `manifest.json`'s own `directory` field already resolves today (repo-relative,
against the CLI's working directory — `docs/MANIFEST_SCHEMA.md`'s "Pointing `directory` at the
repo root itself"). An `extends` value (Client layer inheriting from an Environment layer) still
needs a real relative or root-relative path since there's no separate "root" concept for layer
directories the way there is for project source — root-relative is still recommended there too
for the same fragility reason, resolved against the `.configtransform/` root rather than the repo
root.

### 2. Does `manifest.json`'s indirection (a project label decoupled from its real path) survive?

This is a real, concrete regression worth naming plainly, not glossing over. Today,
`.configtransform/<Project>/` is "a human-readable label only... that mapping is explicit in
`manifest.json`" (`CONFIG_MANAGEMENT.md` §3) — if a project's directory moves, exactly one file's
`directory` field changes. Under this design, `resources` entries reference a project's real path
**directly**, in every layer directory that touches that project — moving a project means editing
every `configtransform.json` that references it (mitigated by being a mechanical find-and-replace,
but a real N-file edit where today it's a 1-file edit). This is the direct cost of the
discoverability win ("one file shows the whole chain, no indirection to follow") and should be
weighed consciously, not discovered later.

### 3. What does `set` do under this design?

Today, `set` (`docs/FIELD_AUTHORING_DESIGN.md`) resolves a target overlay file via
`SetTargetResolver` and writes into it — the file's *existence* in the fixed `Environments/`/
`Clients/` tree is enough. Under this design, writing a patch for a project that **isn't yet a
resource** in the target layer means `set` would also need to add a `resources` entry (and
possibly the `extends` field, for a layer's very first resource) to that layer's
`configtransform.json`, not just write a patch file — a real increase in what `set` has to reason
about (today it never edits the manifest, only overlay content). Needs its own pass once the rest
of this design is settled; not blocking the composition-model decision above, but should be
scoped explicitly before implementation, not discovered mid-build.

## What this design does *not* change (confirmed, not open)

- **Per-client git-crypt key scoping stays possible.** `Clients/<Client>/` is still the outermost
  directory under a per-client path, so a future per-client `.gitattributes` glob
  (`CONFIG_MANAGEMENT.md` §2's "kept as an explicit future escape hatch") still works unchanged.
- **The Environment layer stays optional, per project.** Today this is implicit (an
  `Environments/<Env>.<ext>` file simply doesn't exist for a project with nothing shared across
  clients). Under this design it's still effectively per-project: a Client layer's `extends`
  applies file-wide, but if the Environment layer never listed a given project in its own
  `resources` at all, that project's state flowing through `extends` is just the raw base file —
  the Client layer can still list it with its own `patch` exactly as if there were no
  intermediate layer for that one project. Same outcome, now visible instead of inferred from
  absence.
- **"Missing overlay ≠ error" stays true.** A resource this layer doesn't list at all is exactly
  as unremarkable as a missing `Clients/<Client>/<Env>.<ext>` file is today.
- **Encryption at rest is unaffected.** The `.configtransform/** filter=git-crypt` glob
  (`CONFIG_MANAGEMENT.md` §7.1) covers the whole tree regardless of what's inside it.

## The accepted cost: "no override" is no longer free

Named explicitly, since it's the direct trade for the discoverability win. Today, a client with
no Production override for a given project is *silence* — no file, nothing to write, nothing to
read. Under this design, every `Clients/<Client>/<Environment>/` combination that should resolve
at all needs its own `configtransform.json`, even one whose `resources` list is empty (or that
simply sets `extends` to the Environment layer with zero resources of its own) — because
something has to declare that the chain exists and where it starts. `--dry-run`/`--diff` should
keep reporting exactly what they do today either way (`CONFIG_MANAGEMENT.md` §5.1's found/
not-found reporting) — that behavior is a requirement of any implementation here, not just a
nice-to-have.

## Decision log

| Decision | Chosen | Rejected alternative(s) | Why |
|---|---|---|---|
| Composition model | Self-describing `configtransform.json` per layer directory, explicit `resources` (+ optional `extends`) | Fixed base→Environments→Clients rule inside the tool (current design, `CONFIG_MANAGEMENT.md` §9) | Repo owner's stated preference: easier to understand and add to, independent of the file-count argument against it. See "Origin and problem statement". |
| Manifest file format | JSON (`configtransform.json`) | YAML (`configtransform.yaml`, matching `kustomization.yaml` literally) | This tool has zero YAML parsing anywhere today; adopting it here would add a dependency purely for tooling metadata, not for any config file this tool actually merges. JSON gets the identical self-describing shape at no dependency cost — the `kustomization.yaml`-alikeness was aesthetic, not functional. Confirmed directly by the repo owner. |
| Scope of one `configtransform.json` | One file per client×environment, spanning every project and format it touches | One file per project (mirrors `manifest.json`'s current per-project scope) | Confirmed directly by the repo owner — matches the original worked example and delivers the actual "one file, whole picture" benefit. Accepted consequence: `ConfigTransform.Xml`/`ConfigTransform.Json` likely unify into one CLI dispatcher, since a single file can now mix formats. |
| Patch-to-resource matching | Each `resources` entry pairs a `path` with its own optional `patch` field directly | Two separate `resources`/`patches` lists cross-referenced by filename convention (this document's own earlier proposal); Kustomize's apiVersion/kind/name matching; positional (array-index) pairing | Requested directly by the repo owner: a resource's patch should be unambiguous by construction, not inferred. Kustomize's matching mechanism assumes a structured identity XML/JSON config files don't have; positional pairing breaks as soon as one resource in a multi-resource layer has no patch at all (normal here); the filename-convention alternative still required parsing a naming pattern to recover a relationship that can just be stated directly. |
| Cross-layer inheritance as its own `extends` field, separate from `resources` | `extends: <path>` (one per file, resolved first); `resources` entries always identify a project by its own real path, whether or not `extends` is set | A `resources` entry pointing directly at another `configtransform.json` (mixed with real-base-file entries in the same list) | Once each resource carries its own patch, a `resources` entry pointing at another (multi-project) `configtransform.json` has no unambiguous place to attach a single `patch` — which of that file's several projects would it target? Separating "where this layer starts from" from "what this layer itself adds" removes the ambiguity and keeps every patch on its own, real, unambiguous resource. |
| Path convention for `resources`/`extends` | Repo-root-relative (or `.configtransform`-root-relative for `extends`) | Relative to the `configtransform.json` file's own directory (Kustomize's convention) | Avoids the long, fragile `../../../../` chains that are a known, common complaint about real Kustomize trees — consistent with how `manifest.json`'s `directory` already resolves repo-relatively today. |

## Open items for implementation

Everything in "Open design questions" above must be resolved before implementation starts — none
of it is a checkbox, each is a real fork with a different implementation shape on each side.
Additionally, once those are settled:

- The CLI-unification consequence of "Settled decisions" #2 (`ConfigTransform.Xml`/
  `ConfigTransform.Json` likely merging into one dispatcher) is its own, separately-scoped design
  and implementation pass — not a detail to fold into the rest of this work.
- Whether this **replaces** `manifest.json`/the current `Environments/`/`Clients/` tree outright,
  or the two coexist for some transition period. Given this repo's own SemVer policy allows any
  breaking change pre-1.0 (`CONFIG_MANAGEMENT.md` §10.8) and no real solution repo has adopted the
  current schema in production yet (`docs/ROADMAP.md`'s pilot is synthetic), a clean replacement
  — rather than a dual-support migration path — is the likely right call, but not decided here.
- `docs/MANIFEST_SCHEMA.md`, `docs/GETTING_STARTED.md`, `docs/ONBOARDING.md`, and
  `docs/CONFIG_MANAGEMENT.md` §3/§4/§9 all describe the current schema in detail and would need a
  full rewrite, not just an addendum, once this design is finalized.
- `ManifestLoader`/`ManifestDiscovery`/`ManifestEntrySelector`/`LayerResolution`/
  `SetTargetResolver` in `ConfigTransform.Core`, and each tool's `CliRunner`, are the concrete
  implementation surface — see their current form for what a `configtransform.json`-based
  replacement would need to do instead.

## Related reading

- [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) §9 — the existing "why not Kustomize's
  self-describing overlays" reasoning this document responds to directly.
- [`MANIFEST_SCHEMA.md`](MANIFEST_SCHEMA.md) — the schema this design proposes replacing.
- [`FIELD_AUTHORING_DESIGN.md`](FIELD_AUTHORING_DESIGN.md) — the `set` command whose target
  resolution ("Open design questions" §3 above) would need updating under this design.
