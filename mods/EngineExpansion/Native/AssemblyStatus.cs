using Sprocket.Vehicles.Engines;
using Sprocket.Engines;
namespace SprocketEngineExpansion.Native;

// Adapter contract: the performance module does not depend on the owned mesh implementation.
internal static class AssemblyStatus
{
    internal static Func<EngineBlueprint,bool> IsValid=_=>false;
    internal static Func<EngineBehaviour,bool> IsBehaviourValid=_=>false;
    [ThreadStatic]internal static CombustionEngine? EditorContext;
    internal static bool ValidFor(EngineBlueprint blueprint)=>IsValid(blueprint);
    internal static bool ValidFor(EngineBehaviour behaviour)=>IsBehaviourValid(behaviour);
}
