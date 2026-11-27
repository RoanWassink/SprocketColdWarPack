using Sprocket.Engines;
using Sprocket.Vehicles.Engines;
using SprocketEngineExpansion.Core;
using UnityEngine;
namespace SprocketEngineExpansion.Native;

internal static class Plans
{
    [ThreadStatic]internal static int NativeRead;
    internal sealed record Snapshot(EnginePlan Plan,float RawTorque,float OutputCoefficient,float Mass,float MaterialCost,float AssemblyCost);
    internal static Snapshot Capture(EngineBlueprint bp,EngineSelection? selection=null)
    {
        NativeRead++;
        try
        {
            // Read resolved technology once through native getters. No technology factor duplication.
            float torque=bp.MaxTorque,coefficient=bp.Cast<Sprocket.Engines.IEngineBlueprint>().TorqueCoefficient;int max=bp.MaxRPM;
            var table=Sprocket.Powertrains.Engines.TorqueCurve.NormalizedTorqueCurveLookupTable;
            var points=new Point[table.Length];for(int i=0;i<table.Length;i++)points[i]=new(table[i].x,table[i].y*torque*coefficient);
            if(!float.IsFinite(coefficient)||coefficient<=0)throw new InvalidOperationException("Native torque coefficient must be positive and finite.");
            var saved=selection??Selections.Get(bp);
            var effective=saved with {Kind=EngineDateRegistry.Effective(bp,saved.Kind),Turbo=false};
            return new(new(effective,new(points),max,bp.IdleRPM),torque,coefficient,bp.Mass,bp.MaterialCost,bp.AssemblyCost);
        }
        finally{NativeRead--;}
    }
    internal static EngineKind EffectiveKind(EngineBlueprint bp)=>EngineDateRegistry.Effective(bp,Selections.Get(bp).Kind);
    internal static bool Modified(EngineBlueprint bp)=>NativeRead==0&&EffectiveKind(bp)!=EngineKind.Petrol;
    internal static float PeakTorque(Snapshot value,bool turbo)=>checked((float)value.Plan.Sample(turbo).Points.Max(p=>p.Y));
    internal static AnimationCurve DisplayCurve(Snapshot value,bool turbo)
    {
        var curve=value.Plan.Sample(turbo);float peak=PeakTorque(value,turbo);
        var keys=curve.Points.Select(p=>new Keyframe((float)p.X,(float)(p.Y/peak))).ToArray();
        // Linear tangents match the native piecewise-linear job sampler.
        for(int i=0;i<keys.Length;i++)
        {
            float dxIn=i>0?keys[i].time-keys[i-1].time:0;
            float dxOut=i+1<keys.Length?keys[i+1].time-keys[i].time:0;
            float incoming=dxIn>1e-6f?(keys[i].value-keys[i-1].value)/dxIn:0;
            float outgoing=dxOut>1e-6f?(keys[i+1].value-keys[i].value)/dxOut:0;
            keys[i]=new Keyframe(keys[i].time,keys[i].value,incoming,outgoing);
        }
        return new AnimationCurve(keys);
    }
}

