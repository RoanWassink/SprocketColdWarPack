using System.Reflection;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.AmmunitionStorage;

namespace SprocketCarouselAutoloader;

// The ballistics module owns allocation and finite typed compartments; this module
// owns physical geometry, mechanisms and the existing native damage carrier.
internal static class AmmoLoadoutBridge
{
    private delegate bool HasReader(Cannon cannon);
    private delegate int SupplyReader(Cannon cannon,AmmoRackBehaviour aggregate,int type,out AmmoRackBehaviour supply);
    private delegate bool SameReader(AmmoRackBehaviour aggregate,AmmoRackBehaviour view);
    private delegate void DestroyReader(AmmoRackBehaviour aggregate);
    private delegate int AvailableReader(Cannon cannon,AmmoRackBehaviour aggregate,int type);
    private delegate int TransferReader(Cannon cannon,AmmoRackBehaviour source,AmmoRackBehaviour destination,int type);
    private static HasReader? has;
    private static SupplyReader? supply;
    private static SameReader? same;
    private static DestroyReader? destroy;
    private static AvailableReader? available;
    private static TransferReader? transfer;
    private static long nextSearch;
    private static bool warned;
    private static void Resolve()
    {
        if(has!=null || Environment.TickCount64<nextSearch)return;
        nextSearch=Environment.TickCount64+1000;
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type=assembly.GetType("SprocketExpandedBallistics.AmmoLoadoutApi",false);
            if(type==null)continue;
            has=Bind<HasReader>(type,"HasLoadout");
            supply=Bind<SupplyReader>(type,"GetTypedSupply");
            same=Bind<SameReader>(type,"IsSameStorage");
            destroy=Bind<DestroyReader>(type,"ClearStorage");
            available=Bind<AvailableReader>(type,"GetAvailable");
            transfer=Bind<TransferReader>(type,"TryTransfer");
            break;
        }
    }
    private static T? Bind<T>(Type type,string name) where T:Delegate =>
        type.GetMethod(name,BindingFlags.Public|BindingFlags.Static)?.CreateDelegate<T>();
    private static void Warn(Exception ex)
    {if(!warned){warned=true;Plugin.ModLog.LogWarning("[Autoloaders] Carried ammunition integration unavailable; mixed feeds blocked: "+ex.Message);}}
    internal static bool HasLoadout(Cannon cannon)
    {
        try{Resolve();return has?.Invoke(cannon)==true;}
        catch(Exception ex){Warn(ex);return true;}
    }
    internal static int Supply(Cannon cannon,AmmoRackBehaviour aggregate,int type,out AmmoRackBehaviour? view)
    {
        view=null;
        try
        {
            Resolve();
            if(has?.Invoke(cannon)!=true)return 0;
            if(supply==null)return -1;
            var result=supply(cannon,aggregate,type,out var native);
            if(result==1)view=native;
            return result==0?-1:result;
        }
        catch(Exception ex){Warn(ex);return -1;}
    }
    internal static bool SameStorage(AmmoRackBehaviour aggregate,AmmoRackBehaviour? view)
    {
        if(view==null)return false;
        if(aggregate.Pointer==view.Pointer)return true;
        try{Resolve();return same?.Invoke(aggregate,view)==true;}
        catch(Exception ex){Warn(ex);return false;}
    }
    internal static void Destroy(AmmoRackBehaviour aggregate)
    {try{Resolve();destroy?.Invoke(aggregate);}catch(Exception ex){Warn(ex);}}
    internal static int Available(Cannon cannon,AmmoRackBehaviour aggregate,int type)
    {
        try{Resolve();if(has?.Invoke(cannon)!=true)return aggregate.StoredTypeID.Value==type?aggregate.AmountStored:0;return available?.Invoke(cannon,aggregate,type)??0;}
        catch(Exception ex){Warn(ex);return 0;}
    }
    internal static int Transfer(Cannon cannon,AmmoRackBehaviour source,AmmoRackBehaviour destination,int type)
    {
        try{Resolve();return has?.Invoke(cannon)==true?(transfer?.Invoke(cannon,source,destination,type)??-1):0;}
        catch(Exception ex){Warn(ex);return -1;}
    }
}
