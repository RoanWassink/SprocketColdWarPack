using System.Buffers.Binary;
using SprocketUiKit;

internal static class GraphRenderingTests
{
    internal static void Run(Action<bool,string> check)
    {
        bool Near(float a,float b)=>MathF.Abs(a-b)<.002f;
        bool RectNear(RectF a,RectF b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.W,b.W)&&Near(a.H,b.H);
        foreach(float scale in new[]{1f,1.333f,.75f})
        {
            // Root Canvas units: a maximised panel and graph at its right edge.
            // A ScrollView clips the graph on both axes and shifts with scroll offset.
            RectF Canvas(float x,float y,float w,float h)=>new(-1280+x*scale,-720+y*scale,w*scale,h*scale);
            var panel=Canvas(0,0,1920,1080);
            var viewport=Canvas(240,64,1680,980);
            var clip=GraphClip.Intersect(panel,viewport);
            check(RectNear(clip,viewport),$"graph clip maximised at scale {scale}: full viewport through right edge");
            check(Near(clip.Right,panel.Right),$"graph clip scale {scale}: right edge stays in canvas units");
            var scroll=Canvas(220,100,1700,600);
            clip=GraphClip.Intersect(GraphClip.Intersect(viewport,scroll),panel);
            check(RectNear(clip,Canvas(240,100,1680,600)),$"graph clip ScrollView intersection at scale {scale}");
            var scrolledViewport=Canvas(240,-160,1680,980);
            clip=GraphClip.Intersect(GraphClip.Intersect(scrolledViewport,scroll),panel);
            check(RectNear(clip,Canvas(240,100,1680,600)),$"graph clip scrolled graph at scale {scale}");
            check(RectNear(GraphClip.Intersect(scroll,GraphClip.Intersect(panel,viewport)),clip),$"graph clip ancestor order independent at scale {scale}");
            check(RectNear(GraphClip.Padded(viewport,2*scale,3*scale,4*scale,5*scale),Canvas(242,67,1674,972)), $"graph clip native padding at scale {scale}");
        }
        check(RectNear(GraphClip.Intersect(new(0,0,20,20),new(21,0,10,10)),RectF.Empty),"graph clip disjoint masks give empty rect");
        check(RectNear(GraphClip.Intersect(new(0,0,20,20),new(20,0,10,10)),RectF.Empty),"graph clip touching masks give empty rect");
        check(GraphClip.Padded(new(0,0,20,20),15,0,15,0).W==0,"graph clip excessive padding is empty");
        check(RectNear(GraphClip.Intersect(new(-400,-600,800,900),new(-200,-500,900,300)),new(-200,-500,600,300)),"graph clip handles negative canvas minima");

        foreach(var item in new[]{("IN",13f,36f),("LOGIC",35f,47f),("A LONG OUTPUT TAG",400f,70.4f)})
        {
            var h=GraphHeader.Layout(176,item.Item2);
            check(RectNear(h.Head,new(0,0,176,30)),"graph header rect: "+item.Item1);
            check(Near(h.Chip.X,7)&&Near(h.Chip.Y,5)&&Near(h.Chip.W,item.Item3),"graph chip position and measured width: "+item.Item1);
            check(RectNear(h.Tag,new(6,0,h.Chip.W-12,20)),"graph tag inside chip with padding: "+item.Item1);
            check(Near(h.Title.X,14+item.Item3)&&Near(h.Title.Right,170),"graph title starts after chip and ends inside header: "+item.Item1);
        }
        check(GraphHeader.Layout(100,999).Chip.W==40&&GraphHeader.Layout(100,999).Title.W==40,"graph long tag leaves title room at minimum block width");
        check(GraphHeader.Layout(300,0).Chip.W==36&&GraphHeader.Layout(300,1000).Chip.W==120,"graph chip min 36 and max 40 percent across widths");

        check(GraphMeshLayout.MaxSlots==511&&GraphMeshLayout.MaxSlots*128==65408,"graph UInt16 cap: 511 slots / 65408 vertices");
        check((GraphMeshLayout.MaxSlots+1)*128>=65535,"graph next slot exceeds strict vertex limit");
        check(GraphMeshLayout.WireSlots(int.MaxValue,false)==511&&GraphMeshLayout.WireSlots(int.MaxValue,true)==510,"graph overflow clamps without integer overflow and reserves pending slot");
        check(GraphMeshLayout.WireSlots(511,false)==511&&GraphMeshLayout.WireSlots(511,true)==510,"graph cap switches safely when pending wire begins");
        check(GraphMeshLayout.WireSlots(0,true)==0&&GraphMeshLayout.WireSlots(12,true)==12,"graph pending wire preserves available regular slots");
        foreach(int count in new[]{0,1,2,3,31,510,511})
        {
            var bytes=new byte[count*GraphMeshLayout.IndicesPerSlot*2];
            bool valid=true,quads=true;
            for(int i=0;i<bytes.Length/2;i++)
            {
                int index=GraphMeshLayout.IndexAt(i);
                valid&=index>=0&&index<65536&&index<count*GraphMeshLayout.VerticesPerSlot;
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i*2),checked((ushort)index));
                valid&=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i*2))==index;
                quads&=index/4==i/6&&index/128==i/192;
            }
            check(valid,$"graph {count} slots: all indices round-trip in a 16-bit buffer and stay inside vertex bounds");
            check(quads,$"graph {count} slots: every triangle belongs to its own quad/slot (no fan)");
        }
        check(Enumerable.Range(0,12).Select(GraphMeshLayout.IndexAt).SequenceEqual(new[]{0,2,1,2,3,1,4,6,5,6,7,5}),"graph two-quad index winding matches strip vertices");
        // Demonstrate why interpreting UInt32 as UInt16 creates triangles pointing back to vertex 0.
        var oldBytes=new byte[12];for(int i=0;i<3;i++)BinaryPrimitives.WriteInt32LittleEndian(oldBytes.AsSpan(i*4),GraphMeshLayout.IndexAt(6+i));
        check(BinaryPrimitives.ReadUInt16LittleEndian(oldBytes.AsSpan(0))==4&&BinaryPrimitives.ReadUInt16LittleEndian(oldBytes.AsSpan(2))==0&&BinaryPrimitives.ReadUInt16LittleEndian(oldBytes.AsSpan(4))==6,"graph old 32-bit buffer read as 16-bit produces a 4,0,6 fan triangle");
        check(GraphMeshLayout.Rebuild(true,4,4),"graph new wire identities/pending restart rebuild even with unchanged slots");
        check(GraphMeshLayout.Rebuild(false,4,3)&&GraphMeshLayout.Rebuild(false,0,4),"graph shrinking/growing slot count forces complete rebuild");
        check(!GraphMeshLayout.Rebuild(false,4,4),"graph mouse movement or colour-only change retains topology");
        check(GraphMeshLayout.Rebuild(true,4,0)&&!GraphMeshLayout.Rebuild(false,0,0),"graph last wire removed rebuilds empty mesh once");
        for(int i=0;i<1000;i++) { GraphMeshLayout.IndexAt(i);GraphHeader.Layout(176,35);GraphClip.Intersect(new(0,0,20,20),new(1,1,20,20)); }
        long before=GC.GetAllocatedBytesForCurrentThread();int sink=0;
        for(int i=0;i<1000;i++) { sink+=GraphMeshLayout.IndexAt(i);GraphHeader.Layout(176,35);GraphClip.Intersect(new(0,0,20,20),new(1,1,20,20)); }
        long bytesUsed=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(sink);
        check(bytesUsed==0,"graph rendering helpers reuse values with zero bytes over 1000 polls");
    }
}
