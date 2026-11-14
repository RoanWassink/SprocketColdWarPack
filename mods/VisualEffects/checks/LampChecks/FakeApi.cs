namespace UnityEngine {
    public sealed class GameObject { public string ComponentId="vfxLamp"; public readonly Dictionary<string,string> Data=new(); }
}
namespace BepInEx.Unity.IL2CPP {
    public sealed class Info { public object? Instance; }
    public sealed class IL2CPPChainloader {
        public static IL2CPPChainloader Instance=new();
        public Dictionary<string,Info> Plugins=new();
    }
}
namespace SprocketTelescopicMast.Io {
    using UnityEngine;
    // Stands in for the real mast facade: IoApiClient.TryBind finds the mast plugin by its GUID and then this type by name.
    public static class IoApi {
        public const int ApiVersion=1,ApiRevision=1;
        public static bool RegisterLevelInput(string a,string b,string c,Func<GameObject,bool> read)=>true;
        public static bool RegisterEventInput(string a,string b,string c,Func<GameObject,int> read)=>true;
        public static bool RegisterOutput(string a,string b,string c,Action<GameObject,bool> apply,Func<GameObject,string?> load,Action<GameObject,string> save,string on,string off)=>true;
        public static bool RegisterManagedOutput(string id,string name,string componentId,Action<GameObject,bool> apply,string on,string off)=>true;
        public static bool SetPartData(GameObject part,string key,string? value)=>true;
        public static string? GetPartData(GameObject part,string key)=>null;
        public static bool RegisterPartIcon(string guid,string path)=>guid.Length>0;
    }
}
namespace Fakes {
    using UnityEngine;
    public sealed class FakeMastPlugin { }
    public static class Revision0 {
        public const int ApiVersion=1;
        public static Func<GameObject,string?>? Load;
        public static Action<GameObject,string>? Save;
        public static Action<GameObject,bool>? Apply;
        public static bool RegisterLevelInput(string a,string b,string c,Func<GameObject,bool> read)=>true;
        public static bool RegisterEventInput(string a,string b,string c,Func<GameObject,int> read)=>true;
        public static bool RegisterOutput(string a,string b,string c,Action<GameObject,bool> apply,Func<GameObject,string?> load,Action<GameObject,string> save,string on,string off) {Load=load;Save=save;Apply=apply;return true;}
    }
    public static class Revision1 {
        public const int ApiVersion=1,ApiRevision=1;
        public static string? ComponentId,OutputId,On,Off;
        public static Action<GameObject,bool>? Apply;
        public static int Registrations;
        public static bool Throw;
        public static readonly Dictionary<string,string> Icons=new();
        public static bool RegisterLevelInput(string a,string b,string c,Func<GameObject,bool> read)=>true;
        public static bool RegisterEventInput(string a,string b,string c,Func<GameObject,int> read)=>true;
        public static bool RegisterOutput(string a,string b,string c,Action<GameObject,bool> apply,Func<GameObject,string?> load,Action<GameObject,string> save,string on,string off)=>true;
        public static bool RegisterManagedOutput(string id,string name,string componentId,Action<GameObject,bool> apply,string on,string off) {
            if(Throw)throw new Exception("fixture failure");
            if(OutputId==id)return false;
            Registrations++;OutputId=id;ComponentId=componentId;Apply=apply;On=on;Off=off;return true;
        }
        public static bool SetPartData(GameObject part,string key,string? value) {
            if(Throw)throw new Exception("fixture failure");
            if(part.ComponentId!=ComponentId||key.Length>64||!System.Text.RegularExpressions.Regex.IsMatch(key,@"^visualeffects\.[a-z0-9._-]+$")||System.Text.Encoding.UTF8.GetByteCount(value??"")>4096)return false;
            int total=part.Data.Where(x=>x.Key!=key).Sum(x=>System.Text.Encoding.UTF8.GetByteCount(x.Value))+System.Text.Encoding.UTF8.GetByteCount(value??"");
            if(total>16384)return false;
            if(value==null)part.Data.Remove(key);else part.Data[key]=value;return true;
        }
        public static string? GetPartData(GameObject part,string key) {
            if(Throw)throw new Exception("fixture failure");
            return part.ComponentId==ComponentId&&part.Data.TryGetValue(key,out var s)?s:null;
        }
        public static bool RegisterPartIcon(string guid,string path) {if(Throw)throw new Exception("fixture failure");return Icons.TryAdd(guid,path);}
    }
    public static class InvalidVersion { public const int ApiVersion=0; }
    public static class IncompleteRevision1 {
        public const int ApiVersion=1,ApiRevision=1;
        public static bool RegisterLevelInput(string a,string b,string c,Func<GameObject,bool> read)=>true;
        public static bool RegisterEventInput(string a,string b,string c,Func<GameObject,int> read)=>true;
        public static bool RegisterOutput(string a,string b,string c,Action<GameObject,bool> apply,Func<GameObject,string?> load,Action<GameObject,string> save,string on,string off)=>true;
    }
}
