using System;
using Sprocket.Vehicles;
using Sprocket.Vehicles.FuelTanks;
using Sprocket.Vehicles.PlateStructures;

namespace SprocketFreeformFuelTank;

internal static class FreeformFuelTankPart
{
    internal const string Guid = "b7f4c2e1-8d3a-4f9b-9c12-5e8a3d7f0101";
    internal const string Name = "freeformFuelTank";

    /// <summary>
    /// Default 0.5m x 0.5m x 0.5m closed cube (125 L) in Sprocket's native MeshBlueprint JSON format
    /// expected by PlateStructureMesh.FromJson(string).
    /// Thin 5 mm walls so it looks crisp in freeform edit mode without bloating interior thickness.
    /// </summary>
    internal const string DefaultCubeMeshJson = @"
{
  ""majorVersion"": 0,
  ""minorVersion"": 3,
  ""vertices"": [
    -0.25, 0.0, -0.25,
    -0.25, 0.0,  0.25,
     0.25, 0.0,  0.25,
     0.25, 0.0, -0.25,
    -0.25, 0.5, -0.25,
    -0.25, 0.5,  0.25,
     0.25, 0.5,  0.25,
     0.25, 0.5, -0.25
  ],
  ""edges"": [
    0, 3,  3, 2,  2, 1,  1, 0,
    4, 5,  5, 6,  6, 7,  7, 4,
    0, 4,  1, 5,  2, 6,  3, 7
  ],
  ""edgeFlags"": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
  ""faces"": [
    { ""v"": [0, 3, 2, 1], ""t"": [5, 5, 5, 5], ""tm"": 16843009, ""te"": 0 },
    { ""v"": [4, 5, 6, 7], ""t"": [5, 5, 5, 5], ""tm"": 16843009, ""te"": 0 },
    { ""v"": [0, 1, 5, 4], ""t"": [5, 5, 5, 5], ""tm"": 16843009, ""te"": 0 },
    { ""v"": [1, 2, 6, 5], ""t"": [5, 5, 5, 5], ""tm"": 16843009, ""te"": 0 },
    { ""v"": [2, 3, 7, 6], ""t"": [5, 5, 5, 5], ""tm"": 16843009, ""te"": 0 },
    { ""v"": [3, 0, 4, 7], ""t"": [5, 5, 5, 5], ""tm"": 16843009, ""te"": 0 }
  ]
}";

    /// <summary>
    /// Identifies a Freeform Fuel Tank by the unique co-presence of both FuelTank and PlateStructure
    /// on the same VehicleObject. Never matches vanilla fuel tanks or vanilla armour plate structures.
    /// </summary>
    internal static bool TryGetPair(
        VehicleComponent? component,
        out FuelTank fuelTank,
        out PlateStructure plateStructure)
    {
        fuelTank = null!;
        plateStructure = null!;

        var vo = component?.VehicleObject;
        if (vo == null) return false;

        if (!vo.TryGetBehaviour<FuelTank>(out var ft) || ft == null) return false;
        if (!vo.TryGetBehaviour<PlateStructure>(out var ps) || ps == null) return false;

        fuelTank = ft;
        plateStructure = ps;
        return true;
    }
}
