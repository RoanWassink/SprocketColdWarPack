using System.Buffers.Binary;

namespace SprocketReferenceImages;

internal sealed record DibImage(int Width, int Height, byte[] Rgba);
internal static class DibParser
{
    // Output rows start at the bottom, matching Unity Texture2D.SetPixels32.
    internal static DibImage Parse(byte[] bytes, int gpuMax)
    {
        ReadOnlySpan<byte> b = bytes;
        if (b.Length < 40) throw new InvalidDataException("Short DIB header.");
        uint U(int p) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p));
        int I(int p) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p));
        int header = checked((int)U(0));
        if (header is not (40 or 52 or 56 or 108 or 124) || header > b.Length) throw new InvalidDataException("Unsupported DIB header.");
        int width = I(4), signedHeight = I(8);
        if (signedHeight == int.MinValue) throw new InvalidDataException("Invalid DIB height.");
        int height = Math.Abs(signedHeight);
        BoardPolicy.Dimensions(width, height, gpuMax);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(b[14..]);
        uint compression = U(16);
        if (BinaryPrimitives.ReadUInt16LittleEndian(b[12..]) != 1 || bits is not (24 or 32) || compression is not (0 or 3) || U(32) != 0)
            throw new InvalidDataException("DIB requires 24/32-bit RGB or bitfields, one plane, no palette.");
        if (header >= 108 && U(56) is not (0 or 0x73524742 or 0x57696E20)) throw new InvalidDataException("Unsupported DIB colour space.");
        if (header >= 108 && U(56)==0 && bytes.AsSpan(60,48).ToArray().Any(x=>x!=0)) throw new InvalidDataException("Calibrated DIB colour profiles are unsupported.");
        if (header == 124 && (U(112) != 0 || U(116) != 0)) throw new InvalidDataException("DIB colour profiles are unsupported.");
        int offset = header;
        uint red = 0x00FF0000, green = 0x0000FF00, blue = 0x000000FF, alpha = 0;
        if (compression == 3 || (header>=56 && U(52)!=0))
        {
            if (header == 40) offset = checked(offset + 12);
            if (b.Length < offset) throw new InvalidDataException("Short DIB masks.");
            red = U(40); green = U(44); blue = U(48); alpha = header >= 56 ? U(52) : 0;
            uint allowed = bits == 24 ? 0xFFFFFFu : uint.MaxValue;
            bool Mask(uint m, bool optional) { if(m==0)return optional; if((m & ~allowed)!=0)return false; while((m&1)==0)m>>=1; return (m & (m+1))==0; }
            if (!Mask(red,false)||!Mask(green,false)||!Mask(blue,false)||!Mask(alpha,true) ||
                ((red&green)|(red&blue)|(green&blue)|(alpha&(red|green|blue))) != 0)
                throw new InvalidDataException("Invalid DIB channel masks.");
        }
        // BI_RGB alpha bytes are historically undefined, including zero-filled 32-bit DIBs.
        long stride = (((long)width * bits + 31) / 32) * 4;
        long needed = checked(stride * height);
        if (needed > b.Length-offset || (U(20) != 0 && U(20) < needed)) throw new InvalidDataException("Truncated DIB pixels.");
        var output = new byte[checked(width * height * 4)];
        static byte Channel(uint pixel, uint mask)
        {
            if(mask==0)return 255; int shift=0; while((mask&1)==0){mask>>=1;shift++;}
            return (byte)((((ulong)(pixel>>shift)&mask)*255+mask/2)/mask);
        }
        for (int y=0;y<height;y++) for(int x=0;x<width;x++)
        {
            int sourceY = signedHeight > 0 ? y : height-1-y;
            int p=checked(offset+(int)(sourceY*stride)+x*(bits/8));
            uint pixel = (uint)(b[p]|b[p+1]<<8|b[p+2]<<16); if(bits==32)pixel|=(uint)b[p+3]<<24;
            int a=Channel(pixel,alpha), o=(y*width+x)*4;
            // Opaque output: flatten explicit alpha onto neutral gray.
            output[o]=(byte)((Channel(pixel,red)*a+48*(255-a)+127)/255);
            output[o+1]=(byte)((Channel(pixel,green)*a+48*(255-a)+127)/255);
            output[o+2]=(byte)((Channel(pixel,blue)*a+48*(255-a)+127)/255); output[o+3]=255;
        }
        return new(width,height,output);
    }
}
