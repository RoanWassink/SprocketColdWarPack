namespace SprocketExpandedBallistics.Materials;

internal enum MaterialEditorTarget { UiKit, JsonEditor }

/// <summary>
/// "Edit materials" routes to the Kit panel while <c>[Material editor] Use UI Kit</c> is true and the
/// panel opens. Only then may the older JSON Editor session be used as the fallback.
/// </summary>
internal static class MaterialEditorRoute
{
    internal static MaterialEditorTarget Choose(bool useKit, bool kitAvailable) =>
        useKit && kitAvailable ? MaterialEditorTarget.UiKit : MaterialEditorTarget.JsonEditor;
}
