using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

// Minimal 128x128 RGBA PNG reader/writer (unfiltered rows) for the icon fixture; shared by the
// tests and the fase2 exporter that writes the fixture.
public static class Png
{
    public static byte[] Read(string path)
    {
        byte[] png=File.ReadAllBytes(path);
        using var compressed=new MemoryStream();
        for(int at=8;at<png.Length;)
        {
            int length=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at,4));
            if(Encoding.ASCII.GetString(png,at+4,4)=="IDAT") compressed.Write(png,at+8,length);
            at+=length+12;
        }
        compressed.Position=0;
        using var inflater=new ZLibStream(compressed,CompressionMode.Decompress);
        using var rows=new MemoryStream(); inflater.CopyTo(rows);
        byte[] raw=rows.ToArray(),rgba=new byte[128*128*4];
        for(int y=0;y<128;y++)
        {
            if(raw[y*513]!=0) throw new InvalidDataException("Expected original unfiltered RGBA PNG");
            Array.Copy(raw,y*513+1,rgba,y*512,512);
        }
        return rgba;
    }

    public static void Write(string path,byte[] rgba)
    {
        using var output=File.Create(path);
        output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string type,byte[] data)
        {
            byte[] name=Encoding.ASCII.GetBytes(type),size=new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size,data.Length); output.Write(size); output.Write(name); output.Write(data);
            uint crc=0xffffffff;
            foreach(byte value in name.Concat(data))
            {
                crc^=value;
                for(int k=0;k<8;k++) crc=(crc&1)!=0 ? 0xedb88320^(crc>>1) : crc>>1;
            }
            BinaryPrimitives.WriteUInt32BigEndian(size,crc^0xffffffff); output.Write(size);
        }
        byte[] header=new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header,128); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),128);
        header[8]=8; header[9]=6; Chunk("IHDR",header);
        using var compressed=new MemoryStream();
        using(var encoder=new ZLibStream(compressed,CompressionLevel.SmallestSize,true))
            for(int y=0;y<128;y++) { encoder.WriteByte(0); encoder.Write(rgba,y*512,512); }
        Chunk("IDAT",compressed.ToArray()); Chunk("IEND",Array.Empty<byte>());
    }
}
