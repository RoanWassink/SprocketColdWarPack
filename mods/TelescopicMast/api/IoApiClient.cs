// Copy this file into your mod. It references no mast type and never throws.
//
// Usage, in your plugin's Load() (after BepInEx has loaded the mast):
//   if (IoApiClient.TryBind())
//   {
//       IoApiClient.RegisterManagedOutput("vfx.light", "VFX light", "vfxBulb",
//           (part, on) => SetBulb(part, on), "On", "Off");
//       IoApiClient.RegisterPartSettings("vfxBulb", "visualeffects.settings",
//           "[{\"key\":\"brightness\",\"label\":\"Brightness\",\"type\":\"float\",\"min\":0,\"max\":10,\"default\":1}]",
//           part => ReloadSettings(IoApiClient.GetPartData(part, "visualeffects.settings")));
//       IoApiClient.SetPartData(part, "visualeffects.colour", "#ffcc66");
//       IoApiClient.RegisterPartIcon("your-part-guid", @"C:\...\BepInEx\plugins\...\lamp.png");
//   }
//
// If the mast is absent, TryBind() returns false and every method is a no-op (false/null/void).
// v1.1 methods are optional: an older mast (ApiRevision 0) returns false/null with one log line.
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
        /// <summary>2 for v1.2 settings; 1 for v1.1; 0 on an older mast.</summary>
        public static int ApiRevision { get; private set; }
        public static bool Available { get; private set; }

        private delegate bool RegisterInputHandler(string id, string displayName, string group, Func<GameObject, bool> read);
        private delegate bool RegisterEventInputHandler(string id, string displayName, string group, Func<GameObject, int> readEvents);
        private delegate bool RegisterOutputHandler(string id, string displayName, string componentId,
            Action<GameObject, bool> apply, Func<GameObject, string?> loadRules,
            Action<GameObject, string> saveRules, string onLabel, string offLabel);
        private delegate bool SetPartDataHandler(GameObject part, string key, string? value);
        private delegate string? GetPartDataHandler(GameObject part, string key);
        private delegate bool RegisterManagedOutputHandler(string id, string displayName, string componentId,
            Action<GameObject, bool> apply, string onLabel, string offLabel);
        private delegate bool RegisterPartIconHandler(string partGuid, string pngPath);

        private static RegisterInputHandler? levelInput;
        private static RegisterEventInputHandler? eventInput;
        private static RegisterOutputHandler? output;
        private static SetPartDataHandler? setPartData;
        private static GetPartDataHandler? getPartData;
        private static RegisterManagedOutputHandler? managedOutput;
        private static RegisterPartIconHandler? partIcon;
        private delegate bool RegisterPartSettingsHandler(string componentId, string dataKey, string schemaJson, Action<GameObject>? onChanged);
        private static RegisterPartSettingsHandler? partSettings;
        private static bool missingLogged;

        /// <summary>Find the mast facade through the IL2CPP chainloader and bind its delegates.</summary>
        public static bool TryBind()
        {
            Available = false; ApiVersion = 0; ApiRevision = 0;
            levelInput = null; eventInput = null; output = null;
            setPartData = null; getPartData = null; managedOutput = null; partIcon = null; partSettings = null;
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
            Available = false; ApiVersion = 0; ApiRevision = 0;
            levelInput = null; eventInput = null; output = null;
            setPartData = null; getPartData = null; managedOutput = null; partIcon = null; partSettings = null;
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

                // Optional v1.1 additions: an older mast simply leaves these null.
                var revisionField = facade.GetField("ApiRevision", BindingFlags.Public | BindingFlags.Static);
                if (revisionField != null && revisionField.FieldType == typeof(int))
                    ApiRevision = Convert.ToInt32(revisionField.IsLiteral ? revisionField.GetRawConstantValue() : revisionField.GetValue(null));
                setPartData = BindDelegate<SetPartDataHandler>(facade, "SetPartData",
                    typeof(GameObject), typeof(string), typeof(string));
                getPartData = BindDelegate<GetPartDataHandler>(facade, "GetPartData", typeof(GameObject), typeof(string));
                managedOutput = BindDelegate<RegisterManagedOutputHandler>(facade, "RegisterManagedOutput",
                    typeof(string), typeof(string), typeof(string), typeof(Action<GameObject, bool>), typeof(string), typeof(string));
                partIcon = BindDelegate<RegisterPartIconHandler>(facade, "RegisterPartIcon", typeof(string), typeof(string));

                if (ApiRevision >= 2) partSettings = BindDelegate<RegisterPartSettingsHandler>(facade, "RegisterPartSettings",
                    typeof(string), typeof(string), typeof(string), typeof(Action<GameObject>));
                ApiVersion = version; Available = true; return true;
            }
            catch { return false; }
        }

        private static T? BindDelegate<T>(Type facade, string name, params Type[] parameters) where T : Delegate
        {
            try
            {
                var method = facade.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
                return method == null ? null : (T)Delegate.CreateDelegate(typeof(T), method);
            }
            catch { return null; }
        }

        private static void Missing(string method)
        {
            if (missingLogged || !Available) return;
            missingLogged = true;
            Debug.LogWarning("[IoApiClient] The installed Telescopic Mast has no optional method " + method + "; continuing without it.");
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

        public static bool SetPartData(GameObject part, string key, string? value)
        {
            if (!Available || setPartData == null) { Missing("SetPartData"); return false; }
            try { return setPartData(part, key, value); } catch { return false; }
        }

        public static string? GetPartData(GameObject part, string key)
        {
            if (!Available || getPartData == null) { Missing("GetPartData"); return null; }
            try { return getPartData(part, key); } catch { return null; }
        }

        public static bool RegisterManagedOutput(string id, string displayName, string componentId,
            Action<GameObject, bool> apply, string onLabel = "On", string offLabel = "Off")
        {
            if (!Available || managedOutput == null) { Missing("RegisterManagedOutput"); return false; }
            try { return managedOutput(id, displayName, componentId, apply, onLabel, offLabel); } catch { return false; }
        }

        /// <summary>v1.2: false on older masts; keep consumer defaults in that case.</summary>
        public static bool RegisterPartSettings(string componentId, string dataKey, string schemaJson, Action<GameObject>? onChanged = null)
        {
            if (!Available || ApiRevision < 2 || partSettings == null) { Missing("RegisterPartSettings"); return false; }
            try { return partSettings(componentId,dataKey,schemaJson,onChanged); } catch { return false; }
        }

        public static bool RegisterPartIcon(string partGuid, string pngPath)
        {
            if (!Available || partIcon == null) { Missing("RegisterPartIcon"); return false; }
            try { return partIcon(partGuid, pngPath); } catch { return false; }
        }
    }
}
