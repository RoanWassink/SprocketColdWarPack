using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.AmmunitionStorage;

namespace SprocketCarouselAutoloader;

/// <summary>Physical storage adapter. Counts are shared slots, never an extra ammunition pool.</summary>
public static class AmmoStorageApi
{
    public static int MixedStorageApiVersion=>1;
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
