# Secrets — design

**Status: stage 1 (value secrets) implemented, unreleased; stages 2–3 not started (2026-10-01).**
Decisions below were settled with the repo owner in conversation. Value secrets — placeholders,
`*.secret.env` files, the `secrets` field, `--reveal-secrets`, the strict loader — work as described.
File secrets (`replace`) and the docs/pilot migration don't exist yet. See "Implementation plan",
and `docs/ROADMAP.md` for where this sits in priority.

## Why this exists

`CONFIG_MANAGEMENT.md` §7 recommends git-crypt over the whole `.configtransform/**` tree. That
satisfies requirement 4 ("secrets are encrypted at rest"), but in practice it treats *all*
configuration as one big secret, when it only *may contain* secrets:

- Nobody without the key can read any overlay. GitHub's file view and PR diffs show only
  ciphertext, for a timeout value just as much as for a password.
- PR review has to happen locally. `CONFIG_MANAGEMENT.md`'s decision log had to explicitly reject
  posting a decrypted `--diff` on PRs, because it could leak secrets to people without the key.
- `--list`, `--diff` and `--dry-run` are useless without the key, even for non-secret settings.

The by-the-book fix is to separate secrets from configuration: commit configuration in plaintext
with placeholders, keep the secret values somewhere else (encrypted), and inject them only when
the final files are produced.

**This does not conflict with requirement 7** ("the team cannot afford to separate secrets from
non-secrets inside existing App.config files"), the reason git-crypt's whole-file approach was
chosen over SOPS. That requirement is about the *application*: its deployed config file must keep
working unchanged. Here the separation happens only in the *repo*. `configtransform -o` writes the
same file the app always got, with the real value back in place. No application change is needed.

## Two shapes of secret

1. **Value secrets** — a password, API key or token inside an otherwise ordinary config file.
   The config file holds a placeholder (`{{CFSECRET_NAME}}`); the value lives in an encrypted
   `*.secret.env` file that a layer lists under `secrets`.
2. **File secrets** — a file that is a secret in its entirety: a Firebase service-account JSON,
   an Android `google-services.json`, a `.pem`/`.p12` certificate. The real file lives encrypted
   under `.configtransform/`, named `*.secret.*`, and a resource entry `replace`s its target with
   it, byte for byte.

One `.gitattributes` rule covers both:

```
.configtransform/**/*.secret.* filter=git-crypt diff=git-crypt
```

Everything else under `.configtransform/` becomes plaintext. The same glob works in `.gitignore`
for a team that keeps secrets out of git entirely and provides them some other way (CI only).

## Value secrets

### Placeholder syntax

```
{{CFSECRET_NAME}}
```

A secret's name always starts with `CFSECRET_` ("configtransform secret"), the same way a React app's
public variables start with `REACT_APP_` or Vite's with `VITE_`: a developer sees the prefix and
knows what it is. Exactly: `{{` + `CFSECRET_` (uppercase) + one or more of `[A-Za-z0-9_]` + `}}`.

**The full name, prefix included, is the one spelling used everywhere** — in the placeholder, as the
key in a `*.secret.env` file, as the environment-variable override, and in CI wiring:

| Where | Looks like |
|---|---|
| Config file or patch | `Password={{CFSECRET_ADMIN_DB_PASSWORD}}` |
| `sql.secret.env` | `CFSECRET_ADMIN_DB_PASSWORD=Pa55+w&rd` |
| Environment-variable override | `CFSECRET_ADMIN_DB_PASSWORD` |
| GitHub Actions workflow | `env: { CFSECRET_ADMIN_DB_PASSWORD: ... }` |

So searching the repo for one name finds the placeholder, the secrets-file entry and the CI wiring,
and a secrets file is literally a set of environment variables (`source sql.secret.env` produces the
same overrides). The prefix also keeps placeholders from colliding with other template syntax —
bare `{{…}}` already appears in real config (Helm, Handlebars, some logging templates), and the tool
must never replace something it doesn't own.

A placeholder can be a whole value or part of one:

```xml
<add name="AdminDb"
     connectionString="Server=proddb;User=admin;Password={{CFSECRET_ADMIN_DB_PASSWORD}}"
     xdt:Transform="SetAttributes" xdt:Locator="Match(name)" />
```

Placeholders can appear in a base file or any patch, in any of the four formats. They are found in
the *merged* output, so it doesn't matter which layer wrote them.

### Secret files and the `secrets` field

```json
{
  "extends": ".configtransform/Environments/Production/configtransform.json",
  "secrets": [
    ".configtransform/Clients/Acme/Production/mongo.secret.env",
    ".configtransform/Clients/Acme/Production/sql.secret.env"
  ],
  "resources": [ ... ]
}
```

```
# .configtransform/Clients/Acme/Production/sql.secret.env  (encrypted at rest)
CFSECRET_ADMIN_DB_PASSWORD=Pa55+w&rd
```

- **A layer lists the secret files it contributes**, the same way it lists the patch it contributes
  per resource. Any number of files per layer, grouped however is convenient (per service, per
  system).
- **Paths are repo-root-relative**, like `extends`, `path` and `patch` — no exceptions
  (`SELF_DESCRIBING_OVERLAYS_DESIGN.md` Settled decision #4 already rejected bare, file-relative
  names for `patch`).
- **Every key in a `*.secret.env` file must start with `CFSECRET_`**, or the file is rejected. A
  key without the prefix could never match a placeholder; failing loudly catches the mistake
  instead of leaving a secret that silently never applies.
- **Every entry must end in `.secret.env`**, or the layer is rejected. This guarantees the file is
  covered by the `*.secret.*` git-crypt rule — a secrets file can never sit outside it by accident
  and be committed in plaintext.
- **Format: `.env`**, parsed by the existing `EnvFile` (same grammar as `.env` resources:
  whole-line `#` comments, optional quotes, no `${VAR}` expansion — `CONFIG_MANAGEMENT.md` §5.5).
  The simplest possible format, and it's expected to stay the only one.

### Resolution

For one resolved resource, with its layer chain (outermost first, as everywhere else):

1. Collect every `secrets` file from every layer in the chain. Later layers override earlier ones,
   name by name, like patches. The same name in two files of the *same* layer is an error — there's
   no order between them to decide which wins.
2. An environment variable with the same name overrides every file (see below).
3. Find every `{{CFSECRET_…}}` in the merged output and look its name up. A secret file that
   declares names no placeholder uses is fine (one layer's file may serve several resources).
4. Substitution is a single pass: a secret *value* containing `{{CFSECRET_…}}` is not expanded again.

### Substitution happens inside values, per format engine

The easy-to-get-wrong part. Pasting a value into the output *text* would break the file whenever
the value contains a character that format treats specially — `"` or `\` in JSON, `<` or `&` in
XML, `: ` or `#` in YAML, quotes or spaces in `.env`. The example password above, `Pa55+w&rd`,
would already produce invalid XML.

So each engine substitutes inside its own parsed tree, and its own writer escapes the result:

| Format | Where placeholders are substituted |
|---|---|
| XML | Attribute values and text/CDATA content |
| JSON | String values |
| YAML | Scalar values; a plain scalar is re-quoted if the value can't stay plain |
| `.env` | Values. `EnvFile`'s grammar has no escape sequences, so two kinds of value can't round-trip and are an error: one containing a line break, and one that starts and ends with the same quote character (parsing would strip them) |

A placeholder in a *key* (JSON property name, YAML key, `.env` key, XML element/attribute name) is
an error. In JSON, a whole-value placeholder stays a JSON string — a secret port number comes out as
`"5432"`, not `5432`. That's acceptable for secrets (almost always strings anyway); noted as an
open item.

### What each mode does

**Real run (`-o`)** always substitutes. Before writing anything, every placeholder in every
resource the call covers must resolve; any that is missing or locked is an error and **nothing is
written** (all-or-nothing, not per resource). After substitution, any remaining `{{CFSECRET_` text
anywhere in the output (a key, an XML comment) is also an error — a placeholder must never reach a
deployed file.

**Previews (`--dry-run`, `--diff`, `--diff-layers`) do not substitute by default.** The output keeps
`{{CFSECRET_ADMIN_DB_PASSWORD}}` as written — that's more useful than a `****` mask, because it says
*which* secret goes there. The resolution report printed above the output gains a secrets section:

```
secrets
  CFSECRET_ADMIN_DB_PASSWORD   resolved   .configtransform/Clients/Acme/Production/sql.secret.env
  CFSECRET_MONGO_PASSWORD      MISSING    no secrets file in this chain defines it
  CFSECRET_SMTP_PASSWORD       unknown    .configtransform/Environments/Production/mail.secret.env is locked (git-crypt: run git-crypt unlock)
```

- `resolved` names the file the value came from, or says `environment variable` when the override
  supplied it. The value itself is never printed.
- `MISSING` — every secrets file in the chain is readable and none defines this name.
- `unknown` — at least one secrets file in the chain is still git-crypt-encrypted, so the tool
  can't tell. Detected by git-crypt's own header (NUL + `GITCRYPT` + NUL), which
  `LayerManifestLoader` already recognizes for `configtransform.json`.

**`--reveal-secrets`** opts in to real values in previews: `--dry-run`, `--diff` and
`--diff-layers` show substituted output, and replaced files in full. For a key holder debugging
locally. It should never appear in a CI step that prints to a log. It changes nothing else: the
secrets report still never prints a value, and a real run is the same with or without it.

**In a revealed diff, every side is resolved with the full chain's secrets.** `--diff` compares the
base alone with the full result, and `--diff-layers` compares each layer's state with the next.
Each of those sides gets the same values — those the *whole* chain resolves to. So a diff only ever
shows real configuration changes, never a placeholder turning into its value (which would put the
secret on a `+` line for no reason). The catch: a later layer overriding an earlier layer's secret
*value* doesn't show as a diff line, since the placeholder text is the same on both sides. The
secrets report says which file each value came from.

**`set`'s automatic diff** after a write never substitutes. `set` has no `--reveal-secrets`.

**`--list`** shows each layer's `secrets` files (paths only) next to its patches, with or without
`--reveal-secrets`.

**Summary — when a value can appear:**

| | Without `--reveal-secrets` | With `--reveal-secrets` |
|---|---|---|
| `--dry-run` | placeholder text; replaced file as a one-line note | real values; replaced file in full |
| `--diff`, `--diff-layers` | placeholders on both sides; replaced file as a one-line note | real values, every side resolved with the full chain |
| `--list`, the secrets report, `set`'s auto-diff | never | never |
| real run (`-o`) | written to the output files, never printed | same |

### Environment-variable override

An environment variable named exactly like the secret (e.g. `CFSECRET_ADMIN_DB_PASSWORD`) overrides
any file value. Since every secret name carries the `CFSECRET_` prefix, arbitrary environment
variables are never picked up. This is how CI feeds GitHub Actions secrets
in — the tool itself never knows about GitHub. One invocation resolves one client/environment/host,
so the workflow maps the right secret for that target:

```yaml
- name: Resolve config
  env:
    CFSECRET_ADMIN_DB_PASSWORD: ${{ secrets[format('{0}_{1}_ADMIN_DB_PASSWORD', inputs.client, inputs.environment)] }}
  run: dotnet tool run configtransform -- --client "${{ inputs.client }}" --environment "${{ inputs.environment }}" -o publish/
```

Limits worth knowing on the GitHub side: secret names allow only letters, digits and `_` (a host
like `10.0.1.11` needs mangling), and a repository holds at most 100 secrets — clients ×
environments × hosts × secrets reaches that quickly. The `*.secret.env` files are the primary
store; environment variables are an override.

### The keyless check, and its limit

Secret *names* live inside the encrypted files, so without the key the tool can't know which names
a chain provides — an unresolved placeholder is reported `unknown`, not `MISSING`. With the key
(which CI has anyway, to unlock), the check is complete: every placeholder is `resolved` or
`MISSING`. The alternative — also listing names in plaintext in `configtransform.json` — would let
anyone run the full check, at the cost of a second list to keep in sync. Rejected in favor of the
simpler schema; see the decision log.

## File secrets (`replace`)

```json
{
  "extends": ".configtransform/Environments/Production/configtransform.json",
  "resources": [
    {
      "path": "code/src/firebase.json",
      "replace": ".configtransform/Clients/Acme/Production/firebase.secret.json"
    }
  ]
}
```

- **`path`** is the real file the app uses, committed with harmless content — empty, `{}`, or
  emulator/dev credentials if local builds need something there. It keeps its real name (no
  `.secret.`): the `*.secret.*` rule only covers `.configtransform/`, and a repo-wide rule would
  also encrypt or ignore the committed placeholder, which must exist like any base file.
- **`replace`** is the real file, encrypted at rest. `configtransform -o` writes its exact bytes to
  the output path. Must match `*.secret.*`, for the same reason `secrets` entries must.
- **No parsing, no merging.** Works for any format, including binary (`.p12`, `.jks`, `.pem`),
  whether or not a format engine handles the extension. The base's content is never read.
- **Why not `patch`:** a patch *merges*. If the dev base has keys the production file doesn't,
  they'd survive into a production credential file. Credentials must be exactly the secret file.
- **Layering:** a later layer's `replace` overrides an earlier one. A `replace` supersedes any
  patches from earlier layers (the report says so). A `patch` for the same resource in the same
  layer as a `replace`, or in a later layer, is an error — merging onto a replaced secret file is
  exactly what `replace` exists to prevent.
- **A declared `replace` file that doesn't exist** is an error, like a missing patch.
- **Previews:** never print a replaced file's content unless `--reveal-secrets` is given.
  `--dry-run` prints `(replaced by .configtransform/Clients/Acme/Production/firebase.secret.json,
  2.3 KB, not shown)`; `--diff` prints the same line instead of a diff.
- **A git-crypt-encrypted `replace` file** is an error on a real run ("locked: run
  `git-crypt unlock`") — deploying ciphertext would be worse than failing.

## Compatibility

- **Opt-in.** A repo that never uses `secrets` or `replace` behaves exactly as today, including one
  that keeps whole-tree git-crypt.
- **Older tool versions silently ignore the new fields.** `LayerManifestLoader` uses
  `System.Text.Json`'s default, which skips unknown properties. A repo that adopts `secrets` while
  any machine or CI job still runs an older pinned version gets placeholders, or the empty base
  file, deployed with no error. That can't be fixed retroactively, so: bump
  `.config/dotnet-tools.json` in the same change that first uses either field, and from this
  feature on, **the loader rejects unknown fields** (`JsonUnmappedMemberHandling.Disallow`), so any
  future schema addition fails loudly on a too-old tool instead of being skipped.
- **`set`** is unchanged in v1. It can write a placeholder as a value like any other text; it does
  not write `*.secret.env` files.

## Migrating a repo off whole-tree encryption

1. Upgrade the pinned tool to the version that ships this feature.
2. For each secret value in an overlay or base file: move it into a `*.secret.env` file in the
   layer that set it, list that file under the layer's `secrets`, and put
   `{{CFSECRET_NAME}}` where the value was. For each whole-file secret: rename it `*.secret.*`,
   add a `replace` entry, and commit a harmless file at the real path.
3. Verify with the key: every combination's `--dry-run` reports every secret `resolved`, and
   `-o` output is byte-identical to before the migration.
4. **Check CI for steps that print resolved output** — e.g. a `cat` of a file written by `-o`
   (`config-transform-pilot`'s "Show resolved config" step does exactly this). The tool never
   prints a value on its own, but it can't stop a later step from printing a file it wrote. Remove
   such steps, or mask the values (GitHub Actions' `::add-mask::`).
5. **Before narrowing encryption, make sure no secret value is left in any file that is about to
   become plaintext.** From the next commit on, those files are readable by anyone with repo read
   access.
6. With the repo unlocked, narrow `.gitattributes` from `.configtransform/** …` to
   `.configtransform/**/*.secret.* …`, then `git add --renormalize .configtransform` so files that
   were encrypted get committed in plaintext. Earlier history stays encrypted.

## Decision log

1. **Separate secrets from configuration, opt-in, alongside whole-tree encryption.** Whole-tree
   stays supported; it's just no longer the only option.
2. **Placeholder syntax `{{CFSECRET_NAME}}`, one name used everywhere** (repo owner's call, like
   `REACT_APP_`/`VITE_`). Rejected: `{{cfsecret:NAME}}` — this design's first version — because the
   same secret was then spelled three ways (`cfsecret:ADMIN_DB_PASSWORD` in the placeholder,
   `ADMIN_DB_PASSWORD` in the file, `CFSECRET_ADMIN_DB_PASSWORD` as the environment variable).
   Also rejected: bare `{{SECRET}}` and `${…}` — both already occur in real config files and
   template languages.
3. **Previews keep placeholders and report status; no masking.** Rejected: `****` masks — they hide
   which secret is where. Real values only behind an explicit `--reveal-secrets`.
4. **`secrets` lists files, and names come from inside them** (repo owner's call). Rejected:
   listing secret names in plaintext in `configtransform.json` — it would make the keyless check
   complete, but adds a second list to keep in sync with the files. CI has the key, so the check
   that guards deploys keeps full strength.
5. **Many `*.secret.env` files per layer, repo-root-relative paths.** Rejected: one fixed
   `.secret.env` per layer (too coarse for real grouping), and bare file names (breaks the
   "everything is repo-root-relative" rule).
6. **The `.secret.` naming rule is enforced, not just recommended.** It is the only thing that ties
   a secrets file to the git-crypt rule; a convention that can be broken by a typo would eventually
   commit a secret in plaintext.
7. **`.env` is the secrets format.** Rejected: JSON or YAML secrets files — no benefit for flat
   name/value pairs, and `.env` already has a parser in this repo with no dependencies.
8. **Substitute inside values, per engine.** Rejected: textual substitution on the output — it
   produces invalid files for any value containing that format's special characters.
9. **Environment variables, not a GitHub integration.** Rejected: the tool reading GitHub secrets
   itself — it would couple the tool to one CI system, while environment variables work with any
   CI and locally.
10. **Whole-file secrets via a new `replace` field.** Rejected: reusing `patch` (merging leaks base
    keys into credentials; can't handle binary), and a repo-wide `*.secret.*` rule that also covers
    the committed placeholder at the real path.
11. **All-or-nothing real runs.** A run covering several resources writes nothing if any secret is
    unresolved, so a deploy never ends up half-updated.
12. **The loader becomes strict about unknown fields.** Prevents a repeat of the silent-ignore
    hazard above for any future field.

Found while implementing stage 1, not in the original design:

13. **In YAML, an unquoted placeholder is an error, read as soon as the file is parsed.** YAML reads
    an unquoted value starting with `{` as a map, so `Password: {{CFSECRET_DB}}` parses as a map
    nested in a map's key — the placeholder text is gone before anything can see it, so neither the
    secrets report nor the leftover check would catch it, and the mangled map would deploy
    silently (verified empirically). `YamlLayerMerger` recognizes that shape in every YAML file it
    reads and says to quote the placeholder (`Password: "{{CFSECRET_DB}}"`). Placeholders later in
    a plain value (`Url: https://x/?k={{CFSECRET_K}}`) are fine unquoted.
14. **An empty environment variable counts as unset.** A GitHub Actions expression that looks up a
    secret that doesn't exist evaluates to `""`; using that would deploy an empty secret because of
    a typo in a workflow.
15. **No error message ever contains a secret value.** Two leaks found and fixed during
    implementation: `EnvFile`'s own parse error quotes the offending line, which would print part
    of a secret — both for a malformed `*.secret.env` file and for a multi-line value substituted
    into a `.env` resource. Both now name only the file or key.
16. **`EnvFile` (the `.env` grammar) moved from `ConfigTransform.Env` to Core**, so secrets files and
    `.env` resources are parsed by literally the same code. Core is the shared library every engine
    depends on; the reverse dependency isn't allowed.

## Open items

- **A keyless "check every combination" command** for CI — resolve every client/environment/host
  chain and report unresolved placeholders, without writing anything. `--dry-run` per combination
  covers it today; a dedicated mode may be worth adding once real usage shows the need.
- **JSON types for whole-value placeholders** — currently always a string. A typed form (e.g.
  `{{CFSECRET_PORT|number}}`) could come later if a real case needs it.
- **Escaping a literal `{{CFSECRET_`** in a config value that isn't a placeholder. No known real
  case; v1 has no escape syntax and treats it as a placeholder.
- **`replace` for non-secret whole files** (e.g. a client's logo). `CONFIG_MANAGEMENT.md` §1.1 puts
  branding assets out of scope; v1 requires `*.secret.*` names for `replace`.
- **External secret stores** (Azure Key Vault, AWS Secrets Manager) as another source, behind the
  same resolution step. Not planned; environment variables already let CI fetch from one.
- **`set` writing secrets** into `*.secret.env` files.

## Implementation plan

Each stage is its own PR, with fixture-backed tests across every applicable fixture category
(`CONFIGTRANSFORM_TOOL_DESIGN.md` §3), docs in the same change, and a CHANGELOG entry.

1. **Value secrets.** `secrets` field, `*.secret.env` loading and the `.secret.env` naming check,
   chain resolution and same-layer conflict error, the `CFSECRET_` key check, environment-variable
   override, git-crypt detection,
   per-engine substitution (XML, JSON, YAML, `.env`), the report section, `--reveal-secrets`, the
   all-or-nothing real run and leftover-placeholder check, and the strict loader.
2. **File secrets.** `replace` on resource entries, its layering rules and errors, byte-for-byte
   output for any extension, and preview handling.
3. **Docs and pilot.** Rewrite `CONFIG_MANAGEMENT.md` §7 and `SECRETS_AND_LOCAL_SETUP.md` around the
   narrowed `.gitattributes` rule, update `MANIFEST_SCHEMA.md`, `USAGE.md` and `GETTING_STARTED.md`.
   In `config-transform-pilot`: move one real-looking secret into a `*.secret.env`, add one
   whole-file secret via `replace`, narrow `.gitattributes`, and verify via a real
   `build-transformed.yml` dispatch that output is unchanged and the overlays are now readable on
   GitHub.
