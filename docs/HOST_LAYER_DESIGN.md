# Host layer (`--host`) — design

**Status: PR D (core: resolve/`--dry-run`/`--diff`/`--list`/`set`/`init` scaffolding) is
implemented — see `docs/CHANGELOG.md`'s `[Unreleased]` entry and `docs/USAGE.md` for the current
flag reference. PR E (decision log #7, `--template hosts`) is not yet implemented.** Every
"Changes needed"/"No change needed" claim below was verified against the real code before this
was written, and PR D's implementation follows this design directly — no changes to the design
itself were needed along the way, only the two small precision fixes decision log #6/#7 already
record.

## Why this exists, and why now

Raised directly by the repo owner from a real deployment shape: a Production environment sits
behind a load balancer with several servers, and — unlike every other client/environment
combination this tool handles — those servers sometimes need genuinely different configuration
from each other (e.g. a per-box cache node address), not just a different config from other
environments or other clients.

The workaround already in use elsewhere: fold the server identity into the `Client` name itself,
e.g. `Acme` becomes `Acme-192.168.10.10`, `Acme-192.168.10.11`, `Acme-192.168.10.12`. This works
mechanically — `Client` is just a path segment today (`CLAUDE.md`'s "Self-describing layers") —
but it's a real design smell, not a style nitpick:

- **`Client` stops meaning "client."** `--list`, `--client` filtering, and any future reporting
  by real client now see a pile of fake clients instead of one real one — "how many clients do we
  have" becomes unanswerable from the tool's own model.
- **Combinatorial, not additive.** Every client × every one of its servers is a distinct top-level
  identity, multiplying the `Clients/` tree instead of adding one narrow dimension to it.
- **No natural inheritance from the real client.** `Acme-192.168.10.10` has no built-in way to
  still receive `Acme`'s own client-wide overrides (API keys, feature flags) — that has to be
  wired by hand (e.g. `extends` pointed at `Acme`'s layer directly), which is exactly the kind of
  implicit convention this tool's self-describing-layers design was built to avoid.

## Chosen design: a third, optional layer axis

Add `Host` as a genuine third axis, one more optional hop under the existing Client/Environment
layer — reusing the self-describing `extends` chaining that's already generic to arbitrary depth
(`CLAUDE.md`: "A chain can be deeper than the traditional two hops — `LayerChain`/the merge
engines never assume a fixed depth"). This is not new merge machinery; it's one more layer
directory in the same shape every other layer already uses.

```
.configtransform/
  Environments/
    Production/
      configtransform.json
  Clients/
    Acme/
      Production/
        configtransform.json                 # extends Environments/Production
        Hosts/
          192.168.10.10/
            configtransform.json              # extends Clients/Acme/Production
            patch-App-appsettings.json
          192.168.10.11/
            configtransform.json              # extends Clients/Acme/Production
            patch-App-appsettings.json
```

A host layer's `configtransform.json` is identical in shape to every other layer's — `extends`
plus `resources[]` — it just happens to live one directory deeper and `extends` a Client/
Environment layer instead of an Environment layer. Nothing in `LayerManifest`/`LayerChain`
distinguishes *why* a layer exists; it only ever follows `extends` pointers.

**Optional, not required.** A client with identical config across every Production box never
grows a `Hosts/` folder at all, and nothing about their resolution changes — this mirrors how
`--client` itself is optional today (`LayerPathResolver`already refuses `--client` without
`--environment`; the same shape extends one level further).

## Command shape

```bash
# resolve/apply for one specific box
configtransform --client Acme --environment Production --host 192.168.10.10 --dry-run

# list what a specific host's chain looks like
configtransform --client Acme --environment Production --host 192.168.10.10 --list

# omitting --host still resolves at the Client/Environment layer, exactly as today —
# clients/environments with no per-host variance are completely unaffected
configtransform --client Acme --environment Production --dry-run

# -H is the short alias (capital -- see decision log #2 for why)
configtransform -c Acme -e Production -H 192.168.10.10 --dry-run
```

## What changes in the tool, and what doesn't

**Changes needed:**
- `LayerPathResolver.Resolve` gains an optional `host` parameter: when given, the returned path
  is `.configtransform/Clients/<client>/<environment>/Hosts/<host>/configtransform.json` instead
  of the Client/Environment path. Requires `client`+`environment` also be given — no
  host-without-client layer, the same "requires the level above it" rule `--client` already
  follows for `--environment`.
- `CliOptions`/`CliOptionsParser` gain `--host`/`-H` (see decision log #2 below for the capital-`H`
  choice and its accepted tradeoff against `-h`) plus the matching validation: `--host` requires
  `--client` and `--environment` (mirrors the existing `--client requires --environment` check
  verbatim, one level up). Applies everywhere `--client`/`--environment` already do: a plain
  resolve, `--dry-run`/`--diff`, `--list`, and `set`.
- `HelpPrinter` gains `--host`/`-H` in the flag reference wherever `--client`/`--environment` are
  already listed.
- `init`'s interactive form gains an optional "does this client/environment need per-host
  overrides?" prompt, and its flag-driven quiet mode gains a repeatable `--host <name>` flag
  (paired with a `--client`/`--environment` pair, same shape `InitClients`/`InitEnvironments`/
  `InitResources` already use), so a user can scaffold a `Hosts/<H>/configtransform.json` without
  hand-authoring it — defaulting its `extends` to the matching Client/Environment layer, the same
  default `init`/`set` already apply one level up.
- `CliOptions.Template` changes shape from `bool` to `string?` (`null` = not using `--template`;
  `"default"`/`"hosts"` = which variant), so `--template`'s existing bare/no-value form keeps
  meaning today's minimal canned tree (nothing published about it needs to change) while
  `--template hosts` selects a variant that additionally scaffolds one worked `Hosts/<H>/`
  example under the template's existing client/environment — see decision log #7 below for why
  a value beats a second modifier flag or leaving `--template` untouched.
- `SetTargetResolver.Resolve` needs two real, small changes to write into a `Hosts/` layer —
  verified by reading the actual code, not assumed: (1) its own call to
  `LayerPathResolver.Resolve(root, client, environment)` needs `host` passed through, so
  `set --client Acme --environment Production --host <H> --match ... --set ...` targets the right
  `Hosts/<H>/configtransform.json`; (2) the `defaultExtends` logic (currently: a Client layer's
  default `extends` is computed by calling `LayerPathResolver.Resolve(root, client: null,
  environment)` — "drop one level") needs a third case for a Host target: drop only the host,
  keep client+environment (`LayerPathResolver.Resolve(root, client, environment, host: null)`),
  defaulting a new Host layer to extend its Client/Environment layer. Same *pattern* as today's
  Client→Environment default, one level deeper — not a design blocker — but it doesn't fall out
  for free the way `LayerChain`/`ReverseLookup` did; someone has to write that branch.

**No change needed** (verified against the real code, not assumed):
- `LayerChain.Build`/`ResolveResource`/`PrintChain` — already walk `extends` to arbitrary depth;
  a Host layer is just one more link in the chain they already handle generically.
- `LayerChain.ReverseLookup` (backs `--list --resource`) — already
  `Directory.EnumerateFiles(..., SearchOption.AllDirectories)` over every `configtransform.json`
  under `.configtransform/`, so a `Hosts/` subdirectory is picked up with zero changes.
- `LayerLister` — `ListLayer` only ever takes an already-resolved `targetLayerPath` string (the
  caller builds that path; see `LayerPathResolver`'s real change above) and calls `LayerChain`
  functions on it; `ListReverseLookup` prints `entry.LayerPath` verbatim with no depth-aware
  formatting anywhere. A `Hosts/<H>/` layer's path just prints as-is in either mode.
- Every format engine's `Merge` method — checked the actual signatures, not inferred: all four
  (`XmlLayerMerger`, `JsonLayerMerger`, `EnvLayerMerger`, `YamlLayerMerger`) are exactly
  `Merge(string basePath, IReadOnlyList<string> patchPathsInOrder)`, verbatim. No depth or
  position parameter anywhere to need changing.

## Decision log

1. **Term: `Host`, not `Node`.** `Node` was considered specifically because its first letter
   (`-n`) is unclaimed, avoiding the short-flag collision below entirely — rejected in favor of
   keeping the clearer, more concrete term the problem was described in (a load-balanced
   *server*), accepting the short-alias tradeoff as a result.
2. **Short alias: `-H` (capital), not `-h` and not no alias at all.** `-h` is already `--help` and
   stays there — it's too universal a convention to give up for a rarer, opt-in flag. A same-
   letter-different-case pair is a real, accepted tradeoff, not a free choice: `-H`/`-h` are one
   shift-key apart, and unlike a typo against an *unrecognized* flag (which errors, with a "did
   you mean" suggestion — confirmed empirically: `CliOptionsParser` is already case-sensitive,
   `-E` for `--environment` does not silently match `-e`), a slipped-case typo of `-H` lands on
   `-h`, which is itself always valid — so the failure mode is silently opening the help page
   instead of erroring. Accepted deliberately, in favor of keeping the clearer `Host` term.
3. **Optional, additive axis — not a required concept.** No existing client/environment pair
   changes shape or behavior by this feature existing; only repos that actually declare a
   `Hosts/` layer are affected at all.
4. **Reuses `extends` chaining — no new merge-time mechanism.** The entire feature is "one more
   optional directory level," not a new kind of layer; this is why `LayerChain`, every format
   engine, and `ReverseLookup` need zero changes (see above) — the payoff of the self-describing-
   layers design (`SELF_DESCRIBING_OVERLAYS_DESIGN.md`) doing exactly what it was built for.
5. **Rejected: hyphenated `Client-Host` naming.** See "Why this exists, and why now" above — kept
   here as the explicitly-considered-and-rejected alternative per this repo's decision-log
   convention (`FIELD_AUTHORING_DESIGN.md`/`INIT_COMMAND_DESIGN.md` both keep one).
6. **`init`'s interactive/flag-driven scaffolding gets `--host` support; `--template`'s fixed
   canned tree does not.** `init` (interactive prompts or `--environment`/`--client`/`--resource`
   flags) is a natural, low-cost place to add an opt-in `--host` — it saves hand-authoring the
   `Hosts/` layer's `configtransform.json`. `--template`'s whole design point (`INIT_COMMAND_
   DESIGN.md`) is staying the minimal, immediately-runnable demo with nothing to decide; baking a
   Host example into the one fixed tree would add shape every `--template` user gets, for a
   scenario (multi-host Production) that's genuinely niche.
7. **`--template` becomes a value-taking flag (`--template`/`--template hosts`), not a second
   modifier flag.** Considered and rejected: a separate boolean alongside `--template` (e.g.
   `--template --with-hosts`) — works, but doesn't scale if more variants get requested later
   (each would need its own boolean, each needing its own mutual-exclusivity story against every
   other `init` flag). A named-variant value scales cleanly instead, and — since `--template`
   isn't implemented yet — there's no backward-compatibility cost to changing its shape from
   `bool` to `string?` before it ships. Rejected the option of reusing `--host` as the variant's
   name for the same reason `--host` itself needed a real design pass: it's already claimed
   elsewhere (targeting *one specific* host on a resolve/`--list`/`--dry-run`/`--diff` call), and
   overloading one flag name with two unrelated meanings is exactly the kind of ambiguity this
   design otherwise avoids.

## Open items

- **Sequencing `SetTargetResolver`'s `Hosts/`-layer changes** (see "Changes needed" above for what
  they are — this is now a known, small, real change, not an unverified "maybe zero changes"
  guess). Whether it ships in the same PR as `init`'s scaffolding or as a deliberate follow-up
  (mirroring how YAML's array-of-objects `set` gap shipped after YAML's first version) is an
  implementation-time call, not a design blocker.
- **Pilot validation.** `config-transform-pilot` has no multi-host Production scenario today.
  Adding one (mirroring the `.env`/YAML pattern of adding a dedicated pilot project once a format
  or feature ships) is the natural way to validate this against something more real than this
  document's own worked example.
- **Exact validation-error wording** for `--host` given without `--client`/`--environment`, and
  for `--host` given to `init`/`set` before those commands decide their own scaffolding story
  above — left for implementation time, following this repo's existing `Try:`-suffixed error
  convention (`CliOptionsParser`'s existing messages).
