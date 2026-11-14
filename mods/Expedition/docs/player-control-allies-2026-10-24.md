# Initial own-design player control and living AI allies — 24 October 2026

Status: implemented in the host observer; pure policy checks pass. The coordinator performs combined native builds and gameplay acceptance remains for the user.

## Root integration contract

Before `StartCampaignEncounter`, set `CustomBattleHostSpike.CampaignPlayerDesignName` to the actual native name of the player's locked design. Set `CampaignStartFailed` to an owned-generation callback that faults the current encounter completion (`TechnicalFailure`), then clear that callback in coordinator cleanup. Existing `CampaignCombatStarted` is invoked only after initial control has been verified.

Read `CustomBattleHostSpike.LivingAiAllyCount` at definitive victory before encounter cleanup. The host freezes this count immediately before both its crew-roster victory callback and its native-completion victory callback. The frozen number remains available after `EndCampaignEncounter` while the same owned session and battle scene are present. A new encounter clears it; leaving the owned session/scene or requesting editor return makes the getter return zero. It never grants a reward itself.

Support/prototype snapshots must have a unique actual blueprint `header.name`; changing only the custom-battle unit label cannot prove identity. The coordinator creates that distinct name in an in-memory support blueprint copy while preserving original player/design bytes. Multiple normal copies of the player's own design are allowed: any living own-design copy is a valid initial player tank.

## Native selection evidence and correction

Current `BattleConfig`, `TeamDefinition` and `UnitInstanceInfo` expose no per-design player-pick field. Player is a team flag. The current native `DeathmatchGameMode.SetupGame` continuation (RVA 0x1d8b650) chooses the first registered vehicle matching its player-team predicate (RVA 0x1d87940). The predicate compares native team ID only. Dictionary insertion order consequently is not a reliable own-tank control contract when a different support design joins the player team.

The existing initial friendly-control guard now qualifies the exact own design through `VehicleBehaviour.VehicleInfo.TryCast<Vehicle>().DesignInfo.Name`, valid native `EntityID`, the player team and confirmed living crew. If the current controlled hull already matches and its native control type is Player, it is accepted. Otherwise one native `IVehicleControlPlayerState.SetControlTarget(IVehicleBehaviour)` task is started for a qualifying own-design hull.

The task is polled to completion on Update, checked for cancellation/fault, and the resulting native controlled hull/type is checked again. Combat-start acknowledgement waits for this verification. Missing own identity or failed control is reported through `CampaignStartFailed`; no win/loss is fabricated. Native results wait while initial control is pending. No repeated correction runs after verification, so subsequent player cycling and surviving-ally takeover remain native. The observer continues remembering the actual controlled entity for reward exclusion without issuing control commands.

Stopped observers can drain a pending native handoff without firing a start callback or applying another correction. A new native handoff cannot overlap a previous pending one. No native controller input, tank physics, scene assets or user files are changed.

## Living ally count

The adapter reads only the current battle's native spawn gateways and vehicle behavior. `Sprocket.EntityID.Valid`, its verified packed-int conversion and `ID.TeamID` validate and deduplicate entities. Native ControlType values were verified as None=0, Player=1, AI=2. Only confirmed living AI hulls on the player team count; dead/unreadable crew, enemies and the current player/controller entity are excluded. The current controller is sampled again at victory, so manually switching into a prototype excludes that prototype and can count the now-AI former own hull. Unknown data conservatively produces no extra reward count.

## Checks and manual acceptance

`dotnet run --project tests/PlayerControl -c Release` passes independently of game/Core dependencies: exact own-name qualification, support alias exclusion, wrong-team/invalid-entity/dead/unreadable rejection, AI reward counting, native entity deduplication and current-player exclusion after a manual switch. These checks validate pure policy; they do not establish native handoff timing.

1. Enter a battle containing the own tank and a differently named support/prototype design. The player's initial hull must be the locked own design; the log confirms the exact native name/entity and any awaited handoff.
2. Switch manually into another friendly hull after combat starts, and wait. Initial control correction must not pull the player back.
3. Win with known surviving AI allies, also after taking over another hull. The definitive-victory roster log must match living AI teammates with the actual player excluded.
4. Return to editor and start the next battle. Initial verification runs once for that new encounter; prior control tasks/counts must not affect it.
