# Documentation index

Start at [`CLAUDE.md`](../CLAUDE.md) (repo root) — the fastest orientation, for humans and AI
agents alike. This index is the map for everything beyond that.

| Document | What it's for |
|---|---|
| [`ROADMAP.md`](ROADMAP.md) | **Read this first when resuming work.** The single source of truth for what's planned and what's next — survives across sessions even though conversation context doesn't. |
| [`GETTING_STARTED.md`](GETTING_STARTED.md) | **Start here if you just want to use this tool**, not understand the whole architecture first: the layering diagram, setting up a project from scratch, and day-to-day tasks like adding a field. |
| [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) | The overall architecture this tool is one piece of: multi-client/multi-environment config resolution, git-crypt encryption, CI/CD, deployment. Read this for *why this tool exists and what problem it solves*. |
| [`CONFIGTRANSFORM_TOOL_DESIGN.md`](CONFIGTRANSFORM_TOOL_DESIGN.md) | This repo's own structure and full test plan. |
| [`MANIFEST_SCHEMA.md`](MANIFEST_SCHEMA.md) | The `manifest.json` input contract this tool consumes. |
| [`USAGE.md`](USAGE.md) | CLI reference. |
| [`SECRETS_AND_LOCAL_SETUP.md`](SECRETS_AND_LOCAL_SETUP.md) | What a *consuming* repo needs configured — GitHub Packages feed auth and (if it uses git-crypt) the encryption key — in CI and locally, on every platform. |
| [`CHANGELOG.md`](CHANGELOG.md) | Version history, at a glance — what's *done*, complementing `ROADMAP.md`'s what's *next*. Each version's section also doubles as that release's GitHub Release notes — see `RELEASING.md`. |
| [`RELEASING.md`](RELEASING.md) | How to cut a release: moving `CHANGELOG.md`'s `[Unreleased]` section into a versioned one, tagging, and what `publish.yml` does automatically. |
| [`DOCUMENTATION_POLICY.md`](DOCUMENTATION_POLICY.md) | Why and how documentation is maintained here — read before adding a new doc. |
