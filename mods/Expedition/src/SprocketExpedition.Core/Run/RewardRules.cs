#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SprocketExpedition.Core.Run;

public static class RewardRules
{
    private const int MaxInventory = 6;
    private const int MaxActive = 3;
    private const int MaxChoices = 3;
    private const int MaxFleet = 8;
    private const int MaxGrantedOffers = 10_000;
    private const int MaxOfferRoll = 10_000;

    private static readonly RewardKind[] KindPool =
    {
        RewardKind.MassProduction, RewardKind.AssemblyLine, RewardKind.EliteWorkshop,
        RewardKind.SwarmDoctrine, RewardKind.FieldReinforcements, RewardKind.LendLease,
        RewardKind.SalvageRights, RewardKind.VeteranCrew, RewardKind.WarBonds,
        RewardKind.EmergencyReserve, RewardKind.OperationalFreedom, RewardKind.LastStand,
        RewardKind.CapturedPrototype, RewardKind.ExperimentalContract, RewardKind.EmergencyRecovery
    };

    // EmergencyRecovery deliberately has a very low type weight.
    private static readonly int[] KindWeights = { 10, 8, 5, 6, 8, 6, 6, 7, 8, 7, 4, 4, 6, 3, 1 };
    private static readonly int KindWeightTotal = KindWeights.Sum();

    public static RewardItem Create(long seed, int round, string offerId, int risk,
        bool boss = false, int choice = 0, string? blueprintId = null, int rulesVersion = 5)
        => CreateCore(seed, round, offerId, risk, boss, choice, blueprintId, forcedKind: null, rulesVersion);

    public static RewardState Activate(RewardState state, string id, bool cashBonds = false,
        decimal baseBudget = 0m)
    {
        Validate(state);
        ValidateId(id);
        if (baseBudget < 0m) throw Invalid("baseBudget must not be negative.");

        int index = IndexOf(state.Inventory, id);
        if (index < 0) throw Invalid($"Item '{id}' is not in the inventory.");
        RewardItem item = state.Inventory[index];
        RewardItem[] inventory = RemoveAt(state.Inventory, index);

        if (cashBonds)
        {
            if (item.Kind != RewardKind.WarBonds)
                throw Invalid("Only War Bonds can be cashed in.");
            decimal funds = SafeAdd(state.EarnedFunds, SafeMultiply(baseBudget, 0.15m));
            return state with { Inventory = inventory, EarnedFunds = funds };
        }

        if (item.Kind == RewardKind.OperationalFreedom)
        {
            int roll = Math.Min(MaxOfferRoll, state.OfferRoll + 1);
            return state with { Inventory = inventory, OfferRoll = roll };
        }

        RewardItem? replaced = ActiveReplacement(state, item);
        if (ActivationBlockReason(state, item) is { } blocked) throw Invalid(blocked);

        RewardItem activated = item.Kind == RewardKind.WarBonds
            ? item with { Duration = Math.Max(2, item.Duration), RemainingRounds = 2 }
            : item with { RemainingRounds = item.Duration };

        // A new pickup replaces its active family in place, including when all
        // slots are occupied. Its own strength, blueprint and full timer win;
        // discarded effects/delayed payouts are not applied on replacement.
        var active = replaced is null ? Append(state.Active, activated) : state.Active
            .Select(existing => existing.Id == replaced.Id ? activated : existing).ToArray();
        return state with { Inventory = inventory, Active = active };
    }

    public static RewardItem? ActiveReplacement(RewardState state, RewardItem item)
    {
        Validate(state);
        ValidateItem(item, "item");
        return item.Kind == RewardKind.OperationalFreedom ? null
            : state.Active.FirstOrDefault(active => FamilyOf(active.Kind) == FamilyOf(item.Kind));
    }

    public static string? ActivationBlockReason(RewardState state, RewardItem item)
    {
        Validate(state);
        ValidateItem(item, "item");
        return item.Kind != RewardKind.OperationalFreedom && ActiveReplacement(state, item) == null
            && state.Active.Count >= MaxActive
            ? "All 3 active slots are occupied. Replace an item of the same type, or use MANAGE ACTIVE ITEMS to discard an active bonus."
            : null;
    }

    public static RewardState DiscardActive(RewardState state, string id)
    {
        Validate(state);
        ValidateId(id);
        var index = IndexOf(state.Active, id);
        if (index < 0) throw Invalid($"Item '{id}' is not active.");
        // Discard, not return to storage: prevents refreshing an item's timer
        // or recovering a consumed charge by deactivating/reactivating it.
        return state with { Active = RemoveAt(state.Active, index) };
    }

    public static RewardState Discard(RewardState state, string id)
    {
        Validate(state);
        ValidateId(id);
        int index = IndexOf(state.Inventory, id);
        if (index < 0) throw Invalid($"Item '{id}' is not in the inventory.");
        return state with { Inventory = RemoveAt(state.Inventory, index) };
    }

    public static RewardState Claim(RewardState state, string id)
    {
        Validate(state);
        ValidateId(id);

        int index = IndexOf(state.Choices, id);
        if (index < 0) throw Invalid($"Item '{id}' is not one of the current choices.");
        if (state.Inventory.Count >= MaxInventory)
            throw Invalid("Inventory is full (6/6). Discard a stored item or decline the reward to continue.");

        RewardItem item = state.Choices[index];
        if (item.Kind == RewardKind.EmergencyRecovery &&
            OwnedRecovery(state.Inventory, state.Active) > 0)
            throw Invalid("An Emergency Recovery charge is already owned.");

        return state with
        {
            Inventory = Append(state.Inventory, item),
            Choices = Array.Empty<RewardItem>()
        };
    }

    public static RewardState Victory(RewardState state, RewardItem reward, string offerId,
        bool boss, long seed, int round, decimal baseBudget, int confirmedEnemyKills,
        int survivingAllies)
    {
        Validate(state);
        ValidateId(offerId);
        if (state.GrantedOffers.Contains(offerId, StringComparer.Ordinal))
            return state;

        if (round < 1) throw Invalid("round must be positive.");
        if (baseBudget < 0m) throw Invalid("baseBudget must not be negative.");
        ValidateItem(reward, "reward");
        if (state.GrantedOffers.Count >= MaxGrantedOffers) throw Invalid("Reward history is full.");
        if (state.Choices.Count > 0)
            throw Invalid("Unresolved reward choices must be settled before a boss reward.");

        decimal funds = state.EarnedFunds;
        int salvageUnits = Math.Clamp(confirmedEnemyKills, 0, 12);
        var activeItems = new List<RewardItem>(state.Active.Count);

        foreach (RewardItem activeItem in state.Active)
        {
            int remaining = activeItem.RemainingRounds - 1;
            bool expired = remaining <= 0;

            if (activeItem.Kind == RewardKind.SalvageRights)
                funds = SafeAdd(funds, SafeMultiply(SafeMultiply(baseBudget, 0.01m), salvageUnits));

            if (expired && activeItem.Kind == RewardKind.WarBonds)
                funds = SafeAdd(funds, SafeMultiply(baseBudget, 0.30m));

            if (!expired)
                activeItems.Add(activeItem with { RemainingRounds = remaining });
        }

        funds = SafeAdd(funds, SafeMultiply(baseBudget, 0.05m));

        RewardItem[] inventory = state.Inventory.ToArray();
        RewardItem[] active = activeItems.ToArray();
        RewardItem[] choices = state.Choices.ToArray();

        if (boss)
        {
            choices = CreateBossChoices(seed, round, offerId, inventory, active, state.Version);
        }
        else
        {
            RewardItem held = reward with { RemainingRounds = reward.Duration };
            bool recoveryOwned = OwnedRecovery(inventory, active) > 0;
            bool canStore = inventory.Length < MaxInventory &&
                !(held.Kind == RewardKind.EmergencyRecovery && recoveryOwned);

            if (canStore)
            {
                if (ContainsId(held.Id, inventory, active, choices))
                    throw Invalid($"Item id '{held.Id}' already exists in the reward state.");
                inventory = Append(inventory, held);
            }
            else
            {
                if (choices.Length > 0)
                    throw Invalid("A reward cannot be stored while previous choices are unresolved.");
                if (ContainsId(held.Id, inventory, active, choices))
                    throw Invalid($"Item id '{held.Id}' already exists in the reward state.");
                choices = new[] { held };
            }
        }

        return state with
        {
            Inventory = inventory,
            Active = active,
            Choices = choices,
            GrantedOffers = Append(state.GrantedOffers, offerId),
            EarnedFunds = funds,
            VeteranAllies = Math.Clamp(survivingAllies, 0, 7)
        };
    }

    public static RewardState ConsumeRecovery(RewardState state)
    {
        Validate(state);
        int index = -1;
        for (int i = 0; i < state.Active.Count; i++)
        {
            if (state.Active[i].Kind == RewardKind.EmergencyRecovery)
            {
                index = i;
                break;
            }
        }

        if (index < 0) throw Invalid("No active Emergency Recovery charge is available.");
        return state with { Active = RemoveAt(state.Active, index) };
    }

    public static RewardFleet Fleet(RewardState state, decimal baseAvailable, decimal nativePrice)
    {
        Validate(state);
        if (baseAvailable < 0m) throw Invalid("baseAvailable must not be negative.");
        if (nativePrice <= 0m) throw Invalid("nativePrice must be positive.");

        RewardItem? elite = FindActive(state, RewardKind.EliteWorkshop);
        RewardItem? lastStand = FindActive(state, RewardKind.LastStand);
        RewardItem? contract = FindActive(state, RewardKind.ExperimentalContract);
        RewardItem? massProduction = FindActive(state, RewardKind.MassProduction);
        RewardItem? assemblyLine = FindActive(state, RewardKind.AssemblyLine);
        RewardItem? swarm = FindActive(state, RewardKind.SwarmDoctrine);
        RewardItem? veteran = FindActive(state, RewardKind.VeteranCrew);

        decimal bonus = 0m;
        if (elite is not null) bonus = SafeAdd(bonus, elite.Magnitude);
        if (lastStand is not null) bonus = SafeAdd(bonus, 0.25m);
        if (contract is not null) bonus = SafeAdd(bonus, 0.35m);
        decimal effectiveBudget = SafeMultiply(baseAvailable, SafeAdd(1m, bonus));

        int maximumAllies = MaxFleet;
        if (lastStand is not null) maximumAllies = Math.Min(maximumAllies, 1);
        else if (elite is not null) maximumAllies = Math.Min(maximumAllies, 2);

        if (effectiveBudget < nativePrice)
            return new RewardFleet(0, 0, 0m, effectiveBudget, maximumAllies);

        bool hasReinforcements = state.Active.Any(a =>
            a.Kind == RewardKind.FieldReinforcements || a.Kind == RewardKind.EmergencyReserve);
        bool hasLendLease = state.Active.Any(a =>
            a.Kind == RewardKind.LendLease || a.Kind == RewardKind.CapturedPrototype);

        int freeCandidates = (hasReinforcements ? 1 : 0) + (hasLendLease ? 1 : 0);
        int freeReinforcements = Math.Min(freeCandidates, Math.Max(0, maximumAllies - 1));
        int paidAllyLimit = Math.Max(0, maximumAllies - 1 - freeReinforcements);

        bool swarmEligible = swarm is not null &&
            nativePrice <= SafeDivide(effectiveBudget, 3m);

        decimal totalCost = nativePrice;
        int paidCopies = 1;
        for (int ally = 1; ally <= paidAllyLimit; ally++)
        {
            decimal discount = 0m;
            if (massProduction is not null && ally == 1)
                discount = SafeAdd(discount, massProduction.Magnitude);
            if (assemblyLine is not null)
                discount = SafeAdd(discount, SafeMultiply(assemblyLine.Magnitude, ally));
            if (swarmEligible)
                discount = SafeAdd(discount, swarm!.Magnitude);
            if (veteran is not null && ally <= state.VeteranAllies)
                discount = SafeAdd(discount, veteran.Magnitude);

            if (discount < 0m) discount = 0m;
            if (discount > 0.40m) discount = 0.40m;

            decimal cost = SafeMultiply(nativePrice, 1m - discount);
            decimal nextTotal = SafeAdd(totalCost, cost);
            if (nextTotal > effectiveBudget) break;
            totalCost = nextTotal;
            paidCopies++;
        }

        return new RewardFleet(paidCopies, freeReinforcements, totalCost, effectiveBudget, maximumAllies);
    }

    public static string Describe(RewardItem item, bool active = false)
    {
        RewardParts parts = DescribeParts(item, active);
        string text = $"{parts.Name} {parts.Value} ({parts.RarityName}, {parts.Timing})";
        return parts.Note.Length == 0 ? text : text + "; " + parts.Note;
    }

    // The same text as Describe, split for card layouts (name, effect, rarity,
    // timing, optional extra rule). Describe is composed from this, so the two
    // can never drift apart.
    public static RewardParts DescribeParts(RewardItem item, bool active = false)
    {
        ValidateItem(item, "item");

        string timing = !active && item.Kind == RewardKind.WarBonds ? "cash now or invest for 2 victories" : active
            ? $"{item.RemainingRounds} of {item.Duration} victories remaining"
            : $"active for {item.Duration} victories after activation";
        string note = item.Kind switch
        {
            RewardKind.WarBonds => "pays +30% budget when it expires (delayed)",
            RewardKind.EmergencyRecovery => "one charge: refit and retry after defeat",
            RewardKind.LastStand => "fleet capped at 1 total copy",
            RewardKind.EliteWorkshop => "fleet capped at 2 total copies",
            RewardKind.ExperimentalContract => "enemies +20% from the next battle",
            _ => ""
        };
        return new RewardParts(KindName(item.Kind), DescribeValue(item), RarityName(item.Rarity), timing, note);
    }

    public static void Validate(RewardState state)
    {
        if (state is null) throw Invalid("state must not be null.");
        if (state.Revision < 0) throw Invalid("Reward revision must not be negative.");
        if (state.Version is not (2 or 3 or 4 or 5 or 6 or 7)) throw Invalid("Reward state version must be 2, 3, 4, 5, 6 or 7.");
        if (state.Inventory is null) throw Invalid("Inventory must not be null.");
        if (state.Active is null) throw Invalid("Active must not be null.");
        if (state.Choices is null) throw Invalid("Choices must not be null.");
        if (state.GrantedOffers is null) throw Invalid("GrantedOffers must not be null.");

        if (state.Inventory.Count > MaxInventory)
            throw Invalid($"Inventory cannot exceed {MaxInventory} items.");
        if (state.Active.Count > MaxActive)
            throw Invalid($"Active cannot exceed {MaxActive} items.");
        if (state.Choices.Count > MaxChoices)
            throw Invalid($"Choices cannot exceed {MaxChoices} items.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        ValidateItems(state.Inventory, ids);
        ValidateItems(state.Active, ids);
        ValidateItems(state.Choices, ids);

        var families = new HashSet<RewardKind>();
        foreach (RewardItem item in state.Active)
        {
            if (!families.Add(FamilyOf(item.Kind)))
                throw Invalid("Active rewards contain a duplicate kind or family.");
        }

        if (OwnedRecovery(state.Inventory, state.Active) > 1)
            throw Invalid("Only one Emergency Recovery may be owned.");

        if (state.EarnedFunds < 0m) throw Invalid("EarnedFunds must not be negative.");
        if (state.OfferRoll < 0 || state.OfferRoll > MaxOfferRoll)
            throw Invalid($"OfferRoll must be between 0 and {MaxOfferRoll}.");
        if (state.VeteranAllies < 0 || state.VeteranAllies > 7)
            throw Invalid("VeteranAllies must be between 0 and 7.");

        if (state.GrantedOffers.Count > MaxGrantedOffers)
            throw Invalid($"GrantedOffers cannot exceed {MaxGrantedOffers} entries.");
        var offers = new HashSet<string>(StringComparer.Ordinal);
        foreach (string offer in state.GrantedOffers)
        {
            if (string.IsNullOrWhiteSpace(offer))
                throw Invalid("GrantedOffers entries must be nonempty.");
            if (!offers.Add(offer))
                throw Invalid("GrantedOffers entries must be unique.");
        }
    }

    private static RewardItem CreateCore(long seed, int round, string offerId, int risk,
        bool boss, int choice, string? blueprintId, RewardKind? forcedKind, int rulesVersion = 5)
    {
        ValidateId(offerId);
        if (round < 1) throw Invalid("round must be positive.");
        if (risk < 0 || risk > 2) throw Invalid("risk must be 0, 1, or 2.");
        if (choice < 0) throw Invalid("choice must not be negative.");

        ulong rarityHash = StableHash(seed, round, offerId, risk, choice, 0x51A7);
        RewardRarity rarity = boss ? RewardRarity.UltraRare : PickRarity(risk, rarityHash);

        ulong kindHash = StableHash(seed, round, offerId, risk, choice, 0x91D3);
        RewardKind kind = forcedKind ?? PickKind(kindHash, rulesVersion);

        string? blueprint = null;
        if (kind == RewardKind.LendLease || kind == RewardKind.CapturedPrototype)
        {
            if (string.IsNullOrWhiteSpace(blueprintId))
                kind = RewardKind.FieldReinforcements;
            else
                blueprint = blueprintId;
        }

        if (kind == RewardKind.EmergencyRecovery)
            rarity = RewardRarity.UltraRare;

        ulong durationHash = StableHash(seed, round, offerId, risk, choice, 0xD1A7);
        int duration = kind == RewardKind.EmergencyRecovery
            ? 6
            : DurationFor(rarity, durationHash);

        if (kind == RewardKind.WarBonds)
            duration = Math.Max(2, duration);

        return new RewardItem(MakeId(offerId, choice), kind, rarity,
            MagnitudeFor(kind, rarity), duration, duration, blueprint);
    }

    private static RewardItem[] CreateBossChoices(long seed, int round, string offerId,
        IReadOnlyList<RewardItem> inventory, IReadOnlyList<RewardItem> active, int rulesVersion)
    {
        var existingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (RewardItem item in inventory) existingIds.Add(item.Id);
        foreach (RewardItem item in active) existingIds.Add(item.Id);

        string bossOffer = offerId + "|boss";
        for (int attempt = 0; attempt < 32; attempt++)
        {
            RewardItem[] items = GenerateBossItems(seed, round, bossOffer, rulesVersion);
            var newIds = new HashSet<string>(StringComparer.Ordinal);
            if (items.All(item => !existingIds.Contains(item.Id) && newIds.Add(item.Id)))
                return items;

            bossOffer = offerId + "|boss|" + attempt.ToString(CultureInfo.InvariantCulture);
        }

        throw Invalid("Unable to create unique boss reward choices.");
    }

    private static RewardItem[] GenerateBossItems(long seed, int round, string bossOffer, int rulesVersion)
    {
        var items = new List<RewardItem>(MaxChoices);
        var kinds = new HashSet<RewardKind>();

        for (int choice = 0; choice < 256 && items.Count < MaxChoices; choice++)
        {
            RewardItem candidate = CreateCore(seed, round, bossOffer, 2, true, choice,
                blueprintId: null, forcedKind: null, rulesVersion);
            if (kinds.Add(candidate.Kind)) items.Add(candidate);
        }

        if (items.Count < MaxChoices)
        {
            int extraChoice = 256;
            foreach (RewardKind kind in KindPool)
            {
                if (kind == RewardKind.LendLease || kind == RewardKind.CapturedPrototype ||
                    (rulesVersion >= 6 && kind == RewardKind.EmergencyRecovery) ||
                    kinds.Contains(kind))
                    continue;

                RewardItem candidate = CreateCore(seed, round, bossOffer, 2, true,
                    extraChoice++, blueprintId: null, forcedKind: kind, rulesVersion);
                items.Add(candidate);
                kinds.Add(kind);
                if (items.Count == MaxChoices) break;
            }
        }

        if (items.Count != MaxChoices)
            throw Invalid("Unable to create three distinct boss reward choices.");
        return items.ToArray();
    }

    private static RewardRarity PickRarity(int risk, ulong hash)
    {
        int roll = (int)(hash % 100UL);
        return risk switch
        {
            0 => roll < 90 ? RewardRarity.Common : roll < 99 ? RewardRarity.Rare : RewardRarity.UltraRare,
            1 => roll < 70 ? RewardRarity.Common : roll < 95 ? RewardRarity.Rare : RewardRarity.UltraRare,
            2 => roll < 45 ? RewardRarity.Common : roll < 85 ? RewardRarity.Rare : RewardRarity.UltraRare,
            _ => throw Invalid("risk must be 0, 1, or 2.")
        };
    }

    private static RewardKind PickKind(ulong hash, int rulesVersion)
    {
        int roll = (int)(hash % (ulong)(KindWeightTotal - (rulesVersion >= 6 ? KindWeights[^1] : 0)));
        int cumulative = 0;
        for (int i = 0; i < KindPool.Length; i++)
        {
            cumulative += KindWeights[i];
            if (roll < cumulative) return KindPool[i];
        }
        return KindPool[^1];
    }

    private static int DurationFor(RewardRarity rarity, ulong hash)
    {
        int extra = (int)(hash % 2UL);
        return rarity switch
        {
            RewardRarity.Common => 1 + extra,
            RewardRarity.Rare => 3 + extra,
            RewardRarity.UltraRare => 5 + extra,
            _ => 1
        };
    }

    private static decimal MagnitudeFor(RewardKind kind, RewardRarity rarity) => kind switch
    {
        RewardKind.MassProduction => rarity == RewardRarity.UltraRare ? 0.30m : 0.25m,
        RewardKind.AssemblyLine => rarity switch
        {
            RewardRarity.Common => 0.10m,
            RewardRarity.Rare => 0.15m,
            _ => 0.20m
        },
        RewardKind.EliteWorkshop => 0.20m,
        RewardKind.SwarmDoctrine => 0.20m,
        RewardKind.SalvageRights => 0.01m,
        RewardKind.VeteranCrew => 0.20m,
        RewardKind.WarBonds => 0.30m,
        RewardKind.LastStand => 0.25m,
        RewardKind.ExperimentalContract => 0.35m,
        _ => 1m
    };

    private static RewardKind FamilyOf(RewardKind kind)
        => kind switch { RewardKind.EmergencyReserve => RewardKind.FieldReinforcements,
            RewardKind.CapturedPrototype => RewardKind.LendLease, _ => kind };

    private static int OwnedRecovery(IReadOnlyList<RewardItem> inventory,
        IReadOnlyList<RewardItem> active)
        => inventory.Count(item => item.Kind == RewardKind.EmergencyRecovery) +
           active.Count(item => item.Kind == RewardKind.EmergencyRecovery);

    private static void ValidateItems(IReadOnlyList<RewardItem> items, HashSet<string> ids)
    {
        foreach (RewardItem item in items)
        {
            ValidateItem(item, "reward item");
            if (!ids.Add(item.Id))
                throw Invalid($"Duplicate reward item id '{item.Id}'.");
        }
    }

    private static void ValidateItem(RewardItem? item, string label)
    {
        if (item is null) throw Invalid($"{label} must not be null.");
        if (string.IsNullOrWhiteSpace(item.Id)) throw Invalid($"{label} id must be nonempty.");
        if (!Enum.IsDefined(typeof(RewardKind), item.Kind))
            throw Invalid($"{label} has an undefined kind.");
        if (!Enum.IsDefined(typeof(RewardRarity), item.Rarity))
            throw Invalid($"{label} has an undefined rarity.");
        if ((item.Kind is RewardKind.LendLease or RewardKind.CapturedPrototype) && string.IsNullOrWhiteSpace(item.BlueprintId))
            throw Invalid("Support item has no blueprint identity.");
        if (item.Magnitude < 0m || item.Magnitude > 1m)
            throw Invalid($"{label} magnitude must be between 0 and 1.");
        if (item.Duration < 1 || item.Duration > 6)
            throw Invalid($"{label} duration must be between 1 and 6.");
        if (item.RemainingRounds < 1 || item.RemainingRounds > item.Duration)
            throw Invalid($"{label} remaining rounds must be between 1 and its duration.");
    }

    private static void ValidateId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw Invalid("id must be nonempty.");
    }

    private static RewardItem? FindActive(RewardState state, RewardKind kind)
        => state.Active.FirstOrDefault(item => item.Kind == kind);

    private static int IndexOf(IReadOnlyList<RewardItem> items, string id)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].Id, id, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    private static bool ContainsId(string id, IReadOnlyList<RewardItem> first,
        IReadOnlyList<RewardItem> second, IReadOnlyList<RewardItem> third)
        => IndexOf(first, id) >= 0 || IndexOf(second, id) >= 0 || IndexOf(third, id) >= 0;

    private static RewardItem[] Append(IReadOnlyList<RewardItem> items, RewardItem item)
    {
        var result = new RewardItem[items.Count + 1];
        for (int i = 0; i < items.Count; i++) result[i] = items[i];
        result[items.Count] = item;
        return result;
    }

    private static string[] Append(IReadOnlyList<string> values, string value)
    {
        var result = new string[values.Count + 1];
        for (int i = 0; i < values.Count; i++) result[i] = values[i];
        result[values.Count] = value;
        return result;
    }

    private static RewardItem[] RemoveAt(IReadOnlyList<RewardItem> items, int index)
    {
        var result = new RewardItem[items.Count - 1];
        int target = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (i == index) continue;
            result[target++] = items[i];
        }
        return result;
    }

    private static decimal SafeAdd(decimal left, decimal right)
    {
        try { return checked(left + right); }
        catch (OverflowException) { throw Invalid("Reward arithmetic overflowed."); }
    }

    private static decimal SafeMultiply(decimal left, decimal right)
    {
        try { return checked(left * right); }
        catch (OverflowException) { throw Invalid("Reward arithmetic overflowed."); }
    }

    private static decimal SafeDivide(decimal left, decimal right)
    {
        try { return left / right; }
        catch (DivideByZeroException) { throw Invalid("Reward arithmetic divided by zero."); }
        catch (OverflowException) { throw Invalid("Reward arithmetic overflowed."); }
    }

    private static ulong StableHash(long seed, int round, string offerId, int risk,
        int choice, int salt)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            hash = Mix(hash ^ (ulong)seed);
            hash = Mix(hash ^ (uint)round);

            foreach (char character in offerId)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }

            hash = Mix(hash ^ (uint)risk);
            hash = Mix(hash ^ (uint)choice);
            hash = Mix(hash ^ (uint)salt);
            return Mix(hash);
        }
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }
    }

    private static string MakeId(string offerId, int choice)
        => string.Concat("rw-", offerId, "-", choice.ToString(CultureInfo.InvariantCulture));

    private static string DescribeValue(RewardItem item) => item.Kind switch
    {
        RewardKind.MassProduction => $"{Percent(item.Magnitude)}% off the second tank",
        RewardKind.AssemblyLine => $"{Percent(item.Magnitude)}% extra discount per successive ally (40% cap)",
        RewardKind.EliteWorkshop => $"+{Percent(item.Magnitude)}% budget",
        RewardKind.SwarmDoctrine => $"+{Percent(item.Magnitude)}% ally discount (swarm)",
        RewardKind.SalvageRights => $"+{Percent(item.Magnitude)}% of base budget per defeated enemy (up to 12)",
        RewardKind.VeteranCrew => $"+{Percent(item.Magnitude)}% ally discount (veteran)",
        RewardKind.WarBonds => $"+{Percent(item.Magnitude)}% budget on expiry",
        RewardKind.LastStand => $"+{Percent(item.Magnitude)}% budget",
        RewardKind.ExperimentalContract => $"+{Percent(item.Magnitude)}% budget",
        RewardKind.EmergencyRecovery => "1 recovery charge",
        RewardKind.FieldReinforcements => "1 free reinforcement",
        RewardKind.EmergencyReserve => "1 free reinforcement reserve",
        RewardKind.LendLease => "1 free ally",
        RewardKind.CapturedPrototype => "1 free ally",
        RewardKind.OperationalFreedom => "1 immediate offer reroll",
        _ => "1 unit"
    };

    private static string Percent(decimal magnitude)
        => (magnitude * 100m).ToString("0.##", CultureInfo.InvariantCulture);

    private static string KindName(RewardKind kind) => kind switch
    {
        RewardKind.MassProduction => "Mass Production",
        RewardKind.AssemblyLine => "Assembly Line",
        RewardKind.EliteWorkshop => "Elite Workshop",
        RewardKind.SwarmDoctrine => "Swarm Doctrine",
        RewardKind.FieldReinforcements => "Field Reinforcements",
        RewardKind.LendLease => "Lend Lease",
        RewardKind.SalvageRights => "Salvage Rights",
        RewardKind.VeteranCrew => "Veteran Crew",
        RewardKind.WarBonds => "War Bonds",
        RewardKind.EmergencyReserve => "Emergency Reserve",
        RewardKind.OperationalFreedom => "Operational Freedom",
        RewardKind.LastStand => "Last Stand",
        RewardKind.CapturedPrototype => "Captured Prototype",
        RewardKind.ExperimentalContract => "Experimental Contract",
        RewardKind.EmergencyRecovery => "Emergency Recovery",
        _ => kind.ToString()
    };

    private static string RarityName(RewardRarity rarity) => rarity switch
    {
        RewardRarity.Common => "Common",
        RewardRarity.Rare => "Rare",
        RewardRarity.UltraRare => "Ultra Rare",
        RewardRarity.Special => "Special · Yellow",
        _ => rarity.ToString()
    };

    private static InvalidOperationException Invalid(string message)
        => new InvalidOperationException(message);
}
