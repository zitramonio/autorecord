using System.Text.Json;
using Autorecord.Core.Settings;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Tests;

public sealed class TranscriptExporterTests
{
    [Fact]
    public async Task ExportAsyncCreatesTxtMdSrtAndJson()
    {
        var outputDirectory = CreateTempDirectory();
        var exporter = new TranscriptExporter();

        var outputFiles = await exporter.ExportAsync(
            CreateDocument(),
            outputDirectory,
            [TranscriptOutputFormat.Txt, TranscriptOutputFormat.Markdown, TranscriptOutputFormat.Srt, TranscriptOutputFormat.Json],
            overwrite: false,
            CancellationToken.None);

        Assert.Equal(
            [
                Path.Combine(outputDirectory, "meeting.txt"),
                Path.Combine(outputDirectory, "meeting.md"),
                Path.Combine(outputDirectory, "meeting.srt"),
                Path.Combine(outputDirectory, "meeting.json")
            ],
            outputFiles.AllPaths);
        Assert.Equal(Path.Combine(outputDirectory, "meeting.txt"), outputFiles.TxtPath);
        Assert.Equal(Path.Combine(outputDirectory, "meeting.md"), outputFiles.MarkdownPath);
        Assert.Equal(Path.Combine(outputDirectory, "meeting.srt"), outputFiles.SrtPath);
        Assert.Equal(Path.Combine(outputDirectory, "meeting.json"), outputFiles.JsonPath);

        var txt = await File.ReadAllTextAsync(outputFiles.TxtPath!);
        Assert.Contains("[00:00:01.200 - 00:00:06.800] Speaker 1:", txt);
        Assert.Contains("Привет, это тест.", txt);

        var md = await File.ReadAllTextAsync(outputFiles.MarkdownPath!);
        Assert.Contains("# Транскрипт", md);
        Assert.Contains("Файл: meeting.wav", md);
        Assert.Contains("ASR: GigaAM Fast (asr-fast)", md);
        Assert.Contains("Диаризация: Pyannote Fast (diarization-fast)", md);

        var srt = await File.ReadAllTextAsync(outputFiles.SrtPath!);
        Assert.Contains("1\r\n00:00:01,200 --> 00:00:06,800\r\nSpeaker 1: Привет, это тест.", srt);

        var json = await File.ReadAllTextAsync(outputFiles.JsonPath!);
        Assert.Contains("\"rawDiarizationSegments\"", json);
        Assert.Contains("\"speakerId\": \"speaker-1\"", json);
    }

    [Fact]
    public async Task ExportAsyncAddsSuffixWhenFilesExist()
    {
        var outputDirectory = CreateTempDirectory();
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "meeting.txt"), "existing");
        var exporter = new TranscriptExporter();

        var outputFiles = await exporter.ExportAsync(
            CreateDocument(),
            outputDirectory,
            [TranscriptOutputFormat.Txt, TranscriptOutputFormat.Json],
            overwrite: false,
            CancellationToken.None);

        Assert.Equal(Path.Combine(outputDirectory, "meeting transcript 2.txt"), outputFiles.TxtPath);
        Assert.Equal(Path.Combine(outputDirectory, "meeting transcript 2.json"), outputFiles.JsonPath);
        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "meeting.txt")));
    }

    [Fact]
    public async Task ExportAsyncOverwritesWhenOverwriteTrue()
    {
        var outputDirectory = CreateTempDirectory();
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "meeting.txt");
        await File.WriteAllTextAsync(path, "existing");
        var exporter = new TranscriptExporter();

        var outputFiles = await exporter.ExportAsync(
            CreateDocument(),
            outputDirectory,
            [TranscriptOutputFormat.Txt],
            overwrite: true,
            CancellationToken.None);

        Assert.Equal(path, outputFiles.TxtPath);
        var txt = await File.ReadAllTextAsync(path);
        Assert.Contains("Привет, это тест.", txt);
        Assert.DoesNotContain("existing", txt);
    }

    [Fact]
    public async Task ExportAsyncLeavesNoTempFilesAfterOverwrite()
    {
        var outputDirectory = CreateTempDirectory();
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "meeting.txt");
        await File.WriteAllTextAsync(path, "existing");
        var exporter = new TranscriptExporter();

        await exporter.ExportAsync(
            CreateDocument(),
            outputDirectory,
            [TranscriptOutputFormat.Txt],
            overwrite: true,
            CancellationToken.None);

        Assert.Empty(Directory.EnumerateFiles(outputDirectory, "*.tmp"));
        var txt = await File.ReadAllTextAsync(path);
        Assert.Contains("Привет, это тест.", txt);
        Assert.DoesNotContain("existing", txt);
    }

    [Fact]
    public async Task ExportAsyncLeavesNoTempFilesAfterNonOverwrite()
    {
        var outputDirectory = CreateTempDirectory();
        var exporter = new TranscriptExporter();

        await exporter.ExportAsync(
            CreateDocument(),
            outputDirectory,
            [TranscriptOutputFormat.Txt],
            overwrite: false,
            CancellationToken.None);

        Assert.Empty(Directory.EnumerateFiles(outputDirectory, "*.tmp"));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "meeting.txt")));
    }

    [Fact]
    public async Task ExportAsyncCancellationLeavesNoPartialFinalFileOrTempFiles()
    {
        var outputDirectory = CreateTempDirectory();
        using var cancellation = new CancellationTokenSource();
        var exporter = new TranscriptExporter();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => exporter.ExportAsync(
                CreateDocument(),
                outputDirectory,
                [TranscriptOutputFormat.Txt],
                overwrite: false,
                cancellation.Token));

        Assert.False(File.Exists(Path.Combine(outputDirectory, "meeting.txt")));
        if (Directory.Exists(outputDirectory))
        {
            Assert.Empty(Directory.EnumerateFiles(outputDirectory, "*.tmp"));
        }
    }

    [Fact]
    public async Task ExportAsyncWritesOnlySelectedFormats()
    {
        var outputDirectory = CreateTempDirectory();
        var exporter = new TranscriptExporter();

        var outputFiles = await exporter.ExportAsync(
            CreateDocument(),
            outputDirectory,
            [TranscriptOutputFormat.Markdown],
            overwrite: false,
            CancellationToken.None);

        Assert.Null(outputFiles.TxtPath);
        Assert.Equal(Path.Combine(outputDirectory, "meeting.md"), outputFiles.MarkdownPath);
        Assert.Null(outputFiles.SrtPath);
        Assert.Null(outputFiles.JsonPath);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "meeting.md")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "meeting.txt")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "meeting.srt")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "meeting.json")));
    }

    [Fact]
    public async Task ExportAsyncRejectsEmptyFormats()
    {
        var exporter = new TranscriptExporter();

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(CreateDocument(), CreateTempDirectory(), [], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsNullDocument()
    {
        var exporter = new TranscriptExporter();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => exporter.ExportAsync(null!, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsNullFormats()
    {
        var exporter = new TranscriptExporter();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => exporter.ExportAsync(CreateDocument(), CreateTempDirectory(), null!, false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsUnknownFormat()
    {
        var exporter = new TranscriptExporter();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => exporter.ExportAsync(CreateDocument(), CreateTempDirectory(), [(TranscriptOutputFormat)999], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsBlankInputFile()
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with { InputFile = " " };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsBlankOutputDirectory()
    {
        var exporter = new TranscriptExporter();

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(CreateDocument(), " ", [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task ExportAsyncRejectsInvalidDuration(double durationSec)
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with { DurationSec = durationSec };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsInvalidSegment()
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with
        {
            Segments =
            [
                new TranscriptSegment(1, 2.0, 1.0, "speaker-1", "Speaker 1", "bad timing", 0.9)
            ]
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Theory]
    [InlineData(-0.1, 6.8)]
    [InlineData(1.2, -0.1)]
    [InlineData(double.NaN, 6.8)]
    [InlineData(1.2, double.NaN)]
    [InlineData(double.PositiveInfinity, 6.8)]
    [InlineData(1.2, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 6.8)]
    [InlineData(1.2, double.NegativeInfinity)]
    public async Task ExportAsyncRejectsInvalidSegmentTimestamps(double start, double end)
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with
        {
            Segments =
            [
                new TranscriptSegment(1, start, end, "speaker-1", "Speaker 1", "Привет, это тест.", 0.95)
            ]
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Theory]
    [InlineData(-0.1, 6.8)]
    [InlineData(1.2, -0.1)]
    [InlineData(double.NaN, 6.8)]
    [InlineData(1.2, double.NaN)]
    [InlineData(double.PositiveInfinity, 6.8)]
    [InlineData(1.2, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 6.8)]
    [InlineData(1.2, double.NegativeInfinity)]
    public async Task ExportAsyncRejectsInvalidRawDiarizationTimestamps(double start, double end)
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with
        {
            RawDiarizationSegments = [new DiarizationTurn(start, end, "speaker-1")]
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsBlankSpeakerLabel()
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with
        {
            Segments =
            [
                new TranscriptSegment(1, 1.2, 6.8, "speaker-1", " ", "Привет, это тест.", 0.95)
            ]
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsBlankText()
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with
        {
            Segments =
            [
                new TranscriptSegment(1, 1.2, 6.8, "speaker-1", "Speaker 1", " ", 0.95)
            ]
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsNullSpeakers()
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with { Speakers = null! };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsNullSegments()
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with { Segments = null! };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsyncRejectsNullRawDiarizationSegments()
    {
        var exporter = new TranscriptExporter();
        var document = CreateDocument() with { RawDiarizationSegments = null! };

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(document, CreateTempDirectory(), [TranscriptOutputFormat.Txt], false, CancellationToken.None));
    }

    [Fact]
    public async Task JsonOutputContainsRawDiarizationSegments()
    {
        var outputDirectory = CreateTempDirectory();
        var exporter = new TranscriptExporter();

        var outputFiles = await exporter.ExportAsync(
            CreateDocument(),
            outputDirectory,
            [TranscriptOutputFormat.Json],
            overwrite: false,
            CancellationToken.None);

        await using var stream = File.OpenRead(outputFiles.JsonPath!);
        using var json = await JsonDocument.ParseAsync(stream);
        var rawSegments = json.RootElement.GetProperty("rawDiarizationSegments");

        var rawSegment = Assert.Single(rawSegments.EnumerateArray());
        Assert.Equal(1.2, rawSegment.GetProperty("start").GetDouble());
        Assert.Equal(6.8, rawSegment.GetProperty("end").GetDouble());
        Assert.Equal("speaker-1", rawSegment.GetProperty("speakerId").GetString());
    }

    private static TranscriptDocument CreateDocument()
    {
        return new TranscriptDocument
        {
            InputFile = "C:\\Records\\meeting.wav",
            DurationSec = 8.0,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00"),
            AsrModelId = "asr-fast",
            AsrModelDisplayName = "GigaAM Fast",
            DiarizationModelId = "diarization-fast",
            DiarizationModelDisplayName = "Pyannote Fast",
            Speakers = [new TranscriptSpeaker("speaker-1", "Speaker 1")],
            Segments =
            [
                new TranscriptSegment(1, 1.2, 6.8, "speaker-1", "Speaker 1", "Привет, это тест.", 0.95)
            ],
            RawDiarizationSegments = [new DiarizationTurn(1.2, 6.8, "speaker-1")]
        };
    }

    private static string CreateTempDirectory()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }
}
