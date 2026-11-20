using System.Numerics;
using System.Text.Json;
using SprocketCarouselAutoloader;

internal static class RefillRegressionTests
{
    private sealed class Store : IRefillStore
    {
        public int Size { get; set; } = (25 << 16) | 212;
        public int Type { get; set; } = 7;
        public int Amount { get; set; }
        public int Capacity { get; set; } = 42;
        internal int Slot, Debits, Loads;
        internal bool Installed = true, Broken, Reject, Throw, ThrowAfterAccept, RefuseDebit;
        public bool Take() { if (Amount == 0 || RefuseDebit) return false; Amount--; Debits++; return true; }
        public int Load()
        {
            Loads++;
            if (Throw) throw new InvalidOperationException("fixture storage rejected before acceptance");
            if (Reject || Amount >= Capacity) return 1;
            Amount++;
            if (ThrowAfterAccept) throw new InvalidOperationException("fixture event threw after acceptance");
            return 0;
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var fixture = JsonDocument.Parse(File.ReadAllText("tests/Fixtures/bradley-refill.json")).RootElement;
        int size = (fixture.GetProperty("calibreMm").GetInt32() << 16) | fixture.GetProperty("roundLengthMm").GetInt32();
        var evidence = new List<string>();
        foreach (var kind in new[] { "Belt", "Bustle", "Revolver", "Semi" })
        {
            var source = new Store { Slot = fixture.GetProperty("reserveShellSlot").GetInt32(), Amount = 9 };
            var destination = new Store { Slot = fixture.GetProperty("magazineShellSlot").GetInt32(), Amount = 0, Type = 9 };
            bool Found(Store s) => RefillMatch.Reserve(s.Installed, s.Broken ? 0 : 1, size, s.Size, s.Amount) &&
                RefillMatch.Matches(size, destination.Amount > 0 ? destination.Type : -1, -1, s.Size, s.Type, s.Amount);
            check(Found(source) && source.Slot != destination.Slot, kind + " Bradley same encoded size across slots 32 -> 20 found");
            check(fixture.GetProperty("magazineTypeGuid").GetString() != fixture.GetProperty("reserveTypeGuid").GetString() &&
                source.Type != destination.Type, kind + " real Bradley distinct native type identities represented");
            int total = source.Amount + destination.Amount;
            check(!RefillTransfer.One(source, destination, size, -1, -1) && source.Amount == 9 && destination.Type == 9,
                kind + " occupied cannon forbids empty box retype");
            check(!RefillTransfer.One(source, destination, size, 9, -1, true), kind + " explicit other requested type is refused");
            destination.Reject = true;
            check(!RefillTransfer.One(source, destination, size, -1, -1, true) && source.Amount == 9 && destination.Type == 9,
                kind + " refused first round restores original type and stock");
            destination.Reject = false;
            check(RefillTransfer.One(source, destination, size, -1, -1, true) && destination.Type == source.Type,
                kind + " Bradley wildcard transfer accepted with actual source identity at empty unoccupied boundary");
            check(source.Amount == 8 && destination.Amount == 1 && source.Debits == 2 && destination.Loads == 2 &&
                total == source.Amount + destination.Amount, kind + " exactly one real round conserved");
            foreach (var invalid in new[] { new Store { Amount = 3, Size = (30 << 16) | 265 },
                new Store { Amount = 0 }, new Store { Amount = 3, Broken = true },
                new Store { Amount = 3, Installed = false }, new Store { Amount = 3, Type = 8 } })
                check(!Found(invalid) && invalid.Debits == 0, kind + " incompatible/empty/broken/uninstalled/type reserve rejected");
            destination.Reject = true;
            check(!RefillTransfer.One(source, destination, size, 7, -1) && source.Amount == 8 && destination.Amount == 1,
                kind + " rejected round refunded");
            destination.Reject = false; destination.Throw = true;
            try { RefillTransfer.One(source, destination, size, 7, -1); check(false, kind + " expected throw"); }
            catch (InvalidOperationException) { check(source.Amount == 8 && destination.Amount == 1, kind + " throwing destination refunded"); }
            destination.Throw = false;
            destination.ThrowAfterAccept = true;
            try { RefillTransfer.One(source, destination, size, 7, -1); check(false, kind + " expected event throw"); }
            catch (InvalidOperationException) { check(source.Amount == 7 && destination.Amount == 2, kind + " accepted round not also refunded after event throw"); }
            destination.ThrowAfterAccept = false;
            source.RefuseDebit = true;
            check(!RefillTransfer.One(source, destination, size, 7, -1) && source.Amount + destination.Amount == total,
                kind + " failed debit creates nothing");
            source.RefuseDebit = false; destination.Amount = 42;
            check(!RefillTransfer.One(source, destination, size, 7, -1) && source.Amount == 7, kind + " full magazine never debits");
            destination.Amount = 0;
            while (RefillTransfer.One(source, destination, size, 7, -1))
                check(source.Amount + destination.Amount == 7, kind + " finite drain conservation");
            check(source.Amount == 0 && destination.Amount == 7, kind + " exhausted reserve creates nothing");
            evidence.Add(kind + ": slot 32 -> 20 accepted; different size/type, empty/broken/uninstalled rejected; debit/refund/conservation PASS");
        }
        check(RefillMatch.Matches(size, -1, -1, size, 8, 1), "native Invalid type is a wildcard");
        var pin = new RefillTypePin(-1, 9);
        check(pin.Original(9) == -1 && pin.Original(8) == 8, "remember own type pin without erasing explicit native requests");
        pin = pin.With(9, 7);
        check(pin.Original(7) == -1 && pin.Original(8) == 8, "new real box type retains original wildcard request");
        check(RefillPolicy.CrewStatus(true, true, true, true) == RefillReason.Crew, "assigned idle loader counts without operateCount gate");
        check(RefillPolicy.CrewStatus(true, true, false, true) == RefillReason.NoReach, "reach failure never mislabeled as missing crew");
        check(RefillPolicy.CrewStatus(true, true, true, false) == RefillReason.NoCrewTask, "missing task distinguished from missing assignment");
        check(RefillPolicy.CrewStatus(true, false, true, true) == RefillReason.NoCrew, "inactive or unhealthy crew remains blocked");
        var spec = new Spec("Bradley", ModelKind.Bustle, .248, .221, .308, 25, 75, 137, 42,
            Spacing: new Vector3(.01f));
        var grid = RackGrid.Of(spec.X, spec.Y, spec.Z, 25, 212, spec.Spacing);
        check(grid.Columns == 7 && grid.Layers == 6 && grid.Rows == 1 && grid.Capacity == 42,
            "native spacing: 248x221x308 / 25x212 = 7x6x1 = 42");
        var belt = BeltVisualGeometry.Create(spec);
        var bustle = AutoloaderModelGeometry.Create(spec);
        foreach (int stored in new[] { 42, 25, 2, 0 }) BeltVisualTests.Export("bradley-spaced-" + stored, belt, stored);
        File.WriteAllText("logs/ingame-0.3.2/bustle-spaced-42.json", JsonSerializer.Serialize(new
        {
            spec, state = new { Stored = 42, TopCount = 42, FillHeight = 0 },
            items = bustle.Meshes.Select(p => new { p.Name, vertices = p.Vertices, indices = p.Indices, materials = p.Materials })
        }, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        check(belt.Model.Complete && belt.Layout.Capacity == 42 && bustle.Complete && bustle.LayoutSlots == 42,
            "Belt and bustle models exactly match 42 native slots");
        check(Math.Abs(belt.Layout.PitchX - .035) < 1e-8 && Math.Abs(belt.Layout.PitchY - .035) < 1e-8,
            "Bradley centres retain 10 mm native gaps");
        for (int n = 0; n <= 42; n++)
        {
            var stock = belt.Layout.Stock(n);
            check(stock.UnderLayers * belt.Layout.PerLayer + stock.TopCount == n, "spaced visible stock equals native stock");
            check(stock.TopCount <= 7 && stock.UnderLayers <= 5, "spaced top stock stays in native grid");
        }
        var padded = spec with { MinPadding = new Vector3(.02f), MaxPadding = new Vector3(.01f), Capacity = 30 };
        var paddedGrid = RackGrid.Of(padded.X, padded.Y, padded.Z, 25, 212, padded.Spacing, padded.MinPadding, padded.MaxPadding);
        check(paddedGrid.Capacity == 30 && BeltLayout.Of(padded).Capacity == 30 &&
            AutoloaderModelGeometry.Create(padded).LayoutSlots == 30, "asymmetric native padding shared by gameplay and both layouts");
        evidence.Add("Bradley native spacing: 7 columns x 6 layers x 1 row = 42; Belt and bustle complete; all 0..42 stock states PASS");
        File.WriteAllLines("logs/0.3.2-refill-regression.txt", evidence);
        Console.WriteLine("0.3.2 Bradley reserve/refund/spacing regressions PASS (shared production policies; native/EB execution requires game).");
    }
}
