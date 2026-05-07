using System.Globalization;
using System.Text;
using System.Text.Json;
using Autorecord.Core.Settings;

namespace Autorecord.Core.Transcription.Results;

public sealed class TranscriptExporter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<TranscriptOutputFiles> ExportAsync(
        TranscriptDocument document,
        string outputDirectory,
        IReadOnlyList<TranscriptOutputFormat> formats,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        Validate(document, outputDirectory, formats);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(outputDirectory);

        var selectedFormats = formats.Distinct().ToArray();
        var baseName = Path.GetFileNameWithoutExtension(document.InputFile);
        var targetBasePath = ResolveTargetBasePath(outputDirectory, baseName, selectedFormats, overwrite);

        string? txtPath = null;
        string? markdownPath = null;
        string? srtPath = null;
        string? jsonPath = null;

        foreach (var format in selectedFormats)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (format)
            {
                case TranscriptOutputFormat.Txt:
                    txtPath = BuildPath(targetBasePath, ".txt");
                    await WriteFileAsync(txtPath, BuildTxt(document), overwrite, cancellationToken);
                    break;
                case TranscriptOutputFormat.Markdown:
                    markdownPath = BuildPath(targetBasePath, ".md");
                    await WriteFileAsync(markdownPath, BuildMarkdown(document), overwrite, cancellationToken);
                    break;
                case TranscriptOutputFormat.Srt:
                    srtPath = BuildPath(targetBasePath, ".srt");
                    await WriteFileAsync(srtPath, BuildSrt(document), overwrite, cancellationToken);
                    break;
                case TranscriptOutputFormat.Json:
                    jsonPath = BuildPath(targetBasePath, ".json");
                    await WriteFileAsync(jsonPath, JsonSerializer.Serialize(document, JsonOptions), overwrite, cancellationToken);
                    break;
            }
        }

        return new TranscriptOutputFiles(txtPath, markdownPath, srtPath, jsonPath);
    }

    private static void Validate(
        TranscriptDocument? document,
        string? outputDirectory,
        IReadOnlyList<TranscriptOutputFormat>? formats)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(formats);

        RequireNonBlank(document.InputFile, nameof(TranscriptDocument.InputFile));
        RequireNonBlank(outputDirectory, nameof(outputDirectory));
        RequireNonBlank(document.AsrModelId, nameof(TranscriptDocument.AsrModelId));
        RequireNonBlank(document.AsrModelDisplayName, nameof(TranscriptDocument.AsrModelDisplayName));
        RequireNonNegativeFinite(document.DurationSec, nameof(TranscriptDocument.DurationSec));

        if (formats.Count == 0)
        {
            throw new ArgumentException("At least one transcript output format is required.", nameof(formats));
        }

        foreach (var format in formats)
        {
            if (!Enum.IsDefined(format))
            {
                throw new ArgumentOutOfRangeException(nameof(formats), "Transcript output format must be known.");
            }
        }

        if (document.Speakers is null)
        {
            throw new ArgumentException("Transcript speakers must not be null.", nameof(document));
        }

        if (document.Segments is null)
        {
            throw new ArgumentException("Transcript segments must not be null.", nameof(document));
        }

        if (document.RawDiarizationSegments is null)
        {
            throw new ArgumentException("Raw diarization segments must not be null.", nameof(document));
        }

        foreach (var speaker in document.Speakers)
        {
            if (speaker is null)
            {
                throw new ArgumentException("Transcript speakers must not contain null values.", nameof(document));
            }

            RequireNonBlank(speaker.Id, nameof(TranscriptSpeaker.Id));
            RequireNonBlank(speaker.Label, nameof(TranscriptSpeaker.Label));
        }

        foreach (var segment in document.Segments)
        {
            if (segment is null)
            {
                throw new ArgumentException("Transcript segments must not contain null values.", nameof(document));
            }

            if (segment.End < segment.Start)
            {
                throw new ArgumentException("Transcript segment end must be greater than or equal to start.", nameof(document));
            }

            RequireNonNegativeFinite(segment.Start, nameof(TranscriptSegment.Start));
            RequireNonNegativeFinite(segment.End, nameof(TranscriptSegment.End));
            RequireNonBlank(segment.SpeakerId, nameof(TranscriptSegment.SpeakerId));
            RequireNonBlank(segment.SpeakerLabel, nameof(TranscriptSegment.SpeakerLabel));
            RequireNonBlank(segment.Text, nameof(TranscriptSegment.Text));
        }

        foreach (var turn in document.RawDiarizationSegments)
        {
            if (turn is null)
            {
                throw new ArgumentException("Raw diarization segments must not contain null values.", nameof(document));
            }

            if (turn.End < turn.Start)
            {
                throw new ArgumentException("Raw diarization segment end must be greater than or equal to start.", nameof(document));
            }

            RequireNonNegativeFinite(turn.Start, nameof(DiarizationTurn.Start));
            RequireNonNegativeFinite(turn.End, nameof(DiarizationTurn.End));
            RequireNonBlank(turn.SpeakerId, nameof(DiarizationTurn.SpeakerId));
        }
    }

    private static string ResolveTargetBasePath(
        string outputDirectory,
        string baseName,
        IReadOnlyList<TranscriptOutputFormat> formats,
        bool overwrite)
    {
        if (overwrite)
        {
            return Path.Combine(outputDirectory, baseName);
        }

        var suffixNumber = 1;
        while (true)
        {
            var candidateName = suffixNumber == 1
                ? baseName
                : string.Create(CultureInfo.InvariantCulture, $"{baseName} transcript {suffixNumber}");
            var candidateBasePath = Path.Combine(outputDirectory, candidateName);

            if (formats.All(format => !File.Exists(BuildPath(candidateBasePath, GetExtension(format)))))
            {
                return candidateBasePath;
            }

            suffixNumber++;
        }
    }

    private static string BuildTxt(TranscriptDocument document)
    {
        var builder = new StringBuilder();
        foreach (var segment in document.Segments)
        {
            builder.Append('[')
                .Append(FormatTextTimestamp(segment.Start))
                .Append(" - ")
                .Append(FormatTextTimestamp(segment.End))
                .Append("] ")
                .Append(segment.SpeakerLabel)
                .AppendLine(":");
            builder.AppendLine(segment.Text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildMarkdown(TranscriptDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Транскрипт");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Файл: {Path.GetFileName(document.InputFile)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Создан: {document.CreatedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- ASR: {document.AsrModelDisplayName} ({document.AsrModelId})");

        if (string.IsNullOrWhiteSpace(document.DiarizationModelId)
            && string.IsNullOrWhiteSpace(document.DiarizationModelDisplayName))
        {
            builder.AppendLine("- Диаризация: нет");
        }
        else
        {
            var displayName = string.IsNullOrWhiteSpace(document.DiarizationModelDisplayName)
                ? document.DiarizationModelId
                : document.DiarizationModelDisplayName;
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Диаризация: {displayName} ({document.DiarizationModelId})");
        }

        builder.AppendLine();

        foreach (var segment in document.Segments)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"**[{FormatTextTimestamp(segment.Start)} - {FormatTextTimestamp(segment.End)}] {segment.SpeakerLabel}:**");
            builder.AppendLine();
            builder.AppendLine(segment.Text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildSrt(TranscriptDocument document)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < document.Segments.Count; index++)
        {
            var segment = document.Segments[index];
            builder.AppendLine(CultureInfo.InvariantCulture, $"{index + 1}");
            builder.Append(FormatSrtTimestamp(segment.Start))
                .Append(" --> ")
                .AppendLine(FormatSrtTimestamp(segment.End));
            builder.Append(segment.SpeakerLabel)
                .Append(": ")
                .AppendLine(segment.Text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static async Task WriteFileAsync(
        string path,
        string content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, content, Utf8NoBom, cancellationToken);
            File.Move(tempPath, path, overwrite);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static string GetExtension(TranscriptOutputFormat format)
    {
        return format switch
        {
            TranscriptOutputFormat.Txt => ".txt",
            TranscriptOutputFormat.Markdown => ".md",
            TranscriptOutputFormat.Srt => ".srt",
            TranscriptOutputFormat.Json => ".json",
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Transcript output format must be known.")
        };
    }

    private static string BuildPath(string basePath, string extension)
    {
        return basePath + extension;
    }

    private static string FormatTextTimestamp(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    private static string FormatSrtTimestamp(double seconds)
    {
        return FormatTextTimestamp(seconds).Replace('.', ',');
    }

    private static void RequireNonBlank(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Transcript field '{name}' must not be empty.", name);
        }
    }

    private static void RequireNonNegativeFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentException($"Transcript field '{name}' must be finite and non-negative.", name);
        }
    }
}
