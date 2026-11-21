# Sprocket Floppy Parts

Visual plate-look and soft-body / hinged plate motion mod for Sprocket (BepInEx 6 IL2CPP).

- Version: 0.3.6
- Needs: no other mod

## Features

- **Per-plate visual looks** (`Floppy Parts` foldout in the Plate Structure inspector):
  - Native Armour, Canvas, Camo Net (HDRP alpha clipping + shadows), Rubber, Wood, Glass (two-sided transparent + opaque fallback), Custom Texture.
  - Continuous meter-scale planar UV projection, tile scale, UV rotation, Follow Tank Paint / Preset / Custom Hex tinting, Brightness, Smoothness, and Opacity / Cutout.
- **Play-mode & Photo-mode Motion Simulation**:
  - **Static** (rigid overlay)
  - **Cloth** (2D XPBD cloth with conformal subdivision, gravity drape, areal aerodynamic drag, 3D wind turbulence, muzzle blast shockwaves, and engine/vent airflow flutter)
  - **Rubber** (thick extruded shell with thickness-scaled elastic shape-matching and inward backing resistance)
  - **Hinged** (per-island damped 1-DOF swing around fitted pinned hinge axis toward vertical under gravity, carrying attached parts)
- **Auto & Manual Pinning**:
  - `Auto` mode scans adjacent rigid vehicle parts (turret, mantlet, trunnions, hull) for multi-anchor pinning, falling back to `TopEdge` if nothing is within `Pin radius (cm)`.
  - Manual modes: `Top Edge`, `Bottom Edge`, `Border`, `Corners`, `Top & Bottom`, `Left & Right`.
  - **Pin preview** toggle in Designer highlights pinned (red) vs free (green) vertices.

## How to use it

1. Open the vehicle designer and select a plate structure (such as a mudflap, tarpaulin, camo net sheet, or rubber skirt).
2. Open the **Floppy Parts** foldout in the Plate Structure inspector.
3. Pick a visual look (Canvas, Camo Net, Rubber, Wood, Glass, or Custom Texture) and adjust scale, tint, smoothness, or opacity.
4. Choose a motion mode (`Static`, `Cloth`, `Rubber`, or `Hinged`) and a pinning mode (`Auto` or manual edges). Enable **Pin preview** to check which vertices are pinned (red) vs free (green).
5. Start a test drive or battle to see the plates react to movement, gravity, wind, muzzle blast, and engine airflow.

## Custom Textures

Place custom PNGs (64x64 up to 4096x4096, max 32 MiB) in `BepInEx\plugins\SprocketFloppyParts\textures\<folder>\`:
- `albedo.png` (required)
- `normal.png`, `roughness.png`, `ao.png`, `settings.json` (optional)

## Settings (`BepInEx\config\sprocket.floppyparts.cfg`)

- `Diagnostics.DetailedLogging` (default `true`)
- `Compatibility.TrustUnknownGameBuild` (default `false`)
- `Compatibility.AutoAdoptExpandedBallisticsLooks` (default `false`)
- `UI.FloppyPartsSectionOpen` (default `true`)
- `Simulation.Enabled` (default `true`)
- `Simulation.MaxSimulatedPlates` (default `32`)
- `Simulation.SurfaceCollisionEnabled` (default `true`)
- `Simulation.HingedAttachmentsEnabled` (default `true`)
- `Simulation.MuzzleBlastEnabled` (default `true`)
- `Simulation.ExhaustAndVentAirflowEnabled` (default `true`)

## Building

A .NET 6.0 project in this folder (`SprocketFloppyParts.csproj`). Build with `dotnet build SprocketFloppyParts.csproj -c Release -p:GameDir=<path to Sprocket folder>`. Needs BepInEx 6 IL2CPP.
