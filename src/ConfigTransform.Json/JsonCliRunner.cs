using ConfigTransform.Core;

namespace ConfigTransform.Json;

/// <summary>
/// The ConfigTransform.Json entry point, factored out of Program.cs so it can be exercised
/// directly in tests without spawning a subprocess. Delegates the shared orchestration to
/// <see cref="CliRunner"/>, supplying <see cref="JsonLayerMerger.Merge"/> as the merge engine.
/// </summary>
public static class JsonCliRunner
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        CliRunner.Run(args, stdout, stderr, JsonLayerMerger.Merge);
}
