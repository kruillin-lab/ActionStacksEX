# Spatial Intelligence Engine — Design Specification

**Author:** kruil  
**Date:** 2026-03-20  
**Status:** Draft  
**Related:** ActionStacksEX, ParseLord3

---

## 1. Overview

The Spatial Intelligence Engine extends ActionStacksEX with two geometric targeting capabilities:

1. **`optimalaoe`** — A new pronoun that selects the enemy maximizing AoE hit count using shape intersection math
2. **Party Centroid Ground Targeting** — Auto-intercept of all ground-target abilities to redirect them to the geometric center of the alive party, with always-on visual preview

Additionally, the engine exposes IPC methods so **ParseLord3** can consume these targeting services for AoE and ground-target abilities in its rotation automation.

---

## 2. `<optimalaoe>` Pronoun

### 2.1 Concept

A new `IGamePronoun` that evaluates every hostile target in range and returns the one that maximizes the hit count of the triggering AoE ability. It is purely a targeting decision — no action substitution.

### 2.2 Geometry Engine

#### Action Shape Detection

Read from the Lumina `Action` sheet for the trigger action:

| CastType | Shape | Examples |
|----------|-------|---------|
| 2 | Circle | Holy, Flash, Space Ripper |
| 3 | Cone | Dragoon burst, Unmend (tank) |
| 4 | Line | Dragoon jumps, Salted Earth |

Also read `EffectRange` (radius) from the same row.

#### Circle Hit Testing

For each hostile `BattleChara` in range:
1. Compute distance from player to enemy
2. If distance ≤ radius: enemy is "covered"
3. Count total covered enemies for each enemy as the center
4. Select enemy with the highest count
5. **Tiebreaker**: nearest to player by Euclidean XZ distance

#### Line / Cone Hit Testing

For directional shapes:
1. Get player's current facing vector (normalized XZ)
2. For line: construct a ray from player position along facing direction
3. For cone: construct a cone sector with angle derived from `EffectRange` (arc width)
4. Test each enemy against the shape using dot-product angle checks (cone) or ray-distance checks (line)
5. Select enemy with most intersections
6. **Tiebreaker**: nearest to player

### 2.3 Enemy Filtering

All hostile `BattleChara` objects are considered — no boss/minion filter. The player makes the tactical decision about what constitutes a valid target.

### 2.4 Integration Points

- **Pronoun class**: `OptimalAoEPronoun : IGamePronoun`
- **Placeholder**: `<optimalaoe>`
- **Pronoun ID**: 11_010
- **Registered in**: `PronounManager.Initialize()`
- **Config toggle**: `Configuration.EnableOptimalAoE` (default: false)

### 2.5 Performance

- Geometry math runs on a throttled cache, updated once per framework tick (not per call)
- Only recomputes when entity positions change or action changes
- O(n) enemy scan per call; n is capped at 50 nearby enemies for safety

---

## 3. Party Centroid Ground Target Interception

### 3.1 Concept

When `EnablePartyCentroidGroundTargeting` is true, any ground-target ability (detected via `Action.TargetArea == true`) is automatically redirected to the geometric centroid of all alive party members. No stack or pronoun setup required.

### 3.2 Centroid Calculation

```
Centroid = (Σ party_member_x) / count,  player_y, (Σ party_member_z) / count
```

Only alive party members are included (dead and out-of-range excluded).

### 3.3 Hook Point

In `ActionStackManager.OnUseAction`, after the action is determined to be a ground-target ability:

1. Compute party centroid via `SpatialMath.GetPartyCentroid()`
2. Check if user has explicitly clicked a ground target — if so, skip interception
3. Replace the target coordinates with centroid coordinates before the action executes

### 3.4 Always-On Visualization

- A faint ring (ImGui drawlist, projected via `WorldToScreen`) appears at the centroid position
- Color: translucent green (`0x40FFFFFF`)
- Radius: matches the action's `EffectRange` circle
- The overlay updates every frame (position only, no per-frame allocation)

### 3.5 Integration Points

- **Config toggle**: `Configuration.EnablePartyCentroidGroundTargeting` (default: false)
- **Hook**: `ActionStackManager.OnUseAction` — detects `TargetArea == true`, replaces coordinates
- **Module**: `Modules.SpatialIntelligence` — handles caching, centroid calc, and visualization
- **Visual**: `ImGuiFacade.WorldToScreen` projection into ImGui background drawlist

### 3.6 Priority Rules

1. If user explicitly clicked a ground target on the map → respect it, no redirect
2. If `EnablePartyCentroidGroundTargeting` is false → no redirect
3. Otherwise → redirect to party centroid

---

## 4. ParseLord3 IPC Integration

### 4.1 IPC Provider (ActionStacksEX side)

ActionStacksEX exposes a single IPC provider under prefix `ActionStacksEX`:

| Method | Returns | Description |
|--------|---------|-------------|
| `GetOptimalAoETarget(uint actionId)` | `uint` (GameObject ID) | Returns the optimal AoE target for the given action |
| `GetPartyCentroid()` | `(float x, float y, float z)` | Returns the current party centroid position |
| `IsEnabled()` | `bool` | Returns true if Spatial Intelligence is active |

### 4.2 IPC Subscriber (ParseLord3 side)

ParseLord3 adds an `ActionStacksEX_IPCSubscriber` class that:
1. Checks if ActionStacksEX is loaded via `IPCSubscriber_Common.IsReady("ActionStacksEX")`
2. Subscribes to the `ActionStacksEX` IPC prefix via EzIPC
3. On AoE ability decision: calls `GetOptimalAoETarget(actionId)` and uses result as target
4. On ground ability: calls `GetPartyCentroid()` and uses result as placement coordinates

### 4.3 Fallback

If ActionStacksEX is not loaded, ParseLord3 falls back to its existing targeting behavior.

---

## 5. New Files

### `Modules/SpatialIntelligence.cs`
- `PluginModule` subclass
- `ShouldEnable` → `Config.EnableOptimalAoE || Config.EnablePartyCentroidGroundTargeting`
- `Validate()` → game spatial functions available
- `Enable()` → subscribe to action events, start render loop
- `Disable()` → unsubscribe, stop render loop
- Houses entity cache and throttled update logic

### `SpatialMath.cs`
Pure static math library:
- `GetEnemyHitCount(GameObject* enemy, ActionShape shape, float radius)` → `int`
- `FindBestAoETarget(uint actionId)` → `GameObject*`
- `GetPartyCentroid()` → `Vector3`
- `GetPlayerFacing()` → `Vector2` (normalized XZ)
- `IsPointInCone(Vector3 point, Vector3 origin, Vector2 direction, float arcRad, float range)` → `bool`
- `IsPointInLine(Vector3 point, Vector3 origin, Vector2 direction, float range, float halfWidth)` → `bool`

### `PronounManager.cs` (modified)
- Add `OptimalAoEPronoun` class (ID 11_010, placeholder `<optimalaoe>`)
- Add ID 11_010 to `OrderedIDs`

### `Configuration.cs` (modified)
- `EnableOptimalAoE { get; set; } = false`
- `EnablePartyCentroidGroundTargeting { get; set; } = false`

### `PluginUI.cs` (modified)
- New "Spatial Intelligence" section in the UI
- Two toggle checkboxes with tooltips

### `ActionStackManager.cs` (modified)
- Ground-target centroid interception in `OnUseAction`
- Respect explicit ground target clicks

---

## 6. Configuration

### Spatial Intelligence Section (PluginUI)

```
┌─ Spatial Intelligence ──────────────────────────────┐
│                                                     │
│  [x] Enable Optimal AoE Targeting                   │
│      Target enemy that maximizes AoE hit count       │
│      Use in stacks: <optimalaoe>                    │
│                                                     │
│  [x] Party Centroid Ground Targeting                │
│      Auto-redirect ground abilities to party center  │
│      Shows always-on preview ring                    │
│                                                     │
└─────────────────────────────────────────────────────┘
```

---

## 7. Technical Notes

- All geometry uses XZ plane only (Y is ignored for targeting in FFXIV)
- FFXIV Y-axis is vertical; XZ is the ground plane
- `Vector3.Distance` in FFXIV world space uses Yalms as units (same as game)
- `WorldToScreen` projection uses ImGui drawlist background callback, called per-frame
- Entity cache in `SpatialIntelligence` invalidates on zone change or party change

---

## 8. Out of Scope

- Boss/minion filtering for `<optimalaoe>` (all hostiles included)
- Modifier-key only visualization (always-on confirmed)
- ParseLord3-side AoE target selection beyond the IPC call (PL3 owns its rotation logic)
- Action shape data caching (Lumina sheet read is fast enough at startup)
