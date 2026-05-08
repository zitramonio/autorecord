using Autorecord.Core.Transcription.Engines;
using Autorecord.Core.Transcription.Pipeline;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Tests;

public sealed class TranscriptAssemblerTests
{
    [Fact]
    public void AssembleAssignsSegmentsToOverlappingSpeakerTurns()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(1.0, 3.0, "Добрый день.", null),
            new TranscriptionEngineSegment(4.0, 5.0, "Да.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.5, 3.5, "SPEAKER_00"),
            new DiarizationTurn(3.8, 5.5, "SPEAKER_01")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal("Speaker 1", segments[0].SpeakerLabel);
        Assert.Equal("Speaker 2", segments[1].SpeakerLabel);
    }

    [Fact]
    public void AssembleUsesGreatestPositiveOverlap()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(2.0, 6.0, "Тест.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(1.0, 3.0, "SPEAKER_00"),
            new DiarizationTurn(3.0, 7.0, "SPEAKER_01")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal("SPEAKER_01", segments[0].SpeakerId);
        Assert.Equal("Speaker 2", segments[0].SpeakerLabel);
    }

    [Fact]
    public void AssembleFallsBackToFirstSpeakerWhenNoTurnOverlaps()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(10.0, 12.0, "Без спикера.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.0, 2.0, "SPEAKER_02")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal("SPEAKER_00", segments[0].SpeakerId);
        Assert.Equal("Speaker 1", segments[0].SpeakerLabel);
    }

    [Fact]
    public void AssembleUsesFirstSpeakerLabelForFallbackWhenSpeakerZeroAppearsSecond()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(10.0, 12.0, "Без спикера.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.0, 2.0, "SPEAKER_01"),
            new DiarizationTurn(2.0, 4.0, "SPEAKER_00")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal("SPEAKER_00", segments[0].SpeakerId);
        Assert.Equal("Speaker 1", segments[0].SpeakerLabel);
    }

    [Fact]
    public void AssembleUsesStableSpeakerZeroLabelWhenTurnAppearsSecond()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(3.0, 4.0, "Текст.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.0, 1.0, "SPEAKER_01"),
            new DiarizationTurn(2.0, 5.0, "SPEAKER_00")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal("SPEAKER_00", segments[0].SpeakerId);
        Assert.Equal("Speaker 1", segments[0].SpeakerLabel);
    }

    [Fact]
    public void AssembleAvoidsDuplicateLabelsForCustomSpeakerAndFallbackSpeaker()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(1.0, 2.0, "С известным спикером.", null),
            new TranscriptionEngineSegment(10.0, 11.0, "Без спикера.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.5, 2.5, "custom-a")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal("custom-a", segments[0].SpeakerId);
        Assert.Equal("Speaker 2", segments[0].SpeakerLabel);
        Assert.Equal("SPEAKER_00", segments[1].SpeakerId);
        Assert.Equal("Speaker 1", segments[1].SpeakerLabel);
        Assert.NotEqual(segments[0].SpeakerLabel, segments[1].SpeakerLabel);
    }

    [Fact]
    public void AssembleMergesAdjacentSegmentsForSameSpeaker()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(1.0, 2.0, "Первый.", null),
            new TranscriptionEngineSegment(2.5, 3.0, "Второй.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.5, 3.5, "SPEAKER_00")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        var segment = Assert.Single(segments);
        Assert.Equal(1, segment.Id);
        Assert.Equal(1.0, segment.Start);
        Assert.Equal(3.0, segment.End);
        Assert.Equal("Первый. Второй.", segment.Text);
    }

    [Fact]
    public void AssembleMergesSameSpeakerIgnoringIdCase()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(1.0, 2.0, "Первый.", null),
            new TranscriptionEngineSegment(2.5, 3.0, "Второй.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.5, 2.1, "speaker_00"),
            new DiarizationTurn(2.4, 3.5, "SPEAKER_00")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        var segment = Assert.Single(segments);
        Assert.Equal("Первый. Второй.", segment.Text);
    }

    [Fact]
    public void AssembleDoesNotMergeWhenTextWithSeparatorExceedsLimit()
    {
        var firstText = new string('a', 300);
        var secondText = new string('b', 300);
        var asr = new[]
        {
            new TranscriptionEngineSegment(1.0, 2.0, firstText, null),
            new TranscriptionEngineSegment(2.5, 3.0, secondText, null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.5, 3.5, "SPEAKER_00")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal(2, segments.Count);
        Assert.Equal(firstText, segments[0].Text);
        Assert.Equal(secondText, segments[1].Text);
    }

    [Fact]
    public void AssembleMergesWhenTextWithSeparatorEqualsLimit()
    {
        var firstText = new string('a', 299);
        var secondText = new string('b', 300);
        var asr = new[]
        {
            new TranscriptionEngineSegment(1.0, 2.0, firstText, null),
            new TranscriptionEngineSegment(2.5, 3.0, secondText, null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.5, 3.5, "SPEAKER_00")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        var segment = Assert.Single(segments);
        Assert.Equal(600, segment.Text.Length);
        Assert.Equal(firstText + " " + secondText, segment.Text);
    }

    [Fact]
    public void AssembleDoesNotMergeSameSpeakerWhenGapExceedsLimit()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(1.0, 2.0, "Первый.", null),
            new TranscriptionEngineSegment(3.1, 4.0, "Второй.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.5, 4.5, "SPEAKER_00")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal(2, segments.Count);
        Assert.Equal("Первый.", segments[0].Text);
        Assert.Equal("Второй.", segments[1].Text);
    }

    [Fact]
    public void AssembleDoesNotMergeDifferentSpeakers()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(1.0, 2.0, "Первый.", null),
            new TranscriptionEngineSegment(2.5, 3.0, "Второй.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.5, 2.1, "SPEAKER_00"),
            new DiarizationTurn(2.4, 3.5, "SPEAKER_01")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal(2, segments.Count);
        Assert.Equal("SPEAKER_00", segments[0].SpeakerId);
        Assert.Equal("SPEAKER_01", segments[1].SpeakerId);
    }

    [Fact]
    public void AssembleSkipsBlankAsrSegments()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(0.0, 1.0, " ", null),
            new TranscriptionEngineSegment(1.0, 2.0, "Текст.", null)
        };

        var segments = TranscriptAssembler.Assemble(asr, []);

        var segment = Assert.Single(segments);
        Assert.Equal(1, segment.Id);
        Assert.Equal("Текст.", segment.Text);
    }
}
