using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
namespace SprocketExpandedBallistics;

internal sealed record ArmourComparisonEffect(string Kind,string Label,double? BeforeMm=null,double? AfterMm=null);
internal sealed record ArmourComparisonLayer(int Entity,string? MaterialId,string Label,string Role,double? ThicknessMm,
    double? AngleDegrees,bool Grouped,IReadOnlyList<ArmourComparisonEffect> Effects,double? PartMassKg=null);

// Read-only evidence from trials already requested by the comparison search.
// No extra native shot, raycast, state reset or per-material causal RHA estimate.
internal static class ArmourComparisonEvidence
{
    private sealed class Row
    {
        internal ArmourComparisonLayer Value;
        internal readonly List<ArmourComparisonEffect> Effects=new();
        internal Row(ArmourComparisonLayer value){Value=value;}
    }
    private sealed class Frame
    {
        internal readonly int Count;internal readonly List<Row> Rows=new();internal readonly List<Row> Current=new();
        internal double? Before;
        internal Frame(int count){Count=count;}
    }
    internal sealed class Trial:IDisposable
    {
        private readonly Trial? previous;
        internal readonly ArmourComparisonTarget Target;
        private readonly List<Row> rows=new();
        private readonly Dictionary<(IntPtr,short),Frame> frames=new();
        private readonly HashSet<(IntPtr,short,int,int)> blocks=new();
        internal bool Sealed;
        private bool disposed;
        internal Trial(ArmourComparisonTarget target){Target=target;previous=current;current=this;}
        internal IReadOnlyList<ArmourComparisonLayer> Freeze()=>Array.AsReadOnly(rows.Select(r=>r.Value with{Effects=Array.AsReadOnly(r.Effects.ToArray())}).ToArray());
        public void Dispose(){if(disposed)return;disposed=true;if(ReferenceEquals(current,this))current=previous;}
        internal void Begin(PenetrationSimulation sim,short index)
        {
            if(Sealed||index<0||index>=sim.FragmentCount||(sim.fragments[index].flags&FragmentFlag.OriginalPenetrator)==0)return;
            if(frames.Count<4096)frames[(sim.Pointer,index)]=new(sim.FragmentCount);
        }
        internal void Reached(NativeArmourBlockContext reached)
        {
            if(Sealed||reached.Impact.LiveImpact||!frames.TryGetValue((reached.Sim.Pointer,reached.Index),out var frame))return;
            var sim=reached.Sim;var block=reached.Block;var hits=reached.Intersections;
            int e=block.EnterIntersectionIndex,x=block.ExitIntersectionIndex;
            if(!block.Valid||block.MaterialIndex<0||e<0||x<0||e>=reached.IntersectionCount||x>=reached.IntersectionCount||e>=hits.Length||x>=hits.Length||
                !blocks.Add((sim.Pointer,reached.Index,e,x)))return;
            frame.Current.Clear();frame.Before??=Budget(sim,sim.fragments[reached.Index]);
            var indices=new HashSet<int>{hits[e].objectIndex,hits[x].objectIndex};
            if(block.IntersectedObjectIndices is {} extra)foreach(var member in extra){if(indices.Count>=32)break;indices.Add(member);}
            var grouped=indices.Count>1;
            foreach(var objectIndex in indices)
            {
                if(rows.Count>=24)break;
                if(objectIndex<0||objectIndex>=sim.objects.Length)continue;
                var entity=sim.objects[objectIndex].ID;
                if(!RuntimeArmourComparisonRunner.IsArmourObject(Target.Owner,Target.Snapshot,entity))continue;
                var component=Component(entity);if(component==null)continue;
                var material=component.TryCast<PlateStructure>()?.armourTechID;
                bool housing=false;
                if(component.TryCast<VehicleObjectModel>() is {} model&&Target.Snapshot.Bindings is {} bindings)
                {
                    if(bindings.TryGetCassette(model.ComponentID,out var cassette))material=cassette.MaterialId;
                    else if(bindings.TryGetMount(model.ComponentID,out _))housing=true;
                }
                var definition=SprocketExpandedBallistics.Materials.MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==material);
                var label=housing?"ERA housing":definition==null?EraDisplayName.Material(material):SprocketExpandedBallistics.Materials.MaterialDatabase.MenuLabel(definition);
                double? mass=null;
                try{mass=Finite(component.GetMass(MassType.Everything));}catch{/* Unknown mass is never replaced by an estimate. */}
                var row=new Row(new(entity,material,label,housing?"Passive housing; active charges are separate.":Role(material,Target.Snapshot),null,null,grouped,Array.Empty<ArmourComparisonEffect>(),mass));
                rows.Add(row);frame.Rows.Add(row);frame.Current.Add(row);
            }
        }
        private VehicleComponent? Component(int entity)
        {
            VehicleComponent? exact=null;var reader=Target.Owner.ObjectReader;var items=reader?.Items;
            if(items==null)return null;
            for(int i=0;i<reader!.Count;i++)
            {
                var behaviours=items[i]?.behaviours;if(behaviours==null)continue;
                foreach(var component in behaviours)
                {
                    if(component==null||component.VUID.Value!=entity)continue;
                    if(exact!=null&&exact.Pointer!=component.Pointer)return null;
                    exact=component;
                }
            }
            return exact;
        }
        internal void Update(PenetrationSimulation sim,short index,int entity,string material,double thickness,double angle)
        {
            if(Sealed||!frames.TryGetValue((sim.Pointer,index),out var frame))return;
            foreach(var row in frame.Current.Where(r=>r.Value.Entity==entity))
            {
                var definition=SprocketExpandedBallistics.Materials.MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==material);
                row.Value=row.Value with{MaterialId=material,Label=definition==null?EraDisplayName.Material(material):SprocketExpandedBallistics.Materials.MaterialDatabase.MenuLabel(definition),
                    Role=Role(material,Target.Snapshot),ThicknessMm=Finite(thickness),AngleDegrees=Finite(angle)};
            }
        }
        internal void Record(PenetrationSimulation sim,short index,string kind,string label,double? before,double? after,int? related)
        {
            if(Sealed||!frames.TryGetValue((sim.Pointer,index),out var frame)||frame.Current.Count==0)return;
            if(kind is not ("materialReaction" or "materialStop" or "materialPending" or "materialSupport" or "materialUnavailable" or "eraResidual" or "eraSpent" or "eraUnavailable" or "rodOrientation"))return;
            var row=related is {} id?rows.LastOrDefault(r=>r.Value.Entity==id):frame.Current[0];
            if(row==null)return;
            Add(row,new(kind,Short(label),Finite(before),Finite(after)));
        }
        internal void End(PenetrationSimulation sim,short index)
        {
            if(Sealed||!frames.Remove((sim.Pointer,index),out var frame)||frame.Rows.Count==0)return;
            var children=new List<Fragment>();
            for(int i=frame.Count;i<sim.FragmentCount;i++)
            {
                var child=sim.fragments[i];
                if(child.parentIndex==index&&(child.flags&FragmentFlag.OriginalPenetrator)!=0)children.Add(child);
            }
            double? after=children.Count==1?Budget(sim,children[0]):null;
            if(children.Count==1&&(children[0].speed<=0||(children[0].flags&FragmentFlag.Killed)!=0))after=0;
            Add(frame.Rows[0],new("blockResidual","Whole native block, including listed reactions; not an additive per-material contribution.",frame.Before,after));
        }
        private static void Add(Row row,ArmourComparisonEffect effect)
        {
            if(row.Effects.Contains(effect))return;
            if(row.Effects.Count<8){row.Effects.Add(effect);return;}
            if(effect.Kind is "materialReaction" or "materialStop" or "eraResidual")
            {
                var index=row.Effects.FindIndex(e=>e.Kind is not ("materialReaction" or "materialStop" or "eraResidual"));
                if(index>=0)row.Effects[index]=effect;
            }
        }
    }
    [ThreadStatic]private static Trial? current;
    internal static Trial Begin(ArmourComparisonTarget target)=>new(target);
    internal static void BeginFragment(PenetrationSimulation sim,short index)
    {if(!RuntimeArmourComparisonRunner.IsProbing)return;try{current?.Begin(sim,index);}catch(Exception){}}
    internal static void EndFragment(PenetrationSimulation sim,short index)
    {if(!RuntimeArmourComparisonRunner.IsProbing)return;try{current?.End(sim,index);}catch(Exception){}}
    internal static void Seal()
    {if(!RuntimeArmourComparisonRunner.IsProbing)return;try{if(current!=null)current.Sealed=true;}catch(Exception){}}
    internal static void Reached(NativeArmourBlockContext context)
    {if(!RuntimeArmourComparisonRunner.IsProbing)return;try{current?.Reached(context);}catch(Exception){}}
    internal static void Update(PenetrationSimulation sim,short index,int entity,string material,double thickness,double angle)
    {if(!RuntimeArmourComparisonRunner.IsProbing)return;try{current?.Update(sim,index,entity,material,thickness,angle);}catch(Exception){}}
    internal static void Record(PenetrationSimulation sim,short index,string kind,string label,double? before=null,double? after=null,int? related=null)
    {if(!RuntimeArmourComparisonRunner.IsProbing)return;try{current?.Record(sim,index,kind,label,before,after,related);}catch(Exception){}}
    private static string Role(string? material,ArmourCatalogueSnapshot snapshot)
    {
        var catalogue=snapshot.MaterialReactions;
        if(material!=null&&catalogue?.Bindings.TryGetValue(material,out var binding)==true)
        {
            var recipe=catalogue.Recipes[binding.RecipeId];
            return recipe.Mode switch{
                "sandwich"=>"Disrupts rods / jets with steel on both sides.",
                "backed"=>"Disrupts projectiles with steel backing.",
                "fragmentAbsorber"=>"Absorbs secondary fragments.",
                _=>"Passive resistance plus a material reaction."};
        }
        if(material!=null&&snapshot.Catalogue?.Responses.Any(r=>r.CompatibleMaterialIds.Contains(material)&&r.Kind is "lightEra" or "heavyEra")==true)
            return "One-use explosive reaction.";
        return "Passive armour resistance.";
    }
    private static double? Budget(PenetrationSimulation sim,Fragment f)
    {return f.projectileProfileIndex<sim.projectileProfiles.Length?Finite(f.GetBasePenetration(sim.projectileProfiles[f.projectileProfileIndex].penetratorConstant)*1000):null;}
    private static double? Finite(double? value)=>value is {} v&&double.IsFinite(v)&&v>=0?v:null;
    private static string Short(string value)=>value.Length<=380?value:value[..377]+"...";
}
