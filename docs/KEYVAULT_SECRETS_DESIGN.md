# Azure Key Vault as a secrets source — design

**Status: designed, not implemented.** Implementation is next; the release waits until a design
partner with a real Azure Key Vault has run `docs/KEYVAULT_VERIFICATION.md` (written with the
implementation) and confirmed every state below. This repo's own sessions have no Azure access.

Extends `docs/SECRETS_DESIGN.md`: a layer's `secrets` can list an Azure Key Vault next to (or
instead of) `*.secret.env` files. Everything that design settled — `{{CFSECRET_NAME}}`
placeholders, layer precedence, the environment-variable override, the report's per-secret tree,
all-or-nothing real runs, never printing a value — applies unchanged. This document covers only
what a vault adds.

## Why

git-crypt has one key per repo: whoever holds it can read every secret of every client in every
environment. Key Vault access is granted per vault through Azure RBAC, so "these people may read
repo RA's Production secrets for client CA, but not for client CB" becomes a role assignment.
Before this design, the way to use a vault was a script that loads its secrets into `CFSECRET_*`
environment variables. That works, but the report then shows every such secret only at its last
step (`patched in: $CFSECRET_…`) — which layer set it is lost — and the script, not the tool,
decides precedence between vaults.

## How a layer names a vault

```json
{
  "extends": ".configtransform/Environments/Production/configtransform.json",
  "secrets": [
    "keyvault://kv-ra-prod-ca",
    ".configtransform/Clients/CA/Production/legacy.secret.env"
  ],
  "resources": [ ... ]
}
```

- **`keyvault://<vault-name>`** means `https://<vault-name>.vault.azure.net/`. The name follows
  Azure's own rule (3–24 characters; letters, digits and `-`; starts with a letter; ends with a
  letter or digit; no `--`) and is checked when the layer loads.
- **Only the vault's name, never a URL.** The tool sends Azure access tokens to the vault, so the
  host is always built by the tool and always ends in `.vault.azure.net`. A layer file — which
  anyone opening a pull request can edit — can't point a token at any other host.
- **Files and vaults mix freely** in one layer's `secrets`, in any order. As with two files, the
  same name from two sources of the same layer is an error: there's no order between them.
- **An older tool version rejects the entry** (it doesn't end in `.secret.env`), so a layer using
  a vault never resolves silently without it.

## Which vault secrets count, and their names

Key Vault names allow only letters, digits and `-`, and match case-insensitively. A vault secret
counts only if its name starts with `CFSECRET-` (any case), and maps to a placeholder name by
upper-casing it and turning `-` into `_`:

| Vault secret | Placeholder |
|---|---|
| `CFSECRET-ADMIN-DB-CONNECTION` | `{{CFSECRET_ADMIN_DB_CONNECTION}}` |
| `cfsecret-smtp-password` | `{{CFSECRET_SMTP_PASSWORD}}` |
| `admin-db-connection` | *(ignored — no `CFSECRET-` prefix)* |

- **The prefix is the opt-in.** Vaults often hold secrets for other applications too. Without
  the prefix, an unrelated `DB-PASSWORD` in a client's vault would silently override an
  Environment layer's `CFSECRET_DB_PASSWORD`. This is the same idea as the rule that every key in
  a `*.secret.env` file must start with `CFSECRET_`; ignoring rather than rejecting, because the
  tool doesn't own the vault.
- **Vault-sourced names are always upper-case.** A placeholder with lower-case letters can still
  come from a file or an environment variable, never from a vault.
- **Disabled, expired or not-yet-valid secrets don't count.** They're shown in the tree, so
  "why isn't my secret used?" has an answer: `not patched in (disabled in keyvault://kv-ra-prod-ca)`.

## When the tool contacts Azure

- **Only when something needs a secret.** A run whose resources contain no placeholder never
  contacts a vault, so resolving non-secret configuration never needs an Azure login.
- **Previews and `--list` read names only** — one listing per vault, with no values. Listing
  needs only metadata access (the Key Vault Reader role includes it), so someone allowed to see
  *which* secrets exist but not their values gets the full tree.
- **Values are read only for a real run and `--reveal-secrets`**, and only for names a resource
  actually uses, from the vault that wins for each name. That needs Key Vault Secrets User.
- **Each vault is listed at most once per run**, all vaults in parallel; each value is read at
  most once per run.

## What the report shows

The per-secret tree from `docs/SECRETS_DESIGN.md` gains vault steps:

```
    secrets
      CFSECRET_ADMIN_DB_CONNECTION   resolved
        used in: .configtransform/Environments/Production/patch-Web-AdminPortal.Web-Web.config.xml
        .configtransform/Environments/Production/configtransform.json
          patched in: keyvault://kv-ra-prod-shared
          ↓
        .configtransform/Clients/CA/Production/configtransform.json
          patched in: keyvault://kv-ra-prod-ca
          ↓
        environment variable
          not patched in
```

A vault that can't be listed is an unreadable source, exactly like a git-crypt-locked file: the
name is `unknown` unless a later layer settles it, and a real run writes nothing.

| Situation | Step line |
|---|---|
| Vault defines the name | `patched in: keyvault://kv-ra-prod-ca` |
| Vault listed, name not in it | `not patched in` |
| Name there but disabled / expired / not yet valid | `not patched in (disabled in keyvault://kv-ra-prod-ca)` (or `expired`, `not yet valid`) |
| No Azure login available | `unknown: keyvault://kv-ra-prod-ca can't be read (not signed in to Azure -- run az login)` |
| No list permission (403) | `unknown: keyvault://kv-ra-prod-ca can't be read (403 ForbiddenByRbac: no access)` |
| Vault firewall blocks the caller | `unknown: keyvault://kv-ra-prod-ca can't be read (403 ForbiddenByFirewall: blocked by the vault's network rules)` |
| Vault in a tenant the login isn't for | `unknown: keyvault://kv-ra-prod-ca can't be read (it's in another tenant -- run az login --tenant <tenant>)` |
| Host doesn't resolve / no network | `unknown: keyvault://kv-ra-prod-ca can't be reached (no such vault, or no network)` |

Reading a value can fail after listing succeeded — most often a Key Vault Reader running a real
run. That stops the run before anything is written, naming the vault, the vault secret and the
status (`403 ForbiddenByRbac: can list keyvault://kv-ra-prod-ca but not read its values`).

A missing vault is `unknown`, not an error the way a missing `*.secret.env` file is: from the
outside, "this vault doesn't exist" and "no network" look the same, and only the first is a
mistake in the layer file. A real run fails either way.

## Never printing a value

The rule from `docs/SECRETS_DESIGN.md` holds, and two Azure-specific risks get explicit rules:

- **Azure SDK exception messages are never printed.** They can include whole response bodies. The
  tool prints only what it composes itself: the vault, the vault secret's name, the HTTP status
  and Key Vault's error code (`ForbiddenByRbac`).
- **Values are fetched only into memory** — never written to a cache, a temp file or an
  environment variable, and never logged.

## Signing in

The tool never signs in interactively itself. It reuses an existing sign-in, trying these in
order and using the first that works:

1. **The Azure CLI's sign-in (`az login`).** On a developer machine, `az login` does the browser
   SSO, MFA and conditional access, and keeps its tokens between runs in the OS's protected
   store. In GitHub Actions, `azure/login` leaves `az` signed in for the job.
2. **A service principal or workload identity from environment variables** (`AZURE_TENANT_ID`,
   `AZURE_CLIENT_ID`, and `AZURE_CLIENT_SECRET` or `AZURE_FEDERATED_TOKEN_FILE`), for CI systems
   that set those.
3. **A managed identity**, for self-hosted runners and build agents running in Azure.

Why not more:

- **No browser from the tool.** A tool that might open a browser can stall a CI job or a script
  waiting for a sign-in nobody will complete. With `az` doing the interactive part, the tool is
  non-interactive everywhere and fails fast with `run az login`. A browser fallback (only on a
  terminal, never in CI) can be added later without changing anything else here.
- **Not `DefaultAzureCredential`.** It tries about eight sources in turn (Visual Studio, VS Code,
  Azure PowerShell, …), so "which identity did this use?" becomes guesswork, and Microsoft
  recommends a specific chain for anything beyond local development. The three above are the
  ones a person can reason about.
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

## Recommended Azure layout

Key Vault role assignments can be scoped to a subscription, a resource group, a vault or a single
secret — nothing finer, like "every secret whose name starts with X". Per-secret assignments
don't scale (a subscription allows about 4,000 role assignments) and Microsoft recommends them
only for limited cases. So **the vault is the access boundary**: one vault per repo ×
environment × client, plus one per repo × environment for the Environment layer's shared secrets.

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
  part of the layer schema, so Core validates it; reading a vault goes through an interface Core
  defines (list names with their state, read one value) and the CLI registers the implementation
  — the same way it registers the four format engines.
- **A new project, `ConfigTransform.Secrets.AzureKeyVault`**, implements it with
  `Azure.Security.KeyVault.Secrets` and `Azure.Identity`. It's the only project with Azure
  dependencies; they add a few MB to the tool package.
- **`SecretResolver` becomes lazy for vaults.** Files are still read when the chain is built (so a
  missing or malformed file still fails early); vaults are listed on the first lookup.
  Same-layer duplicate names between a file and a vault are detected at that point.
- **Values move out of `SecretStatus`.** Today a lookup returns the value along with the state; a
  vault's value isn't known until it's read, so substitution asks for values separately — only
  ever for real runs and `--reveal-secrets`.

## Testing

- **Core:** a fake vault source covers precedence across files, vaults and the environment
  variable; same-layer duplicates; each unreadable reason; laziness (no placeholder → no vault
  contacted); values read only for real runs and `--reveal-secrets`; the report lines above.
- **The Azure project:** name mapping and the prefix rule; mapping each Azure exception (status,
  error code, credential failures) to its report reason; that no SDK exception message ever
  reaches output. No test touches the network.
- **A real vault:** `docs/KEYVAULT_VERIFICATION.md`, run by a design partner from a fresh clone —
  build from source, create a throwaway vault with `az`, and compare each command's output with
  the expected tree for every row of the table above, including a 403 (by removing their own role
  assignment), another tenant, and no sign-in. The tool never prints a value, so the output is safe
  to send back.

## Decision log

1. **A vault is another kind of `secrets` entry, not a new field.** It's the same job — a layer
   contributing secret values — with the same precedence, duplicate rule and report.
2. **`keyvault://<name>`, never a URL**, so tokens only ever go to `*.vault.azure.net`. Rejected:
   accepting `https://…` URLs — a layer file is repo content anyone can propose changes to.
3. **Vault secrets count only with a `CFSECRET-` prefix**, which maps mechanically to the
   placeholder name. Rejected: mapping every vault secret (an unrelated secret could silently
   override a lower layer's value), and a separate name list in the layer file (a second list to
   keep in sync).
4. **A built-in source behind a Core interface, in its own project.** Rejected for now:
   external plugin executables found on `PATH` (the way git finds `git-credential-*`) — that needs
   a protocol, and a scheme written in a layer file would choose which program runs on the
   caller's machine. Worth revisiting when there's a second store nobody wants built in.
5. **Azure's .NET library, signing in through `az` first.** Rejected: running `az` for every
   call — about a second of start-up per call, errors only as text to parse, and Windows' `az.cmd`
   needs special launching. `az` stays the only thing a person has to install and sign in to.
6. **No interactive sign-in from the tool; no `DefaultAzureCredential`**; the caller's tenant
   only. See "Signing in".
7. **Names for previews, values only when needed.** Previews and `--list` never read a value, so
   metadata-only access is enough to see the tree.
8. **A vault that can't be read is `unknown`, never an error at load**, matching a locked file —
   including a vault that doesn't exist, since that can't be told apart from having no network.
9. **The release waits for a real-vault check** by a design partner, since no session of this
   repo can reach Azure.

## Open items

- **Sovereign clouds** (Azure China, Azure Government): a `keyvault://` form naming the cloud,
  still never a free-form host.
- **`replace` from a vault** — whole-file secrets stay files for now (a Key Vault secret holds at
  most 25 KB, enough for a Firebase JSON but not every certificate bundle).
- **Pinning a secret version** (`keyvault://kv/…?version=`), if a real case needs it.
- **Other stores** (AWS Secrets Manager, HashiCorp Vault) behind the same seam.
- **A browser sign-in fallback** on a terminal, for people without `az`.
- **External plugins** — decision #4.
