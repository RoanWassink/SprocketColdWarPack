using SprocketTelescopicMast;
HingeRuntime.RunDiscoveryTests();
namespace SprocketTelescopicMast
{
    using Sprocket.Vehicles;
    using SprocketTelescopicMast.Io;
    using UnityEngine;
    internal static partial class HingeRuntime
    {
        private static int checks;
        private static void Check(bool pass, string why) { checks++; if (!pass) throw new Exception(why); }
        private static VehicleObject Part(VehicleBehaviour vehicle, int id, string component)
        {
            var obj = new VehicleObject { Id = id, Vehicle = vehicle.VehicleInfo };
            obj.Components.Values.Add(new VehicleComponent { Id = id + 100, ComponentID = component, VehicleObject = obj });
            return obj;
        }
        internal static void RunDiscoveryTests()
        {
            File.WriteAllText("logs/discovery-traces-0.9.6.txt", "");
            DesignerRoundTrip(false);
            DesignerRoundTrip(true);
            // Retain the 0.9.5 runtime-owner cases, including physics disabled.
            // These are NOT evidence for a real designer vehicle; the cases above are.
            foreach (bool managed in new[] { true, false })
            foreach (bool physicsDisabled in new[] { true, false })
            {
                Vehicles.Clear(); FlowParts.Clear(); Flows.Clear(); Leaves.Clear(); KnownExternalParts.Clear(); Warnings.Clear();
                IoRegistry.ClearForTests(); IoApiCore.ClearForTests(); Plugin.Traces.Clear();
                IoRegistry.RegisterInput(new VehicleBrakingInput());
                DesignerMode = physicsDisabled; HingePanel.IsOpen = true; Time.unscaledTime = 10;
                var calls = new List<(GameObject Part, bool On)>();
                var vehicle = new VehicleBehaviour(42);
                var original = Part(vehicle, 1, "vfxLamp"); original.PhysicsEnabled = !physicsDisabled;
                var mirror = Part(vehicle, 2, "vfxLamp"); mirror.PhysicsEnabled = !physicsDisabled;
                string copiedId = Guid.NewGuid().ToString("D");
                string? envelope = FlowJson.Envelope(copiedId, new FlowDocument { Rev = 2 });
                bool registered = managed ? IoApi.RegisterManagedOutput("visualeffects.lamp", "Lamp", "vfxLamp", (p,on) => calls.Add((p,on)))
                    : IoApi.RegisterOutput("visualeffects.lamp", "Lamp", "vfxLamp", (p,on) => calls.Add((p,on)), _ => envelope, (_,_) => { });
                Check(registered, "fake registration");
                // Exactly the predicate used by Save/Load hooks; reader intentionally empty.
                Check(IsExternalPart(original), "hook recognizes output");
                if (managed)
                {
                    RegisterFlowPart(original);
                    FlowParts[1].SavedId = copiedId; FlowParts[1].Copy = new FlowDocument { Rev = 2 };
                }
                Check(vehicle.VehicleInfo.ObjectReader.Items.Count == 0, "reader empty");
                PrepareExternal(); PrepareFlows();
                Check(Vehicles.ContainsKey(vehicle.Pointer), "known hook object seeds vehicle without scene discovery");
                var io = Vehicles[vehicle.Pointer];
                Check(io.External.Count == 1 && io.External[0].OutputId == "ext#visualeffects.lamp#101", "stable external key");
                var panel = PanelFlow(physicsDisabled, null)!;
                Check(panel != null && panel.Parts.Count == 1 && panel.Parts[0].Title == "Round lamp 1", "real PanelFlow lists readable lamp");
                Check(FlowTemplates.Target(FlowTemplates.Brake, panel!.Flow, null, panel.Parts) != null, "Brake target enabled");
                var editor = new FlowEditor(panel.Flow);
                Check(editor.Template(FlowTemplates.Brake, panel.Parts), "real template connects Brake to lamp");
                Check(panel.Flow.Wires.Any(w => panel.Flow.Nodes.Any(n => n.Id == w.From && n.Type == "vehicle.braking") && panel.Flow.Nodes.Any(n => n.Id == w.To && n.Type == "visualeffects.lamp")), "Brake wire endpoints");
                panel.Save();
                io.Context.VehicleToken = vehicle.gameObject;
                io.Context.Braking = true; TickFlow(io, 1); TickExternal(io);
                io.Context.Braking = false; TickFlow(io, 2); TickExternal(io);
                Check(calls.Any(c => c.Part == original.Components[0].gameObject && c.On) && calls.Last().On == false, "actual tick apply true/false");
                int scans = Plugin.Traces.Count(t => t.StartsWith("[IO EXT] scan"));
                ScanExternal(io); ScanExternal(io);
                Check(Plugin.Traces.Count(t => t.StartsWith("[IO EXT] scan")) == scans, "unchanged scan quiet");
                // Mirror has the same saved identity but a distinct object/component instance.
                Check(IsExternalPart(mirror), "mirror hook recognized"); RegisterFlowPart(mirror);
                if (managed) { FlowParts[2].SavedId = copiedId; FlowParts[2].Copy = panel.Flow; }
                else envelope = FlowJson.Envelope(copiedId, panel.Flow);
                Time.unscaledTime += 3; PrepareExternal(); PrepareFlows();
                panel = PanelFlow(physicsDisabled, panel.Token)!;
                Check(panel.Parts.Count == 2 && panel.Parts.Select(p => p.Id).Distinct().Count() == 2, "mirror own output and new id");
                Check(io.External.OrderBy(e => e.Component.GetInstanceID()).Select(e => e.Sequence).SequenceEqual(new[] { 1,2 }), "Sequence preserved");
                Check(Plugin.Traces.Any(t => t.Contains("Duplicate ioPartId")), "duplicate path exercised");
                Check(Plugin.Traces.Last(t => t.StartsWith("[IO EXT] scan")).Contains("reader=0 known=2 found=visualeffects.lamp:2"), "diagnostic counts prove fallback");
                // Registration path alone, no IsExternalPart call by the test.
                var third = Part(vehicle, 3, "vfxLamp"); RegisterFlowPart(third);
                Time.unscaledTime += 3; PrepareExternal(); PrepareFlows();
                Check(io.External.Count == 3, "RegisterFlowPart is a discovery source for managed and v1");
                // Correct reader source works independently of known objects.
                var readerOnly = Part(vehicle, 4, "vfxLamp"); vehicle.VehicleInfo.ObjectReader.Items.Values.Add(readerOnly);
                Check(vehicle.VehicleInfo.ObjectReader.GetAll<VehicleObject>().Length == 0, "old generic reader misses object");
                ScanExternal(io); Check(io.External.Count == 4, "Items reader adds object even when known parts already found");
                Check(calls.All(c => c.Part != readerOnly.Components[0].gameObject), "scan itself never calls apply");
                vehicle.VehicleInfo.ObjectReader.Items.Values.Add(original);
                ScanExternal(io); Check(io.External.Count == 4, "reader and known objects deduplicate by component instance");
                if (managed)
                {
                    KnownExternalParts.Clear(); vehicle.VehicleInfo.ObjectReader.Items.Values.Clear();
                    ScanExternal(io);
                    Check(io.External.Count == 3, "managed FlowParts alone feed discovery with an empty reader");
                    vehicle.VehicleInfo.ObjectReader.Items.Values.Add(readerOnly); ScanExternal(io);
                }
                // No silent disappearance: use actual PanelFlow with an unlisted managed part.
                if (managed)
                {
                    var e = io.External[0]; e.Disabled = true;
                    var fallback = PanelFlow(physicsDisabled, panel.Token)!;
                    Check(fallback.Parts.Count == 3, "registered managed carrier stays listed without ready ext");
                    Check(!Plugin.Traces.Any(t => t.StartsWith("[IO EXT] managed part not listed:")), "registered carrier never warns");
                }
                mirror.Released = true; ScanExternal(io); Check(io.External.Count == 3, "released mirror removed");
                var otherVehicle = new VehicleBehaviour(43); third.Vehicle = otherVehicle.VehicleInfo;
                PrepareKnownExternalVehicles(); ScanExternal(io); ScanExternal(Vehicles[otherVehicle.Pointer]);
                Check(io.External.Count == 2 && Vehicles[otherVehicle.Pointer].External.Count == 1, "membership follows current owner; no vehicle mixing");
                Console.WriteLine($"PASS managed={managed} runtimeOwnerPhysicsDisabled={physicsDisabled}: hook-only, empty reader, PanelFlow, Brake template, tick true/false, mirror, reader union, lifecycle, change-only logging");
                File.AppendAllLines("logs/discovery-traces-0.9.6.txt", Plugin.Traces);
            }
            Console.WriteLine($"PASS {checks} production discovery integration checks");
        }

        private static void DesignerRoundTrip(bool nonCastable)
        {
            Vehicles.Clear(); FlowParts.Clear(); Flows.Clear(); Leaves.Clear(); KnownExternalParts.Clear(); Warnings.Clear();
            IoRegistry.ClearForTests(); IoApiCore.ClearForTests(); Plugin.Traces.Clear();
            IoRegistry.RegisterInput(new VehicleBrakingInput());
            DesignerMode = true; Time.unscaledTime = 10;
            var calls = new List<(GameObject Part, bool On)>();
            Check(IoApi.RegisterManagedOutput("visualeffects.lamp", "Lamp", "vfxLamp", (p,on) => calls.Add((p,on))), "designer registration");
            var vehicle = new Vehicle { Pointer = (IntPtr)10042, Behaviour = nonCastable ? new Native() : null };
            VehicleObject Make(int id) {
                var obj = new VehicleObject { Id = id, Vehicle = vehicle };
                obj.Components.Values.Add(new VehicleComponent { Id = id + 100, ComponentID = "vfxLamp", VehicleObject = obj });
                vehicle.ObjectReader.Items.Values.Add(obj); return obj;
            }
            var original = Make(1); var mirror = Make(2);
            Check(vehicle.Behaviour?.TryCast<VehicleBehaviour>() == null, "designer has no runtime behaviour");
            Check(UnityEngine.Object.FindObjectsOfType<VehicleBehaviour>().Length == 0, "scene discovery empty");
            Check(vehicle.ObjectReader.GetAll<VehicleObject>().Length == 0 && vehicle.ObjectReader.Items.Count == 2, "native reader contract");
            var seed = new Il2CppSystem.Runtime.Serialization.SerializationInfo();
            seed.AddValue(FlowDocument.PartKey, Guid.NewGuid().ToString("D"), new());
            seed.AddValue(FlowDocument.StateKey, FlowJson.Write(new FlowDocument { Rev = 2 }), new());
            // Save/Load hook method bodies, not direct mutations of FlowParts.
            LoadFlow(original, seed); LoadFlow(mirror, seed); RegisterFlowPart(original); RegisterFlowPart(mirror);
            PrepareExternal(); PrepareFlows();
            Check(Vehicles.Count == 0, "designer never creates VehicleIo or external scan");
            var panel = PanelFlow(true, null)!;
            Check(panel != null && panel.Parts.Count == 2, "designer lamp and mirror listed");
            Check(panel!.Parts.Select(p => p.Id).Distinct().Count() == 2, "duplicate designer identity remapped");
            Check(panel.Parts.Select(p => p.Title).SequenceEqual(new[] { "Round lamp 1", "Round lamp 2" }), "M2 numbering");
            Check(panel.Parts.All(p => p.Type == "visualeffects.lamp" && p.Provider == IoApiCore.FindOutput(p.Type)!.Provider), "registration type/provider");
            Check(FlowTemplates.Target(FlowTemplates.Brake, panel.Flow, null, panel.Parts) != null, "designer Brake target enabled");
            Check(new FlowEditor(panel.Flow).Template(FlowTemplates.Brake, panel.Parts), "designer Brake template");
            panel.Save();
            int listed = Plugin.Traces.Count(t => t.StartsWith("[IO EXT] designer list"));
            PanelFlow(true, panel.Token); PanelFlow(true, panel.Token);
            Check(Plugin.Traces.Count(t => t.StartsWith("[IO EXT] designer list")) == listed, "unchanged designer list quiet");
            Check(Plugin.Traces.Any(t => t == "[IO EXT] designer list parts=2 registration=visualeffects.lamp:2"), "designer diagnostic counts");
            Check(!Plugin.Traces.Any(t => t.StartsWith("[IO EXT] managed part not listed:")), "registered designer has no warning");
            var states = new[] { original, mirror }.Select(obj => {
                var state = new Il2CppSystem.Runtime.Serialization.SerializationInfo();
                state.AddValue("stock", "preserved", new()); SaveFlow(obj, state); return state;
            }).ToArray();
            foreach (var state in states) {
                Check(state.Values["stock"] == "preserved" && state.Values.ContainsKey("ioPartId") && state.Values.ContainsKey("ioFlowV1"), "save writes keys and preserves stock state");
                var flow = FlowJson.Parse(state.Values["ioFlowV1"]);
                Check(flow.Wires.Any(w => flow.Nodes.Any(n => n.Id == w.From && n.Type == "vehicle.braking") && flow.Nodes.Any(n => n.Id == w.To && n.Type == "visualeffects.lamp")), "saved Brake wire");
            }
            File.WriteAllText($"logs/designer-saved-state-{nonCastable}-0.9.6.json", System.Text.Json.JsonSerializer.Serialize(states.Select(s => s.Values), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            original.Components[0].ComponentID = "unregistered";
            Check(PanelFlow(true, panel.Token)!.Parts.Count == 1, "only registered session carriers listed");
            PanelFlow(true, panel.Token);
            Check(Plugin.Traces.Count(t => t.StartsWith("[IO EXT] managed part not listed:")) == 1, "missing registration warns once per part");
            Check(Plugin.Traces.Any(t => t == "[IO EXT] designer list parts=1 registration=visualeffects.lamp:1"), "changed designer list logged");
            original.Components[0].ComponentID = "vfxLamp";
            PanelFlow(true, panel.Token);
            int beforeQuiet = Plugin.Traces.Count;
            Plugin.Detailed = false; original.Name = "Renamed lamp";
            PanelFlow(true, panel.Token);
            Check(Plugin.Traces.Count == beforeQuiet, "DetailedLogging false suppresses designer list trace");
            Plugin.Detailed = true;
            // Clean transition: fresh runtime objects and only the serialized state survive.
            Vehicles.Clear(); FlowParts.Clear(); Flows.Clear(); KnownExternalParts.Clear();
            DesignerMode = false; Time.unscaledTime += 3;
            var runtime = new VehicleBehaviour(42);
            var fresh = new[] { Part(runtime, 11, "vfxLamp"), Part(runtime, 12, "vfxLamp") };
            for (int i = 0; i < fresh.Length; i++) {
                fresh[i].PhysicsEnabled = true; runtime.VehicleInfo.ObjectReader.Items.Values.Add(fresh[i]); LoadFlow(fresh[i], states[i]);
            }
            PrepareExternal(); PrepareFlows();
            var io = Vehicles[runtime.Pointer];
            Check(io.External.Count == 2, "runtime Items scan finds fresh lamps");
            for (int i = 0; i < fresh.Length; i++)
                Check(io.External.Single(e => e.Component.VehicleObject == fresh[i]).PartId == states[i].Values["ioPartId"], "runtime external uses saved carrier identity");
            var playPanel = PanelFlow(false, null)!;
            Check(playPanel.Parts.Count == 2 && playPanel.Parts.Select(p => p.Id).Distinct().Count() == 2, "ready external overrides carrier without duplicate");
            var targetId = playPanel.Flow.Nodes.Single(n => n.Type == "visualeffects.lamp").PartId;
            var target = io.External.Single(e => e.PartId == targetId);
            io.Context.VehicleToken = runtime.gameObject; io.Context.Braking = true; TickFlow(io, 1); TickExternal(io);
            Check(calls.Any(c => c.Part == target.Component.gameObject && c.On), "roundtrip braking apply true");
            io.Context.Braking = false; TickFlow(io, 2); TickExternal(io);
            Check(calls.Last(c => c.Part == target.Component.gameObject).On == false, "roundtrip release apply false");
            Console.WriteLine($"PASS real designer nonCastable={nonCastable}: no VehicleBehaviour, hook-only mirror, M2 list, Brake, SaveFlow/LoadFlow, fresh play objects, identity, apply true/false");
            File.AppendAllLines("logs/discovery-traces-0.9.6.txt", Plugin.Traces);
        }
    }
}
