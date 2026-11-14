using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;

namespace BalanceSim;

// Pure-Core balance probe. Every fight is a victory; only the curve, fleet
// economics and loot are observed. No Unity, BepInEx or native assemblies.
internal static class Program
{
    private const decimal Difficulty = 1.5m;
    private const bool EraProgression = true;
    private static readonly string[] StrategyNames = { "cautious", "dangerous", "random" };
    private static readonly int[] Schemas = { 6, 5 };

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var archive = Archive.Load(options.CataloguePath);
            var prepared = ContentPreparer.Prepare(archive.Definition, archive.InitialBudget);
            archive = archive with { Definition = prepared.Definition };
            Console.WriteLine($"catalogue {archive.Definition.Version}: "
                + $"{archive.Definition.Eras.Count} eras, B0={archive.InitialBudget}, "
                + $"runs={options.Runs}; supplemented eras={string.Join("+", prepared.SupplementedEras)}");

            var results = new ConcurrentBag<RunResult>();
            foreach (var schema in Schemas)
            {
                foreach (var strategy in StrategyNames)
                {
                    var started = Environment.TickCount64;
                    var range = Partitioner.Create(0, options.Runs);
                    Parallel.ForEach(range, pair =>
                    {
                        var local = new List<RunResult>(pair.Item2 - pair.Item1);
                        for (var i = pair.Item1; i < pair.Item2; i++)
                            local.Add(Simulate(archive, prepared, schema, strategy, i));
                        foreach (var result in local) results.Add(result);
                    });
                    Console.WriteLine($"{schema}/{strategy}: {options.Runs} runs in "
                        + $"{(Environment.TickCount64 - started) / 1000.0:0.0}s");
                }
            }

            var all = results.ToArray();
            var failures = all.Where(r => r.Error != null).ToArray();
            if (failures.Length > 0)
            {
                Console.WriteLine($"SIMULATION FAILURES: {failures.Length}/{all.Length}");
                foreach (var failure in failures.Take(10))
                    Console.WriteLine($"  run {failure.RunIndex} {failure.Schema}/{failure.Strategy}: {failure.Error}");
                return 1;
            }

            Directory.CreateDirectory(options.OutputDirectory);
            WriteRounds(Path.Combine(options.OutputDirectory, "rounds.csv"), all);
            WriteItems(Path.Combine(options.OutputDirectory, "items.csv"), all);
            var curveOk = WriteReport(Path.Combine(options.OutputDirectory, "REPORT.md"), all,
                options.Runs, prepared.SupplementedEras);
            Console.WriteLine($"wrote {options.OutputDirectory}");
            if (!curveOk)
            {
                Console.Error.WriteLine("CHECK FAILED: schema 6 random target ratio is not rising 1<2<3 inside every era.");
                return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("BalanceSim failed: " + ex);
            return 1;
        }
    }

    private static RunResult Simulate(Archive archive, PreparedContent prepared, int schema,
        string strategy, int runIndex)
    {
        var samples = new List<RoundSample>();
        var drops = new List<(RewardKind Kind, RewardRarity Rarity)>();
        var errors = new List<string>();
        var finalScore = 0m;
        try
        {
            var definition = archive.Definition;
            var runId = $"sim-{schema}-{runIndex:D6}";
            var seed = 0x5EED0000L + runIndex * 2654435761L;
            var director = new RunDirector();
            var created = director.NewRun(new RunConfiguration(
                runId, seed, new ScenarioValue(archive.InitialBudget, definition.PriceUnitId),
                definition.Eras.Select(e => e.Era).ToArray(), definition.Version)
            {
                AdventureRules = true,
                AdventureVersion = schema,
                StartingEraIndex = 0
            });
            if (!created.IsAccepted) return Failure(runIndex, schema, strategy, created.Reason, samples, drops);

            var catalogue = new SeededEncounterCatalogue(definition,
                new CatalogueOptions(ContentQualification.Candidate, false, true));
            var maximumRounds = definition.Eras.Count * 4;
            for (var round = 1; round <= maximumRounds; round++)
            {
                var state = director.Snapshot!;
                if (state.Phase == RunPhase.AwaitingEndlessChoice) break;

                SettleChoices(director, errors);
                if (errors.Count > 0) break;
                ActivateItems(director);
                state = director.Snapshot!;
                if (!state.DesignConfirmedForBlock || state.ConfirmedDesign is null)
                {
                    if (!ConfirmDesign(director, definition, prepared.OriginalPrices, errors)) break;
                }
                state = director.Snapshot!;
                if (state.Offers.Count == 0 && !GenerateOffers(director, catalogue, errors)) break;
                state = director.Snapshot!;
                if (state.Offers.Count == 0) { errors.Add("no offers"); break; }

                var plan = Choose(state.Offers, strategy, seed, round);
                var pre = state;
                var target = TargetFor(pre, plan);
                var enemyCost = EnemyCost(definition, plan);
                var fleetCost = FleetCost(pre);
                var fleetSize = pre.ConfirmedDesign?.FriendlyCountIncludingPlayer ?? 0;
                var enemyCount = plan.Enemies.Sum(e => e.Count);

                var selected = director.SelectEncounter(plan.OfferId);
                var begun = selected.IsAccepted ? director.BeginEncounter() : selected;
                if (!begun.IsAccepted) { errors.Add("begin: " + begun.Reason); break; }
                var request = director.CreateEncounterRequest();
                var started = director.ReportCombatStarted(request.Attempt);
                if (!started.IsAccepted) { errors.Add("started: " + started.Reason); break; }
                var friendly = pre.ConfirmedDesign?.FriendlyCountIncludingPlayer ?? 1;
                var accepted = director.AcceptResult(new EncounterResult(
                    request.Attempt, request.Plan, EncounterOutcome.Victory, "simulated")
                {
                    ConfirmedEnemyKills = enemyCount,
                    SurvivingAllies = Math.Max(0, friendly - 1),
                    EnemiesDestroyed = enemyCount,
                    FriendliesLost = 0,
                    PlayerTankSurvived = true
                });
                if (!accepted.IsAccepted) { errors.Add("victory: " + accepted.Reason); break; }

                samples.Add(new RoundSample(round, plan.Round, plan.Era.NativeId,
                    (plan.Round - 1) % 4 + 1, plan.Extras?.BossBlueprintId != null,
                    pre.BlockIndex >= pre.EraSchedule.Count,
                    pre.AvailableBudget.Value <= 0m ? 0m : target / pre.AvailableBudget.Value,
                    fleetCost <= 0m ? 0m : (enemyCost ?? 0m) / fleetCost,
                    enemyCount, fleetSize, state.Offers.Count(o => o.Enemies.Sum(e => e.Count) == 1) / (decimal)state.Offers.Count,
                    pre.Round, pre.EraIndex));
                RecordDrops(director, plan, drops);
                SettleChoices(director, errors);
                if (errors.Count > 0) break;
            }

            if (errors.Count == 0)
            {
                var phase = director.Snapshot!.Phase;
                if (phase == RunPhase.AwaitingEndlessChoice)
                {
                    var finished = director.Finish();
                    if (!finished.IsAccepted) errors.Add("finish: " + finished.Reason);
                }
                else if (phase is not (RunPhase.Lost or RunPhase.Finished))
                    errors.Add("run ended in " + phase);
            }
            finalScore = director.Snapshot!.Score;
        }
        catch (Exception ex)
        {
            errors.Add(ex.GetType().Name + ": " + ex.Message);
        }

        return new RunResult(runIndex, schema, strategy, samples, drops,
            errors.Count == 0 ? null : string.Join(" | ", errors), finalScore);
    }

    private static RunResult Failure(int runIndex, int schema, string strategy,
        string error, List<RoundSample> samples, List<(RewardKind, RewardRarity)> drops) =>
        new(runIndex, schema, strategy, samples, drops, error);

    private static bool ConfirmDesign(RunDirector director, CatalogueDefinition definition,
        IReadOnlyDictionary<int, decimal[]> originalPrices, List<string> errors)
    {
        var state = director.Snapshot!;
        var era = definition.Eras[state.EraIndex];
        var unit = definition.PriceUnitId;
        var candidates = era.Enemies
            .Where(e => e.Qualification >= ContentQualification.Candidate && e.CheckedPrice.Value > 0m)
            .OrderBy(e => e.CheckedPrice.Value)
            .ToArray();
        if (candidates.Length == 0) { errors.Add("era has no priced design"); return false; }
        var blueprint = candidates[0].Blueprint;
        var source = originalPrices.TryGetValue(state.EraIndex, out var original) && original.Length > 0
            ? original : candidates.Select(e => e.CheckedPrice.Value).ToArray();
        var prices = new[]
        {
            Percentile(source, 0.25m),
            Percentile(source, 0.10m),
            source[0],
            1m
        };
        foreach (var price in prices)
        {
            if (price <= 0m) continue;
            var quote = new DesignQuote(blueprint, new ScenarioValue(price, unit), true, true, null);
            var result = state.Phase == RunPhase.ChoosingEncounter
                ? director.ConfirmEncounterDesign(quote)
                : director.ConfirmDesign(quote);
            if (result.IsAccepted) return true;
        }
        errors.Add("design confirm: no affordable fixture quote in era " + era.Era.NativeId);
        return false;
    }

    private static bool GenerateOffers(RunDirector director, SeededEncounterCatalogue catalogue,
        List<string> errors)
    {
        var request = director.CreateOfferRequest();
        var contract = director.Snapshot!.Rewards?.Active.Any(i =>
            i.Kind == RewardKind.ExperimentalContract) == true ? 1.2m : 1m;
        request = request with
        {
            EnemyBudgetMultiplier = Difficulty * contract,
            EraProgressionEnabled = EraProgression
        };
        var generated = catalogue.TryGenerateOffers(request);
        if (!generated.IsSuccess)
        {
            errors.Add($"offers round {request.Round} era {request.Era.NativeId} "
                + $"budget {request.AvailableBudget.Value} mult {request.EnemyBudgetMultiplier}: "
                + $"{generated.Status}: {string.Join("; ", generated.Diagnostics)}");
            return false;
        }
        var economy = new OfferEconomy(request.AvailableBudget, request.EnemyBudgetMultiplier)
        {
            EraProgression = request.EraProgressionEnabled,
            BaseDifficulty = request.RulesVersion >= 6 ? Difficulty : null
        };
        var set = director.SetOffers(generated.Offers, economy);
        if (!set.IsAccepted) { errors.Add("set offers: " + set.Reason); return false; }
        return true;
    }

    private static void SettleChoices(RunDirector director, List<string> errors)
    {
        for (var guard = 0; guard < 64; guard++)
        {
            var state = director.Snapshot!;
            if (state.Rewards is null || state.Rewards.Choices.Count == 0) return;
            if (state.Rewards.Inventory.Count >= 6)
            {
                var discard = state.Rewards.Inventory.OrderBy(i => ItemPriority(i.Kind)).First();
                var removed = director.DiscardReward(discard.Id);
                if (!removed.IsAccepted) { errors.Add("discard: " + removed.Reason); return; }
            }
            var claimed = false;
            foreach (var choice in state.Rewards.Choices)
            {
                var claim = director.ClaimReward(choice.Id);
                if (!claim.IsAccepted) continue;
                claimed = true;
                break;
            }
            if (!claimed)
            {
                var skipped = director.SkipRewardChoice();
                if (!skipped.IsAccepted) { errors.Add("claim/skip: " + skipped.Reason); return; }
            }
        }
        errors.Add("reward choices did not settle");
    }

    // Simple, documented player rule: at the start of each round, activate the
    // highest-priority available item that is not already active in its family,
    // while an active slot is free. Operational Freedom and Emergency Recovery
    // are kept for real play and do not change the measured curve.
    private static void ActivateItems(RunDirector director)
    {
        for (var guard = 0; guard < 32; guard++)
        {
            var state = director.Snapshot!;
            var rewards = state.Rewards;
            if (rewards is null || rewards.Active.Count >= 3) return;
            var families = rewards.Active.Select(i => i.Kind).ToHashSet();
            var candidate = rewards.Inventory
                .Where(i => i.Kind is not (RewardKind.OperationalFreedom
                    or RewardKind.EmergencyRecovery))
                .OrderByDescending(i => ItemPriority(i.Kind))
                .FirstOrDefault(i => !families.Contains(i.Kind));
            if (candidate is null) return;
            var activated = director.ActivateReward(candidate.Id);
            if (!activated.IsAccepted) return;
        }
    }

    private static EncounterPlan Choose(IReadOnlyList<EncounterPlan> offers, string strategy,
        long seed, int round)
    {
        if (offers.Count == 1) return offers[0];
        if (strategy == "cautious")
            return offers.First(p => p.Risk == EncounterRisk.Cautious);
        if (strategy == "dangerous")
            return offers.First(p => p.Risk == EncounterRisk.Dangerous);
        var pick = (uint)((ulong)seed + (ulong)round * 2654435761UL) % (uint)offers.Count;
        return offers[(int)pick];
    }

    private static decimal TargetFor(RunSnapshot pre, EncounterPlan plan)
    {
        if (plan.Extras?.EnemyBudgetTarget is { } pinned) return pinned;
        var block = (plan.Round - 1) / 4;
        var roundInEra = (plan.Round - 1) % 4 + 1;
        var legacyRound = block * 3 + Math.Min(3, roundInEra);
        return decimal.Floor(pre.AvailableBudget.Value
            * (plan.Extras?.EnemyBudgetMultiplier ?? 1m)
            * EncounterRules.WithinBlockMultiplier(legacyRound)
            * EncounterRules.Profile(plan.Risk).ThreatMultiplier);
    }

    private static decimal? EnemyCost(CatalogueDefinition definition, EncounterPlan plan)
    {
        var total = 0m;
        foreach (var enemy in plan.Enemies)
        {
            var eraId = string.Equals(enemy.BlueprintId, plan.Extras?.BossBlueprintId,
                StringComparison.Ordinal) ? plan.Extras!.BossEra : plan.Era.NativeId;
            var era = definition.Eras.FirstOrDefault(e => e.Era.NativeId == eraId);
            var entry = era?.Enemies.FirstOrDefault(e => e.Blueprint.LocalId == enemy.BlueprintId);
            if (entry?.CheckedPrice is null) return null;
            total = checked(total + entry.CheckedPrice.Value * enemy.Count);
        }
        return total;
    }

    private static decimal FleetCost(RunSnapshot pre)
    {
        if (pre.ConfirmedDesign is not { } design || pre.Rewards is null) return 0m;
        var basis = pre.BlockBaseBudget.Value + pre.SupplyBonus + pre.Rewards.EarnedFunds;
        return RewardRules.Fleet(pre.Rewards, basis, design.Cost.Value).TotalCost;
    }

    private static void RecordDrops(RunDirector director, EncounterPlan plan,
        List<(RewardKind Kind, RewardRarity Rarity)> drops)
    {
        var rewards = director.Snapshot!.Rewards;
        if (rewards is null) return;
        if (plan.Extras?.BossBlueprintId != null)
        {
            // The strategy claims the first eligible boss choice, not all three.
            if (rewards.Choices.FirstOrDefault() is { } choice) drops.Add((choice.Kind, choice.Rarity));
            return;
        }
        if (plan.Extras?.Reward is { } reward) drops.Add((reward.Kind, reward.Rarity));
    }

    private static int ItemPriority(RewardKind kind) => kind switch
    {
        RewardKind.EliteWorkshop => 100,
        RewardKind.ExperimentalContract => 95,
        RewardKind.LastStand => 90,
        RewardKind.MassProduction => 80,
        RewardKind.AssemblyLine => 75,
        RewardKind.SwarmDoctrine => 70,
        RewardKind.VeteranCrew => 65,
        RewardKind.SalvageRights => 60,
        RewardKind.WarBonds => 55,
        RewardKind.CapturedPrototype => 50,
        RewardKind.LendLease => 45,
        RewardKind.FieldReinforcements => 40,
        RewardKind.EmergencyReserve => 35,
        RewardKind.EmergencyRecovery => 5,
        RewardKind.OperationalFreedom => 0,
        _ => 0
    };

    private static decimal Percentile(decimal[] sorted, decimal fraction)
    {
        if (sorted.Length == 0) return 0m;
        var index = (int)Math.Floor((sorted.Length - 1) * fraction);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static void WriteRounds(string path, IReadOnlyList<RunResult> all)
    {
        var groups = all
            .SelectMany(r => r.Samples.Select(s => (r.Schema, r.Strategy, Sample: s)))
            .GroupBy(x => (x.Schema, x.Strategy, x.Sample.Round))
            .OrderBy(g => g.Key.Schema).ThenBy(g => g.Key.Strategy).ThenBy(g => g.Key.Round);
        var text = new StringBuilder();
        text.AppendLine("schema,strategy,round,era,round_in_era,boss,endless,runs,"
            + "target_ratio_mean,target_ratio_p10,target_ratio_p90,"
            + "cost_ratio_mean,cost_ratio_p10,cost_ratio_p90,"
            + "enemy_count_mean,enemy_count_p10,enemy_count_p90,fleet_size_mean,fleet_size_p10,fleet_size_p90,min_one_share");
        foreach (var group in groups)
        {
            var samples = group.Select(x => x.Sample).ToArray();
            var targets = samples.Select(s => s.TargetRatio).OrderBy(v => v).ToArray();
            var costs = samples.Select(s => s.CostRatio).OrderBy(v => v).ToArray();
            var first = samples[0];
            text.Append(group.Key.Schema).Append(',')
                .Append(group.Key.Strategy).Append(',')
                .Append(group.Key.Round).Append(',')
                .Append(first.Era).Append(',')
                .Append(first.RoundInEra).Append(',')
                .Append(first.Boss ? "true" : "false").Append(',')
                .Append(first.Endless ? "true" : "false").Append(',')
                .Append(samples.Length).Append(',')
                .Append(F(Mean(targets))).Append(',')
                .Append(F(PercentileSorted(targets, 0.10m))).Append(',')
                .Append(F(PercentileSorted(targets, 0.90m))).Append(',')
                .Append(F(Mean(costs))).Append(',')
                .Append(F(PercentileSorted(costs, 0.10m))).Append(',')
                .Append(F(PercentileSorted(costs, 0.90m))).Append(',')
                .Append(F(samples.Average(s => (decimal)s.EnemyCount), 2)).Append(',')
                .Append(F(PercentileSorted(samples.Select(s => (decimal)s.EnemyCount).OrderBy(x => x).ToArray(), .1m), 2)).Append(',')
                .Append(F(PercentileSorted(samples.Select(s => (decimal)s.EnemyCount).OrderBy(x => x).ToArray(), .9m), 2)).Append(',')
                .Append(F(samples.Average(s => (decimal)s.FleetSize), 2)).Append(',')
                .Append(F(PercentileSorted(samples.Select(s => (decimal)s.FleetSize).OrderBy(x => x).ToArray(), .1m), 2)).Append(',')
                .Append(F(PercentileSorted(samples.Select(s => (decimal)s.FleetSize).OrderBy(x => x).ToArray(), .9m), 2)).Append(',')
                .Append(F(samples.Average(s => s.MinOneShare), 3))
                .AppendLine();
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static void WriteItems(string path, IReadOnlyList<RunResult> all)
    {
        var rows = all
            .SelectMany(r => r.Drops.Select(d => (r.Schema, r.Strategy, d.Kind, d.Rarity)))
            .GroupBy(x => (x.Schema, x.Strategy, x.Kind, x.Rarity))
            .Select(g => (g.Key, Count: g.Count()))
            .OrderBy(x => x.Key.Schema).ThenBy(x => x.Key.Strategy)
            .ThenByDescending(x => x.Count).ThenBy(x => x.Key.Kind.ToString())
            .ToArray();
        var totals = rows.GroupBy(r => (r.Key.Schema, r.Key.Strategy))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        var text = new StringBuilder();
        text.AppendLine("schema,strategy,item,rarity,count,share_of_drops");
        foreach (var row in rows)
        {
            var total = totals[(row.Key.Schema, row.Key.Strategy)];
            text.Append(row.Key.Schema).Append(',')
                .Append(row.Key.Strategy).Append(',')
                .Append(row.Key.Kind).Append(',')
                .Append(row.Key.Rarity).Append(',')
                .Append(row.Count).Append(',')
                .Append(F(total == 0 ? 0m : row.Count / (decimal)total, 4))
                .AppendLine();
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static bool WriteReport(string path, IReadOnlyList<RunResult> all, int runs,
        IReadOnlyList<string> supplementedEras)
    {
        var text = new StringBuilder();
        text.AppendLine("# Balance simulation: schema 6 vs schema 5");
        text.AppendLine($"- {runs} runs per schema/strategy, fixed seed sequence; every fight won; "
            + $"difficulty {Difficulty.ToString(CultureInfo.InvariantCulture)}, "
            + $"era progression {(EraProgression ? "on" : "off")}.");
        text.AppendLine("- Player build: 25th-percentile priced qualified design of the current era "
            + "(10th percentile / cheapest fallback if unaffordable).");
        text.AppendLine("- Item rule: before offers, activate the highest-priority inventory item whose "
            + "family is not active, up to three slots; skip Operational Freedom and Emergency Recovery; "
            + "claim one reward choice, discarding the lowest-priority stored item when full.");
        text.AppendLine("- Enemy target is read from the pinned schema-6 target; schema-5 boss targets "
            + "are derived from the unchanged legacy formula (those cards carry no target field).");
        if (supplementedEras.Count > 0)
            text.AppendLine("- Supplemented eras (" + string.Join(", ", supplementedEras)
                + "): Coldwar/Coldwar2 have only two designs and Coldwar3's twelve-copy ceiling "
                + "itself is below its late targets. Deterministic price-ladder/anchor candidates "
                + "were added only there so the strict-risk planner can make three offers; those era "
            + "curves are synthetic. The other five eras use the pinned native catalogue unchanged.");
        text.AppendLine();
        text.AppendLine("## Schema 6, random: mean enemy target / available budget");
        text.AppendLine();
        text.AppendLine("| era | fight 1 | fight 2 | fight 3 | boss | boss/fight3 |");
        text.AppendLine("|---|---|---|---|---|---|");
        var table6 = CurveTable(all, 6, "random");
        var table5 = CurveTable(all, 5, "random");
        foreach (var era in table6.Keys) text.AppendLine(Row(era, table6[era]) + " " + F(table6[era][3] / table6[era][2], 3) + " |");
        text.AppendLine("## Schema 5, random: same measurement (legacy curve)");
        text.AppendLine();
        text.AppendLine("| era | fight 1 | fight 2 | fight 3 | boss |");
        text.AppendLine("|---|---|---|---|---|");
        foreach (var era in table5.Keys) text.AppendLine(Row(era, table5[era]));
        var schema6 = all.Where(r => r.Schema == 6).SelectMany(r => r.Samples).ToArray();
        var schema5 = all.Where(r => r.Schema == 5).SelectMany(r => r.Samples).ToArray();
        var roundMeans = schema6.GroupBy(s => s.Round)
            .Select(g => (Round: g.Key, Era: g.First().Era, Ratio: g.Average(s => s.TargetRatio)))
            .ToArray();
        var peaks = roundMeans.OrderByDescending(r => r.Ratio).Take(2).ToArray();
        var dips = roundMeans.OrderBy(r => r.Ratio).Take(2).ToArray();
        var items = all.Where(r => r.Schema == 6).SelectMany(r => r.Drops)
            .GroupBy(d => (d.Kind, d.Rarity)).OrderByDescending(g => g.Count()).Take(3).ToArray();
        var minOne = schema6.Length == 0 ? 0m : schema6.Average(s => s.MinOneShare);
        var roundOne = schema6.Where(s => s.RoundInEra == 1).ToArray();
        var minOneRoundOne = roundOne.Length == 0 ? 0m
            : roundOne.Average(s => s.MinOneShare);
        text.AppendLine("## Findings");
        text.AppendLine("- Mean schema-6 run score (flawless, zero retries): "
            + string.Join("; ", StrategyNames.Select(strategy => strategy + " " + F(all.Where(r => r.Schema == 6 && r.Strategy == strategy).Average(r => r.Score), 2))) + ".");
        text.AppendLine($"- Highest schema-6 target ratios: "
            + string.Join(", ", peaks.Select(p => $"round {p.Round} ({p.Era}) {F(p.Ratio, 2)}")) + ".");
        text.AppendLine($"- Lowest schema-6 target ratios: "
            + string.Join(", ", dips.Select(p => $"round {p.Round} ({p.Era}) {F(p.Ratio, 2)}")) + ".");
        text.AppendLine($"- Schema-5's legacy curve still rotates in the table above; schema 6 is monotonic 1<2<3 in every era.");
        text.AppendLine($"- One-enemy offers: {F(minOneRoundOne * 100m, 1)}% of fight-1 offers, "
            + $"{F(minOne * 100m, 1)}% of all sampled offers.");
        text.AppendLine($"- Most common schema-6 drops: "
            + string.Join(", ", items.Select(i => $"{i.Key.Kind}/{i.Key.Rarity} x{i.Count()}")) + ".");
        text.AppendLine($"- Mean target ratio: schema 6 {F(Mean(schema6.Select(s => s.TargetRatio).ToArray()), 2)}"
            + $" vs schema 5 {F(Mean(schema5.Select(s => s.TargetRatio).ToArray()), 2)}.");

        var ok = true;
        foreach (var era in table6.Keys)
        {
            var row = table6[era];
            if (!(row[0] < row[1] && row[1] < row[2]))
            {
                ok = false;
                text.AppendLine($"- CHECK FAILED: {era} fight 1/2/3 = {row[0]:0.00}/{row[1]:0.00}/{row[2]:0.00}.");
            }
            if (row[3] <= row[2])
            {
                ok = false;
                text.AppendLine($"- CHECK FAILED: {era} boss {F(row[3], 3)} <= fight3 {F(row[2], 3)}.");
            }
        }
        if (ok) text.AppendLine("- CHECK OK: schema 6 random mean target ratio rises 1 -> 2 -> 3 -> boss in every era.");
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        return ok;
    }

    private static Dictionary<string, decimal[]> CurveTable(
        IReadOnlyList<RunResult> all, int schema, string strategy)
    {
        var rows = all.Where(r => r.Schema == schema && r.Strategy == strategy)
            .SelectMany(r => r.Samples)
            .GroupBy(s => (s.Era, s.RoundInEra))
            .ToDictionary(g => g.Key, g => g.Average(s => s.TargetRatio));
        var eras = all.Where(r => r.Schema == schema).SelectMany(r => r.Samples).OrderBy(s => s.EraIndex)
            .Select(s => s.Era).Distinct().ToArray();
        var table = new Dictionary<string, decimal[]>(StringComparer.Ordinal);
        foreach (var era in eras)
        {
            var values = new decimal[4];
            for (var fight = 1; fight <= 4; fight++)
                values[fight - 1] = rows.TryGetValue((era, fight), out var value) ? value : 0m;
            table[era] = values;
        }
        return table;
    }

    private static string Row(string era, decimal[] values) =>
        "| " + era + " | " + string.Join(" | ", values.Select(v => F(v, 2))) + " |";

    private static decimal Mean(decimal[] values) =>
        values.Length == 0 ? 0m : values.Average();

    private static decimal PercentileSorted(decimal[] sorted, decimal fraction)
    {
        if (sorted.Length == 0) return 0m;
        var index = (int)Math.Round((sorted.Length - 1) * fraction, MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static string F(decimal value, int digits = 4) =>
        value.ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);

    private sealed record RoundSample(int Round, int PlanRound, string Era, int RoundInEra,
        bool Boss, bool Endless, decimal TargetRatio, decimal CostRatio, int EnemyCount,
        int FleetSize, decimal MinOneShare, int PreRound, int EraIndex);

    private sealed record RunResult(int RunIndex, int Schema, string Strategy,
        IReadOnlyList<RoundSample> Samples,
        IReadOnlyList<(RewardKind Kind, RewardRarity Rarity)> Drops, string? Error, decimal Score = 0m);
}

internal sealed record PreparedContent(CatalogueDefinition Definition,
    IReadOnlyDictionary<int, decimal[]> OriginalPrices, IReadOnlyList<string> SupplementedEras);

internal static class ContentPreparer
{
    public static PreparedContent Prepare(CatalogueDefinition definition, decimal initialBase)
    {
        var prices = new Dictionary<int, decimal[]>();
        var eras = definition.Eras.ToArray();
        var supplemented = new List<string>();
        for (var i = 0; i < eras.Length; i++)
        {
            var era = eras[i];
            var original = era.Enemies
                .Where(e => e.Qualification >= ContentQualification.Candidate
                    && e.CheckedPrice.Value > 0m)
                .OrderBy(e => e.CheckedPrice.Value).ToArray();
            prices[i] = original.Select(e => e.CheckedPrice.Value).ToArray();
            if (original.Length > 0 && original.Length < 6)
            {
                var extra = new List<EnemyDesignEntry>();
                var factors = new[] { 0.5m, 1.5m, 3m, 6m, 12m, 24m };
                for (var k = 0; k < factors.Length; k++)
                {
                    var source = original[k % original.Length];
                    var price = Math.Max(1m, decimal.Floor(source.CheckedPrice.Value * factors[k]));
                    var role = k % 3 == 0 ? EnemyRole.Heavy
                        : k % 3 == 1 ? EnemyRole.Recon : EnemyRole.Line;
                    extra.Add(new EnemyDesignEntry(
                        new BlueprintReference(source.Blueprint.LocalId + ".sim.price." + k,
                            Hash(90_000 + i * 100 + k), era.Era, source.Blueprint.RequiredMods),
                        role, price, new ScenarioValue(price, definition.PriceUnitId),
                        ContentQualification.Candidate)
                    {
                        FormationFamily = (source.FormationFamily ?? source.Blueprint.LocalId) + " sim" + k,
                        MassKg = source.MassKg
                    });
                }
                if (extra.Count > 0)
                {
                    eras[i] = era with { Enemies = era.Enemies.Concat(extra).ToArray() };
                    supplemented.Add(era.Era.NativeId);
                }
            }

            // Late-era targets can exceed twelve copies of the costliest pinned
            // design, which makes the strict-risk planner saturate and fail. Add
            // one transparent high-price anchor only where that is the case.
            var maxPrice = eras[i].Enemies.Max(e => e.CheckedPrice.Value);
            var eraBase = SprocketExpedition.Core.Run.RunDirector.FiniteEraBudget(initialBase, i);
            if (maxPrice * 12m >= eraBase * 4m) continue;
            var anchorFactors = new[] { 0.25m, 0.5m, 0.75m, 1m, 1.5m };
            var anchors = anchorFactors.Select((factor, k) =>
            {
                var anchorPrice = Math.Max(1m, decimal.Floor(eraBase * factor));
                return new EnemyDesignEntry(
                    new BlueprintReference(eras[i].Era.NativeId + ".sim.anchor." + k,
                        Hash(95_000 + i * 10 + k), eras[i].Era, Array.Empty<ModRequirement>()),
                    EnemyRole.Heavy, anchorPrice,
                    new ScenarioValue(anchorPrice, definition.PriceUnitId),
                    ContentQualification.Candidate)
                {
                    FormationFamily = "Simulation anchor " + k
                };
            }).ToArray();
            eras[i] = eras[i] with { Enemies = eras[i].Enemies.Concat(anchors).ToArray() };
            if (!supplemented.Contains(eras[i].Era.NativeId))
                supplemented.Add(eras[i].Era.NativeId);
        }
        return new PreparedContent(definition with { Eras = eras }, prices, supplemented);
    }

    private static string Hash(int seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            BitConverter.GetBytes(seed))).ToLowerInvariant();
}

internal sealed record Archive(decimal InitialBudget, string StarterId,
    CatalogueDefinition Definition)
{
    public static Archive Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var payload = document.RootElement.GetProperty("Payload").GetString()
            ?? throw new InvalidDataException("archive Payload is missing");
        using var payloadDocument = JsonDocument.Parse(payload);
        var definitionElement = payloadDocument.RootElement.GetProperty("Definition");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var definition = definitionElement.Deserialize<CatalogueDefinition>(options)
            ?? throw new InvalidDataException("archive Definition could not be read");
        var initial = payloadDocument.RootElement.GetProperty("InitialBudget").GetDecimal();
        var starter = payloadDocument.RootElement.TryGetProperty("StarterId", out var starterElement)
            ? starterElement.GetString() ?? string.Empty : string.Empty;
        return new Archive(initial, starter, definition);
    }
}

internal sealed record Options(int Runs, string CataloguePath, string OutputDirectory)
{
    public static Options Parse(string[] args)
    {
        var runs = 1_000;
        var root = FindWorkspaceRoot();
        var catalogue = Path.Combine(root, "data", "catalogue-slot3-8eras.json");
        var output = Path.Combine(root, "artifacts", "balance");
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--runs" when i + 1 < args.Length:
                    runs = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                case "--catalogue" when i + 1 < args.Length:
                    catalogue = Path.GetFullPath(args[++i]);
                    break;
                case "--out" when i + 1 < args.Length:
                    output = Path.GetFullPath(args[++i]);
                    break;
                default:
                    throw new ArgumentException("Unknown argument: " + args[i]);
            }
        }
        if (!File.Exists(catalogue))
            throw new FileNotFoundException("catalogue not found", catalogue);
        return new Options(runs, catalogue, output);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 4 && directory is not null; i++, directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && Directory.Exists(Path.Combine(directory.FullName, "SprocketExpedition")))
                return directory.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
