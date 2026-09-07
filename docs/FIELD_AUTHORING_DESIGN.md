# Field authoring (`set`) — design

**Status: mostly implemented.** `ConfigTransform.Xml`'s `set` covers the "update an existing
element" case in full (base file, Environment overlay, Client overlay; the bare `--match`/`--set`
defaults with verification; the ambiguous/not-found/"did you mean" error paths; the
auto-`--diff`). `ConfigTransform.Json`'s `set` covers a single key path — both updating an
existing key *and* creating a brand-new one, since JSON has no `Insert`-style gap — **and now also
covers matching/creating an item inside an array of objects**, via a `$elemMatch`-style overlay
syntax (see "JSON / YAML" below). XML's `set` also now supports matching a singleton element by
its **tag name alone** (a reserved `tag=` `--match` coordinate — see "What --match and --set mean,
per format" → XML below), for elements like `customErrors`/`compilation`/`httpRuntime` that have
no identifying attribute at all — a real, previously-undesigned gap reported against the published
tool, found the same way the JSON array-of-objects gap was: by a real user hitting it. See
`docs/USAGE.md`'s `set` section and `docs/CHANGELOG.md`'s `[0.6.0-alpha]`/`[0.7.0-alpha]` entries
for exactly what's live and where. `ConfigTransform.Env`'s `set` is also implemented — the
simplest of the four, since a `.env` file is always flat: no nested-path disambiguation (JSON)
and no update-vs-insert branch (XML) to make at all, just `--match key=<NAME> --set
value=<value>` writing or overwriting a key directly. `ConfigTransform.Yaml`'s `set` is
implemented for its plain-field path (update an existing key, or create a new one) — the same
model as JSON's own plain-field case, since YAML shares JSON's exact nesting. **Not yet
implemented**: XML's `Insert` case (a genuinely brand-new element), XML's array-of-objects
matching, and YAML's array-of-objects matching (see "Open items" below for all three — JSON's
version of the array-of-objects gap, once a real, previously-undesigned problem found during
implementation, is now closed). This document otherwise still reflects the original completed
design from a product-brainstorming session; treat any specific claim about *current* behavior as
superseded by `docs/CHANGELOG.md` where the two differ.

## Why this exists, and why now

`docs/ROADMAP.md`'s "Next up" defers both a `configtransform init` command and a TUI/GUI,
explicitly pending real (non-synthetic) solution-repo validation — the concern being that
guessing at usage patterns from one synthetic pilot risks baking in wrong assumptions. This
document is a deliberate, conscious exception to that gate, not an oversight: it targets one
narrow blocker the TUI/GUI entry names directly — "there's no CLI-level field-authoring feature
to build a UI around yet" — and that specific gap doesn't actually need real-content validation
to design correctly, because it isn't guessing at *usage patterns*; it's built directly from the
mechanics of `Microsoft.Web.Xdt` and `Microsoft.Extensions.Configuration`, which this repo
already understands and tests thoroughly (`docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §3). `init` and
the TUI/GUI themselves stay deferred — this is scoped to field authoring only. (`init` has since
shipped, via the same kind of deliberate exception to this gate — see
`docs/INIT_COMMAND_DESIGN.md`'s "Why this exists, and why now." The TUI/GUI entry remains
deferred.)

**The concrete problem this solves**: hand-writing an XDT overlay is not one operation, it
branches three ways depending on intent (add a new key to the base file; `SetAttributes` +
`Locator="Match(...)"` to override an existing key; `Transform="Insert"` for a client-only key
that exists nowhere else — see `docs/GETTING_STARTED.md`'s "One real difference between XML and
JSON when the key is brand new"), and picking wrong fails **silently**: `Locator="Match(key)"`
against a key that isn't there just matches nothing, no error, nothing written — the operator
believes it worked and finds out in production. A `set` command that decides the operation
mechanically, from the real document, removes that guess entirely.

**Persona this was designed against** (from the brainstorming session): a senior developer
deploying under time pressure who doesn't want to read docs first. They don't need education —
they need the tool to make the correct decision on their behalf, and to make it impossible to
ship a change that's silently wrong.

## Command shape

```
configtransform set --resource <path> --client <C> --environment <E> --match <attr>=<value> [--match ...] --set <attr>=<value> [--set ...]
```

Identical shape regardless of `--resource`'s format — `configtransform` dispatches to the XML or
JSON field-authoring engine by that path's own extension, same principle `docs/USAGE.md` already
holds for the rest of the CLI. `--match` locates the target; `--set` writes fields on it. Both repeatable, and always
typed in full (`--match key=ApiUrl`, not a bare `--match ApiUrl`, except where the "Defaults"
section below applies) — a single consistent shape rather than a shorthand for the simple case,
so the command looks the same regardless of which element it's touching.

After a real write, `set` prints the same `--diff` output `docs/USAGE.md` already documents for
the base file vs. the merged result — automatically, not as a separate step — so the trust-check
a time-pressured operator needs isn't something they can accidentally skip.

## What `--match` and `--set` mean, per format

### XML

`--match` names a real attribute on the target element and the value it must equal —
`Locator="Match(...)"` in XDT terms, compound when more than one `--match` is given
(`Locator="Match(a,b)"`). `--set` names one or more attributes to write via `SetAttributes`.
Which attribute identifies an element is **schema-dependent and never guessed**: `appSettings`
uses `key`, `connectionStrings` uses `name`, a `GenericXml`-shaped file could use anything —
this project's own design principle (`CLAUDE.md`: "no App.config-specific logic anywhere")
rules out a fixed heuristic like "always try `key` first."

```xml
<!-- appSettings: one identity attribute, one value attribute -->
<add key="ApiUrl" value="https://dev.example.com" />
```
```
set --match key=ApiUrl --set value=https://new.example.com
```

```xml
<!-- connectionStrings: one identity attribute, two value attributes -->
<add name="Prod" connectionString="Data Source=old;..." providerName="System.Data.SqlClient" />
```
```
set --match name=Prod --set connectionString="Data Source=new;..." --set providerName=System.Data.SqlClient
```

**Operation decision** (`SetAttributes` vs. `Insert` vs. base-file edit) is resolved
mechanically, not asked for: resolve the target overlay layer as it exists today; if the
matched element is already present in the fully-resolved document up to that layer, the
operation is `SetAttributes`; if absent, `Insert`; a `set` with no `--client`/`--environment`
writes the base file directly (a new key meant for everyone).

**Reserved coordinate: `tag=`.** Some elements have no identifying attribute at all —
`customErrors`, `compilation`, `httpRuntime`, `sessionState`, and similar `system.web`/
`system.webServer` sections are each the only element of their tag under their parent, singleton
by position rather than by any attribute value. Real XDT already has an idiom for this: omit
`xdt:Locator` entirely and it matches by element name alone. `set` exposes the same idiom via a
reserved `tag=` `--match` coordinate — the element's own tag name, not a real attribute:

```xml
<!-- customErrors: no identifying attribute, unique under system.web -->
<customErrors mode="Off" />
```
```
set --match tag=customErrors --set mode=RemoteOnly
```
```xml
<!-- written overlay: no xdt:Locator at all, matching real XDT's own default-match behavior -->
<customErrors xdt:Transform="SetAttributes" mode="RemoteOnly" />
```

`tag` is never written to the overlay as a literal attribute (it isn't one), and never appears
inside a `Locator(...)` string — the tag name is already the overlay element's own name, exactly
as it is for every other `set` case. It combines with real attribute matches too
(`--match tag=add --match key=ApiUrl`), in which case the Locator is built from the attribute
matches only — `tag` narrows *which* elements are even candidates, the same way an attribute match
does, but contributes nothing to the emitted `Locator` string since XDT never needs the tag name
stated there (the overlay element's own tag already carries it). This is a small, deliberate
exception to XML's otherwise-open attribute vocabulary ("no fixed heuristic," above) — parallel to
JSON's own reserved `key`/`literal-key` coordinates below, and accepted for the same reason: a real
schema having an attribute literally named `tag` is a theoretical collision, not a practical one.

### JSON / YAML

`--match key=...` (renamed from an earlier `path=` working name — see decision log) navigates to
a location by field name, `:`-separated for nested fields — matching how
`Microsoft.Extensions.Configuration` (the library `ConfigTransform.Json`'s merge already runs
on) flattens nested JSON internally, and how ASP.NET Core's own command-line config provider
addresses settings (`--Logging:LogLevel:Default=...`). **Not `.`** — dots commonly appear
literally inside real setting names (`api.timeout.ms`-style), colons far less often, so reusing
`:` both avoids the more likely collision and matches a convention this tool's actual audience
already knows, rather than inventing a new one.

```json
{ "Logging": { "LogLevel": { "Default": "Information" } } }
```
```
set --match key=Logging:LogLevel:Default --set value=Warning
```

A plain nested field needs only one `--match` — the path itself is the full identity, no
disambiguation needed. An **array of objects** is the JSON/YAML analogue of XML's
attribute-matching problem, and needs two: one to reach the array (`--match key=...`, as above),
one or more to pick the item (any further `--match <field>=<value>`, using a field name that
isn't `key`/`literal-key`).

```json
{
  "ConnectionStrings": [
    { "name": "Prod",    "connectionString": "Data Source=old;...",     "providerName": "System.Data.SqlClient" },
    { "name": "Staging", "connectionString": "Data Source=staging;..." }
  ]
}
```
```
set --match key=ConnectionStrings --match name=Prod --set connectionString="Data Source=new;..." --set providerName=System.Data.SqlClient
```

**Correction, found while implementing JSON's `set` (not caught at design time)**: the example
above originally assumed `--match name=Prod` could disambiguate an array item the way XDT's
`Locator` does for XML — directly, with the persisted overlay addressing the item by position.
It can't: `Microsoft.Extensions.Configuration` flattens a JSON array to **index-keyed** entries
(`ConnectionStrings:0`, `ConnectionStrings:1`, ...) — merging is purely positional (`docs/USAGE.md`'s
"What 'merge' means"; `JsonLayerMerger`'s own doc comment), with no native concept of "the item
whose `name` equals X" for `set` to hook into or delegate to.

**What ships instead**: a `$elemMatch` overlay shape — named after MongoDB's own operator for
"match an array element by field conditions," a known convention rather than an invented one
(see decision log) — that never writes a position anywhere, including in the persisted overlay
file itself (a hard requirement: an operator reading an overlay file should never need to know or
reconstruct which index a change landed on). The command above is unchanged; what changed is what
gets written and how it gets resolved:

```json
{ "ConnectionStrings": [
  { "$elemMatch": { "name": "Prod" }, "connectionString": "Data Source=new;...", "providerName": "System.Data.SqlClient" }
] }
```

The value under the array's key is always a **list** of these patches — even for a single
condition set — rather than a single bare object, so there is exactly one shape to parse, and so
a second `set` call against the same array in the same overlay file (different conditions) has
somewhere to go: it appends a second patch rather than colliding with the first. Re-running `set`
with the *same* conditions (order-independent) updates that patch in place instead, the same
"idempotent re-run" behavior every other `set` path already has.

No match found for a patch's conditions is not an error — it's an **upsert** (Mongo's own term for
the same idea): a new item is created, combining the `$elemMatch` condition fields themselves (as
the new item's identity — `"name": "Prod"` in the example above) with whatever `--set` wrote. The
overlay file's shape is identical whether a given patch ends up updating or creating; that
decision is made by resolving against the real document, every time the document is merged, never
baked into the file. More than one array item matching one patch's conditions is still a hard
error, exactly like XML's ambiguous-element case, listing every candidate.

**Resolving `$elemMatch` has to happen at real merge time, not only when `set` writes the file** —
this is the direct consequence of the "no index anywhere in the persisted file" requirement. A
hand-written `$elemMatch` overlay (never touched by `set` at all) has to merge correctly too, and
layering is progressive: an Environment-layer patch must resolve against the base array, but a
Client-layer patch must resolve against the base **+ Environment-merged** array — mirroring how
`XmlLayerMerger` already applies the Client transform to the already-Environment-transformed
document, not to the base alone. Since `Microsoft.Extensions.Configuration` has no native concept
of resolving this at all (unlike XDT's `Locator`, a real feature of the library XML's merge
already runs on), `JsonLayerMerger.Merge` gained a pre-processing pass
(`JsonElemMatchResolver.Rewrite`): before a layer reaches `Microsoft.Extensions.Configuration`, any
`$elemMatch` patches in it are resolved against the document as merged through the *prior* layer
only, and rewritten into a real position — expressed as a `JsonObject` keyed by numeric-string
index (`{"1": {...}}`), not a `JsonArray` literal, because a real array can't say "leave every
other index alone, touch only this one" without emitting placeholder nulls for the skipped
indices, and those nulls would themselves flatten to real `IConfiguration` keys and clobber the
base layer's actual values there. A numeric-string object key has no such constraint, and is
proven (empirically, for both `AddJsonFile` and `AddJsonStream` input) to flatten to the exact
same `IConfiguration` path as a real array element at that index. Every merge with no `$elemMatch`
anywhere in either overlay layer takes the original, unmodified code path (`JsonLayerMerger`'s
`LegacyMerge`) — this is what keeps every previously-shipped merge behavior unchanged.

See `src/ConfigTransform.Json/JsonElemMatchResolver.cs` for the resolver itself (shared between
`set`'s eager, set-time-only UX check and `JsonLayerMerger`'s authoritative merge-time
resolution), and `docs/USAGE.md`'s `set` section for more worked examples (compound conditions,
a second patch in the same overlay, progressive layering across Environment/Client).

YAML is implemented (`YamlFieldAuthor`, `docs/CONFIG_MANAGEMENT.md` §5.6) and, as predicted here,
needed no separate design: same tree-of-maps/lists/scalars data model as JSON, same `:`-separated
nested-path grammar, same `key=`/`literal-key=` disambiguation for a literal key that happens to
contain a colon. The one real difference from JSON's `set` is scope, not model: YAML's first
version covers the plain-field path only (update an existing key, or create a new one) — matching
an item inside an array of objects (the `$elemMatch` case above) is **not** ported for YAML.
A `--match` shape with more than one coordinate is refused with a clear "not yet supported"
error rather than guessed at, mirroring how XML's own array-of-objects matching and `Insert`
are refused today. This is a genuine, named scope gap, not an oversight: `JsonElemMatchResolver`
is ~200 lines tightly coupled to `System.Text.Json.Nodes` types (`JsonNode`/`JsonObject`/
`JsonArray`) — porting its `DeepEquals`/`DeepClone`/index-preserving-rewrite logic to YAML's own
`Dictionary<string, object>`/`List<object>` object graph is real, separable work, deliberately
deferred rather than bundled into YAML's first version. This repo's own precedent is the same:
JSON's own `$elemMatch` landed in a later PR than JSON's first `set`.

### `.env`

Same shape as XML's simple case, because the shapes really are the same: a `.env` line
(`FOO=bar`) and an `appSettings` entry (`<add key="FOO" value="bar"/>`) are both "one identity,
one value" — except simpler in practice, since a `.env` file has no nesting at all, so there's
never a disambiguation question to ask (unlike JSON's nested-path-vs-literal-key collision).

```
set --match key=FOO --set value=bar
```

Implemented (`ConfigTransform.Env.EnvFieldAuthor`): `matches` must be exactly one `key=<NAME>`
(bare shorthand already defaults to `key`), `setFields` exactly one `value=<value>` (bare
shorthand defaults to `value`), and the key is validated against the real POSIX env-var-name
grammar (`EnvFile.ValidateKey`) before writing — see `docs/CONFIG_MANAGEMENT.md` §5.5 for the
full grammar. `isBaseTarget` doesn't change the logic at all: a base write and an overlay write
both just parse-or-start-empty, set the key, and reserialize.

## Defaults: bare `--match`/`--set`

`--match FOO` (no explicit `attr=`) defaults to `key=FOO`; `--set bar` defaults to `value=bar`.
This is **not** the same heuristic ruled out for XML's attribute name in general — the
difference is *verification*:

- **Updating something that exists**: the tool checks the real document. `--match Prod` against
  a `connectionStrings` entry finds nothing under `key`, finds `name="Prod"` instead, and refuses
  with the corrected `--match name=Prod` command shown rather than silently substituting it —
  **implemented stricter than originally designed here**: this section's original text allowed
  auto-using a verified alternate attribute; the shipped behavior always asks instead, never
  silently changes which attribute a `--match` resolves to, even when confident. It never blindly
  assumes `key` was right, because it can check.
- **Creating something brand new**: there is nothing to check against, so a default here would
  be an unverifiable guess — writing `<add key="Prod" ...>` for what should have been
  `name="Prod"` is exactly the silent-wrong-in-production failure this feature exists to
  prevent, one layer deeper. **Never defaulted here; the real attribute name must be given
  explicitly.**

Same rule resolves the JSON `--match key=...` vocabulary question directly: for a plain field,
`key`/`literal-key` (below) are the *only* valid coordinate names — nothing else is possible in
that position, so an unrecognized name like `--match FOO=BAR` on a plain field isn't ambiguous
at all, it's simply not a valid attribute name there, and gets reinterpreted as the literal-value
reading with no warning needed. XML's vocabulary is open (any attribute name could be real for
some schema), so the same shortcut doesn't apply there — that's the genuine difference between
the two, not an inconsistency.

## The one rule underneath every ambiguity case

Every distinct-looking edge case surfaced during design collapsed to the same rule:

> Verify against the real document and resolve automatically whenever exactly one
> interpretation is real. Refuse and demand explicit input only when there is nothing to verify
> against — creating something brand new with no existing evidence either way.

Concretely, this rule is what resolves:
- **XML's `key`/`value` defaults** — verified against the existing element when one exists;
  refused on a true insert (above).
- **JSON's colon-path vs. literal flat key** (`Logging:LogLevel:Default` as three nested levels,
  vs. a real top-level key literally named that) — try the nested walk; if a literal flat key
  with that exact name also exists and independently resolves, that's the one pathological case
  that's genuinely ambiguous even with a document to check, and gets a hard error naming both
  candidates, with `--match literal-key=...` (not `key=...`) as the explicit disambiguator. One
  refinement found during implementation: a **single-segment** key (no `:` at all, e.g. `ApiUrl`)
  is never actually ambiguous this way — "nested" and "literal" are the same reading for it, not
  two competing ones, since there's nothing to walk versus not-walk. Treating it as a collision
  was a real bug caught by manual smoke-testing before it shipped (`ApiUrl` — the single most
  common shape a JSON `set` will ever see — always resolving as "ambiguous" on its own is exactly
  the kind of thing that would have made this feature unusable out of the gate); fixed to skip
  the collision check entirely for single-segment values. Also worth knowing: this collision case
  is close to unreachable in practice for Environment/Client-target writes specifically —
  `Microsoft.Extensions.Configuration.Json` itself refuses to *load* a file shaped with a genuine
  collision (duplicate flattened key), so any `set` that merges through it (which every
  non-base-target write does) hits that load failure first. It's only reachable via a base-target
  write reading the file directly (`File.ReadAllText`, no `IConfiguration` involved) — a narrow
  but real window, e.g. right after such a file gets hand-edited, before any real merge would
  have caught the problem.
- **A bare `--match`/`--set` value containing a literal `=`** — turned out, on inspection, not to
  be a real ambiguity at all: `=` always splits on the *first* occurrence only
  (`--set connectionString=Data Source=prod;User=admin` needs no escaping — everything after the
  first `=` is the value, however many more `=` it contains), so `--match FOO=BAR` is always,
  deterministically, "attribute `FOO` equals `BAR`." A value that must contain a literal `=` and
  be matched against the *default* attribute has exactly one correct way to write it —
  `--match key=FOO=BAR` — not a choice between two suggestions.

No case needed a bespoke resolution; each was the same rule applied once more.

## Errors, warnings, and suggestions

- **A refusal always shows a real, concrete, ready-to-run corrected command when one exists** —
  never just "ambiguous, go read the docs." The tool already computed the candidate
  interpretation(s) while detecting the problem; printing them costs nothing extra. This is
  compiler-error-message discipline (Rust, Elm are the well-known references), not typo-fuzzing:
  every suggestion shown is a real alternative the tool actually considered, not a guessed
  correction.
- **Never auto-applied.** No interactive "press Y to accept" prompt — that's exactly the kind of
  thing a time-pressured operator blows through without reading, which would quietly reintroduce
  the failure mode this whole feature exists to prevent, and it would also break usage in CI,
  where nothing can block on stdin (a constraint this CLI already holds throughout —
  `docs/USAGE.md`).
- **Warnings (non-blocking) are only used where the tool has actually verified something, or
  where the vocabulary is closed enough to be certain.** As implemented, the XML `key=`/`value=`
  verified-alternate-attribute case (above) doesn't actually use a warning-and-proceed either —
  it refuses and shows the corrected command, same as the unverifiable case, just for a different
  reason (confident but not what was asked for, vs. no evidence at all). **A hard stop is used
  wherever the tool would otherwise be silently guessing on unverifiable, brand-new content** —
  downgrading that case to a warning would put the entire safety property of this feature behind
  a message a hurried person can scroll past.

## Decision log

| Decision | Chosen | Rejected alternative(s) | Why |
|---|---|---|---|
| How to pick `SetAttributes` vs. `Insert` vs. base-file edit | Resolved mechanically: check whether the matched element already exists in the resolved document up to the target layer | Ask the user to specify the operation explicitly | The check is always answerable from the document itself — asking for information the tool can already determine adds friction with no safety benefit. |
| How the tool decides which XML attribute identifies an element | Never guessed/heuristic (no fixed "try `key`, then `name`" priority list) | A priority-ordered heuristic over common attribute names | This project's own format-generic design principle (`CLAUDE.md`) rules out baked-in schema assumptions; `connectionStrings` (`name`) vs. `appSettings` (`key`) coexisting in one real file was the concrete case that broke the heuristic. |
| `--key`/`--value` vs. `--match`/`--set` | `--match`/`--set`, both repeatable | A single `--key <name> --value <value>` pair | A single pair can't express an element with more than one changed attribute at once (`connectionStrings`' `connectionString` + `providerName` together) — a real shape this codebase's own fixtures already contain (`docs/CONFIGTRANSFORM_TOOL_DESIGN.md` §3.1). |
| JSON/YAML nested-path separator | `:` | `.` (dot) | Dots commonly appear literally in real setting names (`api.timeout.ms`); colons rarely do. `:` also matches `Microsoft.Extensions.Configuration`'s own internal flattening and ASP.NET Core's command-line config convention — not an invented convention. |
| JSON/YAML coordinate-name terminology | `key=` (uniform across all four formats) | `path=` | Keeps the vocabulary identical to XML's/`.env`'s `key=` for the structurally-equivalent simple case, at the cost of `--match` doing two conceptually different jobs under one name for JSON/YAML (pure navigation for a plain field vs. real attribute-matching for an array item) — accepted consciously, not unnoticed. |
| Bare `--match`/`--set` shorthand (defaults to `key`/`value`) | Applied for XML too, but only after verifying against the real document; never applied blind on a brand-new element | Apply the default unconditionally, even when creating something new | Verified default is safe; unverified default on a true insert is the exact silent-wrong-in-production bug this feature exists to prevent, one layer deeper. |
| A literal `=` inside a bare match/set value | No new escaping syntax — fall back to the explicit `attr=value` form, which is already unambiguous (splits on the first `=` only) | A backslash-escape convention for a literal `=` | The explicit form already expresses this correctly with zero new syntax; inventing an escape mechanism would add a second thing to learn for a case with an existing, simpler answer. |
| "Did you mean" suggestions vs. plain errors | Always show a real candidate command when the tool computed one while detecting the problem | Generic error text only | The candidates already exist internally by the time an ambiguity is detected — showing them is free, and turns a dead-end error into a copy-pasteable fix, which matters specifically for the time-pressured persona this was designed against. |
| Downgrading unverifiable-guess refusals to warnings | Rejected — stays a hard stop | Warn but proceed with a default guess | A warning is exactly the kind of message a hurried operator scrolls past; downgrading the one case with zero evidence to check against would put this feature's entire safety property behind attentiveness it was designed not to require. |
| JSON array-of-objects `--match` (found during implementation) | Initially: not implemented, rejected outright with a message naming the reason | Port XML's `Locator`-style value-matching as designed above | `Microsoft.Extensions.Configuration` merges JSON arrays by index, not by matching a field's value — there's no mechanism to port directly. Silently misinterpreting `--match name=Prod` as "index 0" or similar would be exactly the silent-wrong-in-production failure this whole feature exists to prevent. Superseded by the next two rows once a real design existed. |
| Single-segment JSON key vs. the nested/literal collision check | Skip the collision check entirely when the key has no `:` at all | Apply the same nested-vs-literal check uniformly to every key length | A colon-free key (e.g. `ApiUrl`) has only one possible reading — "nested" and "literal" are the same thing for it. Applying the check anyway made the single most common shape a JSON `set` will see (`--match ApiUrl`) always report as ambiguous, a real bug caught by manual smoke-testing, not a design choice. |
| JSON array-of-objects overlay syntax (closing the row above) | `$elemMatch` — MongoDB's own operator name for "match an array element by field conditions" | Inventing a new name (e.g. `$match`); padding an overlay array with `{}` placeholders up to the target index; addressing the item by a plain numeric index in the overlay file | The user explicitly required that no array index ever be visible anywhere, including in the persisted overlay file — ruling out index-based and padding-based approaches outright. `$elemMatch` is a real, already-known convention (this project's own "prefer a known convention over inventing one" principle, same reasoning as `:` over `.` above) — closer in spirit to XDT's `Locator` than any index-shaped alternative. |
| Canonical shape for the `$elemMatch` overlay: always a list of patches, even for one condition set | A list under the array's key (`{"Rules": [{"$elemMatch": {...}, ...}]}`), never a bare single object | A bare `{"$elemMatch": {...}, ...}` object for the single-condition-set case, promoted to a list only when a second patch is added | One shape to parse everywhere (set-authoring, merge-time rewrite, hand-editing) beats two; a bare-object shape has nowhere to put a second `set` call against the same array in the same overlay file (different conditions) without either colliding or silently changing shape between the first and second write. |
| YAML `set` scope for its first version | Plain-field path only (update/create a key); array-of-objects matching refused with a named "not yet supported" error | Port `$elemMatch` to YAML in the same PR | `JsonElemMatchResolver` is tightly coupled to `System.Text.Json.Nodes` types; porting it to a `Dictionary<string, object>`/`List<object>` graph is real, separable work. Matches this repo's own precedent — JSON's own `$elemMatch` landed in a later PR than JSON's first `set` — and keeps YAML's first PR reviewable. |
| YAML dependency shape | `NetEscapades.Configuration.Yaml` (read, via `AddYamlFile`) + `YamlDotNet` directly (write, via `ISerializer`) | A single library for both directions; a lower-level YamlDotNet node-tree API for writing | No single maintained library does both `Microsoft.Extensions.Configuration` integration and serialization; `YamlDotNet`'s high-level `SerializerBuilder` already makes sensible block/flow-style and quoting choices for a plain object graph, so there's no need to hand-manage YAML tags or emitter state the way the lower-level node-tree API would require. |
| Where `$elemMatch` conditions resolve to a real position | At real merge time (`JsonLayerMerger.Merge`, via a new pre-processing pass), re-run on every merge, progressively per layer (Environment resolves against base; Client resolves against base+Environment-merged) | Resolve once, at `set`-authoring time only, and bake the resolved position into the overlay file | The "no index in the persisted file" requirement rules out baking anything in. A hand-written overlay (never touched by `set`) still needs to resolve correctly, and a later merge can see a different array shape than the one `set` saw when it wrote the file (e.g. another layer inserted an item first) — only a fresh, real merge-time resolution is correct in general. `set` still does the same resolution eagerly too, for immediate UX (ambiguous/not-found errors surface right away) — but that check is advisory, not authoritative. |
| Rewritten-position representation inside `Merge`'s pre-processing pass | A `JsonObject` keyed by numeric-string index (`{"1": {...}}`), fed to `Microsoft.Extensions.Configuration` via `AddJsonStream` | A `JsonArray` literal with placeholder entries for skipped indices | A real array can't express "touch only index 1, leave 0 and 2+ alone" without placeholder nulls at the skipped positions, and those nulls would themselves flatten to real `IConfiguration` keys and clobber the base layer's actual values there — the same hazard `JsonLayerMerger`'s own doc comment already warns about for plain overlay arrays. A numeric-string object key has no such constraint, and empirically flattens to the identical `IConfiguration` path as a real array index (verified for both `AddJsonFile` and, since this design switches overlay layers to in-memory streams, `AddJsonStream` specifically — not just inferred from the file case). |
| No match for a patch's conditions | Upsert: create a new item, combining the `$elemMatch` condition fields as its identity plus whatever `--set` wrote | Treat "no match" as an error, requiring a separate insert-only command or flag | Mirrors MongoDB's own upsert semantics for the same shape (a filter document plus an update document) — a known convention again, not an invented one. Keeps the overlay file's shape identical regardless of whether a given patch will update or create, which is the whole point: that decision is made at resolution time, not authoring time. |
| XML tag-only matching for a singleton element (found the same way as the JSON array-of-objects gap: a real user hitting it) | A reserved `tag=` `--match` coordinate; writes no `xdt:Locator` at all when it's the only coordinate given, mirroring real XDT's own default-match-by-name behavior | Require the user to invent a synthetic identifying attribute; guess a default attribute name (e.g. always try `mode`) | `customErrors`/`compilation`/`httpRuntime`-shaped elements genuinely have no identifying attribute — there is nothing to guess without breaking "never guessed" (above). A dedicated reserved coordinate names the real thing (the tag) instead of faking an attribute-shaped answer to a non-attribute question, and mirrors real XDT's own idiom for the exact same case. |
| `.env` grammar: quoting, escaping, `export`, inline comments | A value is opaque text (matching quotes stripped, no escape processing, no `${VAR}` expansion); only a whole-line `#` is a comment; an optional leading `export ` is stripped | Full shell-style escape processing; treat any `#` (including mid-value) as starting a comment; ignore `export` as invalid syntax | There's no formal `.env` spec and real tooling disagrees on all of these — treating a value as opaque text is the one choice that doesn't depend on guessing which dialect a given file follows; a mid-value `#` (e.g. a password) would be silently truncated under an inline-comment rule; `export` is common enough (Bash-sourceable files) that rejecting it would break real files for no benefit. |

## Open items for implementation

- **XML's `Insert` case (a genuinely brand-new element) is not implemented.** This isn't an
  oversight or a missed corner — it's a real gap this design never fully closed: `--match`/`--set`
  say which *attributes* to write, but not the new element's **tag name** or **where in the
  document it belongs** (which parent element to nest it under). For an update, that information
  comes for free — the tool finds the real element and reads its tag/ancestry directly. For an
  Insert, there is nothing to find, so nothing to read it from. Closing this needs either a new
  flag (e.g. an explicit parent path/XPath, or a `--tag <name>` alongside a way to name the
  parent) or some other source of that information — not designed here, deliberately, rather than
  bolting on an under-thought flag under time pressure. Shipped behavior: `set` refuses with a
  clear "not yet supported" message (naming this document) instead of guessing a location.
- **XML's array-of-objects matching is not implemented.** Scoped out alongside `Insert` above
  (same underlying reason: nothing to derive a brand-new item's shape from on create) — though
  *matching an existing* array item, unlike creating one, is mechanically answerable the same way
  an XML element match already is, so this could in principle be implemented independently of
  `Insert`; not done here only for lack of time, not a design blocker. (JSON's equivalent gap —
  once a real, previously-undesigned problem found during implementation, since
  `Microsoft.Extensions.Configuration` merges arrays purely by index with no native
  value-matching to port from XDT — is now closed; see the "JSON / YAML" section above for the
  `$elemMatch` mechanism that closed it, and the decision log for why.)
- **YAML's own array-of-objects matching is not implemented** — the same named, deferred gap as
  XML's, described in the "JSON / YAML" section above alongside YAML's own plain-field `set`.
- `.env` and YAML support have both since shipped; their sections above now describe real,
  implemented behavior rather than a forward-looking design.
- One deliberate deviation from the design above, decided during implementation: the verified
  "found a different real attribute" case (XML's `key`/`value` defaults section, and "Errors,
  warnings, and suggestions") always refuses and shows the corrected command now, rather than
  ever silently substituting the verified attribute and proceeding — stricter than what this
  document originally described, not a bug. See the inline notes on those two sections.

## Related reading

- [`GETTING_STARTED.md`](GETTING_STARTED.md) — the three-way XDT branching this design
  automates, explained for a human doing it by hand today.
- [`USAGE.md`](USAGE.md) — the CLI reference, including `set`'s own section.
- [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) §5.5 — `.env` merge semantics and grammar;
  §5.6 — YAML merge semantics, dependencies, and the case-sensitivity limitation.
- [`ROADMAP.md`](ROADMAP.md) — `init` (since shipped, `docs/INIT_COMMAND_DESIGN.md`) and the
  still-deferred TUI/GUI entries this document partially unblocked.
