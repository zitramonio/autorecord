using System.Text.Json;

namespace Autorecord.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        settings ??= new AppSettings();
        Validate(settings);
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Validate(settings);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    private static void Validate(AppSettings settings)
    {
        if (settings.SilencePromptMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Silence prompt interval must be positive.");
        }

        if (settings.RetryPromptMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Retry prompt interval must be positive.");
        }

        if (!Enum.IsDefined(settings.RecordingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Recording mode must be known.");
        }

        var transcription = settings.Transcription;
        if (transcription is null)
        {
            throw new ArgumentException("Transcription settings must be set.", nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(transcription.SelectedAsrModelId))
        {
            throw new ArgumentException("Selected ASR model ID must be set.", nameof(settings));
        }

        if (transcription.EnableDiarization && string.IsNullOrWhiteSpace(transcription.SelectedDiarizationModelId))
        {
            throw new ArgumentException("Selected diarization model ID must be set.", nameof(settings));
        }

        if (!Enum.IsDefined(transcription.OutputFolderMode))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Transcript output folder mode must be known.");
        }

        if (transcription.OutputFolderMode == TranscriptOutputFolderMode.CustomFolder
            && string.IsNullOrWhiteSpace(transcription.CustomOutputFolder))
        {
            throw new ArgumentException("Custom transcript output folder must be set.", nameof(settings));
        }

        if (transcription.NumSpeakers is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Speaker count must be between 1 and 6.");
        }

        if (transcription.ClusterThreshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Cluster threshold must be greater than 0 and no more than 1.");
        }

        if (transcription.OutputFormats is null)
        {
            throw new ArgumentException("Transcript output formats must be set.", nameof(settings));
        }

        if (transcription.OutputFormats.Count == 0)
        {
            throw new ArgumentException("At least one transcript output format must be selected.", nameof(settings));
        }

        foreach (var format in transcription.OutputFormats)
        {
            if (!Enum.IsDefined(format))
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "Transcript output format must be known.");
            }
        }
    }
}
