using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
namespace SprocketEmitter;

internal static class GunCaliber
{
    // Called only from Throw on the main thread. No native references survive this call.
    internal static GunSize Read(Vehicle vehicle) {
        try {
            if(vehicle==null||vehicle.Released)return GunSize.Invalid("owner unavailable");
            var items=vehicle.ObjectReader?.Items;
            if(items==null)return GunSize.Invalid("no cannon list");
            GunCandidate? best=null;Cannon? winner=null;
            int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for(int i=0;i<n;i++) {
                var obj=items[i];if(obj==null||obj.Released)continue;
                var components=obj.Components;if(components==null)continue;
                int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int j=0;j<count;j++) {
                    var cannon=components[j]?.TryCast<Cannon>();
                    if(cannon==null||!cannon.IsInstalled)continue;
                    var owner=cannon.VehicleObject;if(owner==null||owner.Released)continue;
                    var blueprint=cannon.Blueprint;if(blueprint==null)continue;
                    var candidate=new GunCandidate(blueprint.Caliber,cannon.VUID.Value,true,false);
                    if(candidate.Beats(best)){best=candidate;winner=cannon;}
                }
            }
            if(winner==null||!best.HasValue)return GunSize.Invalid("no installed cannon with valid caliber");
            // Select first, validate the winner second. Never silently substitute a smaller cannon.
            var shell=winner.ShellBlueprint;
            return shell==null?GunSize.Validate(best.Value,false,0,0):
                GunSize.Validate(best.Value,true,shell.Diameter,shell.PropellantLength);
        }catch(Exception e) {
            // An unreadable candidate could be the largest: fail this whole read rather than guessing.
            return GunSize.Invalid("cannon read failed ("+e.GetType().Name+")");
        }
    }
}
