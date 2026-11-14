using System.Buffers.Binary;

namespace SprocketReferenceImages;

internal static class ImageHeaders
{
    internal static (int W, int H) Read(byte[] bytes)
    {
        ReadOnlySpan<byte> b = bytes;
        if (b.Length >= 24 && b[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) && b.Slice(12,4).SequenceEqual("IHDR"u8))
            return (checked((int)BinaryPrimitives.ReadUInt32BigEndian(b[16..])), checked((int)BinaryPrimitives.ReadUInt32BigEndian(b[20..])));
        if (b.Length < 4 || b[0] != 255 || b[1] != 216) throw new InvalidDataException("Expected PNG or JPEG.");
        int i = 2;
        while (i < b.Length)
        {
            if (b[i++] != 255) throw new InvalidDataException("Broken JPEG marker.");
            while (i < b.Length && b[i] == 255) i++;
            if (i >= b.Length) break;
            int marker = b[i++];
            if (marker == 0xD9 || marker == 0xDA) break;
            if (marker is 0x01 or >=0xD0 and <=0xD8) continue;
            if (i + 2 > b.Length) break;
            int n = BinaryPrimitives.ReadUInt16BigEndian(b[i..]);
            if (n < 2 || i + n > b.Length) break;
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (n < 8) break;
                return (BinaryPrimitives.ReadUInt16BigEndian(b[(i+5)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i+3)..]));
            }
            i += n;
        }
        throw new InvalidDataException("Missing or broken JPEG dimensions.");
    }
}
