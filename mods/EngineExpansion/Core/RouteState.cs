using System.Text.Json;
namespace SprocketEngineExpansion.Core;

internal static class RouteState
{
    internal const int MaxBends=32;
    private sealed record Payload(int Schema,double Order);
    internal static string Encode(double order)
    {
        if(!double.IsFinite(order))throw new ArgumentException("Nonfinite route order.");
        return JsonSerializer.Serialize(new Payload(1,order));
    }
    internal static double Decode(string raw)
    {
        try
        {
            using var doc=JsonDocument.Parse(raw);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Route state must be an object.");
            var fields=doc.RootElement.EnumerateObject().ToArray();
            if(fields.Length!=2||fields.Count(f=>f.Name=="Schema")!=1||fields.Count(f=>f.Name=="Order")!=1)
                throw new ArgumentException("Unsupported route fields.");
            var state=JsonSerializer.Deserialize<Payload>(raw)??throw new ArgumentException("Empty route state.");
            if(state.Schema!=1||!double.IsFinite(state.Order))throw new ArgumentException("Unsupported route state.");
            return state.Order;
        }
        catch(JsonException ex){throw new ArgumentException("Invalid route state.",ex);}
    }
    // Each segment is identified by its right-hand point's order. The outlet is order 1.
    internal static double Split(double left,double right,int bendCount)
    {
        if(!double.IsFinite(left)||!double.IsFinite(right)||left<0||right>1||left>=right||bendCount<0||bendCount>=MaxBends)
            throw new ArgumentException("Pipe cannot be split.");
        double middle=left+(right-left)*.5;
        if(middle<=left||middle>=right)throw new ArgumentException("Route order precision exhausted.");
        return middle;
    }
    internal static void Validate(IReadOnlyList<double> bends,IReadOnlyList<double> segments)
    {
        if(bends.Count>MaxBends||segments.Count!=bends.Count+1)throw new ArgumentException("Incomplete pipe topology.");
        double previous=0;
        for(int i=0;i<bends.Count;i++)
        {
            double order=bends[i];
            if(!double.IsFinite(order)||order<=previous||order>=1||segments[i]!=order)throw new ArgumentException("Ambiguous pipe topology.");
            previous=order;
        }
        if(segments[^1]!=1)throw new ArgumentException("Missing outlet segment.");
    }
}
