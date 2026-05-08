using Autorecord.Core.Transcription.Engines;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Transcription.Pipeline;

public static class TranscriptAssembler
{
    public static IReadOnlyList<TranscriptSegment> Assemble(
        IReadOnlyList<TranscriptionEngineSegment> asrSegments,
        IReadOnlyList<DiarizationTurn> turns)
    {
        var speakerLabels = BuildSpeakerLabels(turns);

        var result = new List<TranscriptSegment>();
        foreach (var segment in asrSegments.OrderBy(segment => segment.Start))
        {
            var speakerId = FindBestSpeaker(segment, turns);
            var label = "Speaker 1";
            if (speakerId is null)
            {
                speakerId = "SPEAKER_00";
            }
            else if (!speakerLabels.TryGetValue(speakerId, out label))
            {
                label = "Speaker 1";
            }

            result.Add(new TranscriptSegment(
                result.Count + 1,
                segment.Start,
                segment.End,
                speakerId,
                label,
                segment.Text,
                segment.Confidence));
        }

        return MergeAdjacent(result);
    }

    private static Dictionary<string, string> BuildSpeakerLabels(IReadOnlyList<DiarizationTurn> turns)
    {
        var speakerIds = turns
            .Select(turn => turn.SpeakerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedNumbers = new HashSet<int>();

        foreach (var speakerId in speakerIds)
        {
            if (TryGetSpeakerNumber(speakerId, out var number))
            {
                labels[speakerId] = $"Speaker {number}";
                usedNumbers.Add(number);
            }
        }

        var nextNumber = 1;
        foreach (var speakerId in speakerIds)
        {
            if (labels.ContainsKey(speakerId))
            {
                continue;
            }

            while (usedNumbers.Contains(nextNumber))
            {
                nextNumber++;
            }

            labels[speakerId] = $"Speaker {nextNumber}";
            usedNumbers.Add(nextNumber);
        }

        return labels;
    }

    private static bool TryGetSpeakerNumber(string speakerId, out int number)
    {
        const string prefix = "SPEAKER_";
        number = 0;
        if (!speakerId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(speakerId[prefix.Length..], out var index))
        {
            return false;
        }

        number = index + 1;
        return true;
    }

    private static string? FindBestSpeaker(TranscriptionEngineSegment segment, IReadOnlyList<DiarizationTurn> turns)
    {
        return turns
            .Select(turn => new { turn, overlap = Math.Min(segment.End, turn.End) - Math.Max(segment.Start, turn.Start) })
            .Where(item => item.overlap > 0)
            .OrderByDescending(item => item.overlap)
            .Select(item => item.turn.SpeakerId)
            .FirstOrDefault();
    }

    private static IReadOnlyList<TranscriptSegment> MergeAdjacent(IReadOnlyList<TranscriptSegment> segments)
    {
        var result = new List<TranscriptSegment>();
        foreach (var segment in segments)
        {
            var previous = result.LastOrDefault();
            if (previous is not null &&
                previous.SpeakerId == segment.SpeakerId &&
                segment.Start - previous.End <= 1.0 &&
                previous.Text.Length + 1 + segment.Text.Length <= 600)
            {
                result[^1] = previous with { End = segment.End, Text = previous.Text + " " + segment.Text };
            }
            else
            {
                result.Add(segment with { Id = result.Count + 1 });
            }
        }

        return result.Select((segment, index) => segment with { Id = index + 1 }).ToList();
    }
}
