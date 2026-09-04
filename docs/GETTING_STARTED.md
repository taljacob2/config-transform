# Getting started

For people who just want to *use* this, not understand the whole architecture first. For the
full "why," see `CONFIG_MANAGEMENT.md`; for the field-by-field schema, see `MANIFEST_SCHEMA.md`;
this document only covers the "how" for everyday work.

## The shape of it, in one picture

```mermaid
flowchart TB
    Base["<b>1. Base file</b><br/>App.config / Web.config / appsettings.json<br/><i>always applied</i>"]
    Env["<b>2. Environment layer</b><br/>.configtransform/Environments/Production/configtransform.json<br/><i>optional — shared by every client in this environment</i>"]
    Client["<b>3. Client layer</b><br/>.configtransform/Clients/ClientA/Production/configtransform.json<br/><i>optional — extends the Environment layer, adds its own patch</i>"]
    Result(["<b>Final merged config</b><br/>deployed for ClientA in Production"])

    Base -->|applied first| Env
    Env -->|"applied second<br/>overrides matching keys"| Client
    Client -->|"applied last<br/>overrides matching keys"| Result
```

This is not inheritance in the OOP sense, but unlike the tool's very first design, it *is* now an
explicit, declared reference between layers, not just a fixed rule baked into the tool: a Client
layer's own `configtransform.json` says `"extends": ".../Environments/Production/configtransform.json"`
directly. Each layer's `resources[]` entry pairs a project with its own optional `patch` — a layer
that doesn't list a project at all just leaves it alone; a missing `patch` file that *is* declared
is an error, but a project simply not mentioned isn't. See `MANIFEST_SCHEMA.md` for the full
schema and `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md` for why it's shaped this way.

**The bigger picture** — one base, many environments, many clients per environment, all sharing
that same base:

```mermaid
flowchart LR
    Base["Base file<br/>(one per project)"]
    Base --> P[".configtransform/Environments/Production"]
    Base --> S[".configtransform/Environments/Staging"]
    P --> PA[".configtransform/Clients/ClientA/Production<br/>extends Production"]
    P --> PB[".configtransform/Clients/ClientB/Production<br/>extends Production"]
    S --> SA[".configtransform/Clients/ClientA/Staging<br/>extends Staging"]
```

## Setting up a project from scratch

1. The project already has its base config file (`App.config`, `Web.config`, or
   `appsettings.json`) — nothing changes there. No manifest, no separate declaration file for the
   project itself: `resources[].path` (below) *is* the declaration, pointing straight at the real
   file.
2. Install the tool once per repo:
   ```bash
   dotnet new tool-manifest   # if the repo doesn't already have one
   dotnet tool install --local ConfigTransform.Cli --version <latest>
   ```
   One package covers both XML and JSON projects — `configtransform` dispatches each resource to
   the right merge engine by its own file extension, so there's nothing extra to install even if
   the repo has both. This package is published to a **private-by-default** GitHub Packages
   feed — the install above fails with a 401/403 until `nuget.config` and a `read:packages`
   credential are set up. See [`SECRETS_AND_LOCAL_SETUP.md`](SECRETS_AND_LOCAL_SETUP.md) §1 for
   the one-time setup (CI secret + `nuget.config` + local env vars, with Windows/macOS/Linux
   instructions) — do that first if this repo hasn't already.

   Once installed, `dotnet tool run configtransform` with no arguments at all (or
   `configtransform help`/`--help`/`-h` anytime) prints a quick tldr-style cheat sheet — worth
   running once just to confirm the install worked, before setting up a real override below.
3. Author the first override with `set` rather than hand-writing a `configtransform.json` and a
   patch file — it creates both, correctly, in one step:
   ```bash
   dotnet tool run configtransform -- set \
     --resource path/to/YourProject/App.config \
     --client ClientA --environment Production \
     --match ApiUrl --set https://clienta.example.com
   ```
   `--resource` is the project's own **repo-root-relative** path — not a separate manifest entry,
   not a `.csproj` reference, just the real file's path from the repo root. This one command:
   creates `.configtransform/Clients/ClientA/Production/configtransform.json` (with `extends`
   pointing at the matching Environment layer, even if that doesn't exist yet), authors
   `.configtransform/Clients/ClientA/Production/patch-<project>-App.config.xml`, and prints the
   effective diff. See `docs/FIELD_AUTHORING_DESIGN.md` for everything `set` can do, and `USAGE.md`
   for the full flag reference.
4. Preview before committing anything:
   ```bash
   dotnet tool run configtransform -- \
     --resource path/to/YourProject/App.config \
     --client ClientA --environment Production --diff
   ```
   Every flag above has a short form for less typing: `-r`/`-c`/`-e`. See `docs/USAGE.md` for the
   full flag reference, including omitting `--resource` entirely to process every resource a
   layer touches in one call.

That's the whole setup. No other configuration is needed.

## Day-to-day: adding a field

Three cases, depending on who needs the new value. All three can be done with `set` (recommended
— see step 3 above) or by hand-editing the relevant file directly; the hand-edited shape is shown
here since it's what `set` produces under the hood, and useful to recognize when reading a diff.

### 1. Same value for everyone (every client, every environment)

Just edit the base file directly. Nothing else to touch.

```xml
<!-- App.config -->
<add key="NewSetting" value="defaultValue" />
```
```json
// appsettings.json
{ "NewSetting": "defaultValue" }
```

### 2. Different per environment, same across clients within it

Add the key to the base file with a sensible default, then override it in the Environment layer's
own patch file (`.configtransform/Environments/<Env>/patch-<project-path-dashed>.<ext>`,
referenced from that layer's `configtransform.json`).

```xml
<!-- base -->
<add key="LogLevel" value="Debug" />

<!-- .configtransform/Environments/Production/patch-...xml -->
<add key="LogLevel" value="Warning" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
```
```json
// base
{ "LogLevel": "Debug" }

// .configtransform/Environments/Production/patch-....json
{ "LogLevel": "Warning" }
```

### 3. Different per client

Same idea, one layer deeper — override in the Client layer's own patch file
(`.configtransform/Clients/<Client>/<Env>/patch-<project-path-dashed>.<ext>`).

```xml
<!-- .configtransform/Clients/ClientA/Production/patch-...xml -->
<add key="ApiUrl" value="https://clienta.example.com" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
```
```json
// .configtransform/Clients/ClientA/Production/patch-....json
{ "ApiUrl": "https://clienta.example.com" }
```

### One real difference between XML and JSON when the key is brand new

- **JSON**: any layer can introduce a new key — it just appears in the merged result. No
  special syntax needed.
- **XML/XDT**: `SetAttributes` + `Locator="Match(key)"` requires the key to already exist in
  the document at that point — a `Locator` *matches* an existing element, it doesn't create
  one. So: **always add a genuinely new key to the base file first**, with a sensible default,
  then override it in whichever layers need something different — exactly the pattern in
  cases 2 and 3 above. Only reach for `xdt:Transform="Insert"` (which adds a new element with
  no matching required) when a key should exist for one client only and nowhere else — an
  unusual, deliberate exception, not the default way to add a field.

## Should there be an `init` command?

**Designed, not implemented yet** — full design at `docs/INIT_COMMAND_DESIGN.md`. This section
originally said "not yet," reasoning that `set` already covers the common case, no real solution
repo had validated a "typical" tree shape, and it would mean real added CLI scope. All three are
still true, and the design doc doesn't pretend otherwise — it's a deliberate, named exception to
that gate (the same kind of exception this file's own `FIELD_AUTHORING_DESIGN.md` already made
for `set`), made by the repo owner with the original reasoning fully in view rather than by
waiting for the trigger condition below to fire on its own. See the design doc's "Why this
exists, and why now" for the full argument.

Tracked in `ROADMAP.md`'s "Next up" — implementation is the next actionable step, not a further
design pass.
