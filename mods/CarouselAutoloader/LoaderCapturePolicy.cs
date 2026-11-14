namespace SprocketCarouselAutoloader;

// A controller captures its contributors during native assembly. Repair only a
// loader already advertised by its own weapon; preserve captured crew and order.
internal static class LoaderCapturePolicy
{
    internal static bool TryAppend<T>(T[] captured, T automatic, bool advertised,
        Func<T, T, bool> same, out T[] result)
    {
        result = captured;
        if (!advertised || captured.Any(item => same(item, automatic))) return false;
        result = new T[captured.Length + 1];
        Array.Copy(captured, result, captured.Length);
        result[^1] = automatic;
        return true;
    }
}
