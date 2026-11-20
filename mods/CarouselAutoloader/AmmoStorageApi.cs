using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.AmmunitionStorage;

namespace SprocketCarouselAutoloader;

/// <summary>Physical storage adapter. Counts are shared slots, never an extra ammunition pool.</summary>
public static class AmmoStorageApi
{
    public static int MixedStorageApiVersion=>2;
    public static int GetCarouselCapacity(Cannon cannon,out int basketVuid,out int capacity)
    {
        basketVuid=-1;capacity=0;
        try
        {
            var assigned=CarouselRuntime.Baskets(cannon).Select(CarouselRuntime.State)
                .Where(s=>s.Enabled&&s.CannonId==cannon.VUID.Value).ToArray();
            if(assigned.Length==0)return 0;
            if(assigned.Length!=1)return -1;
            basketVuid=assigned[0].Basket.VUID.Value;
            capacity=CarouselRuntime.Calculate(assigned[0].Basket,cannon).Capacity;
            return capacity>0?1:-1;
        }
        catch{return -1;}
    }
    public static int GetCarouselCapacityForGeometry(Cannon cannon,double projectile,double charge,double diameter,out int capacity)
    {
        capacity=0;
        try
        {
            var assigned=CarouselRuntime.Baskets(cannon).Select(CarouselRuntime.State)
                .Where(s=>s.Enabled&&s.CannonId==cannon.VUID.Value).ToArray();
            if(assigned.Length==0)return 0;
            if(assigned.Length!=1)return -1;
            capacity=CarouselRuntime.Calculate(assigned[0].Basket,cannon,new LoaderShellSize(1,projectile,charge,diameter)).Capacity;
            return capacity>0?1:-1;
        }
        catch{return -1;}
    }
    /// <returns>1: autoloader rack for this cannon (capacity written), 0: not a ready loader rack for this cannon, -1: error.</returns>
    public static int GetRackCapacityForGeometry(Cannon cannon,AmmoRack rack,double projectile,double charge,double diameter,out int capacity)
    {
        capacity=0;
        try
        {
            if(cannon==null||rack==null||!BustleRuntime.IsPart(rack))return 0;
            var state=BustleRuntime.State(rack);
            if(!state.Active||(state.CannonId>=0&&state.CannonId!=cannon.VUID.Value))return 0;
            capacity=BustleRuntime.Capacity(state,cannon,new LoaderShellSize(1,projectile,charge,diameter));
            return 1;
        }
        catch{return -1;}
    }
    public static bool IsReadyMagazine(Cannon cannon,AmmoRack rack)
    {
        try
        {
            if(cannon==null||rack==null||!BustleRuntime.IsPart(rack))return false;
            var state=BustleRuntime.State(rack);
            return state.Active&&state.CannonId==cannon.VUID.Value&&CarouselRuntime.SameTurret(rack,cannon);
        }
        catch{return false;}
    }
    /// <returns>1 when only the ready magazine can currently supply rounds; 0 when all stores are reachable or no restriction applies.</returns>
    public static int GetReachableStorage(Cannon cannon,out AmmoRackBehaviour? readyOnlyAggregate,out string? reason)
    {
        readyOnlyAggregate=null;reason=null;
        try
        {
            if(cannon==null)return 0;
            foreach(var basket in CarouselRuntime.Baskets(cannon))
            {
                var state=CarouselRuntime.State(basket);
                if(!state.Active||state.CannonId!=cannon.VUID.Value||!basket.IsInstalled||basket.HealthFraction<=0||state.Rack==null)continue;
                bool hasCrew=SemiAutoloader.HasCrew(cannon);
                if(RefillPolicy.ReadyMagazineOnly(true,true,false,false,hasCrew))
                {
                    readyOnlyAggregate=state.Rack;
                    reason="no crew loader";
                    return 1;
                }
                return 0;
            }
            foreach(var state in BustleRuntime.Racks(cannon))
            {
                if(!BustleRuntime.Eligible(state,cannon)||state.Rack.behaviour is not {} mag)continue;
                bool full=mag.Capacity>0&&mag.AmountStored>=mag.Capacity;
                bool hasCrew=MagazineRefill.CrewStatus(cannon)==RefillReason.Crew;
                if(RefillPolicy.ReadyMagazineOnly(true,false,state.Revolver,full,hasCrew))
                {
                    readyOnlyAggregate=mag;
                    reason=full?(state.Revolver?"drum full":state.BeltFeed?"belt box full":"bustle full"):"no crew loader";
                    return 1;
                }
                return 0;
            }
            return 0;
        }
        catch{return 0;}
    }
    /// <returns>1: assigned carousel, 0: none, -1: invalid/ambiguous. Preview returns a null aggregate.</returns>
    public static int GetStorage(Cannon cannon,out int basketVuid,out int capacity,out AmmoRackBehaviour aggregate)
    {
        basketVuid=-1;capacity=0;aggregate=null!;
        try
        {
            var assigned=CarouselRuntime.Baskets(cannon).Select(CarouselRuntime.State)
                .Where(s=>s.Enabled&&s.CannonId==cannon.VUID.Value).ToArray();
            if(assigned.Length==0)return 0;
            if(assigned.Length!=1)return -1;
            var state=assigned[0];
            var size=CarouselRuntime.Calculate(state.Basket,cannon);
            basketVuid=state.Basket.VUID.Value;capacity=size.Capacity;
            if(capacity<=0||state.Basket.HealthFraction<=0||state.Damage.Ignited)return -1;
            if(cannon.Vehicle?.Behaviour!=null)
                aggregate=CarouselRuntime.EnsureMixedStorage(state,cannon,size)!;
            return cannon.Vehicle?.Behaviour!=null&&aggregate==null?-1:1;
        }
        catch{return -1;}
    }
}
