namespace SprocketEngineExpansion.Core;
public static class PowerpackMeshNames
{
    public static string? Transmission(string name)
    {
        string n=name.ToLowerInvariant();
        if(n.StartsWith("gearbox_tra_"))
        {
            if(n.Contains("outputl"))return "tra-output-l";
            if(n.Contains("outputr"))return "tra-output-r";
            if(n.Contains("input"))return "tra-input";
            if(n.Contains("mid"))return "tra-shroud";
        }
        if(n.StartsWith("longitudinalgearboxinput")||n.StartsWith("gearbox_lon_input"))return "lon-input";
        if(n.StartsWith("longitudinalgearboxoutput")||n.StartsWith("gearbox_lon_output"))return "lon-output";
        if(n.StartsWith("gearbox_lon_mid"))return "lon-shroud";
        return null;
    }
}
