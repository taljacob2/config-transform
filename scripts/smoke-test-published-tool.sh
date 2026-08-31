#!/usr/bin/env bash
# Verifies a just-published ConfigTransform.* version is actually installable from GitHub
# Packages and runs correctly -- not just that `dotnet pack`/`dotnet build` succeeded.
# Packaging bugs (a missing PackAsTool setting, a wrong ToolCommandName, a missing dependency
# in the .nupkg) are invisible to the regular build/test steps and only show up when a real
# consumer installs the published package. Run by publish.yml immediately after
# `dotnet nuget push`; can also be run manually against a real feed to verify a release.
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

mkdir -p smoke-xml/Project
cat > smoke-xml/Project/App.config <<'CONFIG'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <add key="ApiUrl" value="https://dev.example.com" />
  </appSettings>
</configuration>
CONFIG
cat > smoke-xml/manifest.json <<MANIFEST
{ "directory": "$WORKDIR/smoke-xml/Project", "files": [ { "relativeToDirectory": "App.config", "type": "xml" } ] }
MANIFEST

dotnet tool run configtransform-xml -- \
  --manifest smoke-xml/manifest.json --client SmokeClient --environment Smoke --dry-run
echo "Smoke test passed: ConfigTransform.Xml $VERSION installs and runs correctly from GitHub Packages."

mkdir -p smoke-json/Project
cat > smoke-json/Project/appsettings.json <<'CONFIG'
{ "ApiUrl": "https://dev.example.com" }
CONFIG
cat > smoke-json/manifest.json <<MANIFEST
{ "directory": "$WORKDIR/smoke-json/Project", "files": [ { "relativeToDirectory": "appsettings.json", "type": "json" } ] }
MANIFEST

dotnet tool run configtransform-json -- \
  --manifest smoke-json/manifest.json --client SmokeClient --environment Smoke --dry-run
echo "Smoke test passed: ConfigTransform.Json $VERSION installs and runs correctly from GitHub Packages."
