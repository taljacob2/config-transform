#!/usr/bin/env bash
# Verifies a just-published ConfigTransform.Cli version is actually installable from GitHub
# Packages and runs correctly -- not just that `dotnet pack`/`dotnet build` succeeded.
# Packaging bugs (a missing PackAsTool setting, a wrong ToolCommandName, a dependency left out of
# the .nupkg -- easy to get wrong here since this package bundles both the XDT and
# Microsoft.Extensions.Configuration engines) are invisible to the regular build/test steps and
# only show up when a real consumer installs the published package. Run by publish.yml
# immediately after `dotnet nuget push`; can also be run manually against a real feed to verify a
# release.
#
# Builds a real 2-layer configtransform.json chain (Environment -> Client) with an XML, a JSON,
# a .env, AND a YAML resource all in the SAME layer, and asserts a single omitted-`--resource`
# call resolves all four in one invocation with no skip note -- the actual capability CLI
# unification delivers, not just "the binary starts and parses its args."
#
# Usage: smoke-test-published-tool.sh <version> <github-user> <github-token>
set -euo pipefail

VERSION="$1"
GITHUB_USER="$2"
GITHUB_TOKEN="$3"

WORKDIR=$(mktemp -d)
trap 'rm -rf "$WORKDIR"' EXIT
cd "$WORKDIR"

dotnet new tool-manifest
dotnet nuget add source "https://nuget.pkg.github.com/${GITHUB_USER}/index.json" \
  --name github-packages-verify --username "$GITHUB_USER" --password "$GITHUB_TOKEN" --store-password-in-clear-text

dotnet tool install --local ConfigTransform.Cli --version "$VERSION"

mkdir -p smoke/Project \
         smoke/.configtransform/Environments/Smoke \
         smoke/.configtransform/Clients/SmokeClient/Smoke

cat > smoke/Project/App.config <<'CONFIG'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <add key="ApiUrl" value="https://dev.example.com" />
  </appSettings>
</configuration>
CONFIG

cat > smoke/Project/appsettings.json <<'CONFIG'
{ "ApiUrl": "https://dev.example.com" }
CONFIG

cat > smoke/Project/.env <<'CONFIG'
API_URL=https://dev.example.com
CONFIG

cat > smoke/Project/settings.yaml <<'CONFIG'
ApiUrl: https://dev.example.com
CONFIG

cat > smoke/.configtransform/Environments/Smoke/patch-Project-App.config.xml <<'PATCH'
<?xml version="1.0" encoding="utf-8"?>
<configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
  <appSettings>
    <add key="ApiUrl" value="https://smoke-xml.example.com"
         xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
  </appSettings>
</configuration>
PATCH

cat > smoke/.configtransform/Environments/Smoke/patch-Project-appsettings.json.json <<'PATCH'
{ "ApiUrl": "https://smoke-json.example.com" }
PATCH

cat > smoke/.configtransform/Environments/Smoke/patch-Project-.env <<'PATCH'
API_URL=https://smoke-env.example.com
PATCH

cat > smoke/.configtransform/Environments/Smoke/patch-Project-settings.yaml <<'PATCH'
ApiUrl: https://smoke-yaml.example.com
PATCH

cat > smoke/.configtransform/Environments/Smoke/configtransform.json <<'LAYER'
{ "resources": [
    { "path": "Project/App.config", "patch": ".configtransform/Environments/Smoke/patch-Project-App.config.xml" },
    { "path": "Project/appsettings.json", "patch": ".configtransform/Environments/Smoke/patch-Project-appsettings.json.json" },
    { "path": "Project/.env", "patch": ".configtransform/Environments/Smoke/patch-Project-.env" },
    { "path": "Project/settings.yaml", "patch": ".configtransform/Environments/Smoke/patch-Project-settings.yaml" }
] }
LAYER

cat > smoke/.configtransform/Clients/SmokeClient/Smoke/configtransform.json <<'LAYER'
{ "extends": ".configtransform/Environments/Smoke/configtransform.json", "resources": [] }
LAYER

echo "--- Explicit --resource, one call per format ---"
( cd smoke && dotnet tool run configtransform -- \
    --resource Project/App.config --client SmokeClient --environment Smoke --dry-run ) \
  | tee /tmp/smoke-xml-output.txt
grep -q 'https://smoke-xml.example.com' /tmp/smoke-xml-output.txt

( cd smoke && dotnet tool run configtransform -- \
    --resource Project/appsettings.json --client SmokeClient --environment Smoke --dry-run ) \
  | tee /tmp/smoke-json-output.txt
grep -q 'https://smoke-json.example.com' /tmp/smoke-json-output.txt

( cd smoke && dotnet tool run configtransform -- \
    --resource Project/.env --client SmokeClient --environment Smoke --dry-run ) \
  | tee /tmp/smoke-env-output.txt
grep -q 'https://smoke-env.example.com' /tmp/smoke-env-output.txt

( cd smoke && dotnet tool run configtransform -- \
    --resource Project/settings.yaml --client SmokeClient --environment Smoke --dry-run ) \
  | tee /tmp/smoke-yaml-output.txt
grep -q 'https://smoke-yaml.example.com' /tmp/smoke-yaml-output.txt

echo "--- Omitting --resource: all four formats in ONE call, no skip note ---"
( cd smoke && dotnet tool run configtransform -- \
    --client SmokeClient --environment Smoke --dry-run ) \
  2>/tmp/smoke-mixed-stderr.txt | tee /tmp/smoke-mixed-output.txt
grep -q 'https://smoke-xml.example.com' /tmp/smoke-mixed-output.txt
grep -q 'https://smoke-json.example.com' /tmp/smoke-mixed-output.txt
grep -q 'https://smoke-env.example.com' /tmp/smoke-mixed-output.txt
grep -q 'https://smoke-yaml.example.com' /tmp/smoke-mixed-output.txt
if [ -s /tmp/smoke-mixed-stderr.txt ]; then
  echo "FAIL: expected no stderr output for a fully-registered mixed-format layer, got:"
  cat /tmp/smoke-mixed-stderr.txt
  exit 1
fi

echo "Smoke test passed: ConfigTransform.Cli $VERSION installs and runs correctly from GitHub Packages, resolving XML, JSON, .env, and YAML resources -- including all four in a single call."
