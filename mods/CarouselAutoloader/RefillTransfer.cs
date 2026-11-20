namespace SprocketCarouselAutoloader;

// AmmoRequest::IsMatch (0x210CF40): optional type, exact packed size, enough stock.
// Blueprint slot numbers identify editor records, not ammunition compatibility.
internal static class RefillMatch
{
    internal static bool Reserve(bool installed, float health, int size, int sourceSize, int stock) =>
        installed && health > 0 && size == sourceSize && stock > 0;
    internal static bool Matches(int size, int type, int invalidType, int sourceSize, int sourceType, int stock) =>
        size == sourceSize && (type == invalidType || type == sourceType) && stock >= 1;
}

internal readonly record struct RefillTypePin(int Requested, int Pinned)
{
    internal int Original(int current) => current == Pinned ? Requested : current;
    internal RefillTypePin With(int current, int next) => new(Original(current), next);
}

internal interface IRefillStore
{
    int Size { get; }
    int Type { get; set; }
    int Amount { get; }
    int Capacity { get; }
    bool Take();
    int Load(); // Native LoadAmmo(1) returns overflow: zero means accepted.
}

internal static class RefillTransfer
{
    internal static bool One<T>(T source, T destination, int size, int type, int invalidType, bool canRetype = false) where T : IRefillStore
    {
        int originalType = destination.Type;
        bool retype = originalType != source.Type;
        if (!RefillMatch.Matches(size, type, invalidType, source.Size, source.Type, source.Amount) ||
            destination.Size != size || (retype && !(canRetype && type == invalidType && destination.Amount == 0)) ||
            destination.Amount >= destination.Capacity) return false;
        int sourceBefore = source.Amount, destinationBefore = destination.Amount;
        try
        {
            if (!source.Take()) return false;
            if (retype) destination.Type = source.Type;
            int overflow = destination.Load();
            if (overflow == 0 && destination.Amount == destinationBefore + 1) return true;
            // Count is authoritative even if an event wrapper reported an inconsistent result.
            if (destination.Amount == destinationBefore + 1) return true;
        }
        catch
        {
            // Native change events fire AFTER the count changes. An event throwing after acceptance
            // must not refund an already stored round (nor refund a debit that never happened).
            if (destination.Amount == destinationBefore && retype) destination.Type = originalType;
            if (source.Amount == sourceBefore - 1 && destination.Amount == destinationBefore) source.Load();
            throw;
        }
        if (destination.Amount == destinationBefore && retype) destination.Type = originalType;
        if (source.Amount == sourceBefore - 1 && destination.Amount == destinationBefore) source.Load();
        return false;
    }
}
