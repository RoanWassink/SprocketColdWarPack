namespace SprocketExpandedBallistics;

internal static class CombatReportNoise
{
    internal static bool IsEndedPlate(CombatReportEvent row)=>row.Kind=="materialUnavailable"&&row.Text?.StartsWith("Skipped composite plate #",StringComparison.Ordinal)==true&&row.Text.Contains(": line ended before another solid block; cassette and passive resistance not applied.",StringComparison.Ordinal);
    internal static IReadOnlyList<CombatReportEvent> Collapse(IEnumerable<CombatReportEvent> events)
    {
        var result=new List<CombatReportEvent>();
        var groups=new Dictionary<(string,string,int?),int>();
        foreach(var row in events)
        {
            if(row==null)continue;
            const string ending=": line ended before another solid block; cassette and passive resistance not applied.";
            var end=row.Text?.IndexOf(ending,StringComparison.Ordinal)??-1;
            if(row.Kind!="materialUnavailable"||row.Text==null||!row.Text.StartsWith("Skipped composite plate #",StringComparison.Ordinal)||end<0)
            {result.Add(row);continue;}
            var text=row.Text.Substring(0,end+ending.Length);
            var key=(text,row.Stage,row.RelatedEntity);
            int count=Math.Max(1,row.Occurrences);
            if(groups.TryGetValue(key,out var index))
            {
                count=CombatReportTruth.Count(result[index].Occurrences,count);
                result[index]=row with{Text=text+$" ({count} occurrences)",Occurrences=count,HitId=null};
            }
            else
            {
                groups.Add(key,result.Count);
                result.Add(row with{Text=count>1?text+$" ({count} occurrences)":text,Occurrences=count});
            }
        }
        return result;
    }
}
