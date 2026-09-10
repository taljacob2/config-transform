# Host layer (`--host`) — design

**Status: not implemented — design only.** Nothing below exists in code yet; this document
captures the agreed shape so implementation can start from a settled design instead of guessing,
the same role `FIELD_AUTHORING_DESIGN.md` and `INIT_COMMAND_DESIGN.md` played before their
features were built.

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
```

## What changes in the tool, and what doesn't

**Changes needed:**
- `LayerPathResolver.Resolve` gains an optional `host` parameter: when given, the returned path
  is `.configtransform/Clients/<client>/<environment>/Hosts/<host>/configtransform.json` instead
  of the Client/Environment path. Requires `client`+`environment` also be given — no
  host-without-client layer, the same "requires the level above it" rule `--client` already
  follows for `--environment`.
- `CliOptions`/`CliOptionsParser` gain `--host` (long form only — see decision log below for why
  there's no short alias) plus the matching validation: `--host` requires `--client` and
  `--environment` (mirrors the existing `--client requires --environment` check verbatim, one
  level up). Applies everywhere `--client`/`--environment` already do: a plain resolve,
  `--dry-run`/`--diff`, `--list`, and `set`.
- `HelpPrinter` gains `--host` in the flag reference wherever `--client`/`--environment` are
  already listed.
- `set`/`init` need to decide how (or whether, for a first version) they scaffold a *new* `Hosts/`
  layer — see "Open items" below; this is real, separable scope, not a blocker for the read path.

**No change needed** (verified against the real code, not assumed):
- `LayerChain.Build`/`ResolveResource`/`PrintChain` — already walk `extends` to arbitrary depth;
  a Host layer is just one more link in the chain they already handle generically.
- `LayerChain.ReverseLookup` (backs `--list --resource`) — already
  `Directory.EnumerateFiles(..., SearchOption.AllDirectories)` over every `configtransform.json`
  under `.configtransform/`, so a `Hosts/` subdirectory is picked up with zero changes.
- Every format engine (`XmlLayerMerger`/`JsonLayerMerger`/`EnvLayerMerger`/`YamlLayerMerger`) —
  they only ever see an ordered list of patch paths, never a layer's position in the tree.

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

## Open items

- **`set`/`init` scaffolding a `Hosts/` layer.** Both commands already default a new Client
  layer's `extends` to its matching Environment layer on first write — the same default needs a
  decision for a new Host layer's `extends` (the matching Client/Environment layer). Whether this
  ships in the same PR as the read-path (`--host` targeting a resolve/`--list`/`--dry-run`/
  `--diff`) or as a deliberate follow-up (mirroring how YAML's array-of-objects `set` gap was
  shipped after YAML's first version) is an implementation-time call, not a design blocker.
- **Pilot validation.** `config-transform-pilot` has no multi-host Production scenario today.
  Adding one (mirroring the `.env`/YAML pattern of adding a dedicated pilot project once a format
  or feature ships) is the natural way to validate this against something more real than this
  document's own worked example.
- **Exact validation-error wording** for `--host` given without `--client`/`--environment`, and
  for `--host` given to `init`/`set` before those commands decide their own scaffolding story
  above — left for implementation time, following this repo's existing `Try:`-suffixed error
  convention (`CliOptionsParser`'s existing messages).
