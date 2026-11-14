// Copy this file into your mod. It references no mast type and never throws.
//
// Usage, in your plugin's Load() (after BepInEx has loaded the mast):
//   if (IoApiClient.TryBind())
//   {
//       IoApiClient.RegisterOutput("vfx.light", "VFX light", "vfxBulb",
//           (part, on) => SetBulb(part, on), LoadRules, SaveRules, "On", "Off");
//   }
//   static string? LoadRules(GameObject part) => part.GetComponent<MyBulb>()?.RulesJson;
//   static void SaveRules(GameObject part, string json) => part.GetComponent<MyBulb>()?.SetRulesJson(json);
//
// If the mast is absent, TryBind() returns false and every method is a no-op (false/void).
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace SprocketTelescopicMast.Io
{
    public static class IoApiClient
    {
        public const string MastGuid = "sprocket.telescopicmast";
        public static int ApiVersion { get; private set; }
        public static int ApiRevision { get; private set; }
        public static bool Revision1Available => Available && ApiRevision >= 1 && managedOutput != null && setData != null && getData != null && icon != null;
        public static bool Available { get; private set; }

        private delegate bool RegisterInputHandler(string id, string displayName, string group, Func<GameObject, bool> read);
        private delegate bool RegisterEventInputHandler(string id, string displayName, string group, Func<GameObject, int> readEvents);
        private delegate bool RegisterOutputHandler(string id, string displayName, string componentId,
            Action<GameObject, bool> apply, Func<GameObject, string?> loadRules,
            Action<GameObject, string> saveRules, string onLabel, string offLabel);

        private delegate bool ManagedOutputHandler(string id, string displayName, string componentId, Action<GameObject, bool> apply, string onLabel, string offLabel);
        private delegate bool SetDataHandler(GameObject part, string key, string? value);
        private delegate string? GetDataHandler(GameObject part, string key);
        private delegate bool IconHandler(string guid, string path);
        private static ManagedOutputHandler? managedOutput;
        private static SetDataHandler? setData;
        private static GetDataHandler? getData;
        private static IconHandler? icon;
        private static void Reset() { Available=false; ApiVersion=ApiRevision=0; managedOutput=null;setData=null;getData=null;icon=null;levelInput=null;eventInput=null;output=null; }
        private static RegisterInputHandler? levelInput;
        private static RegisterEventInputHandler? eventInput;
        private static RegisterOutputHandler? output;

        /// <summary>Find the mast facade through the IL2CPP chainloader and bind its delegates.</summary>
        public static bool TryBind()
        {
            Reset();
            try
            {
                var plugins = IL2CPPChainloader.Instance.Plugins;
                if (plugins == null || !plugins.TryGetValue(MastGuid, out var info) || info?.Instance == null) return false;
                var facade = info.Instance.GetType().Assembly.GetType("SprocketTelescopicMast.Io.IoApi", false);
                return Bind(facade);
            }
            catch { return false; }
        }

        /// <summary>Test/advanced hook: bind an already resolved facade type.</summary>
        internal static bool Bind(Type? facade)
        {
            Reset();
            if (facade == null) return false;
            try
            {
                var versionField = facade.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                if (versionField == null || versionField.FieldType != typeof(int)) return false;
                int version = Convert.ToInt32(versionField.IsLiteral ? versionField.GetRawConstantValue() : versionField.GetValue(null));
                if (version < 1) return false;

                levelInput = BindDelegate<RegisterInputHandler>(facade, "RegisterLevelInput",
                    typeof(string), typeof(string), typeof(string), typeof(Func<GameObject, bool>));
                eventInput = BindDelegate<RegisterEventInputHandler>(facade, "RegisterEventInput",
                    typeof(string), typeof(string), typeof(string), typeof(Func<GameObject, int>));
                output = BindDelegate<RegisterOutputHandler>(facade, "RegisterOutput",
                    typeof(string), typeof(string), typeof(string), typeof(Action<GameObject, bool>),
                    typeof(Func<GameObject, string>), typeof(Action<GameObject, string>), typeof(string), typeof(string));
                if (levelInput == null || eventInput == null || output == null) return false;
                var revision = facade.GetField("ApiRevision", BindingFlags.Public | BindingFlags.Static);
                ApiRevision = revision?.FieldType == typeof(int) ? Convert.ToInt32(revision.IsLiteral ? revision.GetRawConstantValue() : revision.GetValue(null)) : 0;
                if (ApiRevision >= 1) {
                    managedOutput=BindDelegate<ManagedOutputHandler>(facade,"RegisterManagedOutput",typeof(string),typeof(string),typeof(string),typeof(Action<GameObject,bool>),typeof(string),typeof(string));
                    setData=BindDelegate<SetDataHandler>(facade,"SetPartData",typeof(GameObject),typeof(string),typeof(string));
                    getData=BindDelegate<GetDataHandler>(facade,"GetPartData",typeof(GameObject),typeof(string));
                    icon=BindDelegate<IconHandler>(facade,"RegisterPartIcon",typeof(string),typeof(string));
                }
                ApiVersion = version; Available = true; return true;
            }
            catch { return false; }
        }

        public static bool RegisterManagedOutput(string id,string name,string componentId,Action<GameObject,bool> apply,string onLabel="On",string offLabel="Off") { try { return Revision1Available && managedOutput!(id,name,componentId,apply,onLabel,offLabel); } catch { return false; } }
        public static bool SetPartData(GameObject part,string key,string? value) { try { return Revision1Available && setData!(part,key,value); } catch { return false; } }
        public static string? GetPartData(GameObject part,string key) { try { return Revision1Available ? getData!(part,key) : null; } catch { return null; } }
        public static bool RegisterPartIcon(string guid,string path) { try { return Revision1Available && icon!(guid,path); } catch { return false; } }

        private static T? BindDelegate<T>(Type facade, string name, params Type[] parameters) where T : Delegate
        {
            try
            {
                var method = facade.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
                return method == null ? null : (T)Delegate.CreateDelegate(typeof(T), method);
            }
            catch { return null; }
        }

        public static bool RegisterLevelInput(string id, string displayName, string group, Func<GameObject, bool> read)
        {
            if (!Available || levelInput == null) return false;
            try { return levelInput(id, displayName, group, read); } catch { return false; }
        }

        public static bool RegisterEventInput(string id, string displayName, string group, Func<GameObject, int> readEvents)
        {
            if (!Available || eventInput == null) return false;
            try { return eventInput(id, displayName, group, readEvents); } catch { return false; }
        }

        public static bool RegisterOutput(string id, string displayName, string componentId,
            Action<GameObject, bool> apply, Func<GameObject, string?> loadRules,
            Action<GameObject, string> saveRules, string onLabel = "On", string offLabel = "Off")
        {
            if (!Available || output == null) return false;
            try { return output(id, displayName, componentId, apply, loadRules, saveRules, onLabel, offLabel); }
            catch { return false; }
        }
    }
}
