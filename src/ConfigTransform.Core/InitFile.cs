namespace ConfigTransform.Core;

/// <summary>
/// One file `configtransform init` will create or update, computed without touching disk
/// (docs/INIT_COMMAND_DESIGN.md) -- what the interactive form's "Confirm and write" step echoes,
/// what `--dry-run` prints instead of writing, and what a real run writes.
/// </summary>
public sealed record InitFile(string FullPath, string RepoRelativePath, string Content);
