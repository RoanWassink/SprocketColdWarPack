using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using SprocketJsonEditor;
using UnityEngine;
namespace SprocketExpandedBallistics;
internal static class CombatReportMenu
{
    private static object? key;private static PropertyInfo? pressed;private static Type? api;private static bool owned;private static int warnings;
    private static CombatReportSession? report;private static bool cursorVisible;private static CursorLockMode cursorLock;
    internal static void Configure()
    {
        try
        {
            if(key!=null)return;
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("SprocketKeybinds.Keybinds",false)).FirstOrDefault(t=>t!=null);
            if(type==null)return;
            api=type;
            BallisticsKeybindMetadata.Register(type);
            var register=type.GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name=="RegisterButton"&&m.GetParameters().Length==6);
            key=register.Invoke(null,new object?[]{BallisticsKeybindMetadata.Owner,BallisticsKeybindMetadata.Name,"combat-report","Combat report","",null});
            pressed=key?.GetType().GetProperty("WasPressedThisFrame");
        }
        catch(Exception ex){Warn(ex);}
    }
    private static void Warn(Exception ex){if(warnings++<3)Plugin.ModLog.LogWarning("[Combat report] Optional menu: "+ex.Message);}
    internal static void Update()
    {
        try
        {
            if(owned&&(report==null||!JsonEditor.IsSessionOpen(report)))RestoreCursor();if(owned){Cursor.visible=true;Cursor.lockState=CursorLockMode.None;}
            // 0.22.3: the keybind toggles the UI Kit panel (the kit handles cursor and input); JSON Editor only as fallback.
            if(key!=null&&pressed?.GetValue(key) is true){if(CombatReportPanel.UseKit&&CombatReportPanel.Toggle())return;if(!JsonEditor.IsOpen)OpenSession();}
        }
        catch(Exception ex){Warn(ex);}
    }
    internal static void Open()
    {
        if(CombatReportPanel.UseKit&&CombatReportPanel.Open())return;
        OpenSession();
    }
    private static void OpenSession()
    {
        try{if(JsonEditor.IsOpen)return;var visible=Cursor.visible;var locked=Cursor.lockState;report=new CombatReportSession(RuntimeDamageFeed.History);JsonEditor.Open(report);owned=JsonEditor.IsSessionOpen(report);if(owned){cursorVisible=visible;cursorLock=locked;Cursor.visible=true;Cursor.lockState=CursorLockMode.None;}}
        catch(Exception ex){Warn(ex);}
    }
    private static void RestoreCursor(){if(owned&&!JsonEditor.IsOpen){Cursor.visible=cursorVisible;Cursor.lockState=cursorLock;}owned=false;report=null;}
    internal static void Clear(){CombatReportPanel.Clear();try{if(owned&&report!=null&&JsonEditor.IsSessionOpen(report))JsonEditor.Close();RestoreCursor();if(key!=null)api?.GetMethod("Unregister",new[]{typeof(string),typeof(string)})?.Invoke(null,new object[]{"sprocket.expandedballistics","combat-report"});}catch(Exception ex){Warn(ex);}owned=false;key=null;pressed=null;api=null;}
}

