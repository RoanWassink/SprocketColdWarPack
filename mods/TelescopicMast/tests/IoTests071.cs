using SprocketTelescopicMast.Io;

// 0.7.1: in-place state merge (existing entries keep name/value/type, only hingeIoV1 is added or
// replaced) and fire detection from the native ProjectileRegister.nextID counter around
// FireInternal, with the existing duplicate filter.
internal static class IoTests071
{
    private static int checks;
    private static void True(bool condition, string what) { checks++; if (!condition) throw new Exception("FAIL: " + what); }

    private sealed class Bag : IStateBag<Type>
    {
        internal readonly List<string> Names = new();
        internal readonly List<string> Values = new();
        internal readonly List<Type> Types = new();
        public int Count => Names.Count;
        public string NameAt(int index) => Names[index];
        public string? ValueAt(int index) => Values[index];
        public void Set(int index, string key, string value, Type type) { Values[index] = value; Types[index] = type; }
        public void Add(string key, string value, Type type) { Names.Add(key); Values.Add(value); Types.Add(type); }
    }

    internal static int Run()
    {
        MergeAddsOnlyOwnKey();
        MergeReplacesInPlace();
        MergeEmptyState();
        FireNeedsARealProjectile();
        FireDuplicateFilter();
        return checks;
    }

    private static void MergeAddsOnlyOwnKey()
    {
        var bag = new Bag();
        bag.Add("vanillaMass", "12.5", typeof(int));
        bag.Add("otherMod", "{\"a\":1}", typeof(string));
        bool replaced = StateMerge.Merge(bag, "hingeIoV1", "{\"v\":1}", typeof(string));
        True(!replaced, "new hinge key reports add");
        True(bag.Count == 3, "existing entries stay and one key is added");
        True(bag.Names[0] == "vanillaMass" && bag.Values[0] == "12.5" && bag.Types[0] == typeof(int), "first existing entry keeps name, value and type");
        True(bag.Names[1] == "otherMod" && bag.Values[1] == "{\"a\":1}" && bag.Types[1] == typeof(string), "second existing entry keeps name, value and type");
        True(bag.Names[2] == "hingeIoV1" && bag.Values[2] == "{\"v\":1}" && bag.Types[2] == typeof(string), "hinge key added once with the JSON value");
    }

    private static void MergeReplacesInPlace()
    {
        var bag = new Bag();
        bag.Add("before", "keep", typeof(int));
        bag.Add("hingeIoV1", "{\"old\":1}", typeof(string));
        bag.Add("after", "keep2", typeof(string));
        bool replaced = StateMerge.Merge(bag, "hingeIoV1", "{\"new\":2}", typeof(string));
        True(replaced, "existing hinge key reports replace");
        True(bag.Count == 3, "replace does not add a duplicate");
        True(bag.Names[0] == "before" && bag.Values[0] == "keep" && bag.Types[0] == typeof(int), "entry before the key is untouched");
        True(bag.Names[2] == "after" && bag.Values[2] == "keep2", "entry after the key is untouched");
        True(bag.Names[1] == "hingeIoV1" && bag.Values[1] == "{\"new\":2}" && bag.Types[1] == typeof(string), "only the hinge value (and its type) changes, in place");
    }

    private static void MergeEmptyState()
    {
        var bag = new Bag();
        bool replaced = StateMerge.Merge(bag, "hingeIoV1", "{}", typeof(string));
        True(!replaced && bag.Count == 1 && bag.Names[0] == "hingeIoV1", "empty state gets the key");
    }

    private static void FireNeedsARealProjectile()
    {
        var duplicates = new DuplicateFilter(.15);
        True(FireGate.Launched(41, 42), "moved nextID means a projectile left");
        True(!FireGate.Launched(42, 42), "unchanged nextID means no projectile");
        True(FireGate.Launched(0x7FFFFFFE, 0), "nextID wrap still means a projectile left");
        True(FireGate.Accept(FireGate.Launched(41, 42), 7, 10.0, duplicates), "real projectile counts");
        True(!FireGate.Accept(FireGate.Launched(42, 42), 7, 10.16, duplicates), "no projectile does not count");
        True(FireGate.Accept(FireGate.Launched(42, 43), 7, 10.17, duplicates), "no projectile did not consume the duplicate window");
    }

    private static void FireDuplicateFilter()
    {
        var duplicates = new DuplicateFilter(.15);
        True(FireGate.Accept(FireGate.Launched(50, 51), 11, 20.0, duplicates), "first real shot counts");
        True(!FireGate.Accept(FireGate.Launched(50, 51), 11, 20.05, duplicates), "duplicate within 150 ms counts once");
        True(!FireGate.Accept(FireGate.Launched(50, 52), 11, 20.14, duplicates), "second projectile in the window still counts once");
        True(FireGate.Accept(FireGate.Launched(50, 53), 11, 20.16, duplicates), "same cannon after the window counts again");
        True(FireGate.Accept(FireGate.Launched(50, 54), 12, 20.16, duplicates), "another cannon is independent");
    }
}
