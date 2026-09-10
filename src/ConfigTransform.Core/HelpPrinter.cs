namespace ConfigTransform.Core;

/// <summary>
/// Prints the tool's help page — shown for no arguments at all, a leading bare "help", or
/// "--help"/"-h" anywhere (see <see cref="CliOptionsParser"/>). A tldr-style cheat sheet, not a
/// full reference: one quick-scan "common commands" table, then one easy example and one more
/// advanced ("tldr") example per command, deliberately shorter than docs/USAGE.md — that stays
/// the authoritative full reference this page points to.
/// </summary>
public static class HelpPrinter
{
    public static void Print(TextWriter stdout, FormatEngineRegistry engines)
    {
        stdout.WriteLine($$"""
            configtransform — resolve, preview, and write per-client/per-environment config overrides

            Layers self-describe what they touch via .configtransform/**/configtransform.json
            (extends + resources[] — docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md). Every path below is
            repo-root-relative; run from the repo root, same as CI. Supported resource formats right
            now: {{engines.SupportedExtensions}}.

            USAGE
              configtransform [--resource <path>] [--client <C>] [--environment <E>] [--host <H>] [--dry-run | --diff | --diff-layers | --output <path>]
              configtransform --list [--client <C> --environment <E> [--host <H>] | --resource <path>]
              configtransform set --resource <path> [--client <C> --environment <E> [--host <H>]] --match <k>=<v> [--match ...] --set <k>=<v> [--set ...]
              configtransform init [--environment <E> ...] [--client <C> ...] [--host <H> ...] [--resource <path> ...] [--yes] [--dry-run]
              configtransform init --template [hosts] [--dry-run]
              configtransform | help | --help | -h            this page (also shown for no arguments at all)

            COMMON COMMANDS
              Preview a merge                   configtransform -r <path> -c <Client> -e <Environment> --dry-run
              See what changed                  configtransform -r <path> -c <Client> -e <Environment> --diff
              Write the merged file             configtransform -r <path> -c <Client> -e <Environment> -o <outFile>
              Resolve everything in one call    configtransform -c <Client> -e <Environment> -o <outDir>
              Target one load-balanced server   configtransform -c <Client> -e <Environment> -H <Host> --dry-run
              Inspect a layer                   configtransform --list -c <Client> -e <Environment>
              Find every layer patching a file  configtransform --list -r <path>
              Author an override                configtransform set -r <path> -c <Client> -e <Environment> --match <field>=<value> --set <field>=<value>
              Try it with a starter tree         configtransform init --template
              Scaffold a real tree               configtransform init -e Production -e Test -c Acme --yes

            --dry-run — print the fully merged result to stdout; nothing written to disk
              easy:  configtransform -r OrderProcessor.Framework/App.config -c Acme -e Production --dry-run
              tldr:  configtransform -c Acme -e Production --dry-run
                     (omit --resource: every resource this layer touches, any format, one call)
                     add -H <Host>/--host <Host> for one specific load-balanced server, when
                     .configtransform/Clients/<C>/<E>/Hosts/<H>/ exists

            --diff — print a unified diff of unpatched vs. merged; nothing written to disk
              easy:  configtransform -r BillingApi.Core/appsettings.json -c Acme -e Production --diff
              tldr:  configtransform -c Acme -e Production --diff
                     (whole layer's diff, mixed XML/JSON, one call)

            --diff-layers — like --diff, but one diff per layer that actually changes the resource
              easy:  configtransform -r BillingApi.Core/appsettings.json -c Acme -e Production --diff-layers
              tldr:  configtransform -c Acme -e Production -H 10.0.1.11 --diff-layers
                     (each layer's own diff, tagged with which earlier layer it overrides when a
                     later layer re-touches a line -- see docs/DIFF_LAYERS_DESIGN.md)

            --output, -o — write the merged result to disk
              easy:  configtransform -r OrderProcessor.Framework/App.config -c Acme -e Production -o publish/App.config
              tldr:  configtransform -c Acme -e Production -o publish/
                     (every resource, mirrored under the directory — how CI resolves a whole
                     client/environment in one step)

            --list — show what a layer resolves, or find every layer touching one resource
              easy:  configtransform --list -c Acme -e Production
              tldr:  configtransform --list -r OrderProcessor.Framework/App.config
                     (reverse lookup: every configtransform.json anywhere that patches this file)

            set — author an overlay field without hand-writing XDT or nested JSON
              easy:  configtransform set -r OrderProcessor.Framework/App.config -c Acme -e Production --match ApiUrl --set https://acme.example.com
              tldr:  configtransform set -r BillingApi.Core/appsettings.json -c Acme -e Production --match key=Rules --match role=Admin --match env=Production --set enabled=true
                     (JSON array-of-objects: matches or creates the item identified by role+env,
                     via $elemMatch — no array position is ever written; upserts if nothing matches)

            init — scaffold .configtransform/Environments/ and .configtransform/Clients/ trees
              easy:  configtransform init --template
                     (Production/Test x Client-A/Client-B, one demo resource whose value names its
                     own layer — immediately runnable, try --diff -c Client-A -e Production -r configtransform-template.json)
              tldr:  configtransform init -e Production -e Test -c Acme --yes
                     (quiet/CI-safe: scans the repo for more .config/.xml/.json candidates too,
                     unless --resource is given; with no flags at all in a real terminal, asks
                     interactively instead)
                     configtransform init --template hosts adds one worked Hosts/Host-1/ layer
                     under Client-A/Production to the same starter tree

            Full reference — every flag, --list's two modes in full, and exactly what set supports
            per format (and why) — is docs/USAGE.md in the config-transform repo.
            """);
    }
}
