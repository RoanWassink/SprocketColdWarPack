using Sprocket.DamageModelling;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
namespace SprocketExpandedBallistics;

// Valid only during the native reached-block callback. No guessed material/face facts.
// 0.22.28: Materials is the MaterialInfo array native passed to GetNextMaterialBlock (null when unread).
internal readonly record struct NativeArmourBlockContext(PenetrationSimulation Sim,short Index,
    CompoundStructure Structure,MaterialBlock Block,Il2CppStructArray<StructureIntersection> Intersections,
    int IntersectionCount,ShellImpactContext Impact,Il2CppStructArray<MaterialInfo>? Materials=null)
{
    internal void RecordSpeedReduction(float before,float after)=>RuntimeArmourResponses.NotifyParentSpeedChange(Sim,Index);
}
