# Order- and type-preserving JSON/YAML merge — design

**Status: implemented, unreleased (2026-10-01).** `JsonLayerMerger` and `YamlLayerMerger` merge
each patch into the base document's own tree. They no longer flatten everything through
`Microsoft.Extensions.Configuration` and rebuild it. Item 4 of the output-fidelity pass in
`docs/ROADMAP.md`.

## Why this exists

Both engines used to load the base and every patch into a `ConfigurationBuilder`, then rebuild a
document from the flattened `IConfiguration` keys. That reused the library's layering rules for
free. The cost was everything flattening loses: every value becomes a string and its type has to
be guessed back, and the rebuilt document has no memory of the source's order, spelling or
formatting. Found during a read-through against `config-transform-pilot`, verified on the
`0.22.0-alpha` code:

| In the source | Written by the old merge |
|---|---|
| `"Pin": "007"` | `"Pin": 7` (string → number, leading zeros gone) |
| `"Version": "1.10"` | `"Version": 1.1` |
| `"FlagAsString": "true"` | `"FlagAsString": true` |
| `"Big": 12345678901234567890` | `"Big": 1.2345678901234567E+19` |
| `"Price": 1.50` | `"Price": 1.5` |
| `"Nothing": null` | `"Nothing": ""` |
| `"EmptyObj": {}`, `"EmptyArr": []` | `null`, `null` |
| base `"Alpha"`, patch `"alpha"` | key renamed to `"alpha"` |
| base `"Obj": {"B": 1}`, patch `"obj": 5` | `"obj": {"B": 1}` — the patch's value silently dropped |
| keys in source order | keys sorted alphabetically |
| YAML `Schedule: "0 * * * *"` | `Schedule: '0 * * * *'` (requoted) |
| a JSON *patch* containing `//` comments | merge crashed (the `$elemMatch` pre-scan parsed patches strictly) |

The type changes are real data corruption, not cosmetics: a .NET app reading the deployed file
through `IConfiguration` sees `"7"` where the source said `"007"`. `--diff` and `--diff-layers`
couldn't show any of it, because they render the unpatched side through the same `Merge` call
(`engine.Merge(base, [])`). Both sides were rewritten the same way, so the diff looked clean while
the deployed file had changed.

## Design: merge into the base document's tree

Parse the base, then apply each patch in chain order directly onto that tree. Write out whatever
results. Nothing is flattened, so nothing needs to be reconstructed.

**Kept from `IConfiguration`'s layering, deliberately** — existing overlays rely on these:

- **Objects/maps merge key by key, recursively.** (How keys *match* changed — see "Key matching
  is case-sensitive" below.)
- **Arrays/sequences merge by index.** An overlay array only overrides the indices it specifies;
  trailing base items survive (`JsonLayerMergerTests.An_overlay_array_overrides_by_index_not_wholesale`).
- **An object whose keys are all indices (`{"1": ...}`) addresses single items of an existing
  array.** This is the IConfiguration idiom for overriding one element, and also the shape
  `JsonElemMatchResolver.Rewrite` resolves `$elemMatch` patches to. An index past the end of the
  array is now an error rather than a silently malformed result: it may update an existing item
  or append the next one, but not leave a gap.
- **`$elemMatch` resolves against the document as merged so far.** The rule is unchanged, and
  simpler to implement now: the accumulated tree is simply the current document.
- **JSON input may contain comments and trailing commas**, as `AddJsonFile` allowed — now in
  patches too, not only in the base.

**Changed:**

- **Values keep exactly the type and text they were written with.** JSON numbers keep their
  source text (`1.50`, `1e5`, 20-digit integers). YAML scalars are never interpreted at all, so
  quoting (`"..."`, `'...'`, plain), block scalars (`|`, `>`) and flow collections (`[a, b]`) come
  through as written.
- **`null`, `{}` and `[]` survive.**
- **Key order and spelling come from the base.** A matched key keeps its base spelling and
  position. A new key is appended after the existing ones, in the patch's order.
- **A later layer replaces a value of a different kind outright.** For example, a scalar landing
  on an object, or `null` landing on an array. The old merge always kept the object and dropped
  the scalar, whichever layer came last.
- **Keys differing only by case within one file are allowed.** JSON and YAML are both
  case-sensitive. The YAML engine used to throw on these (a NetEscapades limitation).
- **Key matching across layers is case-sensitive, and a case-only mismatch is an error.** See
  the next section.
- **YAML layout follows the base file.** The indentation width, and whether block sequences are
  indented under their key, are detected from the base's source positions
  (`YamlLayerMerger.DetectLayout`). Long scalars are never re-wrapped.

### Key matching is case-sensitive

Decided by the repo owner, 2026-10-01, as a follow-up to the tree merge (which first kept
`IConfiguration`'s case-insensitive matching). A patch key overrides an existing key only when it
is spelled exactly the same. JSON and YAML are case-sensitive formats, and the tool claims no
coupling to any ecosystem: for a Python or Node consumer (`config-transform-pilot`'s
`ReportingService`/`NotificationWorker`), `logLevel` and `LogLevel` really are different keys, so
treating one as an override of the other was wrong for them.

Plain case-sensitivity on its own would hurt .NET consumers in a different way: a patch's `apiUrl`
against a base `ApiUrl` would be written as a *second* key, and .NET's configuration loader, which
reads keys case-insensitively, refuses to load a file with two keys that differ only by case. The
app would fail at startup in production instead of at build time. So a patch key that matches an
existing key **only by case** stops the merge with an error naming the patch file, the key path
and the existing spelling (`JsonLayerMerger`/`YamlLayerMerger`'s `CaseOnlyMismatch`). That's right
for every ecosystem: nobody means to have two keys differing only by case, and the .NET mistake is
caught in CI.

`set` applies the same rule to its `--match key=` path before writing anything, suggesting the
real spelling (`Try: --match key=Logging:LogLevel:Default`). A field name inside an `$elemMatch`
item isn't checked until merge time, so `set` now merges the new overlay content *before* writing
it (`SetRunner`); a write the merge would reject leaves nothing on disk.

What this refuses: a patch whose casing differs from its base, which used to override quietly
(now: fix the spelling), and intentionally keeping two keys differing only by case *across
layers*. Two such keys *within one file* are still allowed, and a patch can address either of
them by its exact spelling. `.env` was already case-sensitive (`docs/CONFIG_MANAGEMENT.md` §5.5)
with no case-only check, since `FOO` and `foo` are routinely distinct shell variables. XML was
always case-sensitive.

No `Microsoft.Extensions.Configuration` or `NetEscapades.Configuration.Yaml` package is needed any
more. JSON uses `System.Text.Json.Nodes` and YAML uses YamlDotNet's representation model
(`YamlStream`), which the YAML engine already depended on.

## What still isn't preserved

- **Comments.** Neither `System.Text.Json` nor YamlDotNet's representation model keeps them, and
  the old merge dropped them too. Same for `.env` (`docs/CONFIG_MANAGEMENT.md` §5.5).
- **JSON whitespace layout.** Output is always re-indented with two spaces. A one-line array in
  the source is written one item per line.
- **YAML per-level indentation.** One width is detected and used throughout.
- **YAML anchors/aliases** are expanded into independent copies, as before. Merge keys (`<<:`)
  are not interpreted; `<<` merges like any other key.
- **Multi-document YAML** (`---`) is refused with an error; a config file must be one document.

## Rejected alternatives

1. **Keep the `IConfiguration` merge and re-sort the output into base order afterwards.** This
   fixes ordering only. Types, nulls, empty containers and key spelling are still lost through
   string flattening, and those are the actual corruption.
2. **RFC 7386 JSON Merge Patch.** A known standard, but it replaces arrays wholesale and treats
   `null` as "delete this key". Both would silently change what every existing overlay means.
3. **A comment-preserving editor** (e.g. a concrete-syntax-tree library). No maintained .NET
   library round-trips JSON and YAML comments through an editable tree. Out of scope; the old
   behavior dropped comments too.
4. **Keep case-insensitive matching** (`IConfiguration`'s rule, and this design's first
   version). Right for .NET, wrong for every case-sensitive consumer. Replaced by case-sensitive
   matching plus the case-only-mismatch error; see "Key matching is case-sensitive".
5. **Case-sensitive matching with no case-only check.** It would be format-correct, but a .NET
   overlay with a casing typo would deploy a file that .NET refuses to load.
6. **A per-repo or per-resource option for case sensitivity.** More configuration for a choice
   with one safe answer. The error covers the .NET concern without needing a switch.

## Open items

- **`set`'s rewrite of an existing YAML overlay file** still goes through YamlDotNet's high-level
  `Serializer` (`YamlFieldAuthor`). Key order in the overlay file survives, but its quoting style
  doesn't. That's a separate code path from merging, not touched here.
