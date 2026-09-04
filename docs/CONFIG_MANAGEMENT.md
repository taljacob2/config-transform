# Multi-Client, Multi-Environment Configuration Management — Architecture Spec

Status: **Design finalized, implementation not yet started.** No solution repository has been
chosen yet, and no real inventory of existing config files (App.config / appsettings.json /
other) has been performed against the actual solutions this will apply to. Treat this document
as the agreed target architecture, to be validated against real project structure before or
during implementation.

> **Why this document lives in the `config-transform` tool repo:** this spec describes an
> architecture broader than this repo alone — it also covers how *consuming* solution repos
> are structured (`.configtransform/` trees, `.gitattributes`, CI workflows that live in
> *those* repos, not this one). It's mirrored here anyway, alongside
> `CONFIGTRANSFORM_TOOL_DESIGN.md`, so that anyone (or any AI agent) reading this tool's code
> has the full "why" available in one place without needing to hop repos — see
> `DOCUMENTATION_POLICY.md`. If a solution repo is created and needs its own copy for local
> context, treat this repo's copy as canonical and keep the other in sync with it, not the
> other way round.

## 1. Problem statement

We have .NET solutions serving multiple clients, each deployed to multiple environments
(e.g. Staging, Production). Some projects are .NET Framework (App.config), some are
.NET 6/8 (appsettings.json), and a single csproj may have more than one config file
(App.config, NLog.config, ConnectionStrings.config, etc.). Config files may live anywhere
in the repo — not necessarily under a `src/` convention.

Requirements:

1. All configuration lives **in the application repository** — no external config service.
2. Changes are **documented automatically** via git commit — the commit history is the audit
   log, no separate change-tracking process.
3. Changes are **rollback-able** via standard git operations (`git revert`).
4. Secrets are **encrypted at rest** in the repo.
5. CI/CD **automatically** resolves and deploys the correct configuration for a given
   (client, environment) pair — no manual file copying.
6. Developers retain **full autonomy**: editing and shipping a config change is a normal
   PR/CI flow, not gated by a separate ops process.
7. The team **cannot currently afford** to separate secrets from non-secrets inside existing
   App.config files (they are already committed, mixed together, in production use). Any
   solution must work without that refactor.

### 1.1 Out of scope

This system resolves *configuration value* differences per client — a connection string, a
URL, a feature-flag value. It does **not** solve a client needing a different logo/branding
asset, a different compiled plugin/DLL, or literally different application code/business
logic. If a client-specific need turns out to be one of those rather than a config value, it
needs a different mechanism — not an assumption that this design already covers it.

## 2. Decision log (what we chose, and why)

| Decision | Chosen | Rejected alternative(s) | Why |
|---|---|---|---|
| Config variant management | Base file + layered transform/overlay files (XDT for XML, `Microsoft.Extensions.Configuration`-based merge for JSON) | Full flat config file per (client, environment) | Avoids duplicating shared settings across every client; a shared value change is a one-line edit, not an N-file edit. (Flat remains legitimate if a given project's settings are mostly client-specific with little sharing — decide per project, not globally.) |
| XML transform engine | `Microsoft.Web.Xdt` called directly | SlowCheetah | SlowCheetah is unmaintained tooling glue around the same engine; `Microsoft.Web.Xdt` itself is the actively maintained piece (.NET Foundation). |
| Encryption mechanism | git-crypt, whole-file encryption | SOPS + age/KMS | SOPS only understands JSON/YAML/etc., not XML, and requires secrets to be split out of App.config first — a refactor the team cannot do right now. git-crypt encrypts whole files regardless of format, requiring zero restructuring of existing mixed App.config files. |
| git-crypt key model | Single default symmetric key | Per-user GPG keys | GPG gives an auditable grant history but **not** free revocation — revoking access still requires generating a new content key and re-encrypting everything, the same cost as symmetric-key rotation. Given the team's time constraints, the operational overhead of GPG (per-dev keypairs, key exchange/trust, a CI GPG identity) isn't worth it for a benefit (audit trail of grants) that's thin relative to its cost. |
| Per-client key segmentation | Not implemented now; kept as an explicit future escape hatch | Per-client keys/filters from day one | Adds bookkeeping with no current benefit while everything shares one key. Revisit only if a specific client has an actual isolation requirement. |
| `.gitattributes` scope | One glob: `.configtransform/** filter=git-crypt diff=git-crypt` | Per-client filter names | Per-client filter names only matter once a client is actually split onto its own key (a distinct key collection). Until then it's pure ceremony. |
| Config file location assumption | None — each project's location is declared explicitly via a manifest's `directory` field, pointing at wherever the config file's own directory actually is (not specifically a `.csproj`'s directory — see §4) | Assuming a `src/<Project>/` convention | Source layout is not guaranteed to be consistent (flat at root, arbitrarily nested). The manifest decouples the `.configtransform/` tree from wherever code actually lives — and, as a consequence of that decoupling, from any particular language ecosystem too. |
| Client/environment directory naming | Nested: `Clients/<Client>/<Environment>.config` | Flat: `Clients/<Client>-<Environment>.config` | Nested scales better for browsing once client count grows past a handful, avoids any hyphen-in-name ambiguity for humans reading the tree, and keeps the door open for future per-client git-crypt key scoping via a directory glob. |
| Environment-wide layer | Included: `Environments/<Environment>.config`, applied before the client layer | Skipping straight to `Clients/<Client>/<Environment>.config` | Exists specifically to avoid duplicating settings that are identical across all clients within one environment (e.g. `debug=false` in Production). If a given project turns out to have nothing genuinely shared across clients, this layer can be omitted for that project — decide per project based on actual content, not globally. |
| History of already-committed secrets | Rotate by default; history purge (`git filter-repo`) is optional cleanup, not a substitute | — | Rotation is the only thing that actually closes exposure to anyone who already had repo access. Purging history only stops *future* clones from getting the plaintext; it does not undo exposure to existing clones/forks/CI logs/GitHub's own caches. For credentials in broad use where rotation is genuinely infeasible, purging without rotating is an accepted, informed risk-acceptance — not a claim that the secret is now safe. |
| Transform tool location | Dedicated, separate repository (not copied into each solution repo) | Copy tool source into each solution repo | Multiple solution repos consume the same tool; a shared dedicated repo avoids manually propagating fixes/features into every consumer, at the cost of a real publish/versioning step (accepted, since the team is now past the "can't afford any setup cost" stage for this specific piece). |
| Tool packaging format | `dotnet tool` (NuGet package containing a CLI executable), published to GitHub Packages | Plain library NuGet package; self-contained/single-file executable | `dotnet tool` gives versioned install/update via familiar NuGet tooling. Self-contained/single-file was considered as a zero-.NET-SDK-dependency alternative but rejected for now since developer machines almost certainly already have the SDK (bundled with Visual Studio / needed for the .NET 6/8 projects); revisit only if that assumption turns out false for some machines. |
| Tool install mode | Local tool via a per-repo tool manifest (`.config/dotnet-tools.json`), restored with `dotnet tool restore` | Global tool (`dotnet tool install --global`) | Global installs have no per-repo version pinning — different solution repos could silently drift onto different tool versions depending on whose machine last updated it, with no record anywhere of which version a given repo was actually built against. A committed manifest makes the tool version itself part of each repo's version-controlled, auditable state, consistent with the "everything documented by commit" requirement (§1). |
| Config root folder name | `.configtransform/` (dot-prefixed, tool-branded), name itself not hardcoded — discoverable via an optional repo-root marker file | Hardcoded `Configs/` | `Configs` is a generic name a mature codebase may already have claimed for something unrelated. A dot-prefixed, tool-branded default lowers collision risk the same way `.github/`/`.vscode/` do; making the name itself overridable per repo (rather than hardcoded in the tool) applies the same "don't bake in assumptions" principle already used for project location (§3/§4) and file casing (§5.4) — verify per repo before rollout rather than assuming the default is free everywhere. |
| Per-file overlay subfolder naming | Derived automatically from the base file's own name with extension (e.g. `App.config/`, `NLog.config/`) | A manually-maintained `name` field in the manifest (e.g. `App`, `NLog`) | Matching the real filename makes the tree self-explanatory — `App.config/` immediately reads as "overlays for App.config" without checking the manifest. Deriving it automatically also removes a field that could silently drift out of sync with the real filename. `name` is kept as an *optional* override only for the rare case of two same-named base files in different subdirectories of one project, where auto-derivation would collide. |
| PR review of encrypted config changes | Local-only: reviewers with the key pull the branch and diff locally before approving; GitHub's PR UI shows only a blob-level diff, by design | Have CI post a decrypted `--diff` as a PR comment/check output | Rejected after review: GitHub repo *read* access is routinely broader than the git-crypt key-holder set, so a PR comment would expose secret values to people who were never given the key — and a PR comment is unencrypted, plaintext, emailed to watchers, and persists in history, which is strictly less protected than the file was before. Only safe if a repo's read-access list is provably identical to its key-holder list, which must not be assumed by default. |
| CI trigger for deployment | `workflow_dispatch` only, with `client` and `environment` as required manual inputs. Push/PR triggers a separate, fully automatic build+test workflow that never selects a client/environment — it builds the base config files as committed, untouched. | Automatic path-filtered deploy on push/merge (detect which client/env paths changed and auto-deploy those) | Keeps "does this build" and "deploy this to a specific target" as separate concerns with different trust requirements. The automatic push/PR workflow never needs the git-crypt key or the transform tool at all, since it never touches a client-specific or secret value — a security bonus (stays safe on PRs from forks, where Actions often withholds secrets by default anyway) as well as a simplicity win over auto-detecting deploy targets from changed paths. |

## 3. Repository layout

```
repo-root/
├── ProjectA.Framework/                       # .NET Framework project, may be anywhere in the tree
│   ├── ProjectA.Framework.csproj
│   ├── App.config                            # base (or app.config — casing not assumed, see §5.4)
│   └── NLog.config                           # a second config file in the same project
│
├── services/billing/ProjectB.Core/           # .NET 6/8 project, nested arbitrarily deep
│   ├── ProjectB.Core.csproj
│   └── appsettings.json
│
├── .configtransform/
│   ├── Environments/
│   │   └── Production/
│   │       ├── configtransform.json
│   │       ├── patch-ProjectA.Framework-App.config.xml
│   │       └── patch-ProjectB.Core-appsettings.json.json
│   │
│   └── Clients/
│       ├── ClientA/
│       │   └── Production/
│       │       ├── configtransform.json          # extends .../Environments/Production/configtransform.json
│       │       └── patch-ProjectA.Framework-App.config.xml
│       └── ClientB/
│           └── Production/
│               └── configtransform.json          # extends-only, no resources of its own — see §4
│
├── .config/
│   └── dotnet-tools.json                      # pins the ConfigTransform tool version for this repo — see §11
├── nuget.config                                # points at the GitHub Packages feed hosting the tool — see §11
├── .gitattributes
└── docs/
    └── CONFIG_MANAGEMENT.md                   # this document, once committed
```

`.configtransform/` holds one `configtransform.json` per **layer directory** —
`Environments/<Env>/` and `Clients/<Client>/<Env>/` — not one per project. A layer's `resources[]`
entries are what map it to real project files, each by its own repo-root-relative path
(`ProjectA.Framework/App.config`, `services/billing/ProjectB.Core/appsettings.json`) — there's no
project-to-directory indirection to maintain separately the way `manifest.json`'s `directory`
field once was; see §4 and `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md` for the full design and why
it replaced that indirection.

Note: the transform tool itself (`ConfigTransform.Xml` / `ConfigTransform.Json`) does **not**
live in this repo. It lives in its own dedicated repository and is consumed as a versioned
`dotnet tool` — see §11.

## 4. Layer schema (`configtransform.json`)

```json
{
  "extends": ".configtransform/Environments/Production/configtransform.json",
  "resources": [
    { "path": "services/billing/ProjectB.Core/appsettings.json", "patch": ".configtransform/Clients/ClientA/Production/patch-ProjectB.Core-appsettings.json.json" }
  ]
}
```

- `extends` (optional): the layer this one starts from — another `configtransform.json`'s own
  fully-resolved output, not the raw base files. An Environment layer has none; a Client layer
  typically extends the matching Environment layer.
- `resources[].path`: the project's real config file, **repo-root-relative** — the same identity
  everywhere this design addresses a resource (`--resource`, every `resources[]` entry across the
  tree). **Not** a `.csproj` reference — the tool never opens or validates anything at this path,
  only resolves it against the repo root. This is why the tool has no `TargetFramework` coupling
  (§11, confirmed via the pilot's net35 project), and why it works identically for a
  Node.js/Angular/React app's JSON config — the only real constraint is the config file format
  (XML or JSON today), not the language or ecosystem of the project it belongs to.
- `resources[].patch` (optional): the overlay file for this resource at this layer — omitted
  entirely for a resource this layer doesn't touch, which just passes through untouched. Also
  repo-root-relative, uniformly with `extends`/`path` — no same-directory shorthand, a deliberate
  consistency choice; see `docs/MANIFEST_SCHEMA.md`'s "Path convention" for the full reasoning.

Full field reference, worked examples, and the design history behind this shape:
`docs/MANIFEST_SCHEMA.md` and `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md`.

Onboarding a new project is: run `set` once against its base file (`--resource <path>
--client <C> --environment <E> --match ... --set ...`) — it creates the layer's
`configtransform.json` (with the right `extends`) and authors the patch file in one step, no
separate manifest to hand-write first. Onboarding a new client for an existing project is the
same `set` invocation with the new client's name; a client that needs to inherit an Environment
layer's content with no overrides of its own still needs an (empty-`resources`, extends-only)
`configtransform.json` created for it — the "accepted cost" `docs/SELF_DESCRIBING_OVERLAYS_
DESIGN.md` names explicitly, see `MANIFEST_SCHEMA.md`. Neither requires touching CI logic or the
source tree.

## 5. Config resolution

### 5.1 Merge order (per resource)

```
base file → each layer's own patch, in `extends` order (outermost/base-most first) → final file
```

An Environment layer's own patch (if it lists the resource) applies first, then a Client layer
extending it applies second, and so on for however deep an `extends` chain actually goes — this
is a declared reference in each layer's own `configtransform.json`, not a fixed two-slot rule
baked into the tool (contrast with Kustomize, whose overlays are self-describing the same way —
see §9). A layer simply not listing the resource, or listing it with no `patch`, is not an error;
a `patch` that *is* declared but whose file doesn't exist on disk is a different case — a broken
reference — and always is one. A missing **base** file must fail loudly regardless, since that
indicates something is actually broken, not an intentional absence.

The tool does not attempt to detect or guess typos (e.g. a `configtransform.json` under a
misspelled `Environments/Prodution/` folder) — it isn't in a position to know intent, and
shouldn't try. What it does instead: every single-resource run (`--dry-run`, `--diff`, and real
runs alike) explicitly reports, for each layer in the resolved chain, whether the resource was
listed and patched (e.g. `.configtransform/Environments/Production/configtransform.json:
'ProjectA.Framework/App.config' patched, applying (...)`) or not (`not listed, skipping` /
`listed with no patch, skipping`). This keeps a typo visible to a human reading the output —
because the layer they expected to patch the resource is reported as not doing so — without the
tool trying to be clever about whether an absence was intentional.

### 5.2 XML (.NET Framework)

Uses `Microsoft.Web.Xdt`'s `XmlTransformation`/`XmlTransformableDocument`, applied twice in
sequence (env transform, then client transform) against the same in-memory document, then
saved. Transform files use standard `xdt:Transform`/`xdt:Locator` attributes.

This engine is format-generic across any XML config file, not App.config-specific — it applies
identically to `Web.config` (ASP.NET), `NLog.config`, `ConnectionStrings.config`, or any other
XML file a project has, each as its own manifest entry (§4). One caveat specific to
`Web.config` on actual ASP.NET Web Application projects: MSBuild has its own *native* Web.config
transform mechanism (`Web.Debug.config`/`Web.Release.config`, triggered by `$(Configuration)`
during publish). Where that native mechanism is already in use, this tool's step in
`build-transformed.yml` must run **after** it, overwriting its output — otherwise the two could
either conflict or the native step could clobber our client-specific result, since they're keyed
by different things (MSBuild Configuration vs. our Client+Environment). Worth checking for
during the real inventory pass (§11).

### 5.3 JSON (.NET 6/8)

Uses `Microsoft.Extensions.Configuration`'s own `ConfigurationBuilder` as the merge engine at
**build time** (`AddJsonFile` for base, env override, client override, in order), then
flattens the resulting `IConfigurationRoot` back out to a single `appsettings.json` written
into the publish output. This is Option B from our discussion (build-time resolution) chosen
over Option A (runtime layering via `AddJsonFile` at app startup, selecting the client via an
environment variable) — Option A is more "cloud-native idiomatic" (build once, deploy many)
but would mean every client's secrets potentially ship inside every artifact/image
(since the client isn't known until runtime), widening blast radius and requiring runtime
decryption. Option B keeps one consistent build-time resolution model across both project
types and keeps each deployed artifact scoped to exactly the client it's for.

### 5.4 Case-insensitive file resolution

Some existing files are `App.config`, others `app.config`. Since CI runners are typically
Linux (case-sensitive) while local dev is typically Windows (case-insensitive), a hardcoded
exact-case lookup can work on every developer's machine and still fail in CI. The tool
resolves file names case-insensitively:

```csharp
static string ResolveCaseInsensitive(string directory, string fileName)
{
    var matches = Directory.EnumerateFiles(directory)
        .Where(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (matches.Count == 0)
        throw new FileNotFoundException($"No file matching '{fileName}' (case-insensitive) found in '{directory}'");
    if (matches.Count > 1)
        throw new Exception($"Ambiguous: multiple files matching '{fileName}' case-insensitively in '{directory}': {string.Join(", ", matches)}");

    return matches[0];
}
```

This removes the hazard at the source rather than requiring manifests to record exact
casing or adding a separate CI lint step to catch drift.

### 5.5 Future format extensibility (not implemented — documented intentionally)

XML and JSON cover every known current need (App.config, Web.config, NLog.config,
ConnectionStrings.config, appsettings.json). This design is deliberately future-proofed for
two formats that may come up later, without requiring a redesign when that happens:

- **YAML**: structurally the same as JSON — hierarchical, keyed. Would use
  `Microsoft.Extensions.Configuration` with a YAML file provider in place of `AddJsonFile`,
  reusing the exact same build-time flatten-and-merge approach as `ConfigTransform.Json`
  (§5.3). Shows up in the manifest as `"type": "yaml"`.
- **`.env`**: flat `KEY=VALUE` pairs, no nesting — actually *simpler* to merge than JSON, just
  a dictionary union where later layers override matching keys. Would show up as
  `"type": "env"`.

Neither needs a change to the manifest schema's shape, the `.configtransform/` directory
layout, the encryption approach, or the CI trigger design — only a new `type` value and its
corresponding (small) merge implementation, built when the need is actually confirmed by real
project content. Not being built now; recorded here so the "no redesign needed later" analysis
isn't lost.

## 6. Transform tool CLI

Both `ConfigTransform.Xml` and `ConfigTransform.Json` share the same CLI shape:

```
--resource <repo-root-relative path>   optional — omit for every resource the layer touches
--client <ClientName>                  required
--environment <EnvironmentName>        required
--output <path>                        real runs only — where the merged result is written (CI passes the publish dir path; a directory when --resource is omitted)
--dry-run                              print the fully merged result to stdout; nothing is written to disk
--diff                                 print a unified diff (unpatched vs. fully merged) using `git diff --no-index`; nothing is written to disk except throwaway temp files, cleaned up immediately
```

`--dry-run` and `--diff` never write to the base file's own location — real runs only ever
write to an explicitly passed `--output` path, which CI always points at the build/publish
output directory, never at the source tree. Full flag reference, including `--list` and `set`:
`docs/USAGE.md`.

Example:

```bash
# Preview what ClientA actually gets in Production
dotnet run --project tools/ConfigTransform.Xml -- \
  --resource ProjectA.Framework/App.config \
  --client ClientA --environment Production --dry-run

# See exactly what ClientA's overrides change vs. the unpatched chain
dotnet run --project tools/ConfigTransform.Xml -- \
  --resource ProjectA.Framework/App.config \
  --client ClientA --environment Production --diff

# Real run, as CI invokes it
dotnet run --project tools/ConfigTransform.Xml -- \
  --resource ProjectA.Framework/App.config \
  --client ClientA --environment Production \
  --output publish/App.config
```

## 7. Encryption at rest (git-crypt)

### 7.1 Setup

```bash
git-crypt init
echo ".configtransform/** filter=git-crypt diff=git-crypt" >> .gitattributes
git add .gitattributes
git commit -m "Add git-crypt attributes for .configtransform/"
```

Key distribution: `git-crypt export-key ./git-crypt-key`, shared out-of-band (never via git)
with authorized developers and pasted (base64) into a CI secret. Developers run
`git-crypt unlock ./git-crypt-key` after cloning. See `SECRETS_AND_LOCAL_SETUP.md` §2 (in
`config-transform`) for the concrete, platform-by-platform commands (Windows included) this
summary skips over.

This `.gitattributes` glob is unconditional and has nothing to do with any manifest's `directory`
field — every project's `.configtransform/<Name>/` overlay tree is covered the same way,
including a manifest whose `directory` points at the repo root itself (`"."`) rather than a
subfolder. See `MANIFEST_SCHEMA.md`'s "Pointing `directory` at the repo root itself" for that
case specifically.

> **Disclaimer for whoever runs this the first time:** losing this key, with no backup, means
> everything under `.configtransform/**` becomes **permanently unrecoverable** — this is not a
> bug, it's what encryption without a backdoor means. This is intentional and accepted as part
> of the design (§2), but it means the key must be stored somewhere durable with more than one
> person able to retrieve it (e.g. a team password manager/vault entry) — not solely on the
> laptop of whoever happened to run `git-crypt init`. Do this before distributing the key to
> anyone else, not after.

### 7.2 Migrating already-committed config files

```bash
git add --renormalize .
git commit -m "Encrypt .configtransform/ with git-crypt"
git push
```

This encrypts everything under `.configtransform/**` from this commit forward. It does **not** remove
plaintext from prior commits — that is handled separately (§7.4).

### 7.3 Diffing encrypted files, and the PR review workflow

git-crypt configures a local `textconv` diff driver (`git-crypt init`/`unlock` writes this
into local `.git/config`; the `.gitattributes` mapping is what's shared via git, the actual
driver command is local by design — git will not let a committed `.gitattributes` specify an
arbitrary command to execute). Consequences:

- Anyone with the key and an unlocked clone gets normal, readable `git diff`/`git log -p`
  locally.
- **GitHub's PR web UI shows only a blob-level diff for these files** ("binary file changed" /
  no line-level content) — GitHub's servers don't have the key and can't run a local
  `textconv` command. This is not a defect to work around; it is the correct and intended
  behavior. Anyone without the key seeing "content changed, values not shown" is git-crypt
  doing exactly its job.
- This is a different mechanism from the transform tool's `--diff` flag (§6): git-crypt's
  diff driver shows changes between two git states of a file over time; `--diff` shows the
  semantic difference between a project's base config and what a specific client actually
  ends up with, at the current state, independent of history.

**Review policy: local-only, by design.** Reviewers who hold the git-crypt key review changes
under `.configtransform/**` by pulling the branch, unlocking, and running `git diff`/`--diff`
themselves, then approving on GitHub without GitHub itself ever rendering the content. GitHub's
inline line-comment UI is not available for these specific files as a result — an accepted UX
cost, not a bug to fix.

**Explicitly rejected: having CI post a decrypted `--diff` as a PR comment or check output.**
This was considered and rejected — it would defeat the encryption entirely. GitHub repo *read*
access is routinely broader than the git-crypt key-holder set (people who can browse the repo
or read PRs without ever having been handed the key), so posting decrypted secret values into a
PR comment exposes them to that wider audience. Worse, a PR comment is not encrypted at all —
it sits in GitHub in plaintext, gets emailed to PR watchers, and persists in comment history —
which is strictly less protected than the file was before, not a review-convenience trade worth
making. The only scenario where this would be safe is if a repo's read-access list is already
identical to its key-holder list, which is a fact about that specific repo's permissions that
would need explicit verification, not something to assume as a default behavior.

### 7.4 Handling secrets already exposed in history

Two separate obligations, not one:

1. **Rotate every credential that was ever committed in plaintext**, regardless of anything
   else — this is the only action that actually closes exposure, since anyone who already
   cloned the repo has the plaintext regardless of what happens to the repo afterward.
2. **Optionally purge history** (`git filter-repo`, not BFG) if you also want it removed from
   the repo itself. This rewrites every downstream commit SHA, requires a coordinated
   force-push, and means every collaborator must re-clone. Purging without rotating is a
   legitimate, deliberate choice **only** when rotation is genuinely infeasible (e.g. a
   credential in broad, hard-to-coordinate use) — but it must be understood as forward-looking
   hygiene (stops *future* clones from getting the plaintext), not remediation of past
   exposure (existing clones/forks/CI logs/GitHub's own caches may still hold it).

```bash
git clone --mirror <repo-url> repo-mirror.git
cd repo-mirror.git
git filter-repo --path .configtransform/ProjectA.Framework/App.config/Clients/ClientX/Production.config --invert-paths
# or, to redact a specific known secret value wherever it appears:
# echo 'OLD_SECRET_VALUE==>REDACTED' > replacements.txt
# git filter-repo --replace-text replacements.txt
git push --force --tags origin 'refs/heads/*'
```

Mitigations worth doing alongside a decision not to rotate: check GitHub secret-scanning
alerts for that credential, audit historical repo access, and tighten scope/IP restrictions
on the credential where the credential type supports it.

## 8. CI/CD pipeline

Two separate workflows, deliberately kept apart — "does this build" and "deploy this to a
specific client+environment" are different questions with different trust requirements.

### 8.1 `build.yml` — build/test validation, on push and PR

1. Trigger: every push, every PR — fully automatic, no client or environment selected.
2. `dotnet build`/`msbuild`, run tests — against the base config files exactly as committed
   (`App.config`/`appsettings.json`, no `Environments`/`Clients` layer applied). This is what
   a plain build already does with zero customization — the "most basic App.config available."
3. **No `git-crypt unlock`, no `dotnet tool restore`, no transform step.** This workflow never
   needs the key or the tool, since it never touches a client-specific or secret value. That
   also means it stays safe to run on PRs from forks or other less-trusted contexts, where
   GitHub Actions often withholds repo secrets by default anyway — this workflow simply never
   asks for them.

### 8.2 `build-transformed.yml` — build, resolve, and deploy for one target, on manual dispatch only

1. Trigger: `workflow_dispatch` **only** — never automatic on push or PR. `client` and
   `environment` are required workflow inputs; a human explicitly picks the deployment target
   for every run. This keeps deployment a deliberate action rather than an automatic side
   effect of merging, while still satisfying the "no manual file copying" requirement (§1,
   requirement 5) — the human picks *which* target, CI does all the actual resolution and
   deployment mechanics.
2. `git-crypt unlock` using the CI secret keyfile.
3. NuGet auth for the tool feed, `dotnet tool restore`.
4. `dotnet build`/`msbuild` builds normally against the base config files.
5. Run each `ConfigTransform.*` tool once, using the dispatch inputs as `--client`/`--environment`
   and `--output` pointed at the publish directory (a directory, since `--resource` is omitted) —
   resolves and writes every resource of that tool's format the target layer touches, overwriting
   the built base configs with the resolved ones.
6. **Validate** the merged result: well-formed XML/JSON, and a check that expected keys
   aren't empty — catches an XDT `Locator` that silently failed to match before it ships.
7. Package and deploy that artifact for that specific client + environment.

### 8.3 Deployment mechanism

Targets are Windows Services (via services.msc) and IIS sites — file-copy deployment, not
containers. This requires no change to the resolution/transform design, because .NET's normal
build/publish step already handles the file naming for us:

- **Windows Service**: the runtime expects `<ServiceExecutableName>.exe.config` next to the
  `.exe`. MSBuild/`dotnet publish` already produces this name automatically in the publish
  output directory.
- **IIS**: the one naming exception — ASP.NET requires the literal filename `Web.config`,
  regardless of the project's actual name. Not `{csprojectname}.config` for this case.

Either way, `--output` in `build-transformed.yml` (§8.2 step 5) just needs to point at wherever
the build already placed the correctly-named file in the publish directory — the transform
tool overwrites it there; no renaming logic belongs in our own tool. "Deploy" then means
copying that whole publish folder to the target's real directory on the server (the IIS site
path, or the Windows Service install path).

**Still open:** *how* the bytes physically get from CI onto the target Windows servers — a
self-hosted GitHub Actions runner with network/file-share access, PowerShell remoting/WinRM, or
a dedicated deployment tool (Octopus Deploy was raised early in this design process as
purpose-built for exactly this multi-tenant Windows Service/IIS scenario, with its own
Tenants feature). Not decided; needs its own follow-up.

## 9. Relationship to Kustomize (for context, not implementation)

The base+overlay principle is the same one used in the team's existing Kustomize-based
Kubernetes GitOps repo (`base/` + `overlays/<env>/` + `components/`). This section originally
argued the tool's old fixed base→Environments→Clients rule was *simpler* than Kustomize's
self-describing overlays precisely because there was no per-file selection/composition decision
to make. That argument was correct on its own terms, but the repo owner's position — after
reviewing a real Kustomize tree for contrast — was that self-describing overlays are still easier
to understand and add to, a different axis (authoring ergonomics) than the file-count/boilerplate
argument against them. `docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md` adopted that shape (§3/§4 above,
implemented) — see that document's "Origin and problem statement" for the fuller account of this
reversal, not repeated here.

What actually carried over from Kustomize, and what didn't:

- **Self-describing composition, adopted**: a `configtransform.json`'s own `extends` field is now
  the equivalent of `kustomization.yaml`'s `resources: [../../base]` — the inheritance chain is a
  file you can read, not a fixed rule you have to already know.
- **Path convention, deliberately *not* adopted wholesale**: Kustomize resolves paths relative to
  each `kustomization.yaml`'s own directory, which in a real production tree accumulates long,
  fragile `../../../../` chains — a real, frequently-complained-about pain point that tree
  exhibited directly. This design uses repo-root-relative paths everywhere instead (§4) — same
  self-describing benefit, without that failure mode.
- **Graph composition and `components/`, not adopted**: Kustomize composes a *graph* of many
  independent resources with optional, named, selectively-included components. This system still
  resolves one chain per resource — there's no selection/inclusion decision to make the way
  Kustomize's does, so there is no `components/` equivalent here, and none should be added unless
  a real case for optional, cross-cutting, selectively-included config fragments actually appears.

## 10. Tool distribution & versioning

### 10.1 Where the tool lives

`ConfigTransform.Xml` and `ConfigTransform.Json` live in their **own dedicated repository**,
separate from every solution repo that consumes them. Rationale in §2: with multiple solution
repos consuming the same tool, copying its source into each one means manually propagating
every fix or feature into every consumer; a single shared repo avoids that duplication at the
cost of a real publish step, which is an acceptable trade-off at this point.

### 10.2 How it's packaged and published

Packaged as a **`dotnet tool`** — a NuGet package containing a CLI executable, distinguished
from a normal library package (referenced via `<PackageReference>` in a `.csproj`) by using a
completely different consumption mechanism: a **tool manifest**, not `PackageReference`.

Published to a **GitHub Packages NuGet feed** on the tool's own repository/org. The tool
repo's own CI builds and publishes it (`dotnet pack` + `dotnet nuget push`) on a tagged
release — see `RELEASING.md` and `.github/workflows/publish.yml` (in `config-transform` itself)
for the actual, now-implemented workflow.

### 10.3 How consuming repos install it

**Local tool, via a per-repo manifest — not a global install.** Rationale in §2: a global
install (`dotnet tool install --global`) has no per-repo version pinning, so different
solution repos could silently end up on different tool versions with no record of which
version any given repo actually built against. A committed manifest makes the tool version
itself part of that repo's version-controlled, auditable state — consistent with the
"everything documented by commit" requirement (§1).

One-time setup per consuming repo:

```bash
dotnet new tool-manifest
dotnet tool install --local ConfigTransform.Xml --version 1.0.0
dotnet tool install --local ConfigTransform.Json --version 1.0.0
```

This creates `.config/dotnet-tools.json`, committed to git:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "configtransform.xml":  { "version": "1.0.0", "commands": ["configtransform-xml"] },
    "configtransform.json": { "version": "1.0.0", "commands": ["configtransform-json"] }
  }
}
```

Anyone who clones the repo — developer or CI — runs once:

```bash
dotnet tool restore
```

and thereafter invokes the pinned version as `dotnet configtransform-xml ...` /
`dotnet configtransform-json ...`. Upgrading a repo to a newer tool version is a one-line
change to that repo's own manifest in its own PR, with no effect on any other consuming repo.

### 10.4 Feed authentication

Since a GitHub Packages NuGet feed is private by default, each consuming repo needs a
`nuget.config` (committed to the repo) pointing at the feed, and both developers and CI need
a token with `read:packages` scope configured for auth — developers via their own PAT, CI via
a secret. See `SECRETS_AND_LOCAL_SETUP.md` §1 (in `config-transform`) for the exact mechanics —
including the one real wrinkle a repo *other than* `config-transform`'s own will hit: its
workflow's default `GITHUB_TOKEN` cannot read packages published from a different repository,
even one owned by the same account, so a PAT is required in practice, not just in theory.

### 10.5 Config root folder name and discovery

The root folder (default `.configtransform/`, replacing the earlier `Configs/` placeholder
used throughout this document — see the decision log, §2) is not hardcoded in the tool. An
optional repo-root marker file overrides it per repo:

```json
// .configtransform.settings.json (repo root)
{ "configsRoot": ".configtransform" }
```

The tool walks up from the current directory looking for this marker; if absent, it falls
back to the `.configtransform/` default. A `--configs-root <path>` CLI flag overrides both,
for one-off or testing use. This applies the same "don't bake in assumptions" principle
already used for project location (§3/§4) and file casing (§5.4) to the root folder name
itself — verify the chosen name is actually free in each real target repo before rollout
rather than assuming it.

### 10.6 SDK dependency

Running `dotnet tool restore`/`dotnet tool run` requires the **.NET SDK** on the machine
(the `dotnet tool` subcommand is part of the SDK, not a runtime-only install) — see §2 and
the SDK-dependency discussion. Assumed satisfied for all developers (Visual Studio bundles an
SDK; the .NET 6/8 projects already require one) but not yet explicitly confirmed across the
team.

### 10.7 Testing strategy

Once multiple solution repos depend on this tool, a regression in the merge logic is a
cross-repo blast-radius risk, not a local one — the tool repo carries its own test suite,
designed deliberately rather than added as an afterthought, covering both the .NET Framework
(App.config), IIS (Web.config), and .NET Core (appsettings.json) shapes explicitly, plus
arbitrary/generic files to prove the engine is genuinely format-generic rather than secretly
tied to specific filenames. Full repo structure and the detailed test matrix are their own
companion document: `CONFIGTRANSFORM_TOOL_DESIGN.md`.

### 10.8 Versioning policy

Full SemVer 2.0 (`MAJOR.MINOR.PATCH`, with pre-release identifiers such as `0.3.0-alpha` or
`0.3.0-beta` before reaching a stable `1.0.0`). A breaking change to the CLI's arguments or the
manifest schema requires a major version bump (or staying in `0.x` where any change may break,
per SemVer's own rule for pre-1.0 releases) — so a consuming repo can tell from the version
number alone in its `.config/dotnet-tools.json` (§10.3) whether an upgrade is expected to be a
drop-in change or needs review.

**`-alpha` tracks validation status, not code quality.** A release can be functionally solid —
well-tested, working correctly in `config-transform-pilot` — and still carry `-alpha`, because
that suffix signals the tool as a whole hasn't been validated against real (non-synthetic)
content yet, not that this particular release is shaky. §11's "no real solution repo piloted
yet" is the actual gate; it isn't re-evaluated release by release. Revisited explicitly for
`0.5.0-alpha`: confirmed as still the right call — "business-wise still alpha" even though the
tool itself works and is usable — precisely because that gate hasn't moved. Don't drop `-alpha`
on the strength of one release being good; only the real-content pilot (or an explicit,
deliberate owner decision to call it otherwise, documented here when made) changes that.

## 11. Open items — not yet decided or validated

- **git-crypt key rotation trigger** — *when* (e.g. a key-holder leaving) and *who* decides to
  actually re-key and re-encrypt is not defined. Deliberately deferred to a future discussion,
  not a blocker for implementation; noted here so it isn't forgotten.
- **Deployment transport mechanism** (§8.3) — how CI actually gets the built, resolved artifact
  onto the target Windows servers (self-hosted runner with network access, WinRM/PowerShell
  remoting, or a dedicated tool like Octopus Deploy) is not decided.
- **YAML/`.env` support** — see §5.5: confirmed to fit the existing design without a redesign,
  intentionally not built now.
- **No repository has been chosen for implementation yet** — the tool has its own dedicated
  repo (`config-transform`, implemented and released) and a *synthetic* solution-repo pilot
  (`config-transform-pilot`) now exists and validated the design end to end — see that repo's
  `FINDINGS.md`. The real, employer-owned multi-client repo this design targets is still
  unpiloted; that has to happen in a separate session inside that organization's own
  environment.
- **The GitHub Packages NuGet feed does not exist yet**, and the tool repo's own publish
  workflow (build → pack → push on release) has not been designed — only decided that it will
  work this way (§10.2). *Update:* both now exist and work — `config-transform`'s `publish.yml`
  has shipped two releases, and `config-transform-pilot` consumes them via the local-tool-manifest
  flow described below.
- **Feed authentication mechanics** (token type/scope, how it's supplied to developers vs. CI)
  named as a requirement (§10.4) but not yet worked out in detail. *Update, confirmed by the
  pilot:* a plain PAT with `read:packages`, supplied to CI as a repo secret and substituted into
  `nuget.config` via `%GITHUB_ACTOR%`/`%GITHUB_TOKEN%` env-var placeholders (never committed
  literally), is sufficient — with one wrinkle worth calling out explicitly: a workflow's own
  default `GITHUB_TOKEN` cannot read packages published under a *different* private repository
  owned by the same account, even the same owner's own repo, so a real PAT is required whenever
  the tool repo and the consuming repo are separate private repos (the common case here).
- **SDK-on-every-developer-machine assumption** (§10.6) not yet explicitly confirmed across
  the team.
- **`.configtransform/` has not actually been checked against any real target repo** for an
  existing name collision (§10.5) — only chosen as a lower-collision-risk default. No collision
  in the pilot repo, but that's a fresh repo with nothing else in it — a real target repo (with
  its own existing tooling) is still the real test. Verify per repo before rollout.
- **No real inventory has been done** of which projects have which config files, what
  format they're in, or how much content is actually shared-across-clients vs.
  client-specific per project. The `Environments/` layer, the XDT-vs-flat choice, and the
  overall shape of `.configtransform/` should be validated against real files before or during
  implementation, not assumed from this spec alone. *Partially addressed by the synthetic
  pilot*: layering order, per-project partial coverage, and the "zero client overlays at all"
  case are all now confirmed correct via real CI runs — but a *synthetic* inventory is not a
  *real* one; this item stays open until validated against actual solution-repo content.
- Exact CI platform assumed to be GitHub Actions (matches the repos discussed), not yet
  confirmed as final.
- The manifest-casing lint/validation approach was superseded by case-insensitive resolution
  in the tool (§5.4) — no separate CI lint step is required for that specific hazard, but
  the manifest-vs-git-tracked-file existence check is still worth having in some form.
- Per-client git-crypt key splitting remains a documented but unimplemented escape hatch —
  revisit only if/when a specific client has an actual isolation requirement.
- `launchSettings.json` and `dotnet user-secrets` are local-development-only surfaces, out of
  scope for this pipeline, but worth a one-time check across real projects for accidentally
  committed real secrets (see §1, requirement 7 discussion).
