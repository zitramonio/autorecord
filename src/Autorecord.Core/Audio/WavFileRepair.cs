namespace Autorecord.Core.Audio;

internal static class WavFileRepair
{
    public static bool TryRepairInPlace(string inputPath)
    {
        if (!TryReadRepairableWavInfo(inputPath, out var repairInfo))
        {
            return false;
        }

        using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        WriteSizes(stream, repairInfo);
        return true;
    }

    public static bool TryCreateRepairedCopy(string inputPath, string outputPath)
    {
        if (!TryReadRepairableWavInfo(inputPath, out var repairInfo))
        {
            return false;
        }

        File.Copy(inputPath, outputPath, overwrite: false);
        using var stream = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        WriteSizes(stream, repairInfo);
        return true;
    }

    private static void WriteSizes(FileStream stream, WavRepairInfo repairInfo)
    {
        stream.Position = 4;
        stream.Write(BitConverter.GetBytes((uint)(stream.Length - 8)));
        stream.Position = repairInfo.DataSizeOffset;
        stream.Write(BitConverter.GetBytes((uint)(stream.Length - repairInfo.DataStartOffset)));
    }

    private static bool TryReadRepairableWavInfo(string inputPath, out WavRepairInfo repairInfo)
    {
        repairInfo = new WavRepairInfo(0, 0);
        try
        {
            using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 44 || stream.Length > uint.MaxValue + 8L)
            {
                return false;
            }

            using var reader = new BinaryReader(stream);
            if (!ReadFourCc(reader, "RIFF"))
            {
                return false;
            }

            _ = reader.ReadUInt32();
            if (!ReadFourCc(reader, "WAVE"))
            {
                return false;
            }

            var foundFormatChunk = false;
            while (stream.Position + 8 <= stream.Length && stream.Position < 1_048_576)
            {
                var chunkStart = stream.Position;
                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadUInt32();
                var chunkDataStart = stream.Position;

                if (chunkId == "fmt ")
                {
                    foundFormatChunk = true;
                }
                else if (chunkId == "data" && foundFormatChunk && chunkDataStart < stream.Length)
                {
                    repairInfo = new WavRepairInfo(chunkStart + 4, chunkDataStart);
                    return true;
                }

                if (chunkSize == 0)
                {
                    return false;
                }

                stream.Position = chunkDataStart + chunkSize + (chunkSize % 2);
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return false;
        }
    }

    private static bool ReadFourCc(BinaryReader reader, string expected)
    {
        return new string(reader.ReadChars(4)) == expected;
    }

    private sealed record WavRepairInfo(long DataSizeOffset, long DataStartOffset);
}
