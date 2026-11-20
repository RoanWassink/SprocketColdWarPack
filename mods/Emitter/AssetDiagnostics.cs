namespace SprocketEmitter;

// Pure decisions shared by the runtime and the offline gate.
internal static class StockSource
{
    internal static bool MaterialValid(bool exists,bool shader,bool baseTexture)=>exists&&shader&&baseTexture;
    internal static bool Complete(bool casing,bool projectile,bool material)=>casing&&projectile&&material;
}
internal enum EjectLogDecision { None,Line,Suppression }
internal sealed class EjectLogBudget
{
    private int lines;
    internal EjectLogDecision Next() {
        if(lines<40){lines++;return EjectLogDecision.Line;}
        if(lines==40){lines++;return EjectLogDecision.Suppression;}
        return EjectLogDecision.None;
    }
}
