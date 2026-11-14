using System.Security.Cryptography;
using System.Text;
using Sprocket;
using Sprocket.CustomBattles;
using Sprocket.VehicleDesigner;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;
using SprocketExpedition.Core;
using UObject = UnityEngine.Object;

namespace SprocketExpedition.Game;

/// <summary>
/// Native campaign design capture and Custom Battle encounter adapter.
/// </summary>
internal sealed class CampaignGameAdapter : IGameAdapter
{
    private readonly string _costUnitId;
    private readonly decimal? _fixtureCost;
    private readonly CampaignNativeCatalogue? _nativeContent;
    private byte[]? _lastSnapshotBytes;
    private bool _placeholderDesign;

    internal CampaignGameAdapter(string costUnitId, decimal? fixtureCost = null,
        CampaignNativeCatalogue? nativeContent = null)
    {
        _costUnitId = string.IsNullOrWhiteSpace(costUnitId)
            ? throw new ArgumentException("A native scenario cost unit is required.", nameof(costUnitId))
            : costUnitId;
        _fixtureCost = fixtureCost;
        _nativeContent = nativeContent;
    }

    internal void RestoreLockedDesign(BlueprintReference blueprint, byte[] bytes)
    {
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, blueprint.SnapshotHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The saved tank does not match its recorded hash.");
        _lastSnapshotBytes = bytes.ToArray();
        _placeholderDesign = false;
    }

    public AdapterReadiness CheckReadiness()
    {
        if (CampaignNativeFlow.OwnsSession)
            return new AdapterReadiness(CampaignNativeFlow.Editing,
                "Wait for the Expedition tank editor to finish loading.");
        if (UObject.FindObjectOfType<DeathmatchGameMode>() != null)
        {
            // Placeholder first-playtest path: the controller can start directly in
            // a running Custom Battle and use the native units already loaded.
            return new AdapterReadiness(
                true, "Custom Battle placeholder design path; existing native units will be used.");
        }

        var designer = UObject.FindObjectOfType<VehicleDesignerCore>();
        if (designer != null)
        {
            if (!designer.HasEditor
                || designer.editorState != VehicleDesignerCore.EditorState.Running
                || designer.Target == null)
            {
                return new AdapterReadiness(
                    false, "Wait for the normal vehicle designer to settle with a target.");
            }

            return new AdapterReadiness(
                true, "Normal vehicle designer ready; fixture capture path.");
        }

        return new AdapterReadiness(
            false, "No settled vehicle designer is loaded.");
    }

    public Task<DesignCapture> CaptureDesignAsync(
        EraReference era,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var readiness = CheckReadiness();
        if (!readiness.Ready)
        {
            throw new InvalidOperationException(readiness.Reason);
        }

        IVehicleGateway target;
        decimal cost;
        var designer = UObject.FindObjectOfType<VehicleDesignerCore>();
        if (designer != null
            && designer.HasEditor
            && designer.editorState == VehicleDesignerCore.EditorState.Running
            && designer.Target != null)
        {
            target = designer.Target.TryCast<IVehicleGateway>()
                ?? throw new InvalidOperationException("The normal designer target is not an IVehicleGateway.");
            var nativeCost = target.Cost;
            if (_nativeContent != null && (!float.IsFinite(nativeCost) || nativeCost <= 0f))
                throw new InvalidOperationException("The native editor has no positive design price yet.");
            cost = float.IsFinite(nativeCost) && nativeCost > 0f ? (decimal)nativeCost : 100m;
            _placeholderDesign = false;
        }
        else if (UObject.FindObjectOfType<DeathmatchGameMode>() != null)
        {
            // Placeholder first-playtest design: no native blueprint snapshot is
            // available from a running Custom Battle. The host uses the already
            // loaded native player/allied units; this is explicitly a fixture.
            _placeholderDesign = true;
            _lastSnapshotBytes = null;
            var placeholderBytes = Encoding.UTF8.GetBytes(
                "{\"expedition\":\"placeholder-design\"}");
            var placeholderHash = Convert.ToHexString(
                SHA256.HashData(placeholderBytes)).ToLowerInvariant();
            var placeholderQuote = new DesignQuote(
                new BlueprintReference(
                    "player.placeholder",
                    placeholderHash,
                    era,
                    Array.Empty<ModRequirement>()),
                new ScenarioValue(_fixtureCost ?? 100m, _costUnitId),
                true,
                true,
                null);
            Plugin.Logger.LogInfo(
                "Expedition controller adapter: placeholder design used in the running Custom Battle; "
                + "existing native units will be spawned.");
            return Task.FromResult(new DesignCapture(placeholderQuote, placeholderBytes));
        }
        else
        {
            throw new InvalidOperationException("No settled vehicle designer or Custom Battle is available.");
        }

        if (_fixtureCost is { } fixedCost) cost = fixedCost;

        var serializer = IVehicleBlueprintSerializer.Serializer
            ?? throw new InvalidOperationException("The native blueprint serializer is unavailable.");
        var blueprint = serializer.ToBlueprint(target)
            ?? throw new InvalidOperationException("The native blueprint could not be captured.");
        var json = serializer.SerializeToJSON(blueprint, false);
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidOperationException("The native blueprint serializer returned no data.");
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        _lastSnapshotBytes = bytes;
        if (CampaignNativeFlow.OwnsSession) CampaignNativeFlow.RememberDesign(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var nativeEraValid = _nativeContent == null
            || CampaignNativeCatalogue.DateBelongsToEra(target.DesignInfo.Date, era.NativeId);
        var quote = new DesignQuote(
            new BlueprintReference(
                "player.locked",
                hash,
                era,
                Array.Empty<ModRequirement>()),
            new ScenarioValue(cost, _costUnitId),
            true,
            nativeEraValid,
            nativeEraValid ? null : $"This encounter requires a {era.NativeId} tank. Load or build a design in that native era.");

        return Task.FromResult(new DesignCapture(quote, bytes));
    }

    public Task OpenEditorAsync(
        BlueprintReference blueprint,
        EraReference era,
        ScenarioValue budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var readiness = CheckReadiness();
        if (!readiness.Ready)
        {
            throw new InvalidOperationException(
                "The campaign build boundary requires a settled vehicle editor: "
                + readiness.Reason);
        }

        return Task.CompletedTask;
    }

    public async Task<EncounterResult> RunEncounterAsync(
        EncounterRequest request,
        Func<Task> combatStarted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (CampaignNativeFlow.OwnsSession)
            return await RunEditorBattleAsync(request, combatStarted, cancellationToken);

        if (_placeholderDesign
            || request.PlayerBlueprint.LocalId == "player.placeholder")
        {
            // The fixture design has no native blueprint; use the units already
            // loaded in the Custom Battle. This also covers a resumed placeholder
            // run, where the adapter has no in-memory capture.
            _placeholderDesign = true;
            CustomBattleHostSpike.SetLockedBlueprint(null);
        }
        else if (_lastSnapshotBytes is { Length: > 0 })
        {
            if (!CustomBattleHostSpike.HasLockedBlueprint)
            {
                CustomBattleHostSpike.SetLockedBlueprint(_lastSnapshotBytes);
            }
        }
        else
        {
            return new EncounterResult(
                request.Attempt,
                request.Plan,
                EncounterOutcome.TechnicalFailure,
                "No locked design snapshot was captured for this encounter.");
        }

        // Deliberately no RunContinuationsAsynchronously: the native outcome is
        // delivered on the Unity main thread, and the controller/presenter must
        // continue there.
        var completion = new TaskCompletionSource<(bool Victory, string Detail)>();
        void OnOutcome(bool victory, string detail) => completion.TrySetResult((victory, detail));
        CustomBattleHostSpike.CampaignOutcome = OnOutcome;
        CustomBattleHostSpike.CampaignCombatStarted = () =>
        {
            try { combatStarted().GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    "Expedition campaign adapter: combat-started persistence failed. " + ex);
            }
        };

        var enemyCount = request.Plan.Enemies is { Count: > 0 }
            ? request.Plan.Enemies.Sum(enemy => enemy.Count)
            : 1;
        CustomBattleHostSpike.CampaignFriendlyCount = Math.Max(1, request.FriendlyCountIncludingPlayer);
        CustomBattleHostSpike.CampaignEnemyCount = Math.Max(1, enemyCount);

        var enemyLineup = new List<(UnitDefinition Unit, int Count)>();
        if (CampaignFactionDesigns.TryLoad(out var factionUnits, out var factionReason))
        {
            var groups = request.Plan.Enemies;
            if (groups is { Count: > 0 })
            {
                for (var i = 0; i < groups.Count; i++)
                {
                    var count = Math.Max(1, groups[i].Count);
                    enemyLineup.Add((factionUnits[i % factionUnits.Count], count));
                }
            }
            else
            {
                enemyLineup.Add((factionUnits[0], Math.Max(1, enemyCount)));
            }

            CustomBattleHostSpike.CampaignEnemyDesignSource =
                $"faction:{CampaignFactionDesigns.FactionName}; {factionReason}";
            Plugin.Logger.LogInfo(
                $"Expedition controller adapter: faction design source resolved; {factionReason}; "
                + $"groups={enemyLineup.Count}; totalEnemies={enemyLineup.Sum(e => e.Count)}.");
        }
        else
        {
            CustomBattleHostSpike.CampaignEnemyDesignSource =
                "fallback:loaded Custom Battle enemy; " + factionReason;
            Plugin.Logger.LogWarning(
                $"Expedition controller adapter: faction design source unavailable ({factionReason}); "
                + "falling back to the loaded Custom Battle enemy UnitDefinition.");
        }

        CustomBattleHostSpike.CampaignEnemyLineup =
            enemyLineup.Count > 0 ? enemyLineup : null;

        Plugin.Logger.LogInfo(
            $"Expedition controller adapter: plan {request.Plan.OfferId}; "
            + $"friendlies={CustomBattleHostSpike.CampaignFriendlyCount}; "
            + $"enemies={CustomBattleHostSpike.CampaignEnemyCount}; map={request.Plan.MapId}; "
            + $"enemyDesignSource={CustomBattleHostSpike.CampaignEnemyDesignSource}.");

        BattleStatsBridge.Begin(CampaignBattleStats.Key(request.Attempt.RunId, request.Attempt.EncounterId, request.Attempt.PlanHash, request.Attempt.Generation));
        if (!CustomBattleHostSpike.StartCampaignEncounter())
        {
            CustomBattleHostSpike.CampaignOutcome = null;
            CustomBattleHostSpike.CampaignCombatStarted = null;
            return new EncounterResult(
                request.Attempt,
                request.Plan,
                EncounterOutcome.TechnicalFailure,
                "Start/load a Running Custom Battle with one unit per team before beginning the encounter.");
        }

        try
        {
            var result = await completion.Task.WaitAsync(cancellationToken);
            var metrics = RosterMetrics();
            BattleStatsBridge.Finish(result.Victory ? "Victory" : "FriendlyForceDestroyed");
            return new EncounterResult(
                request.Attempt,
                request.Plan,
                result.Victory ? EncounterOutcome.Victory : EncounterOutcome.FriendlyForceDestroyed,
                result.Detail)
            {
                EnemiesDestroyed = metrics.Destroyed,
                FriendliesLost = metrics.Lost,
                PlayerTankSurvived = metrics.Survived
            };
        }
        catch (OperationCanceledException)
        {
            BattleStatsBridge.Finish(CustomBattleHostSpike.ContactMade ? "CombatRetreat" : "PrecombatAborted");
            throw;
        }
        finally
        {
            // Keep CampaignOutcome registered after a delivered result so the host
            // suppresses native resets behind the controller's result screen. The
            // hub clears it explicitly on return/continue. Pause the observer so
            // the next Begin can restart the host for the next controller offer.
            CustomBattleHostSpike.CampaignCombatStarted = null;
            CustomBattleHostSpike.EndCampaignEncounter();
        }
    }

    private async Task<EncounterResult> RunEditorBattleAsync(
        EncounterRequest request, Func<Task> combatStarted, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<(bool Victory, string Detail)>();
        var sessionGeneration = CampaignNativeFlow.SessionGeneration;
        void Outcome(bool victory, string detail)
        {
            if (!cancellationToken.IsCancellationRequested
                && CampaignNativeFlow.OwnsSession && CampaignNativeFlow.SessionGeneration == sessionGeneration)
            {
                if (completion.TrySetResult((victory, detail))) CampaignMusic.Outcome(victory);
            }
        }
        try
        {
            if (_lastSnapshotBytes is not { Length: > 0 })
                throw new InvalidOperationException("No actual editor tank was captured for this encounter.");
            var player = CustomBattleBlueprintResolver.CreateLockedUnit(_lastSnapshotBytes, out var error)
                ?? throw new InvalidOperationException("Player blueprint could not load: " + error);
            CustomBattleHostSpike.CampaignPlayerDesignName = player.Name;
            var lineup = new List<(UnitDefinition Unit, int Count)>();
            var ambusherMap = new Dictionary<string, CampaignCasemateDetection.Decision>(StringComparer.Ordinal);
            var forcedAmbushers = CampaignCasemateDetection.ParseNames(Plugin.AmbusherDesigns?.Value);
            var deniedAmbushers = CampaignCasemateDetection.ParseNames(Plugin.NotAmbusherDesigns?.Value);
            var ambusherNameConflict = false;
            string? bossNativeName = null;
            string reason;
            if (_nativeContent != null)
            {
                if (!CampaignNativeCatalogue.DateBelongsToEra(player.CreationDate, request.Plan.Era.NativeId))
                    throw new InvalidOperationException("The saved player tank does not belong to the encounter's native era.");
                foreach (var enemy in request.Plan.Enemies)
                {
                    var expectedEra = enemy.BlueprintId == request.Plan.Extras?.BossBlueprintId
                        ? request.Plan.Extras.BossEra! : request.Plan.Era.NativeId;
                    var testDesign = request.TestPool && _nativeContent.Definition.TestPool?.Any(t => t.Design.Blueprint.LocalId == enemy.BlueprintId) == true;
                    if (request.TestPool && !testDesign) throw new InvalidOperationException("Enemy is outside the pinned test pool.");
                    var design = testDesign ? _nativeContent.FindAny(enemy.BlueprintId) : _nativeContent.Find(enemy.BlueprintId, expectedEra);
                    var unit = CustomBattleBlueprintResolver.CreateLockedUnit(design.Bytes, out var loadError)
                        ?? throw new InvalidOperationException($"Enemy '{design.Name}' could not load: {loadError}");
                    if (!CampaignNativeCatalogue.DateFitsEra(unit.CreationDate, design.Era))
                        throw new InvalidOperationException($"Enemy '{design.Name}' has an incompatible native era.");
                    lineup.Add((unit, enemy.Count));
                    var key = unit.Name;
                    var decision = CampaignCasemateDetection.Decide(key, design.Bytes,
                        forcedAmbushers, deniedAmbushers);
                    if (ambusherMap.TryGetValue(key, out var previous)
                        && previous.IsAmbusher != decision.IsAmbusher)
                    {
                        // Same native name, different moulds: no safe per-hull
                        // claim, so keep those hulls out of the role.
                        ambusherMap[key] = new CampaignCasemateDetection.Decision(false,
                            CampaignCasemateDetection.Source.None);
                        ambusherNameConflict = true;
                    }
                    else if (!ambusherMap.ContainsKey(key)) ambusherMap[key] = decision;
                    if (enemy.BlueprintId == request.Plan.Extras?.BossBlueprintId && enemy.Count == 1) bossNativeName = unit.Name;
                }
                if (ambusherNameConflict)
                    Plugin.Logger.LogWarning("Expedition ambusher detection: duplicate native design name had "
                        + "conflicting casemate results; those hulls stay non-ambusher.");
                reason = "Exact native IDs and immutable blueprint bytes; own creation dates retained; "
                    + $"era={request.Plan.Era.NativeId}; saved-price total="
                    + request.Plan.Enemies.Sum(e => _nativeContent.FindAny(e.BlueprintId).Price * e.Count);
            }
            else
            {
                // Existing fixture save slots retain their original host semantics.
                if (!CampaignFactionDesigns.TryLoad(out var enemies, out reason))
                    throw new InvalidOperationException(reason);
                for (var i = 0; i < request.Plan.Enemies.Count; i++)
                    lineup.Add((enemies[i % enemies.Count], request.Plan.Enemies[i].Count));
            }
            if (lineup.Count == 0) throw new InvalidOperationException("The chosen encounter contains no enemies.");
            var special = new List<(UnitDefinition Unit, int Count)>();
            foreach (var reinforcement in request.Reinforcements)
            {
                var tank = _nativeContent?.FindAny(reinforcement.BlueprintId)
                    ?? throw new InvalidOperationException("Reinforcement requires a pinned native design.");
                var capturedTestTank = request.TestPool && _nativeContent?.Definition.TestPool?
                    .Any(t => t.Design.Blueprint.LocalId == tank.Id) == true;
                if (!capturedTestTank && CampaignNativeCatalogue.EraIndex(tank.Era) > CampaignNativeCatalogue.EraIndex(request.Plan.Era.NativeId))
                    throw new InvalidOperationException("Reinforcement belongs to an unavailable future era.");
                var unit = CustomBattleBlueprintResolver.CreateLockedUnit(CampaignBlueprintBlob.SupportClone(tank.Bytes, tank.Name), out var reinforcementError)
                    ?? throw new InvalidOperationException(reinforcementError);
                special.Add((unit, reinforcement.Count));
            }
            var copies = request.FriendlyCountIncludingPlayer - special.Sum(entry => entry.Count);
            if (copies < 1) throw new InvalidOperationException("The player must retain a paid native tank.");
            var friendlyLineup = CustomBattleHostSpike.Lineup(player, copies);
            foreach (var reinforcement in special)
                friendlyLineup.Add(reinforcement.Unit, new UnitInstanceInfo { Count = reinforcement.Count });

            CustomBattleHostSpike.SetLockedBlueprint(_lastSnapshotBytes);
            CustomBattleHostSpike.CampaignFriendlyCount = request.FriendlyCountIncludingPlayer;
            CustomBattleHostSpike.CampaignEnemyCount = lineup.Sum(e => e.Count);
            CustomBattleHostSpike.CampaignEnemyLineup = lineup;
            CustomBattleHostSpike.CampaignEnemyDesignSource = reason;
            var config = new BattleConfig
            {
                MapName = _nativeContent != null
                    ? CampaignMapPool.Read(CampaignNativeCatalogue.MapDirectory, request.Plan.MapId).Id
                    : CampaignNativeFlow.BattleMap,
                // A raw native EnvironmentConfig has zero visibility. Use a
                // clear daytime test environment for this first checked map.
                Environment = new EnvironmentConfig { TimeCycleFraction = 0.5f, FogDistance = 3000f },
                Teams = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<TeamDefinition>(
                    new[]
                    {
                        new TeamDefinition { Name = "Expedition", maxUnits = 8,
                            Flags = TeamDefinitionFlags.Player | TeamDefinitionFlags.Attacker | TeamDefinitionFlags.RequiresUnits,
                            Units = friendlyLineup },
                        new TeamDefinition { Name = "Enemy", maxUnits = 12,
                            Flags = TeamDefinitionFlags.Defender | TeamDefinitionFlags.RequiresUnits,
                            Units = CustomBattleHostSpike.Lineup(lineup) }
                    })
            };
            var tacticalRoles = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_nativeContent != null && request.Plan.Extras?.Special is { } group)
            {
                // Pair the authored role with the native UnitDefinition through the
                // plan's own blueprint identities, never by parallel list index.
                var namesByBlueprint = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < request.Plan.Enemies.Count && i < lineup.Count; i++)
                    namesByBlueprint[request.Plan.Enemies[i].BlueprintId] = lineup[i].Unit.Name;
                var missingRoleNames = 0;
                foreach (var member in group.Enemies)
                {
                    if (!namesByBlueprint.TryGetValue(member.BlueprintId, out var nativeName))
                    {
                        missingRoleNames++;
                        continue;
                    }
                    var role = member.DeploymentRole ?? "Center";
                    if (tacticalRoles.TryGetValue(nativeName, out var existing)
                        && !string.Equals(existing, role, StringComparison.Ordinal))
                    {
                        // A duplicate native name with conflicting roles cannot be
                        // mapped safely by slot. Keep that name neutral rather than
                        // guessing which hull the author meant.
                        tacticalRoles[nativeName] = "Center";
                        Plugin.Logger.LogWarning(
                            $"Expedition formation roles: duplicate native design name '{nativeName}' "
                            + "has conflicting roles; neutral Center retained for those hulls.");
                    }
                    else tacticalRoles[nativeName] = role;
                }
                if (missingRoleNames > 0)
                    Plugin.Logger.LogWarning(
                        $"Expedition formation roles: {missingRoleNames} planned design(s) had no matching native UnitDefinition; "
                        + "their lanes remain neutral Center.");
            }
            CampaignSpawnTrial.Prepare(config, request.Plan.Era.NativeId, request.Plan.PlanHash,
                request.Plan.Extras?.Special?.Pattern.ToString(), tacticalRoles, ambusherMap);
            var combatPrices = _nativeContent?.Definition.Eras.Single(e => e.Era.NativeId == request.Plan.Era.NativeId)
                .Enemies.Select(e => _nativeContent.FindAny(e.Blueprint.LocalId))
                .Select(t => (t.Name, t.Price)).ToArray();
            CampaignEnemyHunt.Prepare(config, request.Plan.PlanHash, combatPrices,
                CampaignNativeCatalogue.EraIndex(request.Plan.Era.NativeId), tacticalRoles, ambusherMap);
            if (bossNativeName != null && request.Plan.Extras?.BossCharacter is { } character)
                CampaignBossTactics.Prepare(bossNativeName, character.Replace("The ", "", StringComparison.Ordinal));
            CampaignTestPool.Ledger.Begin(request.TestPool);
            BattleStatsBridge.Begin(CampaignBattleStats.Key(request.Attempt.RunId, request.Attempt.EncounterId, request.Attempt.PlanHash, request.Attempt.Generation));
            await CampaignNativeFlow.OpenBattleAsync(config, cancellationToken);
            CustomBattleHostSpike.CampaignOutcome = Outcome;
            CustomBattleHostSpike.CampaignStartFailed = reason => completion.TrySetException(new InvalidOperationException(reason));
            CustomBattleHostSpike.CampaignCombatStarted = () =>
            {
                try
                {
                    combatStarted().GetAwaiter().GetResult();
                    CampaignMusic.BeginCombat(request.Plan.Extras?.BossBlueprintId != null, request.Plan.Era.NativeId);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            };
            if (!CustomBattleHostSpike.StartCampaignEncounter(restart: false))
                throw new InvalidOperationException("The native Custom Battle observer could not start.");
            var result = await completion.Task.WaitAsync(cancellationToken);
            var metrics = RosterMetrics();
            BattleStatsBridge.Finish(result.Victory ? "Victory" : "FriendlyForceDestroyed");
            LogTestBattle(request, result.Victory ? "Victory" : "FriendlyForceDestroyed");
            return new EncounterResult(request.Attempt, request.Plan,
                result.Victory ? EncounterOutcome.Victory : EncounterOutcome.FriendlyForceDestroyed, result.Detail)
            {
                ConfirmedEnemyKills = result.Victory ? request.Plan.Enemies.Sum(e => e.Count) : null,
                SurvivingAllies = result.Victory ? CustomBattleHostSpike.LivingAiAllyCount : null,
                EnemiesDestroyed = metrics.Destroyed,
                FriendliesLost = metrics.Lost,
                PlayerTankSurvived = metrics.Survived
            };
        }
        catch (OperationCanceledException)
        {
            var retreat = CustomBattleHostSpike.ContactMade;
            BattleStatsBridge.Finish(retreat ? "CombatRetreat" : "PrecombatAborted");
            LogTestBattle(request, retreat ? "Retreat" : "PrecombatAborted");
            throw;
        }
        catch (Exception ex)
        {
            BattleStatsBridge.Finish("TechnicalFailure");
            LogTestBattle(request, "TechnicalFailure");
            Plugin.Logger.LogError("Expedition editor -> Custom Battle failed: " + ex);
            return new EncounterResult(request.Attempt, request.Plan, EncounterOutcome.TechnicalFailure, ex.Message);
        }
        finally
        {
            if (CampaignNativeFlow.SessionGeneration == sessionGeneration)
            {
                CustomBattleHostSpike.CampaignCombatStarted = null;
                CustomBattleHostSpike.EndCampaignEncounter();
            }
            // Retain this exact outcome owner after delivery to suppress the
            // native Retry screen until the qualified return path leaves combat.
            if (!completion.Task.IsCompletedSuccessfully && CustomBattleHostSpike.CampaignOutcome == Outcome)
            {
                // Retain a no-op campaign owner until the native scene exit;
                // native win/loss screens must not race a requested refit.
                CustomBattleHostSpike.CampaignOutcome = cancellationToken.IsCancellationRequested
                    && CampaignNativeFlow.OwnsSession && CampaignNativeFlow.SessionGeneration == sessionGeneration
                    ? static (_, _) => { } : null;
            }
        }
    }

    private void LogTestBattle(EncounterRequest request, string outcome)
    {
        if (!request.TestPool || !CampaignNativeFlow.OwnsSession
            || CampaignControllerHost.RunSnapshot?.CurrentAttempt != request.Attempt) return;
        try
        {
            var roster = CustomBattleHostSpike.CurrentRoster;
            var tanks = request.Plan.Enemies.Select(e => _nativeContent?.FindAny(e.BlueprintId).Name ?? e.BlueprintId).ToArray();
            var names = string.Join("|", tanks);
            // Victory and defeat need deaths on one side, so both imply contact.
            var battleContact = CustomBattleHostSpike.ContactMade || outcome is "Victory" or "FriendlyForceDestroyed";
            var results = tanks.Select(t => CampaignTestPool.Ledger.For(t, outcome == "Victory")).ToArray();
            Plugin.Logger.LogInfo($"Expedition test pool: battle {request.Plan.Round} enemies={names} outcome={outcome} spawned={roster?.SpawnedEnemies ?? 0}/{request.Plan.Enemies.Sum(e => e.Count)}"
                + $" contact={(battleContact ? "ja" : "nee")} perTank={string.Join("|", results.Select(r => r.Spawned + "/" + r.Contact))}");
            if (CampaignTestPool.AppendResults(CampaignNativeCatalogue.TestPoolResultsPath, request.Plan.Round, request.Attempt.Generation, results, outcome,
                $"{roster?.SpawnedEnemies ?? 0}/{request.Plan.Enemies.Sum(e => e.Count)}", battleContact) is { } warning)
                Plugin.Logger.LogWarning(warning);
        }
        catch (Exception ex) { Plugin.Logger.LogWarning("Expedition test pool battle log: " + ex.Message); }
    }

    public Task CleanupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    // Reuses the same native roster the battle HUD displays. Unreadable hulls
    // make the per-side counts ambiguous, so the log cells stay empty instead of
    // inventing kills or losses.
    private static (int? Destroyed, int? Lost, bool? Survived) RosterMetrics()
    {
        var survived = CustomBattleHostSpike.PlayerTankSurvived;
        var roster = CustomBattleHostSpike.CurrentRoster;
        if (roster is null || roster.Unknown > 0 || roster.Enemies < 0 || roster.Friendlies < 0
            || roster.Spawned < roster.Expected) return (null, null, survived);
        return (Math.Max(0, roster.ExpectedEnemies - roster.Enemies),
            Math.Max(0, roster.ExpectedFriendlies - roster.Friendlies), survived);
    }

    internal static SprocketExpedition.Core.Run.EditorReturnContext CaptureEditorReturn()
    {
        var metrics = RosterMetrics();
        return new(CustomBattleHostSpike.ContactMade, metrics.Destroyed, metrics.Lost,
            metrics.Survived, CustomBattleHostSpike.CombatDurationSeconds);
    }
}
