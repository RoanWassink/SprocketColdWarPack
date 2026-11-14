using System;
using UnityEngine;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;

namespace SprocketOscillatingTurret;

/// <summary>
/// Visual-only tuning for the oscillating elevation drive.
///
/// The functional turret-ring transform now pitches around the lateral elevation
/// axis, which is correct for an oscillating turret but looks wrong for a ring
/// authored as a normal flat yaw bearing. Rolling only the visible model by 90
/// degrees around the part's local forward axis turns the ring's hole toward the
/// pitch axis, so the bearing visually spins on its intended trunnion axis.
///
/// This writes only <see cref="IVehicleModelComponent.ModelLocalRotation"/>. The
/// functional VehicleTransform, mirror plane, attachment points, colliders,
/// mass and cost are untouched. The value is re-applied if the model is rebuilt,
/// and can be disabled or retuned through the BepInEx configuration file.
/// </summary>
internal static class VisualTuning
{
    private static int _warnings;

    internal static void Tick(OwnedTurret entry)
    {
        try
        {
            if(PivotHousingVisuals.Apply(entry))return;
            var rotate = Plugin.VisualRotateSetting;
            var roll = Plugin.VisualRollSetting;
            if (rotate == null || roll == null || !rotate.Value)
            {
                return;
            }

            float degrees = roll.Value;
            if (Math.Abs(degrees) < 0.01f)
            {
                return;
            }

            TurretRing? ring = entry.RingRef;
            if (ring == null)
            {
                return;
            }

            IVehicleModelComponent? model = ring.ringModel;
            if (model == null || !model.HasModel)
            {
                return;
            }

            Quaternion target = Quaternion.AngleAxis(degrees, new Vector3(0f, 0f, 1f));
            Quaternion current = model.ModelLocalRotation;
            float dot =
                current.x * target.x +
                current.y * target.y +
                current.z * target.z +
                current.w * target.w;
            if (Math.Abs(Math.Abs(dot) - 1f) <= 0.001f)
            {
                return;
            }

            model.ModelLocalRotation = target;
            Diagnostics.VisualRoll(Diagnostics.RingId(entry), current, target);
        }
        catch (Exception ex)
        {
            if (_warnings < 3)
            {
                _warnings++;
                Plugin.Warn(
                    "[Visual] ring model rotation failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
