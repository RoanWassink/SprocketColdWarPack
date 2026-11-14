using Sprocket.Vehicles;
using UnityEngine;
using SprocketExpandedBallistics.Materials;

namespace SprocketExpandedBallistics;

// Maps a physical hit or a visual triangle onto the same bounded surface grid.
// The collision mesh is the calibration source: visual covers may be thinner,
// but the active slab's scaled local X/Z bounds define the paid cell partition.
internal static class PlacedEraCells
{
    internal static bool Partitioned(EraVisualPreset preset) => preset.Resizable && preset.CellPitchM>0 && preset.MaxCells>0;

    // Relikt and the modular Nizh/Duplet panels keep their curled rims and bolt stubs when
    // every cell is spent, so they never switch the whole renderer off.
    internal static bool KeepsSpentVisual(EraVisualPreset preset)
        =>preset.PartGuid==CompositeModule.PartGuid||preset.WholeCells&&ModularCellSpec.ForVariant(preset.ModularVariant).ProceduralPanel;

    // Spent coordinates are retained by the ledger across resize/undo. Only
    // cells that still exist in the current layout may affect the current
    // cover/visibility; stale out-of-range indices must not hide a whole layer.
    internal static bool AllSpent(HashSet<int>? spent,ModularCellLayout layout)
        =>ModularCellGeometry.AllSpent(spent,layout.CountU,layout.CountV);

    internal static bool AllSpent(HashSet<int>? spent,EraCellLayout layout)
        =>ModularCellGeometry.AllSpent(spent,layout.CountU,layout.CountV);

    internal static bool ValidScale(Vector3 scale)
    {
        if(!float.IsFinite(scale.x)||!float.IsFinite(scale.y)||!float.IsFinite(scale.z))return false;
        if(scale.x==0||scale.y==0||scale.z==0)return false;
        // Depth is locked. X/Z may grow or mirror (negative); Y scaling would
        // either dilute the paid normal thickness or grant free depth, so a
        // non-unit magnitude Y fails closed.
        return EraCellPartition.DepthLocked(scale.y);
    }

    internal static bool TryLayout(VehicleObjectModel model,EraVisualPreset preset,out EraCellLayout layout)
    {
        layout=default;
        if(!Partitioned(preset))return false;
        var scale=model.GetModelScale();
        if(!ValidScale(scale))return false;
        if(preset.PartGuid==CompositeModule.PartGuid){layout=CompositeModuleEraGeometry.Layout(scale.x,scale.z);return true;}
        if(preset.WholeCells)
        {
            // Count follows the native X/Z scale; bounds are always whole cells.
            return EraCellPartition.TryModular(scale.x,scale.z,preset.CellWidthM,preset.CellLengthM,
                preset.MaxCountU,preset.MaxCountV,out layout,out _,preset.FixedCountV);
        }
        if(preset.CellMaxU>preset.CellMinU&&preset.CellMaxV>preset.CellMinV)
        {
            if(!TryAxis(preset.SurfaceAxes,out var uAxis,out var vAxis))return false;
            var u0=preset.CellMinU*AxisScale(scale,uAxis);var u1=preset.CellMaxU*AxisScale(scale,uAxis);
            var v0=preset.CellMinV*AxisScale(scale,vAxis);var v1=preset.CellMaxV*AxisScale(scale,vAxis);
            return EraCellPartition.TryLayout(Math.Min(u0,u1),Math.Max(u0,u1),Math.Min(v0,v1),Math.Max(v0,v1),
                preset.CellPitchM,preset.MaxCells,out layout,out _);
        }
        if(model.Model?.CollisionMesh is not {} mesh)return false;
        return TryLayout(mesh.vertices,scale,preset.CellPitchM,preset.MaxCells,preset.SurfaceAxes,out layout);
    }

    internal static bool TryModularCounts(Vector3 scale,EraVisualPreset preset,out int countU,out int countV)
    {
        countU=0;countV=0;
        if(!preset.WholeCells||!ValidScale(scale))return false;
        return EraCellPartition.TryModular(scale.x,scale.z,preset.CellWidthM,preset.CellLengthM,
            preset.MaxCountU,preset.MaxCountV,out var layout,out _,preset.FixedCountV)
            && (countU=layout.CountU)>0 && (countV=layout.CountV)>0;
    }

    internal static bool TryLayout(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> vertices,
        Vector3 scale,double pitch,int maxCells,string axes,out EraCellLayout layout)
    {
        layout=default;
        if(vertices==null||vertices.Length<3||!ValidScale(scale))return false;
        if(!TryAxis(axes,out var axisU,out var axisV))return false;
        var minU=double.PositiveInfinity;var maxU=double.NegativeInfinity;
        var minV=double.PositiveInfinity;var maxV=double.NegativeInfinity;
        for(var i=0;i<vertices.Length;i++)
        {
            var v=vertices[i];
            if(!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z))return false;
            var p=Vector3.Scale(v,scale);
            var u=Component(p,axisU);var w=Component(p,axisV);
            minU=Math.Min(minU,u);maxU=Math.Max(maxU,u);
            minV=Math.Min(minV,w);maxV=Math.Max(maxV,w);
        }
        return EraCellPartition.TryLayout(minU,maxU,minV,maxV,pitch,maxCells,out layout,out _);
    }

    internal static bool TryIndices(VehicleObjectModel model,EraVisualPreset preset,Vector3 worldPosition,
        out EraCellLayout layout,out int x,out int y)
    {
        x=0;y=0;
        if(!TryLayout(model,preset,out layout))return false;
        var transform=model.VehicleTransform;
        if(transform==null)return false;
        if(!TryAxis(preset.SurfaceAxes,out var axisU,out var axisV))return false;
        if(preset.PartGuid==CompositeModule.PartGuid)
        {
            var local=transform.WorldToLocalSpace(worldPosition);
            return CompositeModuleEraGeometry.TryCell(local.x,local.z,out x,out y);
        }
        if(preset.WholeCells)
        {
            // Full inverse world transform preserves mirror, rotation and any
            // hierarchy transform. Multiplying by the part's own scale returns
            // the canonical, whole-cell metric frame used by the generator.
            var scale=model.GetModelScale();
            Vector3 canonical;
            try
            {
                var local=transform.WorldToLocalSpace(worldPosition);
                // WorldToLocalSpace already inverted the signed native scale
                // (mirror). Multiplying by the magnitude restores the canonical
                // cell frame while keeping mirrored cells on the correct side.
                canonical=new Vector3(local.x*Math.Abs(scale.x),local.y*Math.Abs(scale.y),local.z*Math.Abs(scale.z));
            }
            catch
            {
                // Rotation-only fallback returns sign*canonical for a mirrored
                // part; undo the sign on the two surface axes.
                var worldLocal=Quaternion.Inverse(transform.Rotation)*(worldPosition-transform.Position);
                canonical=new Vector3(worldLocal.x*(scale.x<0?-1:1),worldLocal.y,worldLocal.z*(scale.z<0?-1:1));
            }
            if(!float.IsFinite(canonical.x)||!float.IsFinite(canonical.y)||!float.IsFinite(canonical.z))return false;
            return EraCellPartition.TryCell(layout,Component(canonical,axisU),Component(canonical,axisV),out x,out y);
        }
        // Inverse part rotation maps a world hit to the same metre-scaled local
        // frame as vertex*scale. This preserves rotation, mirroring and nonuniform
        // X/Z scale without assuming a bbox depth.
        var legacyLocal=Quaternion.Inverse(transform.Rotation)*(worldPosition-transform.Position);
        return EraCellPartition.TryCell(layout,Component(legacyLocal,axisU),Component(legacyLocal,axisV),out x,out y);
    }

    // Visual triangles use the same scaled local frame; pass the collision-derived
    // layout so a cover plate cannot invent a different cell origin.
    internal static bool TriangleCell(in EraCellLayout layout,Vector3 a,Vector3 b,Vector3 c,Vector3 scale,string axes,
        out int x,out int y)
    {
        x=0;y=0;
        var pa=Vector3.Scale(a,scale);var pb=Vector3.Scale(b,scale);var pc=Vector3.Scale(c,scale);
        if(!TryAxis(axes,out var axisU,out var axisV))return false;
        return EraCellPartition.TryTriangleCell(layout,
            Component(pa,axisU),Component(pa,axisV),
            Component(pb,axisU),Component(pb,axisV),
            Component(pc,axisU),Component(pc,axisV),out x,out y);
    }

    private static bool TryAxis(string? axes,out int u,out int v)
    {
        u=0;v=2;
        switch(axes)
        {
            case "xz":u=0;v=2;return true;
            case "zx":u=2;v=0;return true;
            case "xy":u=0;v=1;return true;
            case "yx":u=1;v=0;return true;
            case "yz":u=1;v=2;return true;
            case "zy":u=2;v=1;return true;
            default:return false;
        }
    }

    private static double Component(Vector3 v,int axis)=>axis switch{0=>v.x,1=>v.y,_=>v.z};
    private static double AxisScale(Vector3 scale,int axis)=>axis switch{0=>scale.x,1=>scale.y,_=>scale.z};
}
