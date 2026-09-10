# Documentation index

Start at [`CLAUDE.md`](../CLAUDE.md) (repo root) — the fastest orientation, for humans and AI
agents alike. This index is the map for everything beyond that.

| Document | What it's for |
|---|---|
| [`ROADMAP.md`](ROADMAP.md) | **Read this first when resuming work.** The single source of truth for what's planned and what's next — survives across sessions even though conversation context doesn't. |
| [`ONBOARDING.md`](ONBOARDING.md) | **Joining a repo that already uses this tool? Start here.** A strict, linear, copy-paste checklist to get from a fresh clone to a working local setup and a first successful command — no architecture reading required first. |
| [`GETTING_STARTED.md`](GETTING_STARTED.md) | **Setting `config-transform` up in a new project, or want the day-to-day usage?** The layering diagram, setting up a project from scratch, and tasks like adding a field. Assumes local setup (`ONBOARDING.md`/`SECRETS_AND_LOCAL_SETUP.md`) is already done. |
| [`CONFIG_MANAGEMENT.md`](CONFIG_MANAGEMENT.md) | The overall architecture this tool is one piece of: multi-client/multi-environment config resolution, git-crypt encryption, CI/CD, deployment. Read this for *why this tool exists and what problem it solves*. |
| [`CONFIGTRANSFORM_TOOL_DESIGN.md`](CONFIGTRANSFORM_TOOL_DESIGN.md) | This repo's own structure and full test plan. |
| [`FIELD_AUTHORING_DESIGN.md`](FIELD_AUTHORING_DESIGN.md) | Design for the `set` command (mostly implemented — see its status line) that authors overlay fields without hand-writing XDT/JSON — the `--match`/`--set` model, per-format behavior, and the safety rules behind it. |
| [`INIT_COMMAND_DESIGN.md`](INIT_COMMAND_DESIGN.md) | Design and decision log for `configtransform init` (implemented — see `USAGE.md` for the flag reference): scaffolds a `.configtransform/` tree, scanning the repo for candidate resources, via an interactive form or a flag-driven quiet mode, plus a canned `--template` starter. |
| [`MANIFEST_SCHEMA.md`](MANIFEST_SCHEMA.md) | The `configtransform.json` layer schema this tool consumes today — field-by-field reference (filename predates the design; there's no "manifest" left in the tool's own vocabulary). |
| [`HOST_LAYER_DESIGN.md`](HOST_LAYER_DESIGN.md) | **Implemented** (core: `0.19.0-alpha`; the `--template hosts` variant: `0.20.0-alpha`). A third, optional `--host` layer axis under Client/Environment, for load-balanced production servers that need per-box config — read this before assuming `Client`/`Environment` are the only two layer dimensions this tool will ever have. |
| [`SELF_DESCRIBING_OVERLAYS_DESIGN.md`](SELF_DESCRIBING_OVERLAYS_DESIGN.md) | **Implemented.** The design for the self-describing `configtransform.json` per layer directory that replaced the old fixed base→Environments→Clients rule and `manifest.json` entirely — read this for the *why*; `MANIFEST_SCHEMA.md` for the field reference. |
| [`DIFF_LAYERS_DESIGN.md`](DIFF_LAYERS_DESIGN.md) | **Implemented (`0.22.0-alpha`).** `--diff-layers` splits `--diff`'s output into one diff per layer that actually changed the resource, annotating a changed line with which earlier layer it overrides when relevant. |
| [`USAGE.md`](USAGE.md) | CLI reference. |
| [`SECRETS_AND_LOCAL_SETUP.md`](SECRETS_AND_LOCAL_SETUP.md) | What a *consuming* repo needs configured — GitHub Packages feed auth and (if it uses git-crypt) the encryption key — in CI and locally, on every platform. |
| [`CHANGELOG.md`](CHANGELOG.md) | Version history, at a glance — what's *done*, complementing `ROADMAP.md`'s what's *next*. Each version's section also doubles as that release's GitHub Release notes — see `RELEASING.md`. |
| [`RELEASING.md`](RELEASING.md) | How to cut a release: moving `CHANGELOG.md`'s `[Unreleased]` section into a versioned one, tagging, and what `publish.yml` does automatically. |
| [`DOCUMENTATION_POLICY.md`](DOCUMENTATION_POLICY.md) | Why and how documentation is maintained here — read before adding a new doc. |
