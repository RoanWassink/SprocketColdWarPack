namespace SprocketExpandedBallistics;

// Diagnostic deduplication only. This ledger never gates ObserveBlock, a response,
// speed write, projectile state or live ERA. Each impact owns a fresh instance.
internal readonly record struct ChemicalDiagnosticKey(IntPtr Simulation,short Fragment,int Entry,int Exit);
internal sealed class ChemicalDiagnosticLedger
{
    private readonly HashSet<ChemicalDiagnosticKey> geometry=new(),gaps=new();
    internal bool Geometry(ChemicalDiagnosticKey key)=>Admit(geometry,key);
    internal bool Gap(ChemicalDiagnosticKey key)=>Admit(gaps,key);
    internal static void Observe(Action action){try{action();}catch(Exception){/* Optional report reads must never interrupt native damage. */}}
    internal static string? HitKey(ChemicalDiagnosticKey key)=>key.Simulation!=IntPtr.Zero&&key.Fragment>=0&&key.Entry>=0&&key.Exit>=0?$"{key.Simulation}:{key.Fragment}:{key.Entry}:{key.Exit}":null;
    internal static string GeometryText(string key,int materialIndex,int entryId,string entryFlags,int exitId,string exitFlags,
        double entryMm,double exitMm,double rhaFactor,double density,double pathRhaMm,double? angle,bool grouped)
        =>FormattableString.Invariant($"Native ray block {key}; materialIndex={materialIndex}; entryVUID={entryId} flags={entryFlags}; exitVUID={exitId} flags={exitFlags}; entry={entryMm:0.###} mm; exit={exitMm:0.###} mm; signedSpan={exitMm-entryMm:0.###} mm; RHAfactor={rhaFactor:0.######}; density={density:0.###} kg/m3; pathRHAproxy={pathRhaMm:0.###} mm; entryAngleFromNormal={(angle is {} a&&double.IsFinite(a)?a.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture):"unknown")} deg; grouped={grouped}. Ray span is not face thickness.");
    private static bool Admit(HashSet<ChemicalDiagnosticKey> keys,ChemicalDiagnosticKey key)
    {try{return keys.Count<12&&keys.Add(key);}catch(Exception){return false;}}
}
