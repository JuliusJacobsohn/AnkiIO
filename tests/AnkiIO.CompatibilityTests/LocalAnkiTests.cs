using System.Diagnostics;
using Xunit;

namespace AnkiIO.CompatibilityTests;

public sealed class LocalAnkiTests
{
    [Fact]
    [Trait("Category", "LocalAnkiCompatibility")]
    public void DetectsAnkiOnlyWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ANKI_LOCAL_COMPATIBILITY"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var installation = AnkiInstallationDetector.Detect();
        Assert.NotNull(installation);
        Assert.True(Path.IsPathFullyQualified(installation.ExecutablePath));
        Assert.True(installation.Version > new Version(0, 0));
    }

    [Fact]
    [Trait("Category", "LocalAnkiCompatibility")]
    public async Task InstalledBackendAcceptsSyntheticPackageInIsolatedCollection()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ANKI_LOCAL_COMPATIBILITY"), "1", StringComparison.Ordinal)) return;
        if (!OperatingSystem.IsWindows()) return;

        var installation = AnkiInstallationDetector.Detect() ?? throw new InvalidOperationException("Anki was not detected.");
        var packages = Path.Combine(installation.InstallationDirectory, "app_packages");
        Assert.True(Directory.Exists(packages), "The installed Anki Python packages were not found.");
        var workspace = Path.Combine(Path.GetTempPath(), "AnkiIO-local-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var packagePath = Path.Combine(workspace, "synthetic.apkg");
            var deck = new AnkiDeck("Synthetic compatibility");
            deck.Media.AddBytes("payload.bin", [1, 2, 3, 4]);
            var note = deck.AddNote(AnkiNoteTypes.CreateBasic(), new Dictionary<string, string> { ["Front"] = "synthetic", ["Back"] = "[sound:payload.bin]" });
            note.Cards[0].Scheduling = new AnkiScheduling { Type = AnkiCardType.Review, Queue = AnkiCardQueue.Suspended, Due = 20, Interval = 10, EaseFactor = 2500, Repetitions = 3 };
            await AnkiPackageWriter.WriteAsync(deck, packagePath);

            var script = """
                import os, shutil
                from anki.collection import Collection
                from anki.import_export_pb2 import ImportAnkiPackageRequest
                db=os.environ['ANKIIO_ACCEPT_DB']
                c=Collection(db); c.close()
                shutil.copy2(db, os.environ['ANKIIO_ACCEPT_BACKUP'])
                c=Collection(db)
                r=ImportAnkiPackageRequest(package_path=os.environ['ANKIIO_ACCEPT_PACKAGE'])
                r.options.with_scheduling=True
                c.import_anki_package(r)
                state=c.db.first('select type,queue,due,ivl,factor,reps from cards')
                assert state == [2,-1,20,10,2500,3], state
                media=c.media.dir()
                c.close()
                assert open(os.path.join(media,'payload.bin'),'rb').read() == bytes([1,2,3,4])
                print('ANKIIO_ACCEPTED')
                """;
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("PYTHON_EXECUTABLE") ?? "python",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(script);
            start.Environment["PYTHONPATH"] = packages;
            start.Environment["ANKIIO_ACCEPT_DB"] = Path.Combine(workspace, "collection.anki2");
            start.Environment["ANKIIO_ACCEPT_BACKUP"] = Path.Combine(workspace, "collection.before-import.backup.anki2");
            start.Environment["ANKIIO_ACCEPT_PACKAGE"] = packagePath;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Python could not be started.");
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, error);
            Assert.Contains("ANKIIO_ACCEPTED", output, StringComparison.Ordinal);
            Assert.True(File.Exists(start.Environment["ANKIIO_ACCEPT_BACKUP"]));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
