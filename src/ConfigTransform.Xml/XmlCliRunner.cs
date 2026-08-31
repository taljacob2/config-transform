using ConfigTransform.Core;

namespace ConfigTransform.Xml;

/// <summary>
/// The ConfigTransform.Xml entry point, factored out of Program.cs so it can be exercised
/// directly in tests without spawning a subprocess. Delegates the shared orchestration to
/// <see cref="CliRunner"/>, supplying <see cref="XmlLayerMerger.Merge"/> as the merge engine.
/// </summary>
public static class XmlCliRunner
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        CliRunner.Run(args, stdout, stderr, XmlLayerMerger.Merge);
}
