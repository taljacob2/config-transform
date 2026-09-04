# Secrets and local setup for a consuming repo

Two independent things a repo that *consumes* `config-transform` needs configured — one to
install the tool at all, one only if that repo also follows this architecture's git-crypt
recommendation (`CONFIG_MANAGEMENT.md` §7) — and how to set each up, in CI and on a developer's
machine, on every platform. `GETTING_STARTED.md` assumes this is already done; this is the doc
that gets it done.

**Just joining a repo and want the fast path, not the full reference?** See
[`ONBOARDING.md`](ONBOARDING.md) — a linear checklist distilled from this doc, with the same
information but none of the "why."

## 1. Authenticating to the GitHub Packages feed

`ConfigTransform.Xml`/`.Json` are published to a **private-by-default** GitHub Packages NuGet
feed (`CONFIG_MANAGEMENT.md` §10.2/§10.4). Any repo running `dotnet tool restore` against it
needs a credential — there is no way to `dotnet tool install`/`restore` these packages
anonymously unless they're explicitly made public.

### The one real wrinkle: cross-repo package auth

A GitHub Actions workflow's own default `GITHUB_TOKEN` can only read packages published *from
the repository the workflow runs in*. Since `config-transform` publishes from its own dedicated
repo, **any other repo's CI** trying to restore its packages via that repo's own default
`GITHUB_TOKEN` gets a 401/403 — even when both repos are owned by the same account or org. This
was confirmed the hard way via `config-transform-pilot`'s real CI, not assumed. A personal
access token (PAT) with `read:packages` is required in the consuming repo whenever it differs
from `config-transform`'s own repo — which is the normal case, by design (§2: the tool lives in
its own dedicated repo, not copied into each solution repo).

First check whether it's even needed: `https://<host>/<owner>?tab=packages` — if
`ConfigTransform.Xml`/`.Json` show "Public", skip straight to `nuget.config` below. `<host>` is
`github.com` for the ordinary case; see "Which host?" below if `config-transform` is published
from a GitHub Enterprise Cloud tenant instead.

### Which host, and which feed URL

Two things about the feed URL are worth getting right before wiring anything up, because getting
either wrong produces a working-looking `nuget.config` that fails at restore time:

- **The host isn't always `github.com`.** A GitHub Enterprise Cloud tenant with data residency
  (`https://<subdomain>.ghe.com`) is a different, fully separate host from `github.com` — its own
  domain, identity, and package registry, not a region flag on the regular github.com feed.
- **The feed URL isn't just the host with a different name swapped in.** On `github.com` the
  NuGet v3 feed is `https://nuget.pkg.github.com/<owner>/index.json`. On a `ghe.com` tenant it's
  `https://nuget.<subdomain>.ghe.com/<owner>/index.json` — the `.pkg.` segment is simply absent.
  A template that does `s/github.com/<subdomain>.ghe.com/` on the github.com URL produces
  `nuget.pkg.<subdomain>.ghe.com`, which does not exist. Always use the full feed URL for
  whichever host actually applies — don't derive it from a github.com template at request time.

**Cross-host consumption is a separate concern from picking the right URL.** If the consuming
repo and the repo that actually publishes `config-transform`'s packages live on *different*
GitHub hosts (e.g. the consuming repo is on a `ghe.com` tenant, but `config-transform` publishes
from `github.com`), pointing `nuget.config` at the right URL is necessary but not sufficient:
  - The tenant's Actions runners need outbound network access to the *other* host — many
    enterprise tenants restrict Actions egress to an allowlist, and `nuget.pkg.github.com` (or
    whichever host the packages actually live on) needs to be on it.
  - The PAT used to authenticate must be minted from an account **on the host that serves the
    packages**, not the consuming repo's own host — a `ghe.com` tenant's own identity system
    doesn't carry authorization for a github.com-hosted feed, and vice versa.

  If both repos live on the same host (the common case — including two repos on the same
  `ghe.com` tenant), none of this applies; it's a plain URL substitution.

### CI: one secret, one variable

1. Generate a PAT scoped to **only** `read:packages`, on whichever host actually serves the
   packages: `https://<host>/settings/tokens/new` → check that one scope → set an expiry →
   Generate.
2. Add it as a **secret** in the consuming repo (any name works, since it's referenced explicitly
   in the workflow — this doc uses `GH_PACKAGES_TOKEN`): Settings → Secrets and variables →
   Actions → Secrets tab → New repository secret.
3. Add the full feed URL as a **variable** in the same repo (this doc uses
   `CONFIGTRANSFORM_PACKAGES_SOURCE`): Settings → Secrets and variables → Actions → Variables tab
   → New repository variable. A variable, not a secret — a feed URL isn't sensitive, and it's
   genuinely useful to see which feed a run pulled from directly in the log. Set its value to the
   full URL for whichever host applies (see above) — there is deliberately no default baked into
   the workflow for this; see "Why no default" below.
4. Supply both **at the job level**, not on a single step (the token falls back to the
   workflow's own, so repos where the packages happen to be public need only the variable):
   ```yaml
   jobs:
     transform:
       runs-on: ubuntu-latest
       env:
         GITHUB_ACTOR: ${{ github.actor }}
         GITHUB_TOKEN: ${{ secrets.GH_PACKAGES_TOKEN || secrets.GITHUB_TOKEN }}
         CONFIGTRANSFORM_PACKAGES_SOURCE: ${{ vars.CONFIGTRANSFORM_PACKAGES_SOURCE }}
       steps:
         - name: Restore local tools
           run: dotnet tool restore
   ```
   Job-level matters here, confirmed the hard way: `dotnet build`'s implicit restore for *any*
   project in the repo enumerates every source configured in `nuget.config`, even one that
   project has no dependency on — so `%CONFIGTRANSFORM_PACKAGES_SOURCE%` needs to be expandable
   on every step that runs `dotnet build`/`dotnet restore`, not just the one that runs
   `dotnet tool restore`. Scoping it to a single step's own `env:` (as an earlier draft of this
   doc had it) leaves it unexpanded on every other step — NuGet then treats the literal
   `%CONFIGTRANSFORM_PACKAGES_SOURCE%` string as a nonexistent local path and fails with
   `NU1301`, not an auth error, because the source's `value=` is evaluated unconditionally on
   every invocation while credentials are only checked lazily when a package actually needs to
   be pulled from that source.

   **This applies to every workflow file in the repo that runs `dotnet build`/`dotnet
   restore`/`dotnet tool restore` on anything — not just the workflow that invokes
   `config-transform`'s own CLI tools.** `nuget.config` is resolved per-repo, not per-workflow:
   once its `packageSources` entry reads `%CONFIGTRANSFORM_PACKAGES_SOURCE%`, a plain CI build
   workflow that has nothing to do with `config-transform` (say, one that just builds and tests
   the consuming repo's own projects on every push) needs these same three job-level env vars
   too, or its very first `dotnet restore` after adopting this pattern fails with the same
   `NU1301`. Confirmed the hard way in `config-transform-pilot`: its plain per-push `build.yml`
   had never needed any of this before and had no `env:` block at all, so it broke on the next
   push after only the `config-transform`-specific workflow was fixed. Audit every workflow file
   that touches `dotnet`, not just the ones that call `config-transform`'s tools directly.

#### Why no default

An earlier draft of this workflow snippet had
`vars.CONFIGTRANSFORM_PACKAGES_SOURCE || 'https://nuget.pkg.github.com/<owner>/index.json'` — a
fallback so repos wouldn't need to set the variable at all in the common case. That's a mistake
for a *template* doc like this one: any concrete `<owner>` baked in as a "default" is one specific
account's feed, and a repo that copies this snippet without setting the variable would silently,
successfully restore from that account's packages instead of failing loudly — easy to not notice,
worse when it's noticed late. Requiring the variable to always be set, with no fallback, costs one
repository-variable click and removes that whole failure mode: no `nuget.config` or workflow
anywhere in this doc names a real owner or host.

### `nuget.config` (in the consuming repo, committed)

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github-config-transform" value="%CONFIGTRANSFORM_PACKAGES_SOURCE%" />
  </packageSources>
  <packageSourceCredentials>
    <github-config-transform>
      <add key="Username" value="%GITHUB_ACTOR%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </github-config-transform>
  </packageSourceCredentials>
</configuration>
```

The `%CONFIGTRANSFORM_PACKAGES_SOURCE%`/`%GITHUB_ACTOR%`/`%GITHUB_TOKEN%` env-var substitution
keeps both the feed URL and the credential out of the committed file entirely — NuGet's `%VAR%`
expansion isn't limited to credentials, it applies to config values generally. The same file
works locally, in CI on `github.com`, and in CI on a `ghe.com` tenant, unchanged — only the env
var values differ.

### Local developer setup

The job-level-scoping requirement above is a CI-specific wrinkle, not something to worry about
here: a plain shell's `export`/`$env:`/`set` sets the variable for the rest of that shell
session, so once it's set, every later command — `dotnet tool restore`, `dotnet build`,
`dotnet restore` on any project — sees it. No per-command re-scoping needed, unlike a GitHub
Actions job made of separate steps.

1. Set the three env vars, then restore — pick your shell:

   **Linux/macOS/Git Bash:**
   ```bash
   export GITHUB_ACTOR=<your-github-username>
   export GITHUB_TOKEN=<a PAT with read:packages>
   export CONFIGTRANSFORM_PACKAGES_SOURCE=<the feed URL for your host>
   dotnet tool restore
   ```

   **Windows PowerShell:**
   ```powershell
   $env:GITHUB_ACTOR = "<your-github-username>"
   $env:GITHUB_TOKEN = "<a PAT with read:packages>"
   $env:CONFIGTRANSFORM_PACKAGES_SOURCE = "<the feed URL for your host>"
   dotnet tool restore
   ```

   **Windows cmd.exe:**
   ```
   set GITHUB_ACTOR=<your-github-username>
   set GITHUB_TOKEN=<a PAT with read:packages>
   set CONFIGTRANSFORM_PACKAGES_SOURCE=<the feed URL for your host>
   dotnet tool restore
   ```

   A personal PAT (same scope, `read:packages`, minted on the host that serves the packages)
   works fine here — it doesn't need to be the same token as the CI secret, though it can be.
2. Setting env vars this way only lasts the shell session. To persist: a shell profile
   (`.bashrc`, PowerShell `$PROFILE`) or a durable env var (`setx GITHUB_ACTOR ...` on Windows).
3. Run the tool exactly as CI does — identical invocation on every platform, regardless of host:
   ```
   dotnet tool run configtransform-xml -- --resource <Project>/App.config --client ClientA --environment Production --diff
   ```

## 2. git-crypt for the consuming repo's `.configtransform/` tree

This is the *consuming repo's own* encryption choice (`CONFIG_MANAGEMENT.md` §7) — the tool
itself has no git-crypt dependency at all; `ConfigTransform.Xml`/`.Json` just read plaintext
files off whatever disk they're given, encrypted-and-unlocked or not. Most real consuming repos
will still want this, since it's what this architecture recommends for secrets sitting in
`.configtransform/**` (connection strings, API keys). Skip this whole section if a consuming
repo has decided not to encrypt that tree.

### Setting it up fresh, in a repo that doesn't have a key yet

```bash
# Run once, in the repo root, before anything under .configtransform/ is committed:
git-crypt init
echo ".configtransform/** filter=git-crypt diff=git-crypt" >> .gitattributes
git add .gitattributes
git commit -m "Add git-crypt attributes for .configtransform/"

# git-crypt init just generated a new random AES-256 key, stored in .git/git-crypt/
# (never committed). Export it to a real file so it can be distributed and added as
# a CI secret:
git-crypt export-key ./git-crypt-key
```

If config files already exist under `.configtransform/` before running `git-crypt init`, follow
with the migration step (`CONFIG_MANAGEMENT.md` §7.2):
```bash
git add --renormalize .
git commit -m "Encrypt .configtransform/ with git-crypt"
git push
```
This encrypts everything under `.configtransform/**` from that commit forward — it does not
retroactively scrub plaintext from earlier commits (a separate concern, §7.4).

**Losing the exported key with no backup makes `.configtransform/**` permanently
unrecoverable** — not a bug, what encryption without a backdoor means (§7.1's disclaimer). Get
it into a team password manager/vault before distributing it to anyone, not after.

### Base64-encoding the key for a CI secret

A GitHub secret's value is a single line of text; the raw key file isn't.

- **Linux:** `base64 -w0 ./git-crypt-key > ./git-crypt-key.b64`
- **macOS:** `base64 -i ./git-crypt-key -o ./git-crypt-key.b64` (BSD `base64` has no `-w`; for a
  guaranteed single line instead: `base64 -i ./git-crypt-key | tr -d '\n' > ./git-crypt-key.b64`)
- **Windows PowerShell** (no separate `base64` binary needed):
  ```powershell
  [Convert]::ToBase64String([IO.File]::ReadAllBytes("./git-crypt-key")) | Set-Content -NoNewline ./git-crypt-key.b64
  ```
- **Windows, inside Git Bash**: the Linux command above works unchanged — Git Bash ships a real
  `base64` binary.

Paste the contents of `git-crypt-key.b64` as a repo secret (this doc uses
`GIT_CRYPT_KEY_BASE64`), then delete the loose files and make sure the raw key is safely in a
password manager/vault:

- **Linux/macOS/Git Bash:** `rm ./git-crypt-key ./git-crypt-key.b64`
- **Windows PowerShell:** `Remove-Item ./git-crypt-key, ./git-crypt-key.b64`

In the deployment workflow (`build-transformed.yml`-equivalent — never the plain build/test
workflow, which should never need the key at all, per §8.1):
```yaml
- name: Install git-crypt
  run: sudo apt-get update && sudo apt-get install -y git-crypt   # ubuntu-latest doesn't ship it

- name: Unlock git-crypt
  run: |
    echo "${{ secrets.GIT_CRYPT_KEY_BASE64 }}" | base64 -d > /tmp/git-crypt-key
    git-crypt unlock /tmp/git-crypt-key
    rm /tmp/git-crypt-key
```

### Local developer setup

1. **Install git-crypt:**
   - Debian/Ubuntu: `apt-get install git-crypt`
   - macOS: `brew install git-crypt`
   - Windows: Chocolatey (`choco install git-crypt`), Scoop (`scoop install git-crypt`), or a
     manual binary from git-crypt's
     [GitHub releases](https://github.com/AGWA/git-crypt/releases) placed on `PATH`. Git for
     Windows does **not** bundle git-crypt — it's a separate install on top of Git either way.
   - Verify with `git-crypt --version`.
2. Clone the repo normally. Everything under `.configtransform/` shows as opaque binary — that's
   git-crypt working correctly, not a broken clone.
3. Get the key from whoever holds it, out of band (password manager/vault entry — never via
   git, chat, or email in plaintext).
4. Unlock — identical command on every platform:
   ```
   git-crypt unlock /path/to/the.key
   ```
   `.configtransform/**` is now plaintext in your working copy. `git-crypt lock` re-encrypts it
   locally (to double check the round-trip, or before leaving a shared machine unattended).

   **A real mistake worth flagging directly: this must be the raw decoded key, not the base64
   text.** The same key exists in two forms — the binary file `git-crypt export-key` produces,
   and the base64-encoded single line of it that goes into the CI secret (§1's `GIT_CRYPT_KEY_BASE64`-equivalent, if the repo uses one). Saving the base64 text itself as the local key file
   (easy to do if whoever shared the key handed you that form, or a password-manager entry held
   the base64 string) fails with `git-crypt unlock: <path>: not a valid git-crypt key file` — hit
   for real while setting up `config-transform-pilot`. Decode it back to binary first:
   ```powershell
   [IO.File]::WriteAllBytes("C:\keys\the.key", [Convert]::FromBase64String((Get-Content "C:\keys\the.key.b64" -Raw)))
   ```
   (or `base64 -d` on Linux/macOS/Git Bash) before pointing `git-crypt unlock` at it.

Forgetting this step and running the CLI anyway is a common enough mistake that the tool
detects it directly: it names the `configtransform.json` file, says it's still git-crypt
encrypted, and tells you to run `git-crypt unlock` — rather than surfacing a raw, confusing JSON
parse error
(`'0x00' is an invalid start of a value`) for what is actually just ciphertext, not malformed
JSON.

## Related reading

- `CONFIG_MANAGEMENT.md` §7 for the git-crypt design rationale, §10.2–§10.4 for the packaging
  and feed design.
- `RELEASING.md` for how `config-transform` itself gets built and published to that feed.
- `GETTING_STARTED.md` for what to do once both of these are set up.
