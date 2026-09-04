# Onboarding — first run, start to finish

For a developer **joining a repo that already uses `config-transform`**, who just wants a
working local setup and a first successful command — not to learn the tool's architecture
first. A strict, linear, copy-paste checklist: no explanations of *why*, only what to run and
what to do if it fails. If you want the "why," or you're setting `config-transform` up in a
*new* repo rather than joining one that already has it, see `GETTING_STARTED.md` and
`SECRETS_AND_LOCAL_SETUP.md` instead — this doc exists specifically so you don't have to read
those first.

## Before you start

Ask whoever onboarded you (or check this repo's own README/SECRETS doc) for:

- [ ] The GitHub Packages feed URL this repo uses for `config-transform`'s packages.
- [ ] Whether this repo encrypts `.configtransform/**` with git-crypt (look for a
      `.configtransform/** filter=git-crypt` line in the repo's `.gitattributes` — if it's not
      there, skip every git-crypt step below).
- [ ] If it does: who holds the git-crypt key.

## 1. Install prerequisites (once per machine)

- [ ] .NET SDK 8.0+ — check with `dotnet --version`.
- [ ] git-crypt — **only if this repo uses it** (see above):
  - Debian/Ubuntu: `apt-get install git-crypt`
  - macOS: `brew install git-crypt`
  - Windows: `choco install git-crypt` or `scoop install git-crypt` (Git for Windows does not
    bundle it)
  - Verify: `git-crypt --version`

## 2. Get a GitHub personal access token (once per machine)

- [ ] `https://<host>/settings/tokens/new` → check only the `read:packages` scope → set an
      expiry → Generate. (`<host>` is `github.com` unless your team uses a GitHub Enterprise
      Cloud tenant — ask if unsure.)
- [ ] Save it somewhere you can get back to (password manager).

## 3. Clone and set env vars (once per repo)

```bash
git clone <this repo's URL>
cd <repo>
```

Pick your shell and set the three variables `nuget.config` reads:

**bash/zsh:**
```bash
export GITHUB_ACTOR=<your-github-username>
export GITHUB_TOKEN=<the PAT from step 2>
export CONFIGTRANSFORM_PACKAGES_SOURCE=<feed URL from "Before you start">
```

**PowerShell:**
```powershell
$env:GITHUB_ACTOR = "<your-github-username>"
$env:GITHUB_TOKEN = "<the PAT from step 2>"
$env:CONFIGTRANSFORM_PACKAGES_SOURCE = "<feed URL from "Before you start">"
```

**cmd.exe:**
```
set GITHUB_ACTOR=<your-github-username>
set GITHUB_TOKEN=<the PAT from step 2>
set CONFIGTRANSFORM_PACKAGES_SOURCE=<feed URL from "Before you start">
```

These only last the current shell session — add them to your shell profile (`.bashrc`,
PowerShell `$PROFILE`) or a persistent env var (`setx` on Windows) to avoid resetting them every
time.

## 4. Restore the tool

```bash
dotnet tool restore
```

## 5. Unlock git-crypt — skip entirely if this repo doesn't use it

```bash
git-crypt unlock /path/to/the/key/you/were/given
```

**Common mistake:** if you were handed the key as base64 text instead of the raw key file,
decode it back to binary first — pointing `git-crypt unlock` at the base64 text directly fails
with `not a valid git-crypt key file`.

```bash
base64 -d key.b64 > key   # Linux/macOS/Git Bash
```
```powershell
[IO.File]::WriteAllBytes("key", [Convert]::FromBase64String((Get-Content "key.b64" -Raw)))   # PowerShell
```

## 6. Confirm it works

Always run from the repo root — every path (`--resource`, and everything a `configtransform.json`
itself declares) is resolved relative to your current directory, same as CI.

See what one layer actually has, with no risk of writing anything (pick a real client/environment
this repo already uses):
```bash
dotnet tool run configtransform -- --list --client <Client> --environment <Environment>
```
Or, given a resource's own path, see every layer in the tree that patches it:
```bash
dotnet tool run configtransform -- --list --resource <path/to/App.config>
```

Then see a real merge:
```bash
dotnet tool run configtransform -- -r <path/to/App.config> -c <Client> -e <Environment> --diff
```
(`-r`/`-c`/`-e` are short for `--resource`/`--client`/`--environment` — handy for typing
interactively; `-o`/`--output` works the same way. `configtransform` is one tool for both XML and
JSON projects — it dispatches by `--resource`'s own extension, so there's no separate command to
remember for a `.json`-based project. Omit `--resource` entirely to see every resource this layer
touches, across every format, in one call.)

If that prints a diff (or `(no changes)`), you're set up correctly. Done.

## If something goes wrong

| Symptom | Cause | Fix |
|---|---|---|
| `git-crypt unlock: <path>: not a valid git-crypt key file` | The key file you pointed at is the base64-encoded text, not the decoded binary key | Decode it first — step 5 |
| The tool's error says a configtransform.json is "still git-crypt encrypted" | git-crypt isn't unlocked in this working copy | Run `git-crypt unlock` from the repo root — step 5 |
| `Resource base file not found: '...'` | The command isn't being run from the repo root, or `--resource`'s path is wrong | `cd` to the repo root and retry — step 6 |
| `NU1301 ... doesn't exist` on `dotnet tool restore`/`dotnet build` | The three env vars from step 3 aren't set in *this* shell session | Re-run the `export`/`$env:`/`set` lines — they don't persist across sessions unless added to a shell profile |
| `dotnet tool restore` fails with 401/403 | The PAT is missing `read:packages`, expired, or wasn't picked up as `GITHUB_TOKEN` | Mint a fresh one — step 2 — and confirm the env var is actually set (`echo $GITHUB_TOKEN` / `echo $env:GITHUB_TOKEN`) |

Still stuck? `SECRETS_AND_LOCAL_SETUP.md` has the full reference — every edge case (GitHub
Enterprise Cloud tenants, cross-host feeds, CI vs. local) this checklist deliberately leaves
out.

## Related reading

- [`SECRETS_AND_LOCAL_SETUP.md`](SECRETS_AND_LOCAL_SETUP.md) — the comprehensive reference this
  checklist is distilled from.
- [`GETTING_STARTED.md`](GETTING_STARTED.md) — day-to-day usage once you're set up: the
  layering model, and how to add a field.
- [`MANIFEST_SCHEMA.md`](MANIFEST_SCHEMA.md) — what a `configtransform.json` layer actually
  declares.
