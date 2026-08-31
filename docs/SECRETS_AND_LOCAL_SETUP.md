# Secrets and local setup for a consuming repo

Two independent things a repo that *consumes* `config-transform` needs configured — one to
install the tool at all, one only if that repo also follows this architecture's git-crypt
recommendation (`CONFIG_MANAGEMENT.md` §7) — and how to set each up, in CI and on a developer's
machine, on every platform. `GETTING_STARTED.md` assumes this is already done; this is the doc
that gets it done.

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

First check whether it's even needed: `https://github.com/<owner>?tab=packages` — if
`ConfigTransform.Xml`/`.Json` show "Public", skip straight to `nuget.config` below.

### CI: add a repo secret

1. Generate a PAT scoped to **only** `read:packages`:
   `https://github.com/settings/tokens/new` → check that one scope → set an expiry → Generate.
2. Add it as a secret in the consuming repo (any name works, since it's referenced explicitly in
   the workflow — this doc uses `GH_PACKAGES_TOKEN`): Settings → Secrets and variables → Actions
   → New repository secret.
3. In the workflow step that runs `dotnet tool restore`, supply it (falling back to the
   workflow's own token means repos where the packages happen to be public need nothing extra):
   ```yaml
   - name: Restore local tools
     env:
       GITHUB_ACTOR: ${{ github.actor }}
       GITHUB_TOKEN: ${{ secrets.GH_PACKAGES_TOKEN || secrets.GITHUB_TOKEN }}
     run: dotnet tool restore
   ```

### `nuget.config` (in the consuming repo, committed)

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github-config-transform" value="https://nuget.pkg.github.com/<owner>/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-config-transform>
      <add key="Username" value="%GITHUB_ACTOR%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </github-config-transform>
  </packageSourceCredentials>
</configuration>
```

The `%GITHUB_ACTOR%`/`%GITHUB_TOKEN%` env-var substitution keeps the credential out of the
committed file entirely — the same file works locally and in CI, as long as both env vars are
set wherever `dotnet tool restore` runs.

### Local developer setup

1. Set the two env vars, then restore — pick your shell:

   **Linux/macOS/Git Bash:**
   ```bash
   export GITHUB_ACTOR=<your-github-username>
   export GITHUB_TOKEN=<a PAT with read:packages>
   dotnet tool restore
   ```

   **Windows PowerShell:**
   ```powershell
   $env:GITHUB_ACTOR = "<your-github-username>"
   $env:GITHUB_TOKEN = "<a PAT with read:packages>"
   dotnet tool restore
   ```

   **Windows cmd.exe:**
   ```
   set GITHUB_ACTOR=<your-github-username>
   set GITHUB_TOKEN=<a PAT with read:packages>
   dotnet tool restore
   ```

   A personal PAT (same scope, `read:packages`) works fine here — it doesn't need to be the
   same token as the CI secret, though it can be.
2. Setting env vars this way only lasts the shell session. To persist: a shell profile
   (`.bashrc`, PowerShell `$PROFILE`) or a durable env var (`setx GITHUB_ACTOR ...` on Windows).
3. Run the tool exactly as CI does — identical invocation on every platform:
   ```
   dotnet tool run configtransform-xml -- --manifest .configtransform/<Project>/manifest.json --file App.config --client ClientA --environment Production --diff
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

## Related reading

- `CONFIG_MANAGEMENT.md` §7 for the git-crypt design rationale, §10.2–§10.4 for the packaging
  and feed design.
- `RELEASING.md` for how `config-transform` itself gets built and published to that feed.
- `GETTING_STARTED.md` for what to do once both of these are set up.
