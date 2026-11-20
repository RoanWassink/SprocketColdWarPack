using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using Sprocket.Engines;
using Sprocket.Vehicles.Powertrains;
using SprocketEngineExpansion.Core;
using UnityEngine;
using UnityEngine.Bindings;
namespace SprocketEngineExpansion.Native;

[HarmonyPatch]
internal static class EngineAudio
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSpan { internal IntPtr Begin; internal int Length; }

    private sealed class VoiceState
    {
        internal IntPtr ControllerPtr;
        internal IntPtr BehaviourPtr;
        internal float BasePitch;
        internal float BaseVolume;
        internal GameObject? AuxRoot;
        internal AudioSource? AuxSource;
        internal AudioClip? AttachedClip;
        internal bool AuxFailed;
    }

    internal static bool Enabled=true;
    internal static float AuxVolume=0.45f;

    private static AudioClip? turbineClip,turboClip;
    private static bool clipsAttempted,clipsFailed;
    private static readonly Dictionary<IntPtr,VoiceState> controllers=new();
    private static readonly Dictionary<IntPtr,List<IntPtr>> byBehaviour=new();

    [HarmonyPostfix,HarmonyPatch(typeof(EngineAudioController),nameof(EngineAudioController.UpdateAudio),new[]{typeof(float),typeof(float)})]
    private static void Update(EngineAudioController __instance)
    {
        if(__instance==null)return;
        try
        {
            if(!EngineCharacterHooks.Enabled||!Enabled)
            {
                RestoreAndStop(__instance.Pointer,__instance);
                return;
            }
            var behaviour=__instance.engine?.TryCast<EngineBehaviour>();
            if(behaviour==null||!RuntimeHooks.TryGetState(behaviour.Pointer,out var kind,out var spool,out var req,out var running))
            {
                RestoreAndStop(__instance.Pointer,__instance);
                return;
            }
            if(!controllers.TryGetValue(__instance.Pointer,out var state))
            {
                state=new VoiceState
                {
                    ControllerPtr=__instance.Pointer,
                    BehaviourPtr=behaviour.Pointer,
                    BasePitch=__instance.pitchMultiplier,
                    BaseVolume=__instance.baseVolumeMultipler
                };
                controllers.Add(__instance.Pointer,state);
                if(!byBehaviour.TryGetValue(behaviour.Pointer,out var list))byBehaviour.Add(behaviour.Pointer,list=new());
                list.Add(__instance.Pointer);
            }
            float rpmFraction=behaviour.MaxRPM>1f?Math.Clamp(behaviour.RPM/behaviour.MaxRPM,0.15f,1.15f):0.35f;
            var target=EngineCharacterRules.ComputeAudio(kind,spool,req,running,rpmFraction,EngineCharacterRules.For(kind));
            __instance.pitchMultiplier=state.BasePitch*(float)target.NativePitchScale;
            var host=__instance.aChannel?.source;
            bool hostMuted=host==null||!host.isActiveAndEnabled||host.mute;

            EnsureClips();
            AudioClip? clip=kind==EngineKind.GasTurbine?turbineClip:kind==EngineKind.Diesel?turboClip:null;
            bool auxPlaying=false;
            if(!clipsFailed&&!state.AuxFailed&&!hostMuted&&clip!=null&&target.AuxVolume>0.003&&AuxVolume>0.001f)
            {
                var aux=EnsureAuxSource(state,__instance,clip);
                if(aux!=null&&aux.isActiveAndEnabled)
                {
                    aux.pitch=(float)target.AuxPitch;
                    aux.volume=(float)Math.Clamp(target.AuxVolume*AuxVolume,0.0,1.0);
                    if(!aux.isPlaying)aux.Play();
                    auxPlaying=aux.isPlaying;
                }
            }
            else if(state.AuxSource!=null&&state.AuxSource.isPlaying)
            {
                state.AuxSource.Stop();
            }
            float nativeVolScale=(kind==EngineKind.GasTurbine&&running&&!auxPlaying)?1f:(float)target.NativeVolumeScale;
            __instance.baseVolumeMultipler=state.BaseVolume*nativeVolScale;
        }
        catch(Exception ex){Plugin.Warn("engine audio",ex);}
    }

    internal static void Release(IntPtr behaviourPtr)
    {
        if(!byBehaviour.Remove(behaviourPtr,out var ptrs))return;
        foreach(var ptr in ptrs)
        {
            if(!controllers.Remove(ptr,out var state))continue;
            DestroyAux(state);
        }
    }

    private static void RestoreAndStop(IntPtr controllerPtr,EngineAudioController controller)
    {
        if(!controllers.Remove(controllerPtr,out var state))return;
        if(byBehaviour.TryGetValue(state.BehaviourPtr,out var list))
        {
            list.Remove(controllerPtr);
            if(list.Count==0)byBehaviour.Remove(state.BehaviourPtr);
        }
        try
        {
            controller.pitchMultiplier=state.BasePitch;
            controller.baseVolumeMultipler=state.BaseVolume;
        }
        catch { /* Controller may be disposing. */ }
        DestroyAux(state);
    }

    private static void DestroyAux(VoiceState state)
    {
        try
        {
            if(state.AuxSource!=null&&state.AuxSource.isPlaying)state.AuxSource.Stop();
            if(state.AuxRoot!=null)UnityEngine.Object.Destroy(state.AuxRoot);
        }
        catch { /* Ignored during scene teardown. */ }
        finally { state.AuxSource=null;state.AuxRoot=null;state.AttachedClip=null; }
    }

    private static AudioSource? EnsureAuxSource(VoiceState state,EngineAudioController controller,AudioClip clip)
    {
        if(state.AuxFailed)return null;
        if(state.AuxSource==null)
        {
            var host=controller.aChannel?.source;
            if(host==null)return null;
            GameObject? root=null;
            try
            {
                root=new GameObject("EE_EngineAuxAudio");
                root.hideFlags=HideFlags.HideAndDontSave;
                root.transform.SetParent(host.transform,false);
                root.transform.localPosition=Vector3.zero;
                var src=root.AddComponent<AudioSource>();
                src.playOnAwake=false;
                src.loop=true;
                src.spatialBlend=host.spatialBlend;
                src.minDistance=host.minDistance;
                src.maxDistance=host.maxDistance;
                src.rolloffMode=host.rolloffMode;
                src.dopplerLevel=host.dopplerLevel;
                // Do not read host.outputAudioMixerGroup: Unity 6000.3 IL2CPP UnmarshalUnityObject<AudioMixerGroup>
                // throws TypeInitializationException and leaks per-frame GameObjects.
                state.AuxRoot=root;
                state.AuxSource=src;
            }
            catch(Exception ex)
            {
                state.AuxFailed=true;
                state.AuxSource=null;
                state.AuxRoot=null;
                state.AttachedClip=null;
                try{if(root!=null)UnityEngine.Object.Destroy(root);}catch{}
                Plugin.Warn("engine aux audio disabled",ex);
                return null;
            }
        }
        if(state.AuxSource!=null&&!ReferenceEquals(state.AttachedClip,clip))
        {
            state.AuxSource.clip=clip;
            state.AttachedClip=clip;
        }
        return state.AuxSource;
    }

    private static void EnsureClips()
    {
        if(clipsAttempted||clipsFailed)return;
        clipsAttempted=true;
        try
        {
            turbineClip=Upload(EngineAudioSynthesis.CreateTurbineLoop(),"EE Turbine Loop");
            turboClip=Upload(EngineAudioSynthesis.CreateTurboWhistleLoop(),"EE Diesel Turbo Loop");
            Plugin.ModLog.LogInfo($"[Engine Expansion] Procedural engine audio ready: turbine={turbineClip.samples} samples ({turbineClip.frequency} Hz), diesel-turbo={turboClip.samples} samples ({turboClip.frequency} Hz).");
        }
        catch(Exception ex)
        {
            clipsFailed=true;
            if(turbineClip!=null){UnityEngine.Object.Destroy(turbineClip);turbineClip=null;}
            if(turboClip!=null){UnityEngine.Object.Destroy(turboClip);turboClip=null;}
            Plugin.Warn("procedural engine audio disabled",ex);
        }
    }

    private static AudioClip Upload(float[] samples,string titleText)
    {
        if(Marshal.SizeOf<NativeSpan>()!=Marshal.SizeOf<ManagedSpanWrapper>()||
           Marshal.OffsetOf<ManagedSpanWrapper>(nameof(ManagedSpanWrapper.begin)).ToInt32()!=0||
           Marshal.OffsetOf<ManagedSpanWrapper>(nameof(ManagedSpanWrapper.length)).ToInt32()!=IntPtr.Size)
            throw new NotSupportedException("Unity audio span layout changed");
        var title=titleText.ToCharArray();
        var created=AudioClip.Construct_Internal();
        created.hideFlags=HideFlags.HideAndDontSave;
        GCHandle titlePin=default,samplesPin=default;
        try
        {
            titlePin=GCHandle.Alloc(title,GCHandleType.Pinned);
            samplesPin=GCHandle.Alloc(samples,GCHandleType.Pinned);
            var nativeTitle=new NativeSpan {Begin=titlePin.AddrOfPinnedObject(),Length=title.Length};
            ref var label=ref Unsafe.As<NativeSpan,ManagedSpanWrapper>(ref nativeTitle);
            AudioClip.CreateUserSound_Injected(created.m_CachedPtr,ref label,samples.Length,1,EngineAudioSynthesis.SampleRate,false);
            var nativeSamples=new NativeSpan {Begin=samplesPin.AddrOfPinnedObject(),Length=samples.Length};
            ref var data=ref Unsafe.As<NativeSpan,ManagedSpanWrapper>(ref nativeSamples);
            if(!AudioClip.SetData_Injected(created.m_CachedPtr,ref data,0)||created.samples!=samples.Length||created.channels!=1||created.frequency!=EngineAudioSynthesis.SampleRate)
                throw new InvalidOperationException("Engine audio native upload failed");
            return created;
        }
        catch { UnityEngine.Object.Destroy(created);throw; }
        finally
        {
            if(titlePin.IsAllocated)titlePin.Free();
            if(samplesPin.IsAllocated)samplesPin.Free();
        }
    }
}
