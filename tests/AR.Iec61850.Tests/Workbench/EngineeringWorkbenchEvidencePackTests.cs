using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Workbench;

public sealed class EngineeringWorkbenchEvidencePackTests
{
    [Fact]
    public async Task EvidencePack_Writes_Manifest_And_Profile_Artifacts()
    {
        var output = Path.Combine(Path.GetTempPath(), "ariec61850-workbench-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pack = await new EngineeringWorkbenchEvidencePackBuilder().RunAsync(new EngineeringWorkbenchEvidencePackOptions
            {
                SclPath = MinimalStationPath(),
                OutputFolder = output,
                IncludePublicAlphaReadiness = true,
                ProbeTimeoutMilliseconds = 5000,
                SimulationSteps = 4
            });

            Assert.True(pack.IsComplete, string.Join("; ", pack.Findings.Select(f => $"{f.Code}:{f.Message}")));
            Assert.True(pack.ArtifactCount >= 12);
            Assert.True(File.Exists(Path.Combine(output, "README.md")));
            Assert.True(File.Exists(Path.Combine(output, "manifest.json")));
            Assert.Contains(pack.Artifacts, x => x.RelativePath == "profiles/scl-engineering-profile.md" && x.SizeBytes > 0 && x.Sha256.Length == 64);
            Assert.Contains(pack.Artifacts, x => x.RelativePath == "profiles/mms-readonly-loopback-profile.json" && x.SizeBytes > 0);
            Assert.Contains(pack.Artifacts, x => x.RelativePath == "profiles/public-alpha-readiness-profile.md" && x.SizeBytes > 0);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public async Task ToMarkdown_Renders_Evidence_Pack_Index()
    {
        var output = Path.Combine(Path.GetTempPath(), "ariec61850-workbench-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pack = await new EngineeringWorkbenchEvidencePackBuilder().RunAsync(new EngineeringWorkbenchEvidencePackOptions
            {
                SclPath = MinimalStationPath(),
                OutputFolder = output,
                IncludePublicAlphaReadiness = false,
                ProbeTimeoutMilliseconds = 5000
            });

            var markdown = pack.ToMarkdown();

            Assert.Contains("Engineering Workbench Evidence Pack", markdown);
            Assert.Contains("Artifact Index", markdown);
            Assert.Contains("Scope Boundary", markdown);
            Assert.Contains("profiles/scl-engineering-profile.md", markdown);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    private static string MinimalStationPath()
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scl", "minimal-station.scd");
}
