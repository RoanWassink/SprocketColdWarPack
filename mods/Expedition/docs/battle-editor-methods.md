# Battle Editor method reference (public source only)

Reference: `Hans21223/Sprocket-Battle-Editor`, tag `v0.18.1`, MIT licence. Read-only
source review on 2026-10-20. The mod is **not installed** and is not a dependency of
Expedition. This note records the methods and concepts worth adapting; it contains no
native binaries, decompiled code, personal designs or player data.

## What to take over

### 1. Fresh player spawn / player picks

- `Battle.PreparePick`, `Battle.UnpickTeam`, `Battle.RestorePicks`
- `BattleFile.PickCapacity`, `BattleFile.Slots`, `BattleFile.WithPicks`, `BattleFile.CheckPicks`
- `NativeDesigner.BeginBattle` / native designer handoff

Concept: the player's locked design is selected in the normal designer and spawned as a
fresh battle unit at an authored Team 1 position. The editor's live vehicle is never the
combat vehicle. This is the architecture Expedition needs to keep the design intact and
avoid the post-loss turret damage.

### 2. Authored spawn positions before native physics

- `SpawnPlan.Apply` and `SpawnPlan.Starting` (Harmony prefix on `DeathmatchGameMode.InitiateInternal`)
- `SpawnPoints`, `SpawnTeamMapping.Resolve`, `SpawnTeamMapping.ReadLiveMap`
- `SpawnGround.Position/Height`, `SpawnClearance`, `SpawnSurface`

Concept: replace team spawn locators with authored points before the native spawner
constructs vehicles; never teleport live track physics. This is the route for a fresh
player + ally + enemy fleet at checked map positions.

### 3. Battle/encounter content format

- `BattleFile` (map, units, teams, paths, targets, rules, limits, reserves, free-for-all)
- `BattleUnit` (team, blueprint path, position, path, target, reserve, player pick)
- `PickLimits` (max tanks, budget, max cost, allowed eras)

Concept: a plain JSON battle/encounter catalogue with stable IDs, content paths and
versioning. Expedition can use the same shape for its 18 encounters, three choices per
round, map/spawn layout IDs and enemy rosters.

### 4. Mission rules and outcomes

- `Mission.Rule` conditions/actions: time, destroyed, wipedOut, teamEnters, holds
- actions: message, victory, defeat, artillery, reserves, drive-to
- `Mission.End` and the native mission hooks

Concept: native mission rules can express "enemy formation eliminated" and "friendly
force destroyed" without inventing a parallel simulation. Expedition still owns the
one-result-per-generation and reward policy.

### 5. AI order overrides and control

- `Hooks` (`CommanderAI.Order`, `CommanderAI.UpdateFormationTasks`)
- `Puppet`, `MovementHold`, `MovementHoldPolicy`
- command view / force control / take-control paths

Concept: ally orders, hold/advance/attack overrides and player control transfer can be
driven through the native commander/AI instead of a second AI. Useful for ally order
compliance and "play as a surviving ally".

### 6. Spawn safety and map capacity

- `SpawnSafety`, `SpawnClearance`, `SpawnGround`, `RandomSpawnPlan`, `RandomSpawnPoints`
- map capacity/spawn checks in `MainMenu`

Concept: checked spawn slots, ground clearance and team capacity before a battle starts.
Relevant to the release requirement for qualified maps and safe friendly/enemy spawns.

### 7. Economy reference only

- `Gauntlet`, `GauntletRules` (rounds, funds, repair/refit, upgrade charges)

Concept: a useful comparison for Expedition's budget/supply rules. Do **not** copy its
balance, progression or reward formulas; Expedition's approved 18-battle, three-choice,
fleet-budget and supply model remains authoritative.

## What we will not do

- Install Battle Editor or Map Framework as part of the native gate without a separate
  isolated compatibility test.
- Call its `internal` classes from our plugin; they are not a stable public API.
- Copy native assets, decompiled code or third-party content.
- Replace Expedition's approved rules with Gauntlet's rules.

If code is reused later, the MIT licence and copyright notice must be retained and the
adapted files must be reviewed by the coordinator.
