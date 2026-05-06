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
    }
}
