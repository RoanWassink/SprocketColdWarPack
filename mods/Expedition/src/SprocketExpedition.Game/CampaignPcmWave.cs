using System.Text;

namespace SprocketExpedition.Game;

internal sealed record CampaignPcmWave(int Frequency, int Channels, float[] Samples)
{
    // Bounded local PCM16 assets; do not use the broken IL2CPP media/span helpers.
    internal static CampaignPcmWave Read(Stream stream)
    {
        if (!stream.CanSeek || stream.Length is < 44 or > 64 * 1024 * 1024)
            throw new InvalidDataException("Music WAV size is unsupported.");
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        string Tag() => Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (Tag() != "RIFF") throw new InvalidDataException("Expected RIFF WAV.");
        long end = reader.ReadUInt32() + 8L;
        if (Tag() != "WAVE" || end < 44 || end > stream.Length)
            throw new InvalidDataException("Invalid WAV container.");
        int channels = 0, frequency = 0, bits = 0, encoding = 0, block = 0;
        uint byteRate = 0;
        long dataOffset = 0;
        int dataLength = 0;
        while (stream.Position + 8 <= end)
        {
            var tag = Tag();
            var size = reader.ReadUInt32();
            var next = stream.Position + size;
            if (next > end || next + (size & 1) > end)
                throw new InvalidDataException("Truncated WAV chunk.");
            if (tag == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Invalid WAV format.");
                encoding = reader.ReadUInt16(); channels = reader.ReadUInt16(); frequency = reader.ReadInt32();
                byteRate = reader.ReadUInt32(); block = reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (tag == "data") { dataOffset = stream.Position; dataLength = checked((int)size); }
            stream.Position = next + (size & 1);
        }
        if (encoding != 1 || channels is < 1 or > 2 || frequency is < 8000 or > 96000
            || bits != 16 || block != channels * 2 || byteRate != frequency * block
            || dataLength == 0 || dataLength % block != 0)
            throw new InvalidDataException("Expected mono/stereo PCM16 music WAV.");
        stream.Position = dataOffset;
        var pcm = reader.ReadBytes(dataLength);
        if (pcm.Length != dataLength) throw new InvalidDataException("Truncated WAV samples.");
        var samples = new float[dataLength / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(pcm[i * 2] | pcm[i * 2 + 1] << 8) / 32768f;
        return new CampaignPcmWave(frequency, channels, samples);
    }
}
