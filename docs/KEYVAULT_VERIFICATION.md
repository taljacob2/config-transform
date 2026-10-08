# Azure Key Vault — verification guide

For a design partner with an Azure subscription: a step-by-step check of the Key Vault support
(`docs/KEYVAULT_SECRETS_DESIGN.md`) against real vaults. This repo's own development sessions
have no Azure access, so everything that talks to Azure has only been tested against a fake vault.
This guide is what has to pass before the feature is released.

It takes about 45 minutes. It creates one resource group with two small vaults holding obviously
fake values, and deletes them at the end. **The tool never prints a secret's value** (except with
`--reveal-secrets`, used once below on purpose), and every value here is fake, so the output is safe
to send back.

## What you need

- The **.NET 8 SDK** (`dotnet --version` shows `8.x` or later).
- The **Azure CLI** (`az version`), signed in (`az login`) to a subscription where you can create a
  resource group and assign roles on it (Owner, or Contributor plus User Access Administrator).
- **bash** — Git Bash on Windows works. The commands use bash variables and heredocs.

## 1. Build the tool from this repo

```bash
git clone https://github.com/taljacob2/config-transform.git
cd config-transform
dotnet build -c Release
CT="dotnet $(pwd)/src/ConfigTransform.Cli/bin/Release/net8.0/ConfigTransform.Cli.dll"
git rev-parse --short HEAD     # send this back with your results
```

## 2. Create two throwaway vaults

Vault names are global, so these get a random suffix.

```bash
SUFFIX=$RANDOM
RG=rg-ct-verify-$SUFFIX
LOC=westeurope
KV_SHARED=ctv-shared-$SUFFIX       # plays the Environment layer's vault
KV_CLIENT=ctv-client-$SUFFIX       # plays a client's vault
ME=$(az ad signed-in-user show --query id -o tsv)

az group create --name $RG --location $LOC -o none
for KV in $KV_SHARED $KV_CLIENT; do
  az keyvault create --name $KV --resource-group $RG --location $LOC --enable-rbac-authorization true -o none
  az role assignment create --role "Key Vault Secrets Officer" --assignee-object-id $ME \
    --assignee-principal-type User --scope $(az keyvault show --name $KV --query id -o tsv) -o none
done
echo "waiting for the role assignments to take effect"; sleep 90
```

Fill them with fake secrets:

```bash
az keyvault secret set --vault-name $KV_SHARED --name CFSECRET-DB-PASSWORD   --value shared-db-FAKE   -o none
az keyvault secret set --vault-name $KV_SHARED --name CFSECRET-SMTP-PASSWORD --value shared-smtp-FAKE -o none
az keyvault secret set --vault-name $KV_SHARED --name DB-PASSWORD            --value unrelated-FAKE   -o none

az keyvault secret set --vault-name $KV_CLIENT --name CFSECRET-DB-PASSWORD   --value client-db-FAKE   -o none
az keyvault secret set --vault-name $KV_CLIENT --name legacy-api-key         --value legacy-FAKE      -o none
az keyvault secret set --vault-name $KV_CLIENT --name CFSECRET-DISABLED      --value disabled-FAKE --disabled true -o none
printf 'CFSECRET_QUEUE_URL=amqp://queue-FAKE\nCFSECRET_PUSH_KEY=push-FAKE\n' > notifications.env
az keyvault secret set --vault-name $KV_CLIENT --name notifications-secrets --file notifications.env -o none
printf '{ "type": "service_account", "project_id": "verify-FAKE" }\n' > firebase.json
az keyvault secret set --vault-name $KV_CLIENT --name firebase-service-account --file firebase.json -o none
```

## 3. A scratch workspace that uses them

The tool treats the current directory as the repository root, so this workspace lives outside the
clone.

```bash
WS=$(mktemp -d); cd $WS
mkdir -p app .configtransform/Environments/Production .configtransform/Clients/CA/Production

cat > app/appsettings.json <<'EOF'
{
  "Db": "{{CFSECRET_DB_PASSWORD}}",
  "Smtp": "{{CFSECRET_SMTP_PASSWORD}}",
  "Queue": "{{CFSECRET_QUEUE_URL}}",
  "Legacy": "{{CFSECRET_LEGACY_API_KEY}}"
}
EOF
echo '{ "Off": "{{CFSECRET_DISABLED}}" }' > app/disabled.json
echo '{}' > app/firebase.json

cat > .configtransform/Environments/Production/configtransform.json <<EOF
{
  "secrets": [ "keyvault://$KV_SHARED" ],
  "resources": [ { "path": "app/appsettings.json" }, { "path": "app/disabled.json" } ]
}
EOF

cat > .configtransform/Clients/CA/Production/configtransform.json <<EOF
{
  "extends": ".configtransform/Environments/Production/configtransform.json",
  "secrets": [
    "keyvault://$KV_CLIENT",
    "keyvault://$KV_CLIENT/notifications-secrets",
    { "from": "keyvault://$KV_CLIENT/legacy-api-key", "as": "CFSECRET_LEGACY_API_KEY" }
  ],
  "resources": [
    { "path": "app/firebase.json", "replace": "keyvault://$KV_CLIENT/firebase-service-account" }
  ]
}
EOF
```

In the expected output below, `<KV_SHARED>` and `<KV_CLIENT>` stand for your two vault names.

## 4. The checks

Send back the full output of each numbered command. For each check, the lines under "Expect" must
appear.

### 4.1 Every form resolves, at the right layer

```bash
time $CT -c CA -e Production -r app/appsettings.json --dry-run --color never
```

Expect, among the secrets tree:

```
      CFSECRET_DB_PASSWORD      resolved
        used in: app/appsettings.json
        .configtransform/Environments/Production/configtransform.json
          patched in: keyvault://<KV_SHARED>/CFSECRET-DB-PASSWORD
          ↓
        .configtransform/Clients/CA/Production/configtransform.json
          patched in: keyvault://<KV_CLIENT>/CFSECRET-DB-PASSWORD
```
```
          patched in: keyvault://<KV_SHARED>/CFSECRET-SMTP-PASSWORD
          patched in: keyvault://<KV_CLIENT>/notifications-secrets
          patched in: keyvault://<KV_CLIENT>/legacy-api-key
```

The merged JSON below the tree still shows `{{CFSECRET_…}}` placeholders, not values. Also note
how long `time` reports. A few seconds is expected; tell us if it's much more.

### 4.2 A real run writes the right values

```bash
$CT -c CA -e Production -r app/appsettings.json -o out/appsettings.json --color never && cat out/appsettings.json
```

Expect `client-db-FAKE` (the client vault overrides the shared one), `shared-smtp-FAKE`,
`amqp://queue-FAKE` and `legacy-FAKE`. `unrelated-FAKE` must **not** appear: the vault's
`DB-PASSWORD` has no `CFSECRET-` prefix.

### 4.3 `--reveal-secrets` shows values in a preview

```bash
$CT -c CA -e Production -r app/appsettings.json --dry-run --reveal-secrets --color never
```

Expect the merged JSON to show the same four values as 4.2. The tree above it still shows no
values.

### 4.4 A whole file from the vault

```bash
$CT -c CA -e Production -r app/firebase.json --dry-run --color never
$CT -c CA -e Production -r app/firebase.json -o out/firebase.json && diff out/firebase.json $OLDPWD/firebase.json && echo IDENTICAL
```

Expect `replaced by: keyvault://<KV_CLIENT>/firebase-service-account` in the chain, then
`(replaced by keyvault://<KV_CLIENT>/firebase-service-account, not shown -- pass --reveal-secrets to see it)`,
and finally `IDENTICAL`. (`$OLDPWD` is the clone, where `firebase.json` was written in step 2; if
that doesn't work in your shell, compare the two files by hand.)

### 4.5 A disabled secret

```bash
$CT -c CA -e Production -r app/disabled.json --dry-run --color never
```

Expect:

```
      CFSECRET_DISABLED   MISSING
```
```
          not patched in (keyvault://<KV_CLIENT>/CFSECRET-DISABLED is disabled)
```

### 4.6 `--list`

```bash
$CT -c CA -e Production --list --color never
```

Expect the header to list the client layer's three entries, the last as
`keyvault://<KV_CLIENT>/legacy-api-key as CFSECRET_LEGACY_API_KEY`, and a `secrets` tree at the end
with the same `patched in:` lines as 4.1 plus `CFSECRET_DISABLED`.

### 4.7 A vault name that doesn't exist

```bash
sed -i "s/keyvault:\/\/$KV_SHARED\"/keyvault:\/\/$KV_SHARED-typo\"/" .configtransform/Environments/Production/configtransform.json
$CT -e Production -r app/appsettings.json --dry-run --color never
sed -i "s/$KV_SHARED-typo/$KV_SHARED/" .configtransform/Environments/Production/configtransform.json
```

Expect `unknown: keyvault://<KV_SHARED>-typo can't be reached (no such vault, or no network)`.

### 4.8 A named secret that doesn't exist

```bash
cp .configtransform/Environments/Production/configtransform.json env.bak
sed -i "s/keyvault:\/\/$KV_SHARED\"/keyvault:\/\/$KV_SHARED\/CFSECRET-DB-PASWORD\"/" .configtransform/Environments/Production/configtransform.json
$CT -e Production -r app/appsettings.json --dry-run --color never
mv env.bak .configtransform/Environments/Production/configtransform.json
```

Expect an error ending in `has no secret named 'CFSECRET-DB-PASWORD'`, with a `Try:` line.

### 4.9 Not signed in

```bash
az logout
time $CT -c CA -e Production -r app/appsettings.json --dry-run --color never
az login     # sign back in before continuing
```

Expect `unknown: keyvault://<KV_SHARED> can't be read (not signed in to Azure -- run az login)`
(and the same for the client vault), within about a second or two.

### 4.10 No access (403)

Remove your role on the client vault, wait, and preview:

```bash
CLIENT_ID=$(az keyvault show --name $KV_CLIENT --query id -o tsv)
az role assignment delete --assignee $ME --role "Key Vault Secrets Officer" --scope $CLIENT_ID
sleep 120
$CT -c CA -e Production -r app/appsettings.json --dry-run --color never
$CT -c CA -e Production -r app/appsettings.json -o out/denied.json; echo "exit=$?"; ls out/denied.json
```

Expect `unknown: keyvault://<KV_CLIENT> can't be read (403 ForbiddenByRbac: no access)`, then the
real run failing with `exit=1`, and `ls` reporting that `out/denied.json` doesn't exist.

### 4.11 Names but not values (Key Vault Reader)

Still without Secrets Officer on the client vault, grant only the metadata role:

```bash
az role assignment create --role "Key Vault Reader" --assignee-object-id $ME --assignee-principal-type User --scope $CLIENT_ID -o none
sleep 120
$CT -c CA -e Production -r app/appsettings.json --dry-run --color never
$CT -c CA -e Production -r app/firebase.json --dry-run --color never
$CT -c CA -e Production -r app/appsettings.json -o out/reader.json; echo "exit=$?"
```

Expect the first preview to resolve every name from the client vault **except** what comes from
`notifications-secrets`: its names are inside its value, which a Reader can't read, so that line is
`unknown: keyvault://<KV_CLIENT>/notifications-secrets can't be read (403 …)`. The `firebase.json`
preview should still show the "not shown" note, since it reads metadata only. The real run must
fail with `exit=1`, saying it `can't be read (403 …)`. Please send back the exact 403 codes you see.

### 4.12 Access to one secret only

```bash
az role assignment delete --assignee $ME --role "Key Vault Reader" --scope $CLIENT_ID
az role assignment create --role "Key Vault Secrets User" --assignee-object-id $ME --assignee-principal-type User \
  --scope "$CLIENT_ID/secrets/legacy-api-key" -o none
sleep 120
$CT -c CA -e Production -r app/appsettings.json --dry-run --color never
```

Expect the line `patched in: keyvault://<KV_CLIENT>/legacy-api-key`: reading that one secret works
with a role on that one secret. Next to it, the whole-vault entry `keyvault://<KV_CLIENT>` should
be `unknown: … can't be read (403 …)`, because listing a vault needs access to the whole vault. The
name's overall state is therefore `unknown` too, since the unreadable vault could also hold that
name — that's expected.

Restore your access before continuing:

```bash
az role assignment create --role "Key Vault Secrets Officer" --assignee-object-id $ME --assignee-principal-type User --scope $CLIENT_ID -o none
```

### 4.13 Optional: the vault's firewall

```bash
az keyvault update --name $KV_SHARED --default-action Deny -o none; sleep 60
$CT -e Production -r app/appsettings.json --dry-run --color never
az keyvault update --name $KV_SHARED --default-action Allow -o none
```

Expect `can't be read (403 ForbiddenByFirewall: blocked by the vault's network rules)`, or tell us
what it says instead.

### 4.14 Optional: a vault in another tenant

Only if you can sign in to a second tenant. With `az login` on your usual tenant, point a layer at
a vault in the other tenant. Expect
`can't be read (it's in another tenant -- run az login --tenant <tenant-id>)`, with the other
tenant's ID. Then `az login --tenant <that ID>` and confirm it resolves.

### 4.15 Optional: GitHub Actions

Follow "GitHub Actions" in `docs/KEYVAULT_SECRETS_DESIGN.md` with one managed identity, one
federated credential and one GitHub Environment, then run the workflow. Expect the same tree as
4.1 in the job log, and no value anywhere in the log.

## 5. Clean up

Key Vault keeps deleted vaults for 90 days (soft delete), so purge them too:

```bash
az group delete --name $RG --yes
az keyvault purge --name $KV_SHARED
az keyvault purge --name $KV_CLIENT
rm -rf "$WS"
```

## What to send back

- The commit from step 1, your OS, and `az version`.
- The output of every check you ran, marked with its number. Note anything that didn't match, and
  especially:
  - the exact 403 codes in 4.10–4.13;
  - the timing from 4.1 and 4.9;
  - whether 4.11 and 4.12 behaved as described. These test that reading one secret's metadata
    works with a role on just that secret — the part of the design least certain without a real
    vault.
