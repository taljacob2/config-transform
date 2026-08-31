namespace ConfigTransform.Core.Tests.TestSupport;

/// <summary>Creates an isolated temp directory for a test and deletes it on disposal.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("configtransform-tests-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
