namespace SprocketExpandedBallistics;

internal readonly record struct DeclaredFace(int FaceIndex,double NormalThicknessMm,int SurfaceType,bool Tapered=false);
internal static class DeclaredFacePolicy
{
    // The original 0.22.16 marker check: native truncates the corner average.
    // -1 is the existing non-front sentinel, never a one-millimetre tolerance.
    internal static bool MatchesConfiguredThickness(double configured,double native)=>
        native==-1||(short)configured==native;
    // Native triangleIndex is an index-buffer offset, not a triangle ordinal.
    // Marker values delimit vertex ranges; they are NOT face identifiers.
    internal static bool Resolve(int triangleOffset,IReadOnlyList<int> triangles,IReadOnlyList<short> thickness,
        IReadOnlyList<uint> ranges,IReadOnlyList<byte> types,IReadOnlyList<int> faceWidths,out DeclaredFace result)
    {
        result=default;
        if(triangleOffset<0||triangleOffset%3!=0||triangleOffset>triangles.Count-3||ranges.Count!=types.Count)return false;
        var a=triangles[triangleOffset];var b=triangles[triangleOffset+1];var c=triangles[triangleOffset+2];
        if(a<0||b<0||c<0||a>=thickness.Count||b>=thickness.Count||c>=thickness.Count)return false;
        return ResolveSamples(triangleOffset,triangles.Count,thickness.Count,a,b,c,thickness[a],thickness[b],thickness[c],ranges,types,faceWidths,out result);
    }
    internal static bool ResolveSamples(int triangleOffset,int triangleCount,int vertexCount,int a,int b,int c,
        short ta,short tb,short tc,IReadOnlyList<uint> ranges,IReadOnlyList<byte> types,IReadOnlyList<int> faceWidths,out DeclaredFace result,bool allowNativeSentinel=false,bool allowPolygons=false,bool allowTapered=false)
    {
        result=default;
        if(triangleOffset<0||triangleOffset%3!=0||triangleOffset>triangleCount-3||ranges.Count!=types.Count||
            a<0||b<0||c<0||a>=vertexCount||b>=vertexCount||c>=vertexCount)return false;
        uint start=0;
        for(int range=0;range<ranges.Count;range++)
        {
            var end=ranges[range];
            if(end<=start||end>vertexCount)return false;
            if(a>=start&&a<end&&b>=start&&b<end&&c>=start&&c<end)
            {
                if(types[range] is not (1 or 2))return false; // Edge/rivet/default has no face contract.
                var total=faceWidths.Sum(w=>Math.Max(0,w));
                if(total!=end-start)return false;
                int offset=(int)start;
                for(int face=0;face<faceWidths.Count;face++)
                {
                    var width=faceWidths[face];
                    if(width==0)continue;
                    if(width<3||!allowPolygons&&width>4)return false;
                    if(a>=offset&&a<offset+width&&b>=offset&&b<offset+width&&c>=offset&&c<offset+width)
                    {
                        var t=ta;
                        // Current native face tessellation stores one averaged declared value.
                        if(tb!=t||tc!=t)
                        {
                            // 0.22.24: a tapered To-back-plane face stores different values at its three vertices. The face identity comes from the
                            // marker range, so only the plausibility of the samples is checked; the caller takes the configured face average.
                            if(!allowTapered||ta<=0||tb<=0||tc<=0)return false;
                            result=new(face,(ta+tb+tc)/3.0,types[range],true);return true;
                        }
                        if(t<=0&&!(allowNativeSentinel&&t==-1))return false;
                        result=new(face,t,types[range]);return true;
                    }
                    offset+=width;
                }
                return false;
            }
            start=end;
        }
        return false;
    }
    internal static bool ResolveSinglePlanarFace(int faceCount,int faceWidth,bool culled,double configuredThickness,
        double normalAlignment,short ta,short tb,short tc,out DeclaredFace result,bool allowPolygons=false)
    {
        result=default;
        // Exact source ownership plus a single front/back plane is unambiguous even
        // if the compound native mesh no longer carries the source marker ranges.
        // A side/bevel normal cannot inherit the face's ERA flag.
        if(faceCount!=1||faceWidth<3||!allowPolygons&&faceWidth>4||culled||!double.IsFinite(configuredThickness)||
            configuredThickness<=0||configuredThickness>short.MaxValue||!double.IsFinite(normalAlignment)||
            Math.Abs(normalAlignment)<.9999)return false;
        var nativeDeclared=(short)configuredThickness; // Native tessellation truncates its loop average.
        // Non-front native tessellation deliberately stores -1 for every vertex.
        // This sentinel supplies no thickness; exact face identity/normal and the
        // configured loop average remain mandatory. Mixed/other values are invalid.
        var sentinel=ta==-1&&tb==-1&&tc==-1;
        if(!sentinel&&(ta!=nativeDeclared||tb!=nativeDeclared||tc!=nativeDeclared))return false;
        result=new(0,configuredThickness,normalAlignment>=0?1:2);return true;
    }

    // Material backing only: all configured faces must agree, and the actual native
    // normal must identify a front/back plane. No adjacent-face ERA flag is inferred.
    internal static bool ResolveUniformBacking(IReadOnlyList<double> configured,IReadOnlyList<double> alignments,
        short ta,short tb,short tc,out DeclaredFace result)
    {
        result=default;
        if(configured.Count==0||configured.Count!=alignments.Count)return false;
        var t=configured[0];
        if(!double.IsFinite(t)||t<=0||t>short.MaxValue)return false;
        int matches=0;double alignment=0;
        for(int i=0;i<configured.Count;i++)
        {
            if(!double.IsFinite(configured[i])||Math.Abs(configured[i]-t)>.000001||!double.IsFinite(alignments[i]))return false;
            if(Math.Abs(alignments[i])>=.9999){alignment=alignments[i];matches++;}
        }
        if(matches==0)return false;
        var sentinel=ta==-1&&tb==-1&&tc==-1;
        if(!sentinel&&(ta!=(short)t||tb!=(short)t||tc!=(short)t))return false;
        // The filtered input has no source-face identity mapping. Never invent an index.
        result=new(-1,t,alignment>=0?1:2);return true;
    }

}
