// Minimal stubs so the copyable client shim and the public facade compile in the pure
// net8 test project. They are not shipped and are never loaded by the game.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public sealed class GameObject
    {
        private static int next;
        public int Id { get; } = ++next;
        public override string ToString() => "GameObject#" + Id;
    }

    public static class Debug
    {
        public static readonly List<string> Warnings = new();
        public static void LogWarning(string message) { Warnings.Add(message); }
    }
}

namespace BepInEx
{
    public sealed class PluginInfo
    {
        public object? Instance { get; set; }
    }
}

namespace BepInEx.Unity.IL2CPP
{
    public sealed class IL2CPPChainloader
    {
        public static IL2CPPChainloader Instance { get; } = new();
        public Dictionary<string, BepInEx.PluginInfo> Plugins { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>Facade with the same signatures and ApiVersion=1, used to test the shim's binding.</summary>
internal static class StubFacade
{
    public const int ApiVersion = 1;
    public const int ApiRevision = 1;
    internal static int LevelCalls, OutputCalls, ManagedCalls, IconCalls, SetCalls, GetCalls;
    public static bool RegisterLevelInput(string id, string displayName, string group, Func<UnityEngine.GameObject, bool> read)
    { LevelCalls++; return true; }
    public static bool RegisterEventInput(string id, string displayName, string group, Func<UnityEngine.GameObject, int> readEvents)
    { LevelCalls++; return true; }
    public static bool RegisterOutput(string id, string displayName, string componentId,
        Action<UnityEngine.GameObject, bool> apply, Func<UnityEngine.GameObject, string?> loadRules,
        Action<UnityEngine.GameObject, string> saveRules, string onLabel, string offLabel)
    { OutputCalls++; return true; }
    public static bool SetPartData(UnityEngine.GameObject part, string key, string? value) { SetCalls++; return true; }
    public static string? GetPartData(UnityEngine.GameObject part, string key) { GetCalls++; return "stub"; }
    public static bool RegisterManagedOutput(string id, string displayName, string componentId,
        Action<UnityEngine.GameObject, bool> apply, string onLabel, string offLabel)
    { ManagedCalls++; return true; }
    public static bool RegisterPartIcon(string partGuid, string pngPath) { IconCalls++; return true; }
}

/// <summary>A real v1 mast facade: no v1.1 fields or methods. The shim must keep working.</summary>
internal static class OldFacade
{
    public const int ApiVersion = 1;
    public static bool RegisterLevelInput(string id, string displayName, string group, Func<UnityEngine.GameObject, bool> read) => true;
    public static bool RegisterEventInput(string id, string displayName, string group, Func<UnityEngine.GameObject, int> readEvents) => true;
    public static bool RegisterOutput(string id, string displayName, string componentId,
        Action<UnityEngine.GameObject, bool> apply, Func<UnityEngine.GameObject, string?> loadRules,
        Action<UnityEngine.GameObject, string> saveRules, string onLabel, string offLabel) => true;
}

/// <summary>Same shape, incompatible API version: the shim must refuse it.</summary>
internal static class BadFacade
{
    public const int ApiVersion = 0;
    public static bool RegisterLevelInput(string id, string displayName, string group, Func<UnityEngine.GameObject, bool> read) => true;
    public static bool RegisterEventInput(string id, string displayName, string group, Func<UnityEngine.GameObject, int> readEvents) => true;
    public static bool RegisterOutput(string id, string displayName, string componentId,
        Action<UnityEngine.GameObject, bool> apply, Func<UnityEngine.GameObject, string?> loadRules,
        Action<UnityEngine.GameObject, string> saveRules, string onLabel, string offLabel) => true;
}
