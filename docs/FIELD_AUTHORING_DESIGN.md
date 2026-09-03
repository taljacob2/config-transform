# Field authoring (`set`) — design

**Status: partially implemented.** `ConfigTransform.Xml`'s `set` command exists and covers the
"update an existing element" case in full (base file, Environment overlay, Client overlay; the
bare `--match`/`--set` defaults with verification; the ambiguous/not-found/"did you mean" error
paths; the auto-`--diff`) — see `docs/USAGE.md`'s `set` section and `docs/CHANGELOG.md`'s
`[Unreleased]` entry for exactly what's live and where. **Not yet implemented**: the `Insert`
case (a genuinely brand-new element — see "Open items" below for why that's a real gap, not an
oversight) and `ConfigTransform.Json`'s `set` entirely. This document otherwise still reflects
the original completed design from a product-brainstorming session; treat any specific claim
about *current* behavior as superseded by `docs/CHANGELOG.md` where the two differ.

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
the TUI/GUI themselves stay deferred — this is scoped to field authoring only.

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
configtransform-xml  set --manifest ... --client <C> --environment <E> --match <attr>=<value> [--match ...] --set <attr>=<value> [--set ...]
configtransform-json set --manifest ... --client <C> --environment <E> --match <attr>=<value> [--match ...] --set <attr>=<value> [--set ...]
```

Identical shape across `ConfigTransform.Xml` and `ConfigTransform.Json` — same principle
`docs/USAGE.md` already holds for the rest of the CLI ("both tools share the exact same CLI
shape"). `--match` locates the target; `--set` writes fields on it. Both repeatable, and always
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
attribute-matching problem, and needs two: one to reach the array, one to pick the item.

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

YAML is not designed separately from JSON here: `docs/CONFIG_MANAGEMENT.md` §5.5 already
confirmed YAML fits the existing design without a redesign (same tree-of-maps/lists/scalars
data model, different serialization) — this command's model inherits that, once YAML support
itself lands (still "not needed yet" per `docs/ROADMAP.md`; this design doesn't change that
timeline, it just means `set` won't need separate design work when it does).

### `.env`

Same shape as XML's simple case, because the shapes really are the same: a `.env` line
(`FOO=bar`) and an `appSettings` entry (`<add key="FOO" value="bar"/>`) are both "one identity,
one value."

```
set --match key=FOO --set value=bar
```

(`.env` support itself is also still "not needed yet" per `docs/ROADMAP.md` — same note as
YAML above.)

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
  candidates, with `--match literal-key=...` (not `key=...`) as the explicit disambiguator.
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
- **`ConfigTransform.Json`'s `set` doesn't exist.** `--match key=...`/`literal-key=...` (the
  `:`-separated navigation, and its collision escape hatch) and the array-of-objects double-match
  case are designed above but unimplemented — they'd need their own fixtures (`GenericJson`-style
  arbitrary schema, an array-of-objects case) the way XML's implementation now has
  `XmlFieldAuthorTests`/`XmlSetCommandCliTests` (`tests/ConfigTransform.Xml.Tests/`).
- YAML and `.env` support don't exist in this tool at all yet (`docs/ROADMAP.md`: both "not
  needed yet") — this document's per-format sections for them are forward-looking, not
  something `set` can ship against today.
- One deliberate deviation from the design above, decided during implementation: the verified
  "found a different real attribute" case (XML's `key`/`value` defaults section, and "Errors,
  warnings, and suggestions") always refuses and shows the corrected command now, rather than
  ever silently substituting the verified attribute and proceeding — stricter than what this
  document originally described, not a bug. See the inline notes on those two sections.

## Related reading

- [`GETTING_STARTED.md`](GETTING_STARTED.md) — the three-way XDT branching this design
  automates, explained for a human doing it by hand today.
- [`USAGE.md`](USAGE.md) — the CLI reference `set` will join once implemented.
- [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) §5.5 — YAML/`.env` format compatibility.
- [`ROADMAP.md`](ROADMAP.md) — the `init`/TUI/GUI entries this document partially unblocks.
