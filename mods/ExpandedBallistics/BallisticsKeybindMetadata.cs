using System.Reflection;

namespace SprocketExpandedBallistics;

internal static class BallisticsKeybindMetadata
{
    internal const string Owner="sprocket.expandedballistics";
    internal const string Name="Sprocket Expanded Ballistics";
    private static int warnings;
    internal static void Register(Type api)
    {
        try
        {
            api.GetMethod("RegisterModMetadata",BindingFlags.Public|BindingFlags.Static,
                null,new[]{typeof(string),typeof(string),typeof(string)},null)
                ?.Invoke(null,new object[]{Owner,"Nero",Name});
        }
        catch(Exception ex)
        {
            // Display metadata is optional; it must not disable existing saved controls.
            if(warnings++<2)Plugin.ModLog.LogWarning("[Ballistics keybinds] Display metadata unavailable: "+ex.Message);
        }
    }
}
