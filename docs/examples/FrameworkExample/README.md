# Framework Example

MIT. Author: Nero.

A BepInEx 6 IL2CPP plugin demonstrating Keybinds 0.1.10 and UI Kit 0.1.13:
author metadata, an unbound panel toggle, two registered settings, a Diagnostics
section, guarded setup, and live settings in a custom panel through `Container.Setting`.
The opacity setting changes the panel content opacity. Configuration stays in
`BepInEx\config\sprocket.example.framework.cfg`.

Build with a .NET SDK and the generated BepInEx core/interop assemblies:
`dotnet build FrameworkExample.csproj -c Release -p:GameDir="<Sprocket folder>"`.
The project defaults to the Steam installation. Override `KeybindsDll` and `UiKitDll`
with `-p:KeybindsDll="<DLL path>" -p:UiKitDll="<DLL path>"` when needed.
All references use `Private=false`; distribute only your plugin DLL.

With Sprocket closed, copy `bin/Release/net6.0/FrameworkExample.dll` into
`BepInEx\plugins\FrameworkExample\`. Install both framework components first;
their absence or incompatible versions prevent this example from loading.
In Settings > Controls > Mod keybinds, bind **Toggle example panel** under
**Nero > Framework Example**, then leave Settings and press that binding.
The same settings also appear through Settings > Mod settings > Open after
leaving Settings. Diagnostics.DetailedLogging defaults to false.

See [Sprocket Mod Framework](../../FRAMEWORK.md) for the integration contract.
