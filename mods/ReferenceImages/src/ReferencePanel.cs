using System.Diagnostics;
using SprocketKeybinds;
using SprocketUiKit;

namespace SprocketReferenceImages;

internal static class ReferencePanel
{
    private static Panel panel=null!;
    private static ModKeybind paste=null!;
    internal static void Initialize()
    {
        ImageLibrary.Refresh();
        var key=Keybinds.RegisterButton(Plugin.Guid,"Reference Images","panel","Reference Images / Panel","");
        paste=Keybinds.RegisterButton(Plugin.Guid,"Reference Images","paste","Reference Images / Paste","");
        panel=UiKit.CreatePanel(Plugin.Guid,"images","Reference Images","Select a board and choose a picture",760,510);
        panel.AllowedContexts=UiContexts.Designer;
        UiKit.BindToggle(panel,key);
        panel.OnOpen+=()=>{if(!BoardRuntime.Designer||BoardRuntime.Selected==null)panel.Close();};
        var columns=panel.Columns(340,0);
        var library=columns[0].Section("Images",()=>ImageLibrary.Files.Count);
        library.List(()=>ImageLibrary.Files,(file,row)=>{row.Title=file;row.Selected=BoardRuntime.Selected?.File==file;}).OnSelect(file=>Run(()=>BoardRuntime.Assign(file))).Empty("No PNG or JPG files. Use Paste or Open folder.").Fill();
        var buttons=columns[0].Footer();
        buttons.Button("Open folder",()=>Run(()=>Process.Start(new ProcessStartInfo(ImageLibrary.Folder){UseShellExecute=true})));
        buttons.Button("Refresh",()=>Run(BoardRuntime.RefreshImages));
        var edit=columns[1].Section("Selected board");
        edit.Label(()=>BoardRuntime.Selected?.File is {Length:>0} file ? file : "NO IMAGE",TextStyle.Value).Wrap();
        edit.Label(()=>BoardRuntime.Selected?.SizeText ?? "Select exactly one Reference Image board.").Wrap();
        edit.FieldRow("Mirror").Toggle(()=>BoardRuntime.Selected?.Mirror??false,v=>Run(()=>BoardRuntime.SetMirror(v))).EnabledWhen(()=>BoardRuntime.Selected!=null);
        edit.Button("Paste",Paste,ButtonStyle.Primary).EnabledWhen(()=>BoardRuntime.Selected!=null);
        edit.Label("Scale, move and rotate with the native tools. Pictures keep their aspect ratio inside the board.").Wrap();
        edit.Label("Configure Reference Images / Panel and Reference Images / Paste in vanilla Settings / keybinds.").Wrap();
    }
    private static void Run(Action action)
    {
        try{action();panel.SetStatus("Ready",StatusKind.Success);}
        catch(Exception ex){panel.SetStatus(ex.Message,StatusKind.Error);Plugin.WarnOnce("panel error: "+ex.Message);}
    }
    internal static void Keys(){if(BoardRuntime.Designer && BoardRuntime.Selected!=null && paste.WasPressedThisFrame)Paste();}
    private static void Paste()=>Run(()=>{if(BoardRuntime.Designer && BoardRuntime.Selected!=null)BoardRuntime.Assign(ClipboardImport.Paste());});
    internal static void Open(){if(BoardRuntime.Designer && BoardRuntime.Selected!=null)panel.Open();}
    internal static void Close(){if(panel!=null && panel.IsOpen)panel.Close();}
}
