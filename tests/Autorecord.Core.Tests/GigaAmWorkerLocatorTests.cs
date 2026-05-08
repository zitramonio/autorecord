using Autorecord.Core.Transcription.Engines;

namespace Autorecord.Core.Tests;

public sealed class GigaAmWorkerLocatorTests
{
    [Fact]
    public void ResolveWorkerPathPrefersBundledWorker()
    {
        var root = CreateTempDirectory();
        try
        {
            var appBase = Path.Combine(root, "app");
            var installedRoot = Path.Combine(root, "appdata");
            var bundledWorker = Path.Combine(appBase, "workers", "gigaam", "worker.exe");
            var installedWorker = Path.Combine(installedRoot, "GigaAM", "worker.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(bundledWorker)!);
            Directory.CreateDirectory(Path.GetDirectoryName(installedWorker)!);
            File.WriteAllText(bundledWorker, "bundled");
            File.WriteAllText(installedWorker, "installed");

            var path = GigaAmWorkerLocator.ResolveWorkerPath(appBase, installedRoot);

            Assert.Equal(bundledWorker, path);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveWorkerPathFallsBackToInstalledWorker()
    {
        var root = CreateTempDirectory();
        try
        {
            var appBase = Path.Combine(root, "app");
            var installedRoot = Path.Combine(root, "appdata");
            var installedWorker = Path.Combine(installedRoot, "GigaAM", "worker.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(installedWorker)!);
            File.WriteAllText(installedWorker, "installed");

            var path = GigaAmWorkerLocator.ResolveWorkerPath(appBase, installedRoot);

            Assert.Equal(installedWorker, path);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveWorkerPathReturnsInstalledPathWhenWorkerIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var appBase = Path.Combine(root, "app");
            var installedRoot = Path.Combine(root, "appdata");

            var path = GigaAmWorkerLocator.ResolveWorkerPath(appBase, installedRoot);

            Assert.Equal(Path.Combine(installedRoot, "GigaAM", "worker.exe"), path);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
