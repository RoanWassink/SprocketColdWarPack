# Sprocket Freeform Fuel Tank

Freeform Fuel Tank adds a shapeable freeform fuel tank part to the Sprocket vehicle designer. Build custom-shaped internal or external fuel tanks using the game's freeform mesh tools, with fuel capacity and mass computed automatically from the enclosed mesh volume.

- Version: 0.1.4
- Plugin GUID: `sprocket.freeformfueltank`
- Needs: no other mod
- Game version tested: 0.2.56.2

## What it adds

- **Freeform Fuel Tank** part in the designer part list, editable with Sprocket's freeform mesh tools.
- **Automatic volume, capacity, and mass calculation** from the enclosed mesh volume (default 1000 L/m³, matching the vanilla Box Fuel Tank).
- **Live fuel tank collider & damage model integration** matching the custom shape.

## How to use it

1. Open the vehicle designer and place a **Freeform Fuel Tank** from the parts list.
2. Shape the tank with the freeform edit tools to fit inside sponsons, turret cheeks, or custom hull spaces.
3. Capacity and mass update automatically from the enclosed volume.

## Settings

The settings file is `BepInEx\config\sprocket.freeformfueltank.cfg`. BepInEx creates it on first start.

- `[FuelTank] FillEfficiency` (`1.0`): fraction of enclosed mesh volume usable as fuel capacity.
- `[FuelTank] FuelDensityKgPerLitre`: fuel mass per litre.
- `[FuelTank] RequireClosedMesh`: whether open/unclosed meshes are rejected when computing fuel volume.
- `[Diagnostics] DetailedLogging` (`true`): detailed logs for builds, volumes, and colliders.

## Building

A .NET 6.0 project in this folder (`SprocketFreeformFuelTank.csproj`). Build with `dotnet build SprocketFreeformFuelTank.csproj -c Release -p:GameDir=<path to Sprocket folder>`. Needs BepInEx 6 IL2CPP.
