namespace SprocketExpandedBallistics;

// Render-only index projection. -1 belongs to the persistent frame; never cut it.
internal static class SpentCellRenderPolicy
{
    internal static bool ExactOwner(int component,int group,int renderer,bool knownMesh)
        =>component==group&&component==renderer&&knownMesh;
    internal static int[] Filter(int[] indices,int[] cells,IReadOnlySet<int> spent)
    {
        if(indices.Length%3!=0||indices.Length/3!=cells.Length)throw new InvalidDataException("Render cell map differs from source triangles.");
        var result=new List<int>(indices.Length);
        for(int t=0;t<cells.Length;t++)
            if(cells[t]<0||!spent.Contains(cells[t]))
            {result.Add(indices[3*t]);result.Add(indices[3*t+1]);result.Add(indices[3*t+2]);}
        return result.ToArray();
    }
}
