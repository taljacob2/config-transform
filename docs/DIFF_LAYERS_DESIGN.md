# Per-layer diff attribution (`--diff-layers`) — design

**Status: design only — not implemented.** Nothing in this document is real code yet; every
"Changes needed"/"No change needed" claim below was checked against the real code
(`GitDiff.cs`, `LayerChain.cs`, `CliRunner.cs`, `FormatEngine.cs`) as it stands today, the same
verify-before-writing-it-down convention `HOST_LAYER_DESIGN.md` and `FIELD_AUTHORING_DESIGN.md`
both follow.

## Why this exists, and why now

Raised directly by the repo owner while looking at a real `--diff` output on a chain three or
four layers deep (`base → Environment → Client → Host`): today's `--diff` (`GitDiff.Render`,
`CliRunner.RunOneResource`/`RunEveryResource`) always diffs `base` straight against the final
merged result. That's the right default — it answers "what does this box actually get" — but it
throws away a real, useful fact the tool already computes in the process of resolving the chain:
*which* layer is responsible for *which* change. A user staring at a changed line has no way to
tell, from the diff alone, whether it came from the Environment layer, the Client layer, or one
specific Host override, short of re-running `--list` and reasoning it out by hand.

## Chosen design: an opt-in `--diff-layers` flag, not a change to `--diff`

`--diff` stays exactly as it is today — byte-for-byte, no behavior change, zero risk to any
script or workflow already depending on its output. `--diff-layers` is a new, separate flag that
replaces the single base-vs-merged diff with one diff *per layer that actually patches the
resource*, each one diffing "the content after every earlier layer" against "the content after
this layer too" — so each hunk shows exactly what one layer's patch changed, not the cumulative
result of every layer before it.

```
$ configtransform --client Acme --environment Production --host 10.0.1.11 --diff-layers --resource Web/AdminPortal.Web/Web.config

Resolving 'Web/AdminPortal.Web/Web.config'
    base
      Web/AdminPortal.Web/Web.config
      ↓
    Environments/Production/configtransform.json
      patched in: .configtransform/Environments/Production/patch-Web-AdminPortal.Web-Web.config.xml
      ↓
    Clients/Acme/Production/configtransform.json
      patched in: .configtransform/Clients/Acme/Production/patch-Web-AdminPortal.Web-Web.config.xml
      ↓
    Clients/Acme/Production/Hosts/10.0.1.11/configtransform.json
      patched in: .configtransform/Clients/Acme/Production/Hosts/10.0.1.11/patch-Web-AdminPortal.Web-Web.config.xml

[Environments/Production/configtransform.json]
-  <add key="Timeout" value="30" />
+  <add key="Timeout" value="60" />

[Clients/Acme/Production/configtransform.json overrides Environments/Production/configtransform.json]
-  <add key="Timeout" value="60" />
+  <add key="Timeout" value="90" />

[Clients/Acme/Production/Hosts/10.0.1.11/configtransform.json]
-  <add key="CacheNode" value="redis-a" />
+  <add key="CacheNode" value="redis-a.internal:6379" />
```

The existing resolution report (`LayerChain.PrintChain`, already printed before every
`--dry-run`/`--diff`/real run) is unchanged and still prints first — `--diff-layers` only changes
what comes after it. A layer that's part of the chain but declares no patch for this resource (a
`not patched in` step) contributes no section at all, the same way it contributes nothing to
today's `--diff`.

### The "overrides" annotation

A bracket header names the layer that owns the diff below it. When every changed line in that
diff was previously untouched (its content traces straight back to `base`, or the line is
genuinely new), the header carries no "overrides" clause — see the first hunk above. When the
layer changes a line some *earlier* layer in the chain had already changed, the header for that
hunk names the layer it's overriding — see the second hunk above (`Timeout` was first set by
Environment, then re-set by Client).

**Mixed-owner hunks.** A single hunk can contain multiple changed lines with *different* prior
owners (e.g. one line last touched by Environment, another by Client, both re-touched by the same
Host patch). A single header tag would misattribute one of them, so in that case the header names
only the current layer, and each changed line whose prior owner differs from "the layer the
header already names" gets its own trailing `(overrides <label>)` note:

```
[Clients/Acme/Production/Hosts/10.0.1.11/configtransform.json]
-  <add key="CacheNode" value="redis-a" />                    (overrides Clients/Acme/Production/configtransform.json)
-  <add key="Region" value="us-east" />                       (overrides Environments/Production/configtransform.json)
+  <add key="CacheNode" value="redis-a.internal:6379" />
+  <add key="Region" value="us-east-1" />
```

A changed line with no prior owner (new in this layer, never present before it) gets no note
either way — there is nothing to override.

## How this is computed — no format-engine changes needed

The key fact that makes this cheap: `LayerMerge` (`FormatEngine.cs`) is already
`Merge(string basePath, IReadOnlyList<string> patchPathsInOrder)` — it takes the *whole* ordered
patch list as a plain list, not some opaque chain object. Nothing stops calling it with a
**prefix** of that list. So the "content after every earlier layer" needed for each hop's diff is
just:

```csharp
var merged = new List<string> { engine.Merge(resolved.BasePath, []) };  // index 0 = base only
foreach (var patchPath in resolved.PatchPathsInOrder)
    merged.Add(engine.Merge(resolved.BasePath, appliedSoFar.Append(patchPath).ToList()));
```

— one `Merge` call per patched layer (already an `O(layers)` cost the tool pays anyway for
`--list`/the resolution report), computed entirely in `CliRunner`/a new small helper in Core.
**None of the four merge engines (`XmlLayerMerger`, `JsonLayerMerger`, `EnvLayerMerger`,
`YamlLayerMerger`) need to change at all** — this reuses the exact same generic entry point every
format already implements, the same "no change needed" payoff `HOST_LAYER_DESIGN.md` found for
`LayerChain`/`ReverseLookup`/`Merge` itself.

`resolved.Steps` (`ResolvedResource.Steps`, already built by `LayerChain.ResolveResource`) already
gives the ordered label + patch-path pairs needed to know which layers have a patch to diff at
all — a step with `PatchPath: null` is skipped, exactly as described above.

### Attribution algorithm (per resource, per adjacent pair of merged states)

For each consecutive pair `(previous, current)` in the list above (one pair per patched layer):

1. Run `GitDiff.Render(previous, current)` exactly as `--diff` already does — same unified-diff
   text, same meta-line stripping. This is the one part of the whole feature that's genuinely new
   work, everything else above is reuse.
2. Parse the hunk headers (`@@ -oldStart,oldCount +newStart,newCount @@`) already present in that
   output to walk old-line-position → new-line-position: a context line (unchanged, printed with a
   leading space) carries its owner straight across; a removed line's owner is looked up in the
   running owner map (defaulting to "base," i.e. no note, if absent) and offered as the
   "overrides" candidate for whichever added line replaces it; an added line's owner becomes this
   step's label. Lines never shown by git (outside every hunk's context window, i.e. genuinely
   unchanged for this hop) keep their existing owner, shifted by the cumulative old→new offset
   already implied by the hunks processed so far — no new content ever needs to be read to know
   this, since those lines are guaranteed identical between `previous` and `current`.
3. The owner map produced by this hop becomes the *input* owner map for the next hop (the next
   pair's "previous" content is this hop's "current" content, line-for-line).
4. Render the hunk text as `GitDiff` already renders it, plus the header-tag/inline-note logic
   from "The overrides annotation" above, driven by the owner lookups from step 2.

This is a bounded, self-contained pass over already-computed diff text — no new diff engine, no
change to `GitDiff.Render`'s own output format for plain `--diff`. It belongs as its own small
type in Core (working name: `LayerDiffAttribution`), mirroring `InsertWhitespaceFormatter`'s own
precedent in `ConfigTransform.Xml` for "a bounded, single-purpose helper with its own focused
test file" rather than folding this logic into `CliRunner` directly.

## What changes in the tool, and what doesn't

**Changes needed:**
- `CliOptions` gains `bool DiffLayers`.
- `CliOptionsParser` parses `--diff-layers`; validates it's not combined with `--diff` on the same
  invocation (mirrors this repo's existing flag-exclusivity checks, e.g. `--list --resource`'s
  exclusivity against `--client`/`--environment`/`--host`), with a `Try:` message naming the
  other flag.
- A new `LayerDiffAttribution` type in `ConfigTransform.Core` implementing the algorithm above.
- `CliRunner.RunOneResource`/`RunEveryResource` gain a `DiffLayers` branch parallel to the existing
  `Diff` branch, calling the new helper instead of a single `GitDiff.Render`.
- `HelpPrinter` gains a `--diff-layers` mention wherever `--diff` is already listed.

**No change needed** (verified against the real code, not assumed):
- Every format engine's `Merge` signature — already exactly what's needed, see above.
- `GitDiff.Render` itself — reused as-is, per hop; its own output format for plain `--diff` is
  untouched.
- `LayerChain`/`ResolvedResource`/`ChainStep` — `Steps`/`PatchPathsInOrder` already carry
  everything the attribution pass needs; no new fields.
- `LayerLister`/`--list` — entirely unaffected; this is a `--diff`-family feature only.

## Decision log

1. **A separate opt-in flag (`--diff-layers`), not a modifier on `--diff`.** Considered and
   rejected: `--diff --show-layers` (a flag that's meaningless without its parent) — a single
   flag name reads clearer and needs no "used without --diff" validation case at all. Keeping
   plain `--diff` completely unchanged means zero migration cost for anything already parsing or
   scripting against its output.
2. **N incremental `Merge` calls, not new provenance-tracking inside the merge engines.** The
   whole feature is buildable without touching `XmlLayerMerger`/`JsonLayerMerger`/`EnvLayerMerger`/
   `YamlLayerMerger` at all, because `LayerMerge` already accepts an arbitrary patch-list prefix —
   see "How this is computed" above. Rejected the alternative of having each engine report which
   nodes/keys it touched during `Apply` (the same reference-identity technique
   `InsertWhitespaceFormatter` uses internally) — strictly more powerful in principle, but it
   would need four separate, format-specific implementations instead of one format-agnostic pass
   over already-rendered diff text, for no attribution benefit this design doc's examples need.
3. **Hunk-level tag by default, line-level note only on a genuine mixed-owner hunk.** A tag per
   changed line unconditionally would be correct but noisy for the common case (a whole hunk's
   changes share one prior owner, or none). Named explicitly in "The overrides annotation" above.
4. **Reuses `GitDiff.Render`'s existing unified-diff text and hunk-header format, rather than a
   new diff engine.** `git diff --no-index`'s hunk headers already carry the exact old/new
   line-position bookkeeping the attribution pass needs; writing a second, bespoke line-diff
   implementation just to get structured line mappings would duplicate logic `GitDiff` already
   has for free.

## Open items

- **`--diff-layers` for the whole-layer run (`--resource` omitted)** — presumably the same
  `=== <path> ===`-wrapped treatment `--diff` already gets in `RunEveryResource`, one N-section
  block per resource instead of one diff per resource. Not a design blocker, just needs stating
  explicitly at implementation time.
- **Multi-hunk verification.** Every worked example in this document is a single-hunk change per
  layer. The owner-map carry-forward across *multiple* hunks in the same hop (step 2's "shift by
  cumulative offset" case) needs a real fixture with two separate, non-adjacent changes in one
  patch before this is implemented with confidence, not just assumed correct from the algorithm
  description.
- **Exact flag-exclusivity error wording** for `--diff-layers --diff` together — left for
  implementation time, following this repo's existing `Try:`-suffixed error convention.
- **Naming**: `LayerDiffAttribution` is a working name only, not settled.
