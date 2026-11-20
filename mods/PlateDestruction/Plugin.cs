using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
namespace PlateDestructionP0;

[BepInPlugin("nero.sprocket.platedestruction.p0","Plate Destruction P0",Version)]
public sealed class Plugin:BasePlugin
{
    internal const string Version="0.8.3";
    internal static Plugin Instance=null!;
    internal static ConfigFile Cfg=null!;
    internal ConfigEntry<string> TargetMode=null!,KeyBreak=null!,KeyDent=null!,RebuildMode=null!,ArmourUpdateMode=null!,BlastHoleMode=null!;
    internal ConfigEntry<float> EffectScaleBoost=null!,EffectOffsetM=null!,DefaultPlateStrength=null!,BlastBase=null!,BlastRadiusPer100mm=null!,BlastStrengthPerMm=null!,MinCalibreMm=null!,KineticDepthMm=null!,KineticRadiusM=null!,BlastMaxDepthMm=null!,BlastRadiusFactor=null!,MaxDepthMm=null!,CellSizeM=null!,EdgeJitterM=null!,SpeedMin=null!,SpeedMax=null!,SpeedPerRatio=null!,LifetimeS=null!,SubdivTargetEdgeM=null!,SubdivCoreEdgeM=null!,HullTurretDentScale=null!,HullTurretBlastScale=null!,BlastHoleMaxRadiusM=null!,SmoothAngleDeg=null!,CraterLipRatio=null!,BlastBuckling=null!,TearCurlDepthMm=null!,TearCurlWidthM=null!,DebrisWarpMm=null!,DentFrameBudgetMs=null!,HeatBlastScale=null!;
    internal ConfigEntry<bool> EnableHitLog=null!,EnableInventory=null!,EnableRouteA=null!,EnableEffects=null!,DecalOnHit=null!,AffectPlateStructure=null!,AffectModelParts=null!,LogDecouplers=null!,LogEveryHitObject=null!,LogRawFields=null!,LogDamage=null!,DetailedLogging=null!,BreakEnabled=null!,PerSideBreak=null!,BuilderPatch=null!,KineticLogOnly=null!,DentEnabled=null!,DebrisEnabled=null!,DentNonDestructiblePlates=null!,SubdivEnabled=null!,LazyLowDetail=null!,ExitBlowoutEnabled=null!,PerCellThickness=null!,RimCapsEnabled=null!;
    internal ConfigEntry<int> EffectCalibreMm=null!,MaxLines=null!,SummaryEvery=null!,ForceBuildAfterMs=null!,MaxCount=null!,MaxKineticDentsPerShot=null!,SubdivMaxPasses=null!,SubdivMaxVertices=null!,RebuildCoalesceMs=null!,MaxAttachedParts=null!,MaxDentsPerFrame=null!,MaxNonDestructibleBlastDents=null!;
    public override void Load()
    {
        Instance=this;
        // The brief asks for this exact file name (not the default GUID-based one).
        Cfg=new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath,"sprocket.platedestruction.p0.cfg"),true);
        BreakEnabled=Cfg.Bind("Break","BreakEnabled",true,"Master switch: destructible plates break when enough blast damage has accumulated. Off = blasts are only counted and logged.");
        DefaultPlateStrength=Cfg.Bind("Break","DefaultPlateStrength",150f,"Blast strength used for a plate side when its thickness cannot be read.");
        BlastHoleMode=Cfg.Bind("Break","BlastHoleMode","Auto","How blasts tear open destructible plates: Auto = punch localized ragged holes on coarse/small plates (<180 tris or <=8 cells) and cell-tear fine plates; Always = localized ragged blast holes on all destructible plates; Off = legacy cell tearing only.");
        BlastHoleMaxRadiusM=Cfg.Bind("Break","BlastHoleMaxRadiusM",0.45f,"Maximum radius (m) of a single localized ragged blast hole.");
        BlastBase=Cfg.Bind("Blast","BlastBase",190f,"Blast damage to one plate side = BlastBase x (calibre / 100 mm)^3 x falloff(d), falloff(d) = 1 / (1 + (d / r)^2), r = BlastRadiusPer100mm x calibre / 100. d = distance from the blast to the closest point of that side's bounds.");
        BlastRadiusPer100mm=Cfg.Bind("Blast","BlastRadiusPer100mm",0.5f,"Falloff radius r (m) of a 100 mm blast; scales linearly with the calibre. A blast does nothing beyond 4 x r.");
        BlastStrengthPerMm=Cfg.Bind("Blast","BlastStrengthPerMm",5f,"Strength of one plate side = BlastStrengthPerMm x mean thickness (mm), no area factor. Default: a 3.2 mm skirt (16) is torn off by a 57 mm HE shell right next to it (not by 20-30 mm); a 20 mm plate (100) needs 105 mm HE close by or 152/155 mm HE within about 1 m.");
        MinCalibreMm=Cfg.Bind("Blast","MinCalibreMm",20f,"Explosions below this calibre (mm) are ignored.");
        HeatBlastScale=Cfg.Bind("Blast","HeatBlastScale",0.35f,"Blast damage and dent scale applied to HEAT shaped-charge visual explosions (0 = ignore HEAT blast, 1 = full HE blast).");
        KineticLogOnly=Cfg.Bind("Kinetic","LogOnly",true,"Kinetic hits (AP/APFSDS) never tear off a plate (they make holes and later dents). They are only logged ([KIN] lines). false is not supported yet and behaves as true.");
        LogRawFields=Cfg.Bind("Diagnostics","LogRawFields",false,"Once per shot, log the raw fragment fields and the CompoundStructure.HitBuffer ([RAW] lines). Switch on without a rebuild.");
        LogDamage=Cfg.Bind("Diagnostics","LogDamage",true,"Log one [DMG] line per blast that damages a destructible plate side, one [KIN] line per kinetic hit on one, and one [PLATE] line per plate with its blast strength.");
        DetailedLogging=Cfg.Bind("Diagnostics","DetailedLogging",false,"Verbose diagnostic logs ([HIT] per-fragment lines, [INVENTORY], [ERAPROBE], [ARMOUR] pass lines, per-part [ATTACHED] lines). Turn off for lowest overhead.");
        KeyBreak=Cfg.Bind("Keys","Break","Delete","Key (UnityEngine.InputSystem.Key name) that tears off the side or plate of the last targeted hit now, only if it is destructible. Play only (test key).");
        EnableHitLog=Cfg.Bind("Switches","HitLog",true,"Subscribe to DamageModel.SimulationRan. Needed for damage counting.");
        EnableInventory=Cfg.Bind("Switches","Inventory",true,"Log an armour/health/decoupler inventory per vehicle when it is first seen in Play.");
        EnableRouteA=Cfg.Bind("Switches","RouteA",true,"Allow a break to remove the plate from the damage model (the two BuildDamageModelColliders prefixes). Off = the plate is only hidden.");
        EnableEffects=Cfg.Bind("Switches","Effects",true,"Play native effects (explosion when a plate or side tears off, decal on a kinetic hit if DecalOnHit is on).");
        AffectPlateStructure=Cfg.Bind("Parts","PlateStructure",true,"PlateStructure plates can be targeted.");
        AffectModelParts=Cfg.Bind("Parts","VehicleObjectModel",false,"Model parts (VehicleObjectModel with damage colliders) can be targeted. They have no Destructable checkbox, so they never break.");
        LogDecouplers=Cfg.Bind("Switches","LogDecouplers",true,"Inventory lists components that carry a PartDecoupler.");
        LogEveryHitObject=Cfg.Bind("Switches","LogEveryHitObject",false,"One line per hit object (otherwise only per shot).");
        MaxLines=Cfg.Bind("Limits","MaxHitLines",200,"Max hit detail lines per session; afterwards only a summary every SummaryEvery shots.");
        SummaryEvery=Cfg.Bind("Limits","SummaryEvery",50,"Summary interval (shots) after the line limit is reached.");
        TargetMode=Cfg.Bind("Target","TargetMode","FirstPlate","FirstPlate = the first eligible component the original penetrator hits (entry plate, default); LastPlate = the last one (0.0.3 behaviour, for comparison).");
        RebuildMode=Cfg.Bind("RouteA","RebuildMode","Raise","Raise = RaiseDirtyFlags(DamageModel) only; RaiseAndBuild = also call Vehicle.Build() if the rebuild did not happen within ForceBuildAfterMs.");
        ForceBuildAfterMs=Cfg.Bind("RouteA","ForceBuildAfterMs",1000,"Only for RaiseAndBuild: wait this long before calling Vehicle.Build().");
        ArmourUpdateMode=Cfg.Bind("RouteA","ArmourUpdateMode","InPlace","InPlace = degenerate broken triangles directly in the live DamageModel (sub-millisecond, zero stutter); Rebuild = coalesced RaiseDirtyFlags(DamageModel).");
        RebuildCoalesceMs=Cfg.Bind("RouteA","RebuildCoalesceMs",200,"Coalescing delay (ms) when ArmourUpdateMode=Rebuild or when falling back to a full DamageModel rebuild.");
        EffectCalibreMm=Cfg.Bind("Effects","EffectCalibreMm",120,"Calibre in mm passed to the effects (the gun calibre is not available at the hit; the penetrator diameter is too small to see).");
        EffectScaleBoost=Cfg.Bind("Effects","EffectScaleBoost",3f,"Temporary multiplier on the explosion asset MinScale/MaxScale during the break explosion (restored right after). 1 = off.");
        EffectOffsetM=Cfg.Bind("Effects","EffectOffsetM",0.02f,"Move the effect position this far along the surface normal toward the shooter (m), so it is in front of the plate.");
        DecalOnHit=Cfg.Bind("Effects","DecalOnHit",false,"Play our own penetration decal on a kinetic hit on a destructible plate. Off by default: the game draws its own hole.");
        PerSideBreak=Cfg.Bind("Break","PerSide",true,"A mirrored pair (left and right skirt is ONE plate structure) breaks one side at a time: the side that is hit first. Off = both sides break together (0.1.1 behaviour). Needs the builder patch.");
        BuilderPatch=Cfg.Bind("Switches","BuilderPatch",true,"Patch DamageModelStructureBuilder.AddMesh/AddBox (log of every call of a destructible plate; drops the armour of a broken half). Off = per-side break is unavailable.");
        DentEnabled=Cfg.Bind("Dent","Enabled",true,"Visual dents on destructible plates: kinetic hits make a small dent, blasts that do not tear a side off make a bigger one (vertex displacement of a private copy of the plate mesh; the armour does not change). Off = no dents.");
        KineticDepthMm=Cfg.Bind("Dent","KineticDepthMm",30f,"Depth (mm) of the dent of one kinetic hit.");
        KineticRadiusM=Cfg.Bind("Dent","KineticRadiusM",0.15f,"Radius (m) of the dent of one kinetic hit.");
        BlastMaxDepthMm=Cfg.Bind("Dent","BlastMaxDepthMm",120f,"Depth (mm) of the dent of a blast that is just below tearing the side off: depth = BlastMaxDepthMm x q, q = this blast's damage / the strength of the side (0..1).");
        BlastRadiusFactor=Cfg.Bind("Dent","BlastRadiusFactor",0.6f,"Radius of a blast dent = clamp(BlastRadiusFactor x blast falloff radius r, 0.10, 1.0) m.");
        MaxDepthMm=Cfg.Bind("Dent","MaxDepthMm",150f,"Dents add up; the total displacement of one vertex never exceeds this (mm).");
        DentNonDestructiblePlates=Cfg.Bind("Dent","DentNonDestructiblePlates",true,"Also dent ordinary (non-destructible) hull/turret/addon plates on kinetic and blast hits (visual deformation only).");
        MaxDentsPerFrame=Cfg.Bind("Dent","MaxDentsPerFrame",2,"Maximum visual mesh dents processed per frame; remaining dents stay queued for following frames to avoid frame spikes.");
        DentFrameBudgetMs=Cfg.Bind("Dent","DentFrameBudgetMs",4f,"Soft per-frame wall-time budget (ms) for mesh denting after the first dent of the frame.");
        MaxNonDestructibleBlastDents=Cfg.Bind("Dent","MaxNonDestructibleBlastDents",3,"Maximum closest non-destructible hull/turret/addon plates dented by a single HE/HESH blast event.");
        MaxKineticDentsPerShot=Cfg.Bind("Dent","MaxKineticDentsPerShot",4,"Maximum distinct PlateStructures/impacts dented along one shot's penetrator path.");
        SubdivEnabled=Cfg.Bind("Dent","SubdivEnabled",true,"Locally subdivide coarse plates around impacts and torn edges for realistic crater/buckling geometry.");
        SubdivTargetEdgeM=Cfg.Bind("Dent","SubdivTargetEdgeM",0.065f,"Outer target triangle edge length (m) around impact craters and torn cell borders when subdividing.");
        SubdivCoreEdgeM=Cfg.Bind("Dent","SubdivCoreEdgeM",0.032f,"Finer target triangle edge length (m) right inside the crater core, forged rim, and ricochet groove.");
        SubdivMaxPasses=Cfg.Bind("Dent","SubdivMaxPasses",5,"Maximum local subdivision passes per impact/tear event.");
        SubdivMaxVertices=Cfg.Bind("Dent","SubdivMaxVertices",20000,"Safety cap on vertices per dented plate mesh to bound memory and GPU cost.");
        HullTurretDentScale=Cfg.Bind("Dent","HullTurretDentScale",1.45f,"Visual depth multiplier for kinetic craters and ricochet gouges on non-destructible hull/turret plates.");
        HullTurretBlastScale=Cfg.Bind("Dent","HullTurretBlastScale",1.8f,"Visual buckling strength boost for HE blast dents on non-destructible hull/turret plates.");
        ExitBlowoutEnabled=Cfg.Bind("Dent","ExitBlowoutEnabled",true,"Create an outward petaled exit blowout when a penetrator punches out the far side of a hull/turret plate.");
        SmoothAngleDeg=Cfg.Bind("Dent","SmoothAngleDeg",50f,"Crease threshold angle (deg) for spatial-weld normal smoothing across split-vertex seams.");
        CraterLipRatio=Cfg.Bind("Dent","CraterLipRatio",0.28f,"Height of the raised forged metal lip around kinetic impact craters relative to crater depth.");
        BlastBuckling=Cfg.Bind("Dent","BlastBuckling",0.45f,"Strength of multi-frequency sheet-metal buckling and tension creases in blast dents.");
        TearCurlDepthMm=Cfg.Bind("Dent","TearCurlDepthMm",95f,"Outward curl/petal deformation depth (mm) on surviving mesh edges next to torn cells.");
        TearCurlWidthM=Cfg.Bind("Dent","TearCurlWidthM",0.22f,"Width (m) of the curled band along torn cell borders.");
        DebrisWarpMm=Cfg.Bind("Dent","DebrisWarpMm",70f,"Bending, buckling and twist magnitude (mm) applied to detached flying debris meshes.");
        RimCapsEnabled=Cfg.Bind("Dent","RimCapsEnabled",true,"0.8.0: generate solid sheared-steel cross-section walls along torn cell edges and blast holes so armor thickness is visible.");
        LazyLowDetail=Cfg.Bind("Dent","LazyLowDetail",true,"Defer cloning/updating a plate's low-detail LOD mesh until its renderer actually becomes visible.");
        DebrisEnabled=Cfg.Bind("Debris","Enabled",true,"The cells torn off by one blast fly away as one piece (one object per blast and side) together with the attached parts of those cells and lies on the ground (own ballistic integration, no physics objects). Off = it only disappears.");
        MaxCount=Cfg.Bind("Debris","MaxCount",16,"At most this many debris objects exist; the oldest is removed first.");
        MaxAttachedParts=Cfg.Bind("Debris","MaxAttachedParts",24,"Maximum attached part meshes carried by a single debris object.");
        LifetimeS=Cfg.Bind("Debris","LifetimeS",120f,"A debris object is removed after this many seconds.");
        SpeedMin=Cfg.Bind("Debris","SpeedMin",3f,"Launch speed (m/s) = clamp(SpeedMin + SpeedPerRatio x D/strength, SpeedMin, SpeedMax), D = damage of the tearing blast.");
        SpeedMax=Cfg.Bind("Debris","SpeedMax",12f,"Upper limit of the launch speed (m/s).");
        SpeedPerRatio=Cfg.Bind("Debris","SpeedPerRatio",4f,"Extra launch speed (m/s) per unit of D/strength.");
        CellSizeM=Cfg.Bind("Cells","CellSizeM",0.5f,"0.4.1: each destructible plate side is split into cells of about this size (m) in the plane of its two largest extents. A blast breaks single cells (damage and strength per cell), not the whole side. Read when the plate is adopted.");
        EdgeJitterM=Cfg.Bind("Cells","EdgeJitterM",0.08f,"0.4.1: cell edges are ragged: a smooth seeded offset of up to this size (m) is added to the triangle centroid before it is assigned to a cell (the same offset in the render meshes and the armour).");
        PerCellThickness=Cfg.Bind("Cells","PerCellThickness",true,"0.8.0: compute each destructible cell's blast strength from its own local armor thickness instead of one plate-wide average (keeps 80 mm+ glacis realistic while thin fenders/skirts still tear easily).");
        KeyDent=Cfg.Bind("Keys","Dent","End","Key (UnityEngine.InputSystem.Key name) that puts a test dent (blast style, q 0.5, radius 155 mm) at the entry point of the last shot on a destructible plate. Play only (test key).");
        try
        {
            // Always patch: the editor checkbox needs it. The collider prefixes only act while the break state excludes a plate.
            var harmony=new Harmony("nero.sprocket.platedestruction.p0");
            harmony.PatchAll(typeof(Patches));
            Log.LogInfo("[PD-P0] patches installed: "+harmony.GetPatchedMethods().Count()+" (Destructable checkbox postfix, 2 collider prefixes, ProjectileImpactEffect.PlayEffect scale/blast prefix + finalizer).");
        }
        catch(Exception e){Log.LogError("[PD-P0] patching failed, checkbox and plate removal disabled: "+e);}
        if(BuilderPatch.Value)
        {
            try
            {
                var bh=new Harmony("nero.sprocket.platedestruction.p0.builder");
                bh.PatchAll(typeof(BuilderPatches));
                BuilderPatches.Installed=true;
                Log.LogInfo("[PD-P0] builder patches installed: "+bh.GetPatchedMethods().Count()+" (DamageModelStructureBuilder.AddMesh 6- and 9-argument prefixes, AddBox log prefix).");
            }
            catch(Exception e){Log.LogError("[PD-P0] builder patching failed, per-side break disabled (plates break as one piece): "+e);}
        }
        else Log.LogInfo("[PD-P0] builder patches switched off (Switches/BuilderPatch=false): per-side break disabled.");
        AddComponent<Runtime>();
        Log.LogInfo("Plate Destruction P0 "+Version+" loaded: Destructable plates dent under hits, break cell by cell under blasts and the torn piece flies away. Test keys: "+KeyBreak.Value+" (tear off the last targeted plate), "+KeyDent.Value+" (test dent).");
    }
    internal static void Info(string s)=>Instance.Log.LogInfo("[PD-P0] "+s);
    internal static void Warn(string s)=>Instance.Log.LogWarning("[PD-P0] "+s);
}
