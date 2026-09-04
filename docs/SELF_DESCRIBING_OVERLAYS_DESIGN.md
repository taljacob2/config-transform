# Self-describing overlays (`configtransform.json`) — design

**Status: implemented.** This document exists to get every open
question on the table before any code changes, the same way `FIELD_AUTHORING_DESIGN.md` did for
`set` — and, like that document once it reached this stage, every design question below is now
settled by the repo owner. In order: **file format is JSON** (`configtransform.json`, not
`.yaml`); **scope is one file per client×environment, spanning every project/format that
client+environment touches** (meaning `ConfigTransform.Xml`/`ConfigTransform.Json` unifying into
one CLI dispatcher is an accepted, first-class consequence, not a side effect to avoid); **each
resource carries its own patch directly, as a field on the same entry**, not two separate lists
cross-referenced by convention; **every path in the file — `extends`, `path`, and `patch`
alike — is repo-root-relative**, with no exception for a patch file that happens to live in the
same directory as the `configtransform.json` referencing it; **`manifest.json` is fully
replaced**, not kept alongside this design; **`set` gains a new `--resource <path>` targeting
flag**, with the mechanics for creating/updating a layer's `resources` entries and patch files
worked out in "Settled decisions" #6; and **every other command (`--dry-run`, `--diff`, `--list`,
a real run) gets the same `--resource` flag, with omitting it meaning "every project this layer
touches, in one call"** — a real behavior change for a real run's `--output` (becomes a directory)
and a new reverse-lookup mode for `--list`, worked out with full CLI examples in "Settled
decisions" #7. See "Open items for implementation" for what's left — implementation planning, not
further design.

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
  "extends": ".configtransform/Environments/Production/configtransform.json",
  "resources": [
    { "path": "OrderProcessor.Framework/App.config", "patch": ".configtransform/Clients/Acme/Production/patch-OrderProcessor.Framework-App.config.xml" }
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
  `Clients/` files (XDT transform for XML, plain or `$elemMatch`-bearing JSON for JSON), addressed
  by the **same repo-root-relative convention as `extends`/`path`** — even though, in practice, it
  almost always lives in the same directory as the `configtransform.json` referencing it. See
  "Path convention" below for why that repetition is deliberate, not an oversight. Omitted
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
4. **Path convention: every path in the file — `extends`, `path`, and `patch` alike — is
   repo-root-relative. No exception for `patch`, even though it almost always names a file sitting
   in the very same directory as the `configtransform.json` referencing it.** This document's
   first pass treated `patch` as an implicit same-directory filename (no prefix needed) and gave
   `extends` a *different* anchor (`.configtransform/`-root) than `path` (repo-root) — both were
   real inconsistencies, caught by the repo owner, not oversights left in on purpose. The
   temptation to special-case `patch` was to avoid repeating a directory path the file is already
   sitting in (`{"patch": ".configtransform/Clients/Acme/Production/patch-...xml"}` instead of
   just `{"patch": "patch-...xml"}`) — rejected: predictability for whoever is reading or writing
   the file matters more than saving that repetition, and "some fields work one way, others work
   another" is a real cognitive tax regardless of how principled the internal reason for the split
   is. One rule, everywhere, no exceptions. This still avoids the long, fragile `../../../../`
   chains real Kustomize trees accumulate (a well-known, frequently-complained-about pain point,
   not a hypothetical one) — repo-root-relative gets full consistency *and* short paths, which is
   why it beat adopting Kustomize's own file-relative convention wholesale.
5. **`manifest.json` is redundant — this design fully replaces it, not a coexistence/migration
   period.** Directly confirmed by the repo owner: the directory-indirection `manifest.json`
   provided (a project label decoupled from its real path, one file to edit if a project moves)
   is accepted as a real, named loss, not worth preserving alongside the new tree. Consistent with
   this repo's own SemVer policy (any breaking change is fine pre-1.0, `CONFIG_MANAGEMENT.md`
   §10.8) and the fact that no real solution repo has adopted the current schema in production yet
   (`docs/ROADMAP.md`'s pilot is synthetic) — there's no live user of `manifest.json` a dual-support
   path would actually be protecting.
6. **What `set` does under this design.** Today, `set` (`docs/FIELD_AUTHORING_DESIGN.md`) resolves
   a target overlay file via `SetTargetResolver` and writes into it — the file's *existence* in the
   fixed `Environments/`/`Clients/` tree is enough, and `--manifest`/`--file` (via `manifest.json`)
   pick the one project in scope. Under this design, with `manifest.json` gone and one
   `configtransform.json` spanning several projects, `set` needs real changes, confirmed as
   follows:
   - **New targeting flag**: `--resource <path>`, naming the project directly by its real
     repo-root-relative path (the *same* addressing scheme `resources[].path` already uses
     everywhere) — replacing `--manifest`/`--file`. `--client`/`--environment` keep their current
     meaning; they now resolve to a `configtransform.json` path instead of a raw overlay file
     (base file directly with neither; `.configtransform/Environments/<E>/configtransform.json`
     with only `--environment`; `.configtransform/Clients/<C>/<E>/configtransform.json` with both).
   - **Target layer file doesn't exist yet** → `set` creates it. For a Client-layer write, it also
     sets `extends` to the matching Environment layer's path — even if that file doesn't exist on
     disk yet either. New rule this requires, stated explicitly: an `extends` target that doesn't
     exist resolves to "nothing to inherit," the same treatment a missing overlay already gets
     today (`CONFIG_MANAGEMENT.md` §5.1) — not an error.
   - **Target resource not yet in `resources`** → `set` appends `{"path": ..., "patch": <new
     file>}` and authors the new patch file. **Listed but no `patch` yet** → adds the `patch`
     field, authors the new file. **Listed with a `patch` already** → updates that existing patch
     file in place — today's idempotent re-run behavior, unchanged.
   - **Which engine (XML vs. JSON) handles a resource** is inferred from its `path`'s file
     extension, not a declared field — consistent with this repo's existing stance that format is
     the only real constraint, never redundantly declared (`CLAUDE.md`'s "Core concepts").
   - **What does *not* change**: the actual patch-file *content* authoring —
     `XmlFieldAuthor`/`JsonFieldAuthor`, the `--match`/`--set` model, `$elemMatch`, verified
     defaults — none of it. This redesign is entirely about *finding or creating the right patch
     file*, not writing into it.
7. **The rest of the CLI — `--dry-run`, `--diff`, `--list`, and a real run — beyond `set`.**
   `set`'s redesign above covers one command; every other command needs the same `--resource`
   flag and a real behavior decision about what happens without it.
   - **Manifest/root discovery mostly disappears.** `ManifestDiscovery` exists today to
     disambiguate between multiple `manifest.json` candidates under `.configtransform/*/
     manifest.json`. With `manifest.json` gone, there's nothing to disambiguate — given
     `--client`/`--environment` (or neither), the target `configtransform.json` path is fully
     determined. No `--manifest` flag anywhere anymore. (The `.configtransform/` root folder's
     *name* is still configurable per `CONFIG_MANAGEMENT.md` §10.5, so a small discovery step for
     *that* survives — just not the old ambiguity.)
   - **`--resource <path>` is a tool-wide flag**, not `set`-specific — the same addressing
     scheme, used identically by `--dry-run`, `--diff`, a real run, and `--list`.
   - **Omitting `--resource` processes every project the resolved layer touches, in one
     invocation** — not an error, and not limited to one project per call the way every command
     is today. `--dry-run`/`--diff` print each resource's result labeled by its own `path`. A
     real run requires `--output <dir>` in this case (a single `--output <file>` only makes sense
     paired with `--resource`) and writes one file per resource, each resource's own path mirrored
     under that directory — this is the one genuine behavior change here, not just a reshuffled
     flag, and is what actually delivers "one file, whole picture" as a real capability rather
     than just a readable manifest.
   - **`--list` gets a new meaning, and a new mode that closes a real gap this design creates.**
     There's no `files[]` array to list anymore. Given `--client`/`--environment`, `--list` shows
     that layer's `resources` (each with its `patch`, if any, and what `extends` contributes).
     Given `--resource <path>` instead, `--list` becomes a **reverse lookup** — every layer in the
     tree that patches this one project. This mode isn't a nice-to-have: today, "every overlay for
     App.config" is one directory listing (`.configtransform/<Project>/App.config/`); under this
     design, the same question means searching every `configtransform.json` in the tree for a
     matching `resources[].path` — a real ergonomic loss (the flip side of "one file, whole
     picture") that `--list --resource` exists specifically to answer.
   - **`--diff` is conceptually unchanged** — still "unpatched vs. fully resolved" — it just walks
     `extends` plus the layer's own patch instead of the old fixed base→env→client chain. No new
     flag, only the same new resolution logic `set` and `--dry-run` already need.
   - **Confirms, tool-wide, what "Settled decisions" #2 flagged for `set` alone**: since dispatch
     is per-resource by file extension, `ConfigTransform.Xml`/`ConfigTransform.Json` unifying into
     one CLI entry point isn't a `set`-specific consequence — every command needs it, since a
     single multi-resource invocation can span both formats at once.

   Worked example — same tree as "Proposed shape" above, `OrderProcessor.Framework/App.config`
   (XML) patched at both layers, `BillingApi.Core/appsettings.json` (JSON) patched only at
   Environment:

   ```
   $ configtransform --client Acme --environment Production --resource OrderProcessor.Framework/App.config --dry-run
   ```
   ```xml
   <configuration>
     <appSettings>
       <add key="ApiUrl" value="https://acme.example.com" />
     </appSettings>
   </configuration>
   ```

   Omitting `--resource` — both projects this layer touches, one call:
   ```
   $ configtransform --client Acme --environment Production --dry-run
   ```
   ```
   === OrderProcessor.Framework/App.config ===
   <configuration>
     <appSettings>
       <add key="ApiUrl" value="https://acme.example.com" />
     </appSettings>
   </configuration>

   === BillingApi.Core/appsettings.json ===
   {
     "ApiUrl": "https://prod.example.com",
     "RetryCount": 3
   }
   ```
   `BillingApi.Core` shows Environment's value — Acme's `configtransform.json` never lists it, so
   it passes through `extends` untouched, same "missing overlay ≠ error" rule as always.

   A real run, same scope, `--output` as a directory:
   ```
   $ configtransform --client Acme --environment Production --output publish/
   ```
   ```
   Wrote 'publish/OrderProcessor.Framework/App.config'.
   Wrote 'publish/BillingApi.Core/appsettings.json'.
   ```

   `--list` for one layer:
   ```
   $ configtransform --list --client Acme --environment Production
   ```
   ```
   Clients/Acme/Production/configtransform.json
     extends: Environments/Production/configtransform.json

     OrderProcessor.Framework/App.config
       patched here: patch-OrderProcessor.Framework-App.config.xml
       also patched in: Environments/Production/configtransform.json

     BillingApi.Core/appsettings.json
       not patched here — inherited from Environments/Production/configtransform.json
   ```

   `--list --resource` — the reverse lookup:
   ```
   $ configtransform --list --resource OrderProcessor.Framework/App.config
   ```
   ```
   OrderProcessor.Framework/App.config is patched in:
     Environments/Production/configtransform.json  (patch-OrderProcessor.Framework-App.config.xml)
     Clients/Acme/Production/configtransform.json   (patch-OrderProcessor.Framework-App.config.xml, extends Environments/Production)
   ```

   `configtransform` above is a placeholder invocation name — "Settled decisions" #2's CLI
   unification is confirmed as a consequence, but no specific binary/project name has been chosen;
   see "Open items for implementation".

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
| Path convention | Repo-root-relative, uniformly, for `extends`, `path`, and `patch` — no exception for a same-directory `patch` file | Relative to the `configtransform.json` file's own directory (Kustomize's convention, for all three); the document's own first pass, which special-cased `patch` as an implicit same-directory filename and gave `extends` a different anchor than `path` | Two real inconsistencies caught by the repo owner, not left in on purpose. Predictability for whoever reads/writes the file outweighs the repetition saved by special-casing `patch`; repo-root-relative still avoids the long, fragile `../../../../` chains real Kustomize trees accumulate, so this gets both consistency and short paths, unlike adopting Kustomize's file-relative convention wholesale. |
| `manifest.json`'s fate | Fully replaced — a clean break, no coexistence/migration period | Keep `manifest.json` alongside the new tree for some transition period, or preserve its directory-indirection some other way | Confirmed directly by the repo owner: the indirection is an accepted, named loss, not worth preserving. No real solution repo has adopted the current schema in production yet, so there's no live user a dual-support path would protect — consistent with this repo's SemVer policy allowing any breaking change pre-1.0. |
| `set`'s new targeting flag | `--resource <path>`, naming the project by its real repo-root-relative path | Keep `--manifest`/`--file`; invent a new project-label indirection to replace `manifest.json`'s | `manifest.json` is gone (see the row above), so there's no project label left to target by — `path` is already how every resource is addressed everywhere else in this design, so reusing it for `set`'s own targeting needs no new vocabulary. |

## Implemented — what shipped, and what's still open

All seven "Settled decisions" above are confirmed by the repo owner and are now real code, not
just design. `LayerManifest`/`LayerManifestLoader`/`LayerPathResolver`/`LayerChain`/`LayerLister`
(`ConfigTransform.Core`) replace `Manifest`/`ManifestLoader`/`ManifestDiscovery`/
`ManifestEntrySelector`/`ManifestLister`/`LayerResolution` (all deleted); `XmlLayerMerger`/
`JsonLayerMerger` take an arbitrary-length ordered patch chain; `SetTargetResolver` and both
tools' `CliRunner` are rewritten around `--resource`. `docs/MANIFEST_SCHEMA.md`,
`docs/GETTING_STARTED.md`, `docs/ONBOARDING.md`, `docs/USAGE.md`, and `CLAUDE.md` are rewritten;
`docs/CONFIG_MANAGEMENT.md` §3/§4/§5.1/§9 are updated in the same change.

One item was explicitly deferred, not overlooked:

- **The CLI-unification consequence of "Settled decisions" #2** (`ConfigTransform.Xml`/
  `ConfigTransform.Json` merging into one dispatcher) is still its own, separately-scoped design
  and implementation pass, exactly as flagged when this design was written — not started. The
  interim behavior implemented instead: each tool processes every resource of its own format that
  a resolved layer touches, skipping the other format's resources with a stderr note rather than
  an error or silence. See `docs/ROADMAP.md`'s "Next up" for what a unification pass would still
  need to decide (binary/project name, how `set`'s engine selection carries over to one
  dispatcher).

## Related reading

- [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) §9 — the existing "why not Kustomize's
  self-describing overlays" reasoning this document responds to directly.
- [`MANIFEST_SCHEMA.md`](MANIFEST_SCHEMA.md) — the schema this design proposes replacing.
- [`FIELD_AUTHORING_DESIGN.md`](FIELD_AUTHORING_DESIGN.md) — the `set` command whose target
  resolution ("Settled decisions" #6 above) needs updating under this design.
