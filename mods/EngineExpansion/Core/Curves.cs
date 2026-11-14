namespace SprocketEngineExpansion.Core;

public readonly record struct Point(double X, double Y);
public sealed class Curve
{
    public Point[] Points { get; }
    public Curve(IEnumerable<Point> points)
    {
        Points = points.ToArray();
        if (Points.Length < 2 || Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.Y < 0))
            throw new ArgumentException("Curve requires finite nonnegative values and at least two points.");
        for (int i = 1; i < Points.Length; i++)
            if (Points[i].X <= Points[i-1].X) throw new ArgumentException("Curve coordinates must increase.");
    }
    public double At(double x)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (x <= Points[0].X) return Points[0].Y;
        for (int i=1;i<Points.Length;i++)
            if(x<=Points[i].X) return Points[i-1].Y+(Points[i].Y-Points[i-1].Y)*(x-Points[i-1].X)/(Points[i].X-Points[i-1].X);
        return Points[^1].Y;
    }
    // Exact extremum of x * (a*x+b), including stationary points inside segments.
    public double MaxProduct(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min < 0 || max < min) throw new ArgumentException("Invalid operating range.");
        var nodes=Points.Select(p=>p.X).Where(x=>x>min&&x<max).Prepend(min).Append(max).ToArray();
        double result=0;
        for(int i=1;i<nodes.Length;i++)
        {
            double l=nodes[i-1],r=nodes[i],a=(At(r)-At(l))/(r-l),b=At(l)-a*l;
            result=Math.Max(result,Math.Max(l*At(l),r*At(r)));
            if(a<0){double peak=-b/(2*a);if(peak>l&&peak<r) result=Math.Max(result,peak*At(peak));}
        }
        return result;
    }
}
