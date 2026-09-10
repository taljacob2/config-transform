# Scaffolding a tree (`init`) — design

**Status: implemented**, matching this design as written — `docs/CHANGELOG.md`'s `[Unreleased]`
entry and `docs/ROADMAP.md`'s "Current state" have exactly what shipped and where (`InitScanner`/
`InitPlanner`/`InitTemplate`/`InitRunner` in `ConfigTransform.Core`, `PatchFileNaming` shared with
`SetTargetResolver`). Treat this document as the design rationale and decision log behind current
behavior, not a plan still to be executed; if a specific claim here ever reads as aspirational,
`CHANGELOG.md`/the code are the source of truth.

**One real gap this design surfaced, since fixed**: the "Immediately runnable" demo below assumes
a resolve/`--dry-run`/`--diff` invocation can omit `--client`/`--environment` (base-only or
Environment-only), the same way `set`/`--list` always could. At the time `init` first shipped,
`CliOptionsParser`'s default (real-run) branch didn't actually allow that — it required both
unconditionally, a stricter rule than `--list`/`set` ever had, and stricter than the underlying
engine (`LayerPathResolver`/`LayerChain`) needed. That mismatch was caught by this design's own
manual smoke test (the demo below didn't work as written), documented as a correction rather than
fixed at the time, then reported independently by a real user hitting the exact same error against
the published tool and fixed properly in a follow-up change — `--client` now requires
`--environment` (no client-only layer) but neither is otherwise required, uniformly across every
mode. The demo below is the original, correct design; no correction needed anymore.

**`init` gained a fourth, optional axis: `--host`/`-H`** (`docs/HOST_LAYER_DESIGN.md`) — repeatable,
cross-multiplied with every declared client × environment pair, the same way clients already
cross-multiply with environments; requires both `--client` and `--environment`, same "requires the
level above it" rule `--client` itself already follows. The interactive form gained one more
prompt, asked only once at least one client was given (mirroring how the clients prompt is only
meaningful once environments exist). A new Host layer's `extends` defaults to its matching
Client/Environment layer — the same convention every other layer's default already follows, one
level deeper. See "Manifest shape" and "Related fix to `SetTargetResolver`" below for how this
threads through the rest of the design; nothing else in this document changed.

## Why this exists, and why now

`docs/ROADMAP.md`'s "Later / not yet scheduled" and `docs/GETTING_STARTED.md`'s "Should there be
an `init` command?" both say **not yet**, with an explicit trigger condition: revisit once a few
solution repos have gone through the manual setup steps identically, with no real per-repo
variation, because that repetition is the actual signal that automating it earns its complexity.
As of this document, only one solution repo (`config-transform-pilot`) has gone through that setup
— the trigger, taken literally, hasn't fired.

This document is a deliberate, conscious exception to that gate, not an oversight — the same kind
of exception `docs/FIELD_AUTHORING_DESIGN.md` already made for `set` against the same gate. It's
the repo owner's explicit call to design this now rather than wait for more pilots, made with the
gate's original reasoning fully in view: the risk being accepted is that some default this design
picks (directory-scan filters, the exact skeleton `resources[]` shape, the template's tree shape)
might turn out wrong once a second or third real repo goes through it, and would need revising
then. That's an accepted, bounded cost — every default below is a plain, local, mechanical
convention (matching how `resources[].path`/`extends`/manifest JSON shape already work, nothing
invented), not a guess at a workflow this repo hasn't seen yet. Nothing about `init` requires
guessing at *usage patterns* the way a TUI/GUI's workflow design would (see `ROADMAP.md`'s
TUI/GUI entry, which stays deferred — this document does not reopen that one).

**The concrete problem this solves**: today there's no dedicated tree-creation step at all —
`docs/GETTING_STARTED.md`'s "Setting up a project from scratch" gets a first
`.configtransform/Clients/<Client>/<Env>/configtransform.json` into existence only as a *side
effect* of running `set` to author a first override. That means the very first thing a new
project does is create a client-specific override before an environment-only layer with no
overrides has ever existed — workable, but backwards from how the schema actually reads (an
Environment layer has no `extends`; a Client layer `extends` the matching Environment layer, so
the Environment layer conceptually comes first). `init` creates the plain, no-overrides skeleton
tree directly, in the order the schema implies, and now also solves a second, real question `set`
never touches: knowing what to actually list in `resources[]` requires already knowing every
config file this project touches. Today that's entirely manual, typed by hand, one path per
project. `init` scans for candidates instead.

## Command shape

```
configtransform init [--scan-root <dir>] [--yes]
configtransform init --environment <Env> [--environment <Env> ...] [--client <Client> ...] [--resource <path> ...] [--no-scan] [--yes]
configtransform init --template
configtransform init --dry-run [any of the above]
```

Two hard modes, chosen up front — not inferred from a partial mix of flags (see "Rejected: flag
pre-fill skips the matching prompt" below):

- **Interactive** — no `--environment`/`--client`/`--resource`/`--template`/`--yes`/`--no-scan`
  flag present at all, and stdin is a real terminal (`!Console.IsInputRedirected`). Runs the form
  described below.
- **Quiet** — any of those flags present, or stdin is not a terminal. Fully flag-driven, prompts
  nothing, safe for CI. If stdin is redirected/not a terminal and *none* of those flags are
  present either, `init` fails immediately with `Error: init needs --environment (or --template)
  when not running in an interactive terminal.` — it never attempts a `Console.ReadLine()` that
  would just read EOF and silently misbehave, the same CI-safety concern already on record in
  `FIELD_AUTHORING_DESIGN.md` for why `set` has no interactive prompt of its own.

`--template` is mutually exclusive with every other init flag (`--scan-root`,
`--environment`, `--client`, `--resource`, `--no-scan`, `--yes`) — combining them is a
`CliOptionsParser`-style error naming both flags, matching the existing convention (e.g.
`"--client requires --environment with 'set'"`).

## Interactive mode: a form, not a TUI

Plain sequential `Console.ReadLine()` prompts — one question at a time, answered by typing text
and pressing enter, no cursor-addressed redraw, no arrow-key navigation, no third-party TUI
library. This is a deliberate, load-bearing choice, not a placeholder for "a real TUI later" (see
the decision log): every existing command in this CLI is 100% flag-driven with zero interactive
surface today (confirmed — no `Console.ReadLine`/`Console.In` anywhere in the repo before this),
and no TUI/prompt package (Spectre.Console, Terminal.Gui, or similar) is referenced by any
`.csproj` under `src/`. Pulling one in would be the first UI dependency this repo has ever taken
on, for a command that runs once per repo (or once per new client/environment) — a cost this
design doesn't need to pay, because a numbered checklist over plain stdin covers everything the
brief actually asked for ("interactive... like a form").

The flow:

1. **Scan.** Recursively walk from `--scan-root` (default: repo root) for files whose extension a
   registered `FormatEngine` handles (`.config`/`.xml`/`.json` today — reads
   `FormatEngines.All`, never a hardcoded list, so a future format registers itself here too, same
   as everywhere else in the tool). Skip `.git/`, `.configtransform/` itself, and common
   build/dependency directories (`bin/`, `obj/`, `node_modules/`) — see "Scanning: directory
   filters, not content filters" below for why the list stops there.
2. **Present candidates as a numbered checklist**, repo-root-relative paths, one per line.
   `Select which of these are resources to manage (e.g. "1,3,5", "all", "none"):` — parsed as a
   comma-separated list of 1-based indices, or the literal `all`/`none`. An out-of-range index is
   a re-prompt with the specific bad token named, not a silent skip.
3. **`Environments (comma-separated, e.g. Production,Test):`** — at least one required; a blank
   answer re-prompts.
4. **`Clients (comma-separated, or blank for none yet):`** — genuinely optional; an
   environment-only tree (no clients) is a valid, expected shape (mirrors "an Environment layer
   has no `extends`" being the base case, and matches a real onboarding order — environments
   usually exist before the first client does).
5. **Confirm and write** — echoes the exact tree about to be created (every file path, one line
   each) before touching disk, then writes it. `--dry-run` stops right here, after step 5's
   preview, writing nothing — same meaning `--dry-run` already has for a real run.

Every selected resource applies to every declared environment (and, transitively, every client)
— `init` doesn't ask "which environments does resource X apply to," it scaffolds the full cross
product. Narrowing which layer actually overrides which resource is `set`'s job once real
overrides exist; `init` only has to get the skeleton in place. (See "Open items" for the one
genuine follow-up gap this leaves: per-resource environment scoping.)

**Why this is worth calling out as an actual advantage, not just a limitation**: because the whole
wizard is `Console.ReadLine()` over `Console.In`, it's exactly as testable as any other stdin-based
xUnit test — `Console.SetIn(new StringReader("1,2\nProduction,Test\nAcme\n"))`, run `init`, assert
the resulting files — with no need for a UI-automation harness a real TUI would require. This is
the same reasoning behind `set` having no prompt of its own, applied here to justify the shape the
one prompt-driven command *does* take.

## Quiet mode

Same three inputs, from flags instead of prompts, and nothing is asked:

- `--resource <path>` (repeatable) — explicit list; skips scanning entirely if present at all.
  Each path must exist on disk already (`init` never creates a resource file outside `--template`
  mode — see "Errors" below) and is taken repo-root-relative, the same convention as everywhere
  else in this tool (`docs/MANIFEST_SCHEMA.md`'s "Path convention").
- No `--resource` given → scans (same scan as interactive mode, same directory filters), and:
  - `--yes` present → every scanned candidate is selected, no confirmation.
  - `--yes` absent and stdin is a terminal → falls back to the interactive checklist step (step 2
    above) even though other flags made this "quiet" mode overall; only the environment/client
    questions are skipped because those flags were given.
  - `--yes` absent and stdin is *not* a terminal → error: `Error: init found N candidate
    resources; pass --yes to accept them all, or --resource to name them explicitly (not running
    in an interactive terminal).`
- `--environment <Env>` (repeatable, at least one required unless `--template`) / `--client
  <Client>` (repeatable, optional) — same flag identity as the existing single-value
  `--environment`/`--client` used to *target* a run, reused here with repeatable arity because
  `init` is declaring the set of environments/clients to create, not selecting one to run against
  (see decision log for why this doesn't get separate `--environments`/`--clients` plural flags).
- `--no-scan` with no `--resource` given at all is an error (`Error: --no-scan requires at least
  one --resource — nothing to list otherwise.`) — it exists purely to make "I'm passing resources
  explicitly, don't also scan" unambiguous when combined with `--resource`, not as a way to
  produce a resource-less tree.

## Template mode

`configtransform init --template` — the one case `init` creates a resource file
itself, since a template has to scaffold against *something* even in a repo with no existing
config files yet (scanning a fresh/empty repo would just find nothing). Fixed, non-configurable
content:

- Two environments: `Production`, `Test`.
- Two clients: `Client-A`, `Client-B`.
- One resource, written to `configtransform-template.json` at the repo root (created if missing;
  refuses to overwrite if a different file is already there — see "Errors"):
  ```json
  { "message": "Hello, world! (from base config)" }
  ```

**Every layer overrides `message` with its own value, naming itself** — not the same string
repeated at every layer. This is deliberate: a template's entire purpose is to be run against
immediately and show the layering mechanism actually working, not just to prove the tree was
created. A patch per layer, each following the existing `patch-{path-with-'/'-as-'-'}.{ext}`
naming convention (`SetTargetResolver`'s own scheme, in the same directory as the
`configtransform.json` referencing it) — **except that the trailing `.{ext}` is only appended
when it isn't already there**: `configtransform-template.json`'s own extension already is `json`,
same as the JSON engine's patch extension, so the patch file is `patch-configtransform-template.json`,
not `patch-configtransform-template.json.json`. This is a small, real fix to
`SetTargetResolver.cs`'s naming rule as it exists today — see "Related fix to `SetTargetResolver`"
below — not something invented for the template alone; `init` reusing the *fixed* rule keeps it
one convention instead of two.

| Layer | `resources[].patch` content |
|---|---|
| *(base)* `configtransform-template.json` | `{ "message": "Hello, world! (from base config)" }` |
| `Environments/Production` | `{ "message": "Hello, world! (from Production config)" }` |
| `Environments/Test` | `{ "message": "Hello, world! (from Test config)" }` |
| `Clients/Client-A/Production` | `{ "message": "Hello, world! (from Client-A Production config)" }` |
| `Clients/Client-A/Test` | `{ "message": "Hello, world! (from Client-A Test config)" }` |
| `Clients/Client-B/Production` | `{ "message": "Hello, world! (from Client-B Production config)" }` |
| `Clients/Client-B/Test` | `{ "message": "Hello, world! (from Client-B Test config)" }` |

Thirteen files total (1 base + 2 Environment manifests + 2 Environment patches + 4 Client
manifests + 4 Client patches) — every one of them written, none left as an empty
patch-less skeleton the way a scanned/typed `init` run's Environment layers are (see "Manifest
shape" below); a template's whole point is to be immediately runnable, so it always has a real
override to show at every layer.

**Immediately runnable, and that's the demo**, three invocations against the exact same
`--resource configtransform-template.json`, no other setup:

```
configtransform --resource configtransform-template.json --dry-run
  → { "message": "Hello, world! (from base config)" }               # no --client/--environment: base file alone

configtransform --environment Production --resource configtransform-template.json --dry-run
  → { "message": "Hello, world! (from Production config)" }         # Environment layer only, no client

configtransform --client Client-A --environment Production --resource configtransform-template.json --dry-run
  → { "message": "Hello, world! (from Client-A Production config)" } # full chain: base → Production → Client-A
```

`--diff` against any of the above shows exactly one line changing (`message`), which is the point
— a brand-new user's very first command after `init --template` can be `--diff` instead of
`--dry-run`, and see the override mechanism itself rather than just a merged blob.

- The full skeleton tree from "Manifest shape" below otherwise applies unchanged — template mode
  differs only in *where the inputs come from* (fixed, not scanned/typed) and in always writing a
  real patch at every layer instead of the patch-less Environment default — not in the underlying
  mechanics of how the tree gets built.

**`--template` was a bare switch, not a named flag, until a second template actually showed up.**
There was exactly one template (referred to in this document as "the hello-world template" purely
for readability — it isn't a name the CLI itself ever takes as input) — see the decision log for
why that was a deliberate reversal of this document's original name-based-flag proposal. "Open
items" below already predicted the accepted cost once a second template arrived: `--template`
would grow a value, defaulting to today's tree so every existing invocation keeps working
unchanged. That's now happened (`docs/HOST_LAYER_DESIGN.md` decision log #7): a bare `--template`
(or the explicit `--template default`) still builds exactly the hello-world tree above,
byte-for-byte; `--template hosts` additionally scaffolds one worked
`Hosts/Host-1/configtransform.json` example (`docs/HOST_LAYER_DESIGN.md`) under the template's
existing Client-A/Production layer, via `InitTemplate.BuildHostsPlan`, reusing every file
`BuildPlan` already produces rather than duplicating the tree. `CliOptionsParser` peeks the token
after `--template`: a value that isn't itself a recognized flag (or the `help` verb) is consumed
as the variant name; anything else defaults to `"default"`.

### Related fix to `SetTargetResolver`

`SetTargetResolver.cs`'s existing patch-filename rule (used by `set` today, already shipped) is
`patch-{path-with-'/'-as-'-'}.{patchExt}` unconditionally — for any resource whose own extension
already equals the engine's patch extension, that produces a stuttering double extension:
`appsettings.json` → `patch-appsettings.json.json`, the exact case this template's own resource
hits. This isn't a template-specific concern; every JSON resource `set` has ever created a patch
for has this same stutter today, silently. The fix is small and safe to make everywhere at once,
not just for `init`: append `.{patchExt}` only when the sanitized path doesn't already end with
it. It's safe because `SetTargetResolver.ResolveTarget` only computes this candidate name when
creating a **new** patch entry (`existingEntry?.Patch is not null` always wins and is used as-is,
untouched) — no already-recorded `patch` path in any existing `configtransform.json` is affected,
only file names chosen for patches that don't exist yet. This document assumes that fix lands as
part of the same implementation pass as `init` (so `init`'s template and `set`'s own patch
creation agree on one naming rule, not two) — see "Open items" if it turns out `set`'s side needs
to be split into its own separate change instead.

## Scanning: directory filters, not content filters

The scan excludes only structural noise every project regardless of ecosystem would want excluded
— `.git/`, `.configtransform/` itself, `bin/`, `obj/`, `node_modules/` — plus, as of a real user's
report against the published tool, two exact filenames that are universal .NET/NuGet *tooling*
manifests rather than application config: `dotnet-tools.json` (always the local tool manifest
`dotnet new tool-manifest` creates, conventionally under `.config/`) and `nuget.config` (the
package-source manifest). Beyond those two, it does **not** try to guess which `.json`/`.config`/
`.xml` files are "really" config (by name, by schema, by location) — `CLAUDE.md`'s own
core-concepts rule is explicit that this tool never special-cases by filename or schema, and
scanning is not exempt just because it isn't a merge operation. The numbered checklist is where
that judgment call happens, made by the person running `init`, not guessed by the tool. This does
mean a `--template`-free, `--yes`-free scan can surface files a human wouldn't consider "config"
(a `tsconfig.json`, a `package.json` sitting outside `node_modules/`) — that's accepted;
deselecting a couple of extra checklist lines is a small, one-time cost, and it's strictly safer
than a content heuristic silently missing a real resource because it didn't look "config-shaped"
(e.g. `docs/CLAUDE.md`'s own `GenericJson`/`GenericXml` test fixtures exist specifically to prove
this tool never assumes a schema — a smart-filter scan would contradict that same principle it's
built next to).

**Why `dotnet-tools.json`/`nuget.config` are a named exception, not a crack in that rule**: unlike
a `tsconfig.json` or `package.json` — which *could* plausibly be a real resource in some repo, so
guessing wrong either way carries real risk — these two are never a per-client/per-environment
application resource in *any* repo; they configure the development toolchain itself (which dotnet
tools are installed, which NuGet feeds to use), not anything a client or environment would ever
need overridden. Excluding them by exact name has no "guessed wrong and silently missed a real
resource" failure mode a schema/content heuristic would, so it doesn't reopen the door the
directory-exclude precedent already establishes: both lists exclude by exact, hardcoded name, not
by inferring intent from content or shape.

## Manifest shape

Every environment gets exactly one file, every client×environment pair gets exactly one file,
built with the same `JsonSerializerOptions` (`WriteIndented = true`,
`DefaultIgnoreCondition.WhenWritingNull`) `SetTargetResolver` already writes with — `init` is a
second creator of `configtransform.json`, not a second convention.

`.configtransform/Environments/<Env>/configtransform.json` — every selected resource, no `patch`
(nothing to override yet; `extends` is omitted since Environment layers never have one):
```json
{
  "resources": [
    { "path": "OrderProcessor.Framework/App.config" },
    { "path": "OrderProcessor.Framework/appsettings.json" }
  ]
}
```

`.configtransform/Clients/<Client>/<Env>/configtransform.json` — declares `extends` only, empty
`resources[]` (the documented "a layer that only exists to declare `extends`, with nothing of its
own to add" shape from `docs/MANIFEST_SCHEMA.md`):
```json
{
  "extends": ".configtransform/Environments/<Env>/configtransform.json",
  "resources": []
}
```

**Every selected resource is listed in the Environment layer even with no `patch`, deliberately —
this is not optional.** `LayerChain.ResolveAllResources` (the no-`--resource` "process every
resource" path) unions `resources[].path` across the chain; a resource never listed anywhere in
the chain is invisible to that path entirely, even though the base file exists on disk. Without
this, a freshly `init`'d tree would resolve to *zero* resources on a plain
`configtransform --client X --environment Y` call until someone ran `set` once per resource —
defeating the entire point of scaffolding the tree up front. This is the one genuinely
non-obvious mechanical fact this design leans on; see the decision log for it as a named decision,
not an incidental detail.

## Idempotency

Re-running `init` against a tree it (or `set`) already touched is safe, matching the convention
`SetTargetResolver.EnsureResourceListed` already established for `set`: load-or-create each
manifest, then merge in whatever's new (an environment/client that didn't exist yet, a resource
not yet listed at that layer) without touching anything already there — an existing entry's
`patch` is never cleared, an existing `resources[]` entry is never duplicated (matched by path,
case-insensitively, the same `LayerChain.PathsEqual` convention used everywhere else). This is
what makes `init` safe to run again later to onboard a second client or a newly-added resource,
rather than a one-shot, destructive bootstrap.

## Errors, warnings, and suggestions

- `--template` combined with any other init flag → error naming both flags (mirrors the existing
  `set`-mode validation style).
- Interactive mode entered with stdin not actually a terminal after all (redirected but the
  up-front `IsInputRedirected` check somehow passed, or EOF reached mid-wizard) → error naming
  which question was left unanswered, suggesting the quiet-mode flag that would have supplied it.
- Quiet mode, `--client` given without any `--environment` → error, same wording pattern as `set`'s
  existing `"--client requires --environment with 'set' (there is no client-only layer)."`
- Quiet mode, no `--environment` and no `--template` → error naming both as the two ways to supply
  what's being scaffolded.
- `--resource <path>` naming a file that doesn't exist on disk → error (`init` never authors a
  resource file itself outside `--template` mode; a nonexistent explicit path is always a typo or
  a not-yet-created file, worth failing loud on rather than quietly scaffolding a manifest entry
  whose base file resolution would fail the moment anyone actually ran `configtransform` against
  it).
- An environment or client name colliding case-insensitively with one that already exists under a
  *different* casing (`Production` vs. `production`) → error, the same case-insensitive-collision
  hazard `FileResolver`/`CLAUDE.md`'s "Case-insensitive file resolution" bullet already names for
  resource files (Linux CI vs. Windows dev) — a directory name is exactly as exposed to that
  hazard as a resource file name is.
- `--template` given and `configtransform-template.json` already exists with *different*
  content than the template's own → error, refusing to overwrite (never silently clobbers content
  that isn't `init`'s own template output); if the content is byte-for-byte the template's own
  (e.g. a re-run), it's a no-op, consistent with idempotency above.
- Scan finds zero candidates and no `--resource`/`--template` was given → not an error — proceeds
  to the environment/client questions (or flags) with an empty resource list; a
  `resources: []` skeleton is a legitimate, if unusual, starting point (same shape the schema
  already allows for a Client layer with nothing of its own).

## Decision log

| Decision | Chosen | Rejected alternative(s) | Why |
|---|---|---|---|
| Interactivity model | Plain sequential `Console.ReadLine()` form, no TUI | Full-screen TUI (Spectre.Console/Terminal.Gui), arrow-key multi-select | First UI dependency this repo would ever take on, for a once-per-repo command; a redraw-based UI is far harder to test deterministically (no `Console.SetIn` equivalent), and a numbered checklist covers what was actually asked for ("interactive... like a form"). |
| Mode selection | Two hard modes (interactive / quiet), chosen up front by flag presence + TTY check | Flags pre-fill/skip individual prompts, blending both modes | Simpler to implement and reason about — each mode is one clean validation branch, matching how `CliOptionsParser` already keeps `set`/`--list`/real-run validation in separate branches rather than merging partial states. Progressive flag-prefill is a real usability win but genuinely separable; deferred, see "Open items." |
| CI safety | Refuse to enter interactive mode (or continue mid-wizard) when stdin isn't a real terminal; error immediately instead | Silently read whatever's on redirected/empty stdin | Same concern already on record in `FIELD_AUTHORING_DESIGN.md` for why `set` has no prompt at all — a CI run must fail loud, not hang or apply blank answers. |
| Scan filtering | Directory-level excludes only (`.git/`, `.configtransform/`, `bin/`, `obj/`, `node_modules/`) | Content/filename heuristics for "is this really a config file" | `CLAUDE.md`'s core-concepts rule: never special-case by filename or schema. The checklist is where human judgment belongs; the tool only excludes structural noise every project has regardless of ecosystem. |
| Scan filtering, later addition | Also exclude two exact filenames — `dotnet-tools.json`, `nuget.config` | Leave them to the checklist, same as every other candidate | Reported against the published tool: `init` surfaced `.config/dotnet-tools.json` (this very repo's own tool manifest) as a checklist candidate. Unlike a `tsconfig.json`/`package.json` (which could plausibly be a real resource somewhere), these two are never a per-client/per-environment application resource in any repo — excluding them by exact name carries none of the "guessed wrong" risk a content/schema heuristic would, so it's a named exception to the row above, not a reversal of it. See "Scanning: directory filters, not content filters" for the full reasoning. |
| Environment layer lists every selected resource, even with no `patch` | Yes, always | Leave resources unlisted until a real override exists via `set` | `LayerChain.ResolveAllResources`'s no-`--resource` union only sees resources actually present in some layer's `resources[]`; skipping this would make a freshly `init`'d tree resolve to nothing until `set` ran once per resource, defeating the point of scaffolding. |
| Flag identity for environments/clients | Reuse `--environment/-e`/`--client/-c`, repeatable in `init` mode | New `--environments`/`--clients` plural flags | Keeps the flag vocabulary from growing for a mode-scoped arity difference — the same pattern `--match`/`--set` already use (repeatable, no separate plural sibling) rather than a new naming convention. |
| Idempotent re-runs | Merge into existing manifests (mirrors `SetTargetResolver.EnsureResourceListed`) | Refuse if `.configtransform/` already has content, or always overwrite | Onboarding a second client, or a newly-added resource, later is a normal case, not an error — same convention `set` already established. |
| `--template` shape | A bare switch, no value | A named flag (`--template <name>`), a fully configurable template system (custom env/client names, custom resource content) | There is exactly one template, meant to stay the basic/default starter even if a second one is ever added — a bare switch says that plainly. A named flag optimizes for a future that may never arrive at the cost of a slightly worse everyday command (`init --template hello-world` vs. `init --template`) for the one template that actually exists. Configurable templates would just be quiet mode with extra steps — no separate feature earns its complexity yet. See "Open items" for the accepted cost if a second template does show up later. |
| `hello-world` template's `message` value | A distinct value per layer, naming that layer (`"...from Client-A Production config"`, etc.) | The same `"Hello, world!"` string repeated at every layer | A template exists to be run against immediately — a repeated string would create a tree that merges but never visibly *changes*, hiding the one thing `init --template` is supposed to demonstrate. A distinct message per layer makes `--diff`/`--dry-run` show the override chain working on the very first command. |
| Patch filename: trailing extension only appended if not already present | Yes, and retrofitted onto `SetTargetResolver`'s existing rule too, not just for `init` | Keep `SetTargetResolver`'s unconditional `patch-{path}.{ext}` rule, let `init`'s template produce an inconsistent, cleaner-looking name of its own | A resource whose own extension already matches the patch extension (every plain `.json` resource, which is the common case) gets a stuttering `name.json.json` under the unconditional rule — real, already live in `set` today, not template-specific. Fixing it in one place keeps `init` and `set` agreeing on one naming convention; fixing it only in `init`'s template would leave two different, competing conventions in the same tool. Safe to change: only affects filenames chosen for patches that don't exist yet (`SetTargetResolver` always reuses an already-recorded `patch` path verbatim). |
| Proceeding before the "few solution repos" gate's trigger condition is literally met | Yes, as an explicit, named owner override | Wait for a second/third pilot repo first | Same kind of exception `FIELD_AUTHORING_DESIGN.md` already made for `set` against this same gate — every default here is a mechanical convention already proven elsewhere in this tool (manifest JSON shape, path resolution, idempotency), not a guess at workflow patterns the way a TUI/GUI's design would be. The accepted risk (a default proving wrong against a second real repo) is bounded and explicitly named, not ignored. |

## Open items for implementation

- **Progressive flag pre-fill** (a given `--environment`/`--client` flag skips just that prompt
  in interactive mode rather than forcing full quiet mode) — a real usability improvement, cut
  from v1 to keep mode-selection logic simple (see decision log). Worth revisiting once `init`
  itself has been used a few times.
- **Per-resource environment/client scoping** — v1 always scaffolds the full cross product (every
  resource × every environment × every client). A tree where resource A only exists in
  `Production` and resource B only in `Test` isn't expressible from the wizard/flags as designed;
  today the only escape hatch is hand-editing the generated `configtransform.json` afterward
  (same escape hatch `set` already relies on for anything outside its own scope). Worth a second
  pass once real trees show this actually matters, rather than designed speculatively now.
- ~~**A second template**~~ — closed: `--template hosts` (`docs/HOST_LAYER_DESIGN.md` decision
  log #7) is exactly the value-taking-flag shape this item predicted, `default` kept as the
  existing tree for compatibility. Left here, struck through, as the historical record of the
  call this item was watching for.
- **Whether the `SetTargetResolver` patch-naming fix ships in the same change as `init`, or
  separately** — this document assumes the same change (see "Related fix to `SetTargetResolver`"
  under "Template mode"), since shipping `init` with a naming rule `set` doesn't share would be a
  worse outcome than splitting the fix out first. If implementation finds a reason they need to
  be separate, the fix should still land, just not necessarily gated on `init` itself.
- **Scan performance on a very large repo** — `Directory.EnumerateFiles(..., AllDirectories)` is
  already used once in this codebase (`LayerChain.ReverseLookup`) without a performance concern
  raised; worth re-checking only if a real repo's scan turns out slow in practice, not speculatively
  now.

## Related reading

- `docs/GETTING_STARTED.md`'s "Setting up a project from scratch" and "Should there be an `init`
  command?" — the manual process this replaces, and the original gate this document is a named
  exception to.
- `docs/ROADMAP.md`'s "Later / not yet scheduled" — the tracked item and trigger condition this
  document responds to.
- `docs/MANIFEST_SCHEMA.md` — the `configtransform.json` shape `init` writes, unchanged.
- `docs/FIELD_AUTHORING_DESIGN.md` — the prior, structurally identical exception to the same gate,
  and the `SetTargetResolver` idempotency convention this design reuses.
- `CLAUDE.md`'s core-concepts bullets on format-genericity and case-insensitive file resolution —
  both directly constrain the scanning and naming rules above.
