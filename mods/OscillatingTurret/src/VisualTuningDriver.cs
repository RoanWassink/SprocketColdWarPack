using System;
using System.Collections.Generic;
using UnityEngine;

namespace SprocketOscillatingTurret;

/// <summary>
/// Production per-frame driver for the visual-only ring model maintenance.
///
/// It runs the registered visual tuning on a one-second cadence. The native
/// command, motor and state inspection that lived here during the 0.1.4
/// investigation has been removed.
/// </summary>
public sealed class VisualTuningDriver : MonoBehaviour
{
    public VisualTuningDriver(IntPtr pointer) : base(pointer) { }

    public void LateUpdate()
    {
        VisualTuningRuntime.Tick();
    }
}

internal static class VisualTuningRuntime
{
    private static int _warnings;
    private static float _nextTick;

    public static void Tick()
    {
        try
        {
            if (Time.realtimeSinceStartup < _nextTick)
            {
                return;
            }

            _nextTick = Time.realtimeSinceStartup + 1f;

            List<OwnedTurret> entries = ElevationRegistry.SnapshotForVisualTuning();
            PivotHousingVisuals.Sweep(entries);
            foreach (OwnedTurret entry in entries)
            {
                VisualTuning.Tick(entry);
            }
        }
        catch (Exception ex)
        {
            Warn(ex);
        }
    }

    private static void Warn(Exception ex)
    {
        if (_warnings >= 3)
        {
            return;
        }

        _warnings++;
        try
        {
            Plugin.Warn(
                "[Visual] ring model maintenance error: " +
                ex.GetType().Name + ": " + ex.Message);
        }
        catch
        {
            // Never let visual maintenance break gameplay.
        }
    }
}
