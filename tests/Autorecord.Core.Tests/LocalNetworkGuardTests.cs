using Autorecord.Core.Transcription.Pipeline;

namespace Autorecord.Core.Tests;

public sealed class LocalNetworkGuardTests
{
    [Fact]
    public void AssertTranscriptionRuntimeIsOfflineAllowsCurrentCoreTranscriptionTypes()
    {
        LocalNetworkGuard.AssertTranscriptionRuntimeIsOffline();
    }

    [Fact]
    public void FindSourceViolationsAllowsOnlyModelDownloadServiceInCurrentTranscriptionSource()
    {
        var transcriptionSourceRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Autorecord.Core",
            "Transcription");

        var violations = LocalNetworkGuard.FindSourceViolations(transcriptionSourceRoot);

        Assert.Empty(violations);
    }

    [Fact]
    public void FindViolationsReportsHttpClientOutsideDownloadService()
    {
        var violations = LocalNetworkGuard.FindViolations([typeof(FakeNetworkedTranscriptionComponent)]);

        Assert.Contains(violations, violation =>
            violation.Contains(nameof(FakeNetworkedTranscriptionComponent), StringComparison.Ordinal) &&
            violation.Contains(nameof(HttpClient), StringComparison.Ordinal));
    }

    [Fact]
    public void FindViolationsReportsHttpClientCreatedInsideMethodBody()
    {
        var violations = LocalNetworkGuard.FindViolations([typeof(FakeHiddenNetworkCallComponent)]);

        Assert.Contains(violations, violation =>
            violation.Contains(nameof(FakeHiddenNetworkCallComponent), StringComparison.Ordinal) &&
            violation.Contains(nameof(FakeHiddenNetworkCallComponent.Run), StringComparison.Ordinal) &&
            violation.Contains(nameof(HttpClient), StringComparison.Ordinal));
    }

    [Fact]
    public void FindViolationsReportsHttpClientCreatedInsidePropertyAccessor()
    {
        var violations = LocalNetworkGuard.FindViolations([typeof(FakeNetworkedAccessorComponent)]);

        Assert.Contains(violations, violation =>
            violation.Contains(nameof(FakeNetworkedAccessorComponent), StringComparison.Ordinal) &&
            violation.Contains("NetworkName", StringComparison.Ordinal) &&
            violation.Contains(nameof(HttpClient), StringComparison.Ordinal));
    }

    [Fact]
    public void FindViolationsReportsForbiddenTypeInAnyGenericArgument()
    {
        var violations = LocalNetworkGuard.FindViolations([typeof(FakeGenericNetworkDependencyComponent)]);

        Assert.Contains(violations, violation =>
            violation.Contains(nameof(FakeGenericNetworkDependencyComponent), StringComparison.Ordinal) &&
            violation.Contains(nameof(HttpClient), StringComparison.Ordinal));
    }

    [Fact]
    public void FindViolationsReportsDnsUsageInsideMethodBody()
    {
        var violations = LocalNetworkGuard.FindViolations([typeof(FakeDnsNetworkCallComponent)]);

        Assert.Contains(violations, violation =>
            violation.Contains(nameof(FakeDnsNetworkCallComponent), StringComparison.Ordinal) &&
            violation.Contains(nameof(FakeDnsNetworkCallComponent.Resolve), StringComparison.Ordinal) &&
            violation.Contains("System.Net.Dns", StringComparison.Ordinal));
    }

    [Fact]
    public void FindViolationsReportsHttpClientCreatedInsideStaticConstructor()
    {
        var violations = LocalNetworkGuard.FindViolations([typeof(FakeStaticConstructorNetworkCallComponent)]);

        Assert.Contains(violations, violation =>
            violation.Contains(nameof(FakeStaticConstructorNetworkCallComponent), StringComparison.Ordinal) &&
            violation.Contains(".cctor", StringComparison.Ordinal) &&
            violation.Contains(nameof(HttpClient), StringComparison.Ordinal));
    }

    [Fact]
    public void FindSourceViolationsReportsHiddenNetworkUsageOutsideDownloadBoundary()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Engines"));
        Directory.CreateDirectory(Path.Combine(root, "Models"));
        Directory.CreateDirectory(Path.Combine(root, "Pipeline"));
        File.WriteAllText(
            Path.Combine(root, "Engines", "BadEngine.cs"),
            "public sealed class BadEngine { public void Run() { using var client = new WebClient(); } }");
        File.WriteAllText(
            Path.Combine(root, "Models", "ModelDownloadService.cs"),
            "public sealed class ModelDownloadService { private readonly HttpClient _httpClient; }");
        File.WriteAllText(
            Path.Combine(root, "Engines", "ModelDownloadService.cs"),
            "public sealed class ModelDownloadService { private readonly HttpClient _httpClient; }");
        File.WriteAllText(
            Path.Combine(root, "Pipeline", "LocalNetworkGuard.cs"),
            "public static class LocalNetworkGuard { private const string Token = \"HttpClient\"; }");

        var violations = LocalNetworkGuard.FindSourceViolations(root);

        Assert.Contains(violations, violation =>
            violation.Contains("BadEngine.cs", StringComparison.Ordinal) &&
            violation.Contains("WebClient", StringComparison.Ordinal));
        Assert.Contains(violations, violation =>
            violation.Contains(Path.Combine("Engines", "ModelDownloadService.cs"), StringComparison.Ordinal) &&
            violation.Contains(nameof(HttpClient), StringComparison.Ordinal));
        Assert.DoesNotContain(violations, violation =>
            violation.Contains(Path.Combine("Models", "ModelDownloadService.cs"), StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Autorecord.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class FakeNetworkedTranscriptionComponent
    {
        public FakeNetworkedTranscriptionComponent(HttpClient httpClient)
        {
            HttpClient = httpClient;
        }

        public HttpClient HttpClient { get; }
    }

    private sealed class FakeHiddenNetworkCallComponent
    {
        public string Run()
        {
            using var httpClient = new HttpClient();
            return httpClient.GetType().Name;
        }
    }

    private sealed class FakeNetworkedAccessorComponent
    {
        public string NetworkName
        {
            get
            {
                using var httpClient = new HttpClient();
                return httpClient.GetType().Name;
            }
        }
    }

    private sealed class FakeGenericNetworkDependencyComponent
    {
        public Dictionary<string, HttpClient> Clients { get; } = [];
    }

    private sealed class FakeDnsNetworkCallComponent
    {
        public string Resolve()
        {
            return System.Net.Dns.GetHostName();
        }
    }

    private sealed class FakeStaticConstructorNetworkCallComponent
    {
        private static readonly string Name;

        static FakeStaticConstructorNetworkCallComponent()
        {
            using var httpClient = new HttpClient();
            Name = httpClient.GetType().Name;
        }

        public string GetName() => Name;
    }
}
