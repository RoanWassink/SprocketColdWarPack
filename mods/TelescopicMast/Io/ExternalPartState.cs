namespace SprocketTelescopicMast.Io;

/// <summary>
/// The three state entries an external API part can carry: its managed flow copy ("ioFlowV1"),
/// its stable part id ("ioPartId") and its per-part data ("ioPartDataV1"). Always the existing
/// StateMerge, so every other entry keeps its name, value and type.
/// </summary>
internal static class ExternalPartState
{
    internal static void WriteFlow<TType>(IStateBag<TType> state, TType stringType, string partId, FlowDocument flow)
    {
        StateMerge.Merge(state, FlowDocument.PartKey, partId, stringType);
        StateMerge.Merge(state, FlowDocument.StateKey, FlowJson.Write(flow), stringType);
    }

    /// <summary>Read a managed flow copy. Returns an error text only for a present but unreadable copy.</summary>
    internal static string? ReadFlow<TType>(IStateBag<TType> state, out string? partId, out FlowDocument? flow)
    {
        partId = null; flow = null; string? error = null;
        for (int i = 0; i < state.Count; i++)
        {
            string name = state.NameAt(i);
            if (name == FlowDocument.PartKey) partId = state.ValueAt(i);
            else if (name == FlowDocument.StateKey)
            {
                string? json = state.ValueAt(i);
                if (string.IsNullOrWhiteSpace(json)) continue;
                try { flow = FlowJson.Parse(json); }
                catch (Exception ex) { error = ex.Message; }
            }
        }
        return error;
    }

    /// <summary>Write the part-data key only when this part ever held data (including an emptied object).</summary>
    internal static void WriteData<TType>(IStateBag<TType> state, TType stringType, object part)
    {
        string? json = PartData.StateJson(part);
        if (json != null) StateMerge.Merge(state, PartData.StateKey, json, stringType);
    }

    internal static void ReadData<TType>(IStateBag<TType> state, object part, string componentId)
    {
        PartData.Attach(part, componentId);
        for (int i = 0; i < state.Count; i++)
            if (state.NameAt(i) == PartData.StateKey)
            {
                // A state without the key is the transform backup of a part whose component data
                // arrived via LoadData; keep that data instead of clearing it.
                PartData.ClearEntry(part);
                PartData.LoadState(part, state.ValueAt(i));
                return;
            }
    }
}
