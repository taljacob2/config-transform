#!/usr/bin/env bash
# Verifies a just-published ConfigTransform.* version is actually installable from GitHub
# Packages and runs correctly -- not just that `dotnet pack`/`dotnet build` succeeded.
# Packaging bugs (a missing PackAsTool setting, a wrong ToolCommandName, a missing dependency
# in the .nupkg) are invisible to the regular build/test steps and only show up when a real
# consumer installs the published package. Run by publish.yml immediately after
# `dotnet nuget push`; can also be run manually against a real feed to verify a release.
#
# Builds a real 2-layer configtransform.json chain (Environment -> Client) and asserts the
# merged output on stdout, so this exercises extends/resources/patch resolution end to end,
# not just "the binary starts and parses its args."
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

dotnet tool install --local ConfigTransform.Xml --version "$VERSION"
dotnet tool install --local ConfigTransform.Json --version "$VERSION"

mkdir -p smoke-xml/Project \
         smoke-xml/.configtransform/Environments/Smoke \
         smoke-xml/.configtransform/Clients/SmokeClient/Smoke

cat > smoke-xml/Project/App.config <<'CONFIG'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <add key="ApiUrl" value="https://dev.example.com" />
  </appSettings>
</configuration>
CONFIG

cat > smoke-xml/.configtransform/Environments/Smoke/patch-Project-App.config.xml <<'PATCH'
<?xml version="1.0" encoding="utf-8"?>
<configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
  <appSettings>
    <add key="ApiUrl" value="https://smoke.example.com"
         xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
  </appSettings>
</configuration>
PATCH

cat > smoke-xml/.configtransform/Environments/Smoke/configtransform.json <<'LAYER'
{ "resources": [ { "path": "Project/App.config", "patch": ".configtransform/Environments/Smoke/patch-Project-App.config.xml" } ] }
LAYER

cat > smoke-xml/.configtransform/Clients/SmokeClient/Smoke/configtransform.json <<'LAYER'
{ "extends": ".configtransform/Environments/Smoke/configtransform.json", "resources": [] }
LAYER

( cd smoke-xml && dotnet tool run configtransform-xml -- \
    --resource Project/App.config --client SmokeClient --environment Smoke --dry-run ) \
  | tee /tmp/smoke-xml-output.txt
grep -q 'https://smoke.example.com' /tmp/smoke-xml-output.txt
echo "Smoke test passed: ConfigTransform.Xml $VERSION installs and runs correctly from GitHub Packages."

mkdir -p smoke-json/Project \
         smoke-json/.configtransform/Environments/Smoke \
         smoke-json/.configtransform/Clients/SmokeClient/Smoke

cat > smoke-json/Project/appsettings.json <<'CONFIG'
{ "ApiUrl": "https://dev.example.com" }
CONFIG

cat > smoke-json/.configtransform/Environments/Smoke/patch-Project-appsettings.json.json <<'PATCH'
{ "ApiUrl": "https://smoke.example.com" }
PATCH

cat > smoke-json/.configtransform/Environments/Smoke/configtransform.json <<'LAYER'
{ "resources": [ { "path": "Project/appsettings.json", "patch": ".configtransform/Environments/Smoke/patch-Project-appsettings.json.json" } ] }
LAYER

cat > smoke-json/.configtransform/Clients/SmokeClient/Smoke/configtransform.json <<'LAYER'
{ "extends": ".configtransform/Environments/Smoke/configtransform.json", "resources": [] }
LAYER

( cd smoke-json && dotnet tool run configtransform-json -- \
    --resource Project/appsettings.json --client SmokeClient --environment Smoke --dry-run ) \
  | tee /tmp/smoke-json-output.txt
grep -q 'https://smoke.example.com' /tmp/smoke-json-output.txt
echo "Smoke test passed: ConfigTransform.Json $VERSION installs and runs correctly from GitHub Packages."
