# Azure Key Vault as a secrets source — design

**Status: implemented on `main`, not released.** The release waits until a design partner with a
real Azure Key Vault has run `docs/KEYVAULT_VERIFICATION.md` and confirmed every state below —
this repo's own sessions have no Azure access, so everything that talks to Azure is covered by
unit tests against a fake vault and by that guide, not yet by a real vault. Two things were
checked for real without one: a vault name that doesn't exist (`can't be reached`, through the
real Azure SDK) and every sign-in step failing on a machine without `az` (see "Signing in").

Extends `docs/SECRETS_DESIGN.md`: a layer's `secrets` can list Azure Key Vault sources next to (or
instead of) `*.secret.env` files, and a resource's `replace` can name a vault secret instead of a
file. Everything that design settled — `{{CFSECRET_NAME}}` placeholders, layer precedence, the
environment-variable override, the report's per-secret tree, all-or-nothing real runs, never
printing a value — applies unchanged. This document covers only what a vault adds.

## Why

git-crypt has one key per repo: whoever holds it can read every secret of every client in every
environment. Key Vault access is granted per vault (or per secret) through Azure RBAC, so "these
people may read repo RA's Production secrets for client CA, but not for client CB" becomes a role
assignment. Before this design, the way to use a vault was a script that loads its secrets into
`CFSECRET_*` environment variables. That works, but the report then shows every such secret only
at its last step (`patched in: $CFSECRET_…`) — which layer set it is lost — and the script, not the
tool, decides precedence between vaults.

## How a layer uses Key Vault

Five forms. The first four are `secrets` entries; the fifth is a resource's `replace`.

| Entry | What it supplies |
|---|---|
| `"keyvault://kv-ra-prod-ca"` | every secret in the vault named `CFSECRET-…`, one placeholder each |
| `"keyvault://kv-ra-prod-ca/CFSECRET-SMTP-PASSWORD"` | that one secret, for `{{CFSECRET_SMTP_PASSWORD}}` |
| `"keyvault://kv-ra-prod-ca/notifications-secrets"` | that one secret's text, read as a `.env` file: one placeholder per line |
| `{ "from": "keyvault://kv-ra-prod-ca/legacy-api-key", "as": "CFSECRET_LEGACY_API_KEY" }` | that one secret, for the placeholder `as` names |
| `"replace": "keyvault://kv-ra-prod-ca/firebase-service-account"` (on a resource) | that one secret's text, as the whole file |

```json
{
  "extends": ".configtransform/Environments/Production/configtransform.json",
  "secrets": [
    "keyvault://kv-ra-prod-ca",
    "keyvault://kv-ra-prod-ca-legacy/CFSECRET-REPORTING-TOKEN",
    "keyvault://kv-ra-prod-ca/notifications-secrets",
    { "from": "keyvault://kv-ra-prod-ca/legacy-api-key", "as": "CFSECRET_LEGACY_API_KEY" },
    ".configtransform/Clients/CA/Production/legacy.secret.env"
  ],
  "resources": [
    { "path": "services/notifications/NotificationWorker/firebase.json",
      "replace": "keyvault://kv-ra-prod-ca/firebase-service-account" }
  ]
}
```

- **A named secret's own name decides what it is: `CFSECRET-…` is one value, any other name is
  `.env` text.** A single secret used without `as` must carry the prefix anyway — it's how the
  tool knows which placeholder it fills — so a named secret without it has no other meaning. The
  tool knows the form from the layer file alone, before contacting Azure, and never guesses from
  the content. `as` is for a single value whose name can't change (an app already reads
  `legacy-api-key` from the vault at runtime).
- **Either mistake fails before anything deploys.** `.env` text given a `CFSECRET-…` name is read as
  one value, so the names inside it show as `MISSING` and a real run stops. A single value missing
  the prefix is read as `.env` text and fails as invalid `.env` syntax, or as a key without the
  `CFSECRET_` prefix — reported without quoting the line or the key, since in a misread value
  either could be part of a secret.
- **`keyvault://<vault-name>[/<secret-name>]`, never a URL.** The vault name follows Azure's rule
  (3–24 characters; letters, digits and `-`; starts with a letter; ends with a letter or digit; no
  `--`), the secret name Key Vault's (1–127 letters, digits and `-`); both are checked when the
  layer loads. The tool always builds `https://<vault-name>.vault.azure.net/` itself: it sends
  Azure access tokens there, and a layer file — which anyone opening a pull request can edit —
  must not be able to point a token at any other host.
- **Files and vault sources mix freely** in one layer's `secrets`. As with two files, the same
  name from two sources of the same layer is an error — including a whole vault and one of its own
  secrets listed alongside it.
- **The whole-vault form uses only `CFSECRET-…` secrets.** A `.env`-text secret in the same vault
  isn't picked up by it; it's read only when named. So nothing unrelated in a vault is read by
  accident.
- **An older tool version rejects every form**: a string entry that doesn't end in `.secret.env`,
  an object entry, and a `replace` outside `.configtransform/`. A layer using Key Vault never
  resolves silently without it.

## Names: placeholder and vault secret

For a single value without `as` — the whole-vault form and a named `CFSECRET-…` secret — the
vault secret's name is the placeholder's name with each `_` written as `-`:

| Placeholder | Vault secret |
|---|---|
| `{{CFSECRET_ADMIN_DB_CONNECTION}}` | `CFSECRET-ADMIN-DB-CONNECTION` |
| `{{CFSECRET_SMTP_PASSWORD}}` | `CFSECRET-SMTP-PASSWORD` (or `cfsecret-smtp-password`) |

- **`_` ↔ `-` is the only change, and it can't be avoided.** Key Vault names allow letters, digits
  and `-`, never `_`; placeholders, `*.secret.env` keys and environment variables allow `_` and
  never `-` (a shell can't `export A-B=…`). One secret's name has to work in all of those — that's
  what lets a CI environment variable override a vault value, or a vault override a file.
- **Case follows Key Vault**, which matches names case-insensitively, so the tool adds no case
  rule for vault names. Placeholders themselves are upper snake case, enforced
  (`docs/SECRETS_DESIGN.md` decision #28) — the sources disagree about case, and Key Vault ignoring
  it is one reason why.
- **The prefix is the opt-in.** Vaults often hold secrets for other applications too; without it,
  an unrelated `DB-PASSWORD` in a client's vault could silently override an Environment layer's
  `CFSECRET_DB_PASSWORD`. Same idea as every `*.secret.env` key having to start with `CFSECRET_`.
  It holds by construction: a placeholder's name starts with `CFSECRET_`, so the vault secret it
  looks up starts with `CFSECRET-`, and no other secret can ever match.
- **`.env` text and `as` use exact names** — no rename at all.
- **Disabled, expired or not-yet-valid secrets don't count.** They're shown in the tree, so "why
  isn't my secret used?" has an answer:
  `not patched in (keyvault://kv-ra-prod-ca/CFSECRET-SMTP-PASSWORD is disabled)`.

## Whole files (`replace`)

```
az keyvault secret set --vault-name kv-ra-prod-ca --name firebase-service-account --file firebase.json
```

- **Written exactly as stored**, as for a `replace` file — no merging, no parsing.
- **Certificates:** a secret Key Vault keeps for an imported certificate (content type
  `application/x-pkcs12`) comes back base64-encoded; the tool decodes it to the real bytes. Other
  binary files stay git-crypt `replace` files for now.
- **At most 25 KB**, Key Vault's limit per secret — enough for a Firebase service account, PEM
  certificates and `.env` files; anything larger stays a git-crypt `replace` file.
- **The `.secret.` naming and inside-`.configtransform/` rules apply only to files**: a vault
  secret never lands in the repo.
- **The chain shows `replaced by: keyvault://kv-ra-prod-ca/firebase-service-account`**, and
  previews keep the one-line "not shown" note, without reading the value.
- Often only one field of such a file is secret (a service account's `private_key`). Committing
  the file with a placeholder for that field (`"private_key": "{{CFSECRET_FIREBASE_PRIVATE_KEY}}"`)
  keeps the rest reviewable and patchable per layer; `replace` is for files that are opaque or
  handed over whole.

## When the tool contacts Azure

- **Only when something needs it.** A run whose resources contain no placeholder and replace none
  from a vault never contacts Azure, so resolving non-secret configuration never needs a sign-in.
- **Previews and `--list` read as little as each form allows:**
  - a whole vault: one listing — names only, no values (the Key Vault Reader role is enough);
  - a named single secret, or a `replace`: that secret's metadata only;
  - `.env` text: its value, because the names are inside it — so for this form, previews need
    value access (Key Vault Secrets User). The values are still never printed.
- **Values are read only for a real run and `--reveal-secrets`**, and only for names a resource
  actually uses, from the source that wins for each name.
- **Each source is read at most once per run**, all sources in parallel.

## What the report shows

The per-secret tree from `docs/SECRETS_DESIGN.md` gains vault steps, each naming the exact
secret — ready to paste into `az keyvault secret show`:

```
    secrets
      CFSECRET_ADMIN_DB_CONNECTION   resolved
        used in: .configtransform/Environments/Production/patch-Web-AdminPortal.Web-Web.config.xml
        .configtransform/Environments/Production/configtransform.json
          patched in: keyvault://kv-ra-prod-shared/CFSECRET-ADMIN-DB-CONNECTION
          ↓
        .configtransform/Clients/CA/Production/configtransform.json
          patched in: keyvault://kv-ra-prod-ca/CFSECRET-ADMIN-DB-CONNECTION
          ↓
        environment variable
          not patched in
```

A source that can't be read is an unreadable source, exactly like a git-crypt-locked file: the
name is `unknown` unless a later layer settles it, and a real run writes nothing.

| Situation | Step line |
|---|---|
| A single-value source defines the name | `patched in: keyvault://kv-ra-prod-ca/CFSECRET-ADMIN-DB-CONNECTION` |
| `.env` text defines the name | `patched in: keyvault://kv-ra-prod-ca/notifications-secrets` |
| Source read, name not in it | `not patched in` |
| Name there but disabled / expired / not yet valid | `not patched in (keyvault://kv-ra-prod-ca/CFSECRET-ADMIN-DB-CONNECTION is disabled)` (or `is expired`, `is not yet valid`) |
| No Azure sign-in available | `unknown: keyvault://kv-ra-prod-ca can't be read (not signed in to Azure -- run az login)` |
| No permission (403) | `unknown: keyvault://kv-ra-prod-ca can't be read (403 ForbiddenByRbac: no access)` |
| Vault firewall blocks the caller | `unknown: keyvault://kv-ra-prod-ca can't be read (403 ForbiddenByFirewall: blocked by the vault's network rules)` |
| Vault in a tenant the sign-in isn't for | `unknown: keyvault://kv-ra-prod-ca can't be read (it's in another tenant -- run az login --tenant <tenant>)` |
| Host doesn't resolve / no network | `unknown: keyvault://kv-ra-prod-ca can't be reached (no such vault, or no network)` |

- **A named secret that doesn't exist is an error**, in every mode — the vault answered, so it's a
  broken reference in the layer file, like a missing `*.secret.env` or `replace` file.
- **A vault that doesn't exist is `unknown`, not an error**: from the outside, "no such vault" and
  "no network" look the same, and only the first is a mistake in the layer file. A real run fails
  either way.
- **Reading a value can fail after its metadata was readable** — most often a Key Vault Reader
  running a real run. That stops the run before anything is written, naming the source and the
  status (`403 ForbiddenByRbac: can list keyvault://kv-ra-prod-ca but not read its values`).

## Never printing a value

The rule from `docs/SECRETS_DESIGN.md` holds, and three Azure-specific risks get explicit rules:

- **Azure SDK exception messages are never printed.** They can include whole response bodies. The
  tool prints only what it composes itself: the source, the secret's name, the HTTP status and Key
  Vault's error code (`ForbiddenByRbac`).
- **`.env`-text errors quote neither the line nor the key.** A file's keys are safe to name, but a
  single value misread as `.env` text (see above) would turn part of a secret into a "key".
- **Values are fetched only into memory** — never written to a cache, a temp file or an
  environment variable, and never logged.

## Signing in

The tool never signs in interactively itself. It reuses an existing sign-in, trying these in
order and using the first that works:

1. **The Azure CLI's sign-in (`az login`).** On a developer machine, `az login` does the browser
   SSO, MFA and conditional access, and keeps its tokens between runs in the OS's protected
   store. In GitHub Actions, `azure/login` leaves `az` signed in for the job. A self-hosted runner
   or build agent running in Azure signs in with its managed identity the same way:
   `az login --identity`, or `azure/login` with `auth-type: IDENTITY`.
2. **A service principal or workload identity from environment variables** (`AZURE_TENANT_ID`,
   `AZURE_CLIENT_ID`, and `AZURE_CLIENT_SECRET` or `AZURE_FEDERATED_TOKEN_FILE`), for CI systems
   that set those.

One token per run is shared by every vault (the `az` step starts a process each time it's asked),
and a failed sign-in is reported once, not once per vault.

Why not more:

- **No browser from the tool.** A tool that might open a browser can stall a CI job or a script
  waiting for a sign-in nobody will complete. With `az` doing the interactive part, the tool is
  non-interactive everywhere and fails fast with `run az login`. A browser fallback (only on a
  terminal, never in CI) can be added later without changing anything else here.
- **Not `DefaultAzureCredential`.** It tries about eight sources in turn (Visual Studio, VS Code,
  Azure PowerShell, …), so "which identity did this use?" becomes guesswork, and Microsoft
  recommends a specific chain for anything beyond local development. The three above are the
  ones a person can reason about.
- **No managed-identity step of its own.** It was the third step in the first draft. Measured on
  a machine outside Azure, it takes about 25 seconds to give up — the operating system's connect
  timeout to Azure's metadata address, which no SDK timeout or retry setting shortened — so every
  "not signed in" took 25 seconds instead of under one. `az login --identity` covers agents in
  Azure without the probe.
- **One tenant: the one you're signed in to.** A vault in another tenant is reported as such
  (see the table) rather than the tool quietly requesting a token for that tenant. Otherwise a
  layer file naming a vault in some other tenant would make the tool ask for tokens there.

## GitHub Actions (standard hosted runners)

No Azure secret is stored in GitHub. For each job, GitHub issues a short-lived signed token (an
OIDC token) describing the job — repository, environment, branch. Entra ID trusts that token for
one managed identity, if the token's subject matches what the identity was told to trust, and
returns an Azure access token for that identity, valid for about an hour. `azure/login` does the
exchange and leaves `az` signed in; `configtransform` then reuses that sign-in (step 1 above).

One-time setup per repo × environment × client (scriptable with `az` or Bicep/Terraform):

1. **A user-assigned managed identity**, e.g. `id-ra-prod-ca`, with **Key Vault Secrets User** on
   `kv-ra-prod-ca` and `kv-ra-prod-shared`.
2. **A federated credential on that identity**, trusting exactly one GitHub Environment:
   ```
   az identity federated-credential create \
     --name gh-production-ca --identity-name id-ra-prod-ca --resource-group rg-ra-production \
     --issuer https://token.actions.githubusercontent.com \
     --subject repo:<owner>/<repo>:environment:production-ca \
     --audiences api://AzureADTokenExchange
   ```
3. **A GitHub Environment `production-ca`** holding the variable `AZURE_CLIENT_ID` (the
   identity's client ID — not a secret). Restrict its deployment branches to `main`, and add
   required reviewers if production needs an approval.
4. **Repository variables** `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`.

```yaml
jobs:
  resolve:
    runs-on: ubuntu-latest
    environment: ${{ inputs.environment }}-${{ inputs.client }}   # picks the identity, and its protection rules
    permissions:
      id-token: write    # lets the job request GitHub's OIDC token
      contents: read
    steps:
      - uses: actions/checkout@v4
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - run: dotnet tool run configtransform -- -c "${{ inputs.client }}" -e "${{ inputs.environment }}" -o publish/
```

- **A job only gets the identity of the Environment it runs in**, so a workflow for client CB can't
  read client CA's vault, and the Environment's branch and reviewer rules gate every token.
- **Pull requests from forks get no OIDC token**, so they can't reach any vault.
- **`az` is preinstalled** on GitHub's Ubuntu and Windows images.
- **Standard runners have no fixed IP addresses**, so they can't reach a vault that only allows
  private endpoints or an IP allow-list. Keep the vault's public endpoint enabled with RBAC as the
  gate, or use self-hosted or private-networking runners.
- **An identity takes at most 20 federated credentials** — plenty for one Environment each.
- **If `azure/login` fails with "No subscriptions found"**, the identity only has roles on the
  vaults themselves; add `allow-no-subscriptions: true` to the `azure/login` step. Key Vault access
  needs no subscription.
- **Resolve soon after `azure/login`.** Its sign-in lasts about an hour; a resolve step right after
  it is well inside that.

## Recommended Azure layout

Key Vault role assignments can be scoped to a subscription, a resource group, a vault or a single
secret — nothing finer, like "every secret whose name starts with X". Per-secret assignments
don't scale as the main mechanism (a subscription allows about 4,000 role assignments) and
Microsoft recommends them only for limited cases. So **the vault is the access boundary**: one
vault per repo × environment × client, plus one per repo × environment for the Environment
layer's shared secrets.

```
subscription
└── rg-ra-production                  ← "every client of repo RA in Production" (rarely granted)
    ├── kv-ra-prod-shared             ← listed by .configtransform/Environments/Production
    ├── kv-ra-prod-ca                 ← listed by .configtransform/Clients/CA/Production
    └── kv-ra-prod-cb                 ← listed by .configtransform/Clients/CB/Production
```

| Entra ID group | Role | Scope |
|---|---|---|
| `ra-prod-ca-readers` | Key Vault Secrets User | `kv-ra-prod-ca` and `kv-ra-prod-shared` |
| `ra-prod-ca-writers` | Key Vault Secrets Officer | `kv-ra-prod-ca` |
| `ra-prod-ca-viewers` | Key Vault Reader (names only — previews, `--list`) | `kv-ra-prod-ca` and `kv-ra-prod-shared` |
| team lead | Key Vault Data Access Administrator (can grant only Key Vault data roles) | `rg-ra-production` |

- **Readers of a client also need the shared vault**, since the client's chain includes the
  Environment layer. If the Environment's secrets mustn't be readable by every client's people,
  don't share them: give each client vault its own copy.
- **Per-secret access for the odd exception.** A layer that names specific secrets
  (`keyvault://kv/CFSECRET-…`) needs access only to those, so a handful of secret-scope role
  assignments works where a whole-vault listing wouldn't.
- **Hosts rarely need their own vault** — the same people deploy all of a client's hosts. Give a
  host-specific value its own name in the client vault (`CFSECRET-ADMIN-DB-CONNECTION-H1`, used as
  `{{CFSECRET_ADMIN_DB_CONNECTION_H1}}` by that host's patch). Add a host vault only if different
  people must see different hosts.
- **Vault names are 3–24 characters and globally unique**, so repo, environment and client need
  short codes; keep the full names in tags.
- **Consider keeping production values away from developer machines entirely**: grant developers
  Test/Staging vaults, and Production only to CI identities behind GitHub Environment reviewers,
  with time-limited (PIM) access for emergencies.

## Architecture

- **Core gets a small secret-source seam and no Azure dependency.** The `keyvault://` grammar is
  part of the layer schema, so Core parses and validates it; reading a source goes through an
  interface Core defines (list a vault's names with their state, read one secret's metadata, read
  one value) and the CLI registers the implementation — the same way it registers the four format
  engines.
- **A new project, `ConfigTransform.Secrets.AzureKeyVault`**, implements it with
  `Azure.Security.KeyVault.Secrets` and `Azure.Identity`. It's the only project with Azure
  dependencies; they add a few MB to the tool package.
- **`secrets` entries become a string or an object** (`{ "from", "as" }`), read by a custom JSON
  converter; the strict loader still rejects any other shape or field. `replace` stays a string.
- **`SecretResolver` becomes lazy for vault sources.** Files are still read when the chain is
  built (so a missing or malformed file still fails early); vault sources are read on the first
  lookup. Same-layer duplicate names between sources are detected at that point.
- **Values move out of `SecretStatus`.** Today a lookup returns the value along with the state; a
  vault's value isn't known until it's read, so substitution asks for values separately — only
  ever for real runs and `--reveal-secrets`.
- **`ReplaceStep` reads from either a file or a vault secret**, behind the same seam.

## Testing

- **Core:** a fake vault source covers every form; precedence across files, vault sources and the
  environment variable; same-layer duplicates; the name rules and the name-decides-the-form rule;
  each unreadable reason; a named secret that doesn't exist; laziness (no placeholder → no vault
  contacted); values read only for real runs and `--reveal-secrets`; `replace` from a vault; the
  report lines above.
- **The Azure project:** mapping each Azure exception (status, error code, credential failures) to
  its report reason, including that no SDK exception message ever reaches output; a secret's state
  from its properties; the run-wide token cache. No test touches the network. (Certificate base64
  decoding is in Core's `ReplaceStep`, covered by the CLI tests.)
- **A real vault:** `docs/KEYVAULT_VERIFICATION.md`, run by a design partner from a fresh clone —
  build from source, create a throwaway vault with `az`, and compare each command's output with
  the expected tree for every form and every row of the table above, including a 403 (by removing
  their own role assignment), another tenant, and no sign-in. The tool never prints a value, so
  the output is safe to send back.

## Decision log

1. **Vault sources are more kinds of `secrets` entry, not a new field** — the same job (a layer
   contributing secret values), with the same precedence, duplicate rule and report. Likewise a
   vault `replace` is still `replace`.
2. **`keyvault://<vault>[/<secret>]`, never a URL**, so tokens only ever go to
   `*.vault.azure.net`. Rejected: accepting `https://…` URLs — a layer file is repo content anyone
   can propose changes to.
3. **One-value secrets without `as` carry a `CFSECRET-` prefix**, and the placeholder's name maps
   to the vault name by `_` → `-` alone; case is Key Vault's own (case-insensitive). Rejected:
   mapping every vault secret (an unrelated secret could silently override a lower layer's
   value); allowing `-` in placeholder names so vault names match letter for letter (such a
   secret could never come from a `*.secret.env` file or a shell environment variable); an
   explicit name map per vault in the layer file (a second list to keep in sync).
4. **Specific secrets can be named, one entry each** — for least privilege (only those secrets
   need access) and for picking a few secrets out of a shared vault. Several secrets are several
   entries rather than a list syntax.
5. **`as` gives a single secret an explicit placeholder name** — for vault secrets whose names
   other applications already depend on.
6. **A named secret's own name decides one value vs `.env` text** — `CFSECRET-…` is one value,
   anything else `.env` text. Proposed by the repo owner in review, replacing a `"format": "env"`
   field from an earlier draft: a single value without the prefix couldn't name a placeholder, so
   that reading never had another meaning, and both forms stay plain strings. Rejected: Key
   Vault's content-type metadata as the signal (easy to forget, and a forgotten one would
   silently change the meaning).
7. **`replace` can name a vault secret** — the Firebase-service-account case `config-transform-pilot`
   already has as a git-crypt file.
8. **A built-in source behind a Core interface, in its own project.** Rejected for now:
   external plugin executables found on `PATH` (the way git finds `git-credential-*`) — that needs
   a protocol, and a scheme written in a layer file would choose which program runs on the
   caller's machine. Worth revisiting when there's a second store nobody wants built in.
9. **Azure's .NET library, signing in through `az` first.** Rejected: running `az` for every
   call — about a second of start-up per call, errors only as text to parse, and Windows' `az.cmd`
   needs special launching. `az` stays the only thing a person has to install and sign in to.
10. **No interactive sign-in from the tool; no `DefaultAzureCredential`**; the caller's tenant
    only. See "Signing in".
11. **Previews read as little as each form allows**, never a value except `.env` text's (whose
    names are inside it), and never print one.
12. **A source that can't be read is `unknown`**, matching a locked file — including a vault that
    doesn't exist, since that can't be told apart from having no network. **A named secret that
    doesn't exist is an error**, since the vault answered.
13. **The release waits for a real-vault check** by a design partner, since no session of this
    repo can reach Azure.

From implementation:

14. **No managed-identity sign-in step** — measured at about 25 seconds to fail outside Azure; see
    "Signing in". `az login --identity` replaces it.
15. **"One tenant" is Azure.Identity's tenant-discovery switch turned off**
    (`Azure.Identity.DisableTenantDiscovery`), so the token is always for the signed-in tenant; a
    vault elsewhere answers 401, and the tenant its challenge names goes into the message. To be
    confirmed against a real vault (`docs/KEYVAULT_VERIFICATION.md`).
16. **An unreadable named secret makes only its own name `unknown`.** A locked file or an
    unreadable vault could hold any name, so they make every name in their layer uncertain; a
    named secret can only ever hold one.
17. **A duplicate name between a vault source and another source in the same layer is reported
    when a placeholder uses it** — the vault isn't read before that. Two files still fail as soon
    as they're read, as before.
18. **A metadata-only read of one secret lists its versions** (the newest is the current one)
    rather than reading the secret, so a preview needs no permission to read values — and works
    with a role assigned on that one secret. To be confirmed against a real vault.

## Open items

- **Sovereign clouds** (Azure China, Azure Government): a `keyvault://` form naming the cloud,
  still never a free-form host.
- **Binary files other than certificates** from a vault (a base64 convention).
- **Pinning a secret version** (`keyvault://kv/…?version=`), if a real case needs it.
- **Other stores** (AWS Secrets Manager, HashiCorp Vault) behind the same seam.
- **A browser sign-in fallback** on a terminal, for people without `az`.
- **External plugins** — decision #8.
