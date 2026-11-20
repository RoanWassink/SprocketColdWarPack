using System.Text.RegularExpressions;

// Lex only the Load body: string/comment braces and separate catch/finally blocks
// must not make an unguarded call appear to be covered by an earlier try.
internal static class LoadSafety
{
    internal sealed record Call(string Name, int TryBlock);
    internal static Call[] Calls(string source)
    {
        const string nonCode = """//[^\r\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'""";
        var code = Regex.Replace(source, nonCode, " ");
        var load = Regex.Match(code, @"\bvoid\s+Load\s*\(\s*\)\s*\{");
        if (!load.Success) throw new InvalidDataException("Load body not found for safety scan.");
        var tokens = Regex.Matches(code[(load.Index + load.Length - 1)..], @"[A-Za-z_]\w*|[^\s]").Select(m => m.Value).ToArray();
        var scopes = new Stack<int>();
        var result = new List<Call>();
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token == "{") scopes.Push(i > 0 && tokens[i - 1] == "try" ? i : scopes.Count > 0 ? scopes.Peek() : -1);
            else if (token == "}") { scopes.Pop(); if (scopes.Count == 0) break; }
            else if (i + 3 < tokens.Length && tokens[i + 1] == "." && tokens[i + 3] == "(")
            {
                string name = token + "." + tokens[i + 2];
                if (name is "Keybinds.RegisterButton" or "Keybinds.RegisterModMetadata" or "ModSettings.Register" or "Config.Bind")
                    result.Add(new(name, scopes.Peek()));
            }
        }
        return result.ToArray();
    }
    internal static void Run(string plugin, Action<bool, string> check)
    {
        var calls = Calls(plugin);
        check(calls.Length == 4, "Load safety scan sees all four optional initialization calls");
        foreach (var call in calls) check(call.TryBlock >= 0, "Load optional call is inside try: " + call.Name);
        check(calls[0].Name == "Config.Bind", "DetailedLogging is the first optional initialization");
        check(calls.Where(c => c.Name != "Config.Bind").Select(c => c.TryBlock).Distinct().Count() == 1,
            "own metadata, shortcut and settings registration share one try block");
        check(Regex.Matches(plugin, "Mod settings shortcut unavailable; keybind API still works").Count == 1,
            "optional settings initialization has one shortcut warning");
        foreach (string name in new[] { "Keybinds.RegisterButton", "Keybinds.RegisterModMetadata", "ModSettings.Register", "Config.Bind" })
        {
            check(Calls("void Load() { " + name + "(); }").Single().TryBlock < 0, "scan rejects unguarded " + name);
            check(Calls("void Load() { try { if (ready) { " + name + "(); } } catch {} }").Single().TryBlock >= 0, "scan accepts nested guarded " + name);
            check(Calls("void Load() { try {} catch { " + name + "(); } }").Single().TryBlock < 0, "scan rejects call in catch: " + name);
            check(Calls("void Load() { try {} finally { " + name + "(); } }").Single().TryBlock < 0, "scan rejects call in finally: " + name);
            check(Calls("void Load() { try {} catch {} " + name + "(); }").Single().TryBlock < 0, "earlier try does not guard " + name);
        }
        check(Calls("void Load() { // try { Keybinds.RegisterButton(); }\n var s = \"try { ModSettings.Register(); }\"; }").Length == 0,
            "Load scan ignores fake try/calls in comments and strings");
    }
}
