using Sprocket.TechTrees;
using Sprocket.Vehicles;

namespace SprocketHydropneumatic;

internal static class HydroEraAccess
{
    private static DateTime Date(TechDate date) => new(date.Year, date.Month, date.Day);
    internal static bool Allowed(VehicleComponent? component) => Allowed(component?.Vehicle);
    internal static bool Allowed(IVehicleGateway? vehicle)
    {
        try
        {
            // The owning vehicle's actual design date is authoritative. An invalid
            // or missing date keeps the native suspension rather than guessing.
            var design = vehicle?.DesignInfo;
            var eras = VehicleClassifications.eras;
            if (design == null || eras == null || eras.Length == 0) return false;
            var starts = new DateTime[eras.Length];
            for (int i = 0; i < eras.Length; i++)
            {
                if (eras[i] == null) return false;
                starts[i] = Date(eras[i].StartDate);
            }
            return HydroEraPolicy.Allows(Date(design.Date), starts);
        }
        catch (Exception ex) { RuntimeHydro.Warn("HPS date unavailable; native suspension retained", ex); return false; }
    }
}
