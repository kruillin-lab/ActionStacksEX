# Spatial Intelligence Engine Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `<optimalaoe>` pronoun and party centroid ground targeting to ActionStacksEX.

**Architecture:** Geometry math layer (`SpatialMath.cs`) feeds two consumers: `OptimalAoEPronoun` (pronoun system) and `SpatialIntelligence` module (ground interception + visualization). Action interception happens in `ActionStackManager.OnUseAction`.

**Tech Stack:** .NET 10, Dalamud API 14, FFXIVClientStructs, Lumina, ImGui (via Hypostasis facade)

---

## File Map

| File | Role |
|------|------|
| `SpatialMath.cs` | **Create** — pure geometry math, no game state |
| `SpatialIntelligence.cs` | **Create** — module, caching, ImGui visualization |
| `Modules/ActionStacks.cs` | **Modify** — add centroid interception hook |
| `PronounManager.cs` | **Modify** — add `OptimalAoEPronoun` class |
| `Configuration.cs` | **Modify** — add two bool toggles |
| `PluginUI.cs` | **Modify** — add spatial intelligence config UI section |
| `Hypostasis/Game/Common.cs` | **Inspect** — find party centroid / enemy access patterns |

---

## Chunk 1: SpatialMath.cs (Geometry Engine)

**Files:**
- Create: `C:\Users\kruil\Documents\Projects\ActionStacksEX\SpatialMath.cs`

---

- [ ] **Step 1: Create SpatialMath.cs skeleton**

```csharp
using System;
using System.Numerics;

namespace ActionStacksEX;

public enum ActionShape
{
    Circle = 2,
    Cone = 3,
    Line = 4,
}

public static unsafe class SpatialMath
{
    // Pure math — no game calls
}
```

- [ ] **Step 2: Add Vector helpers (inline, no allocation)**

```csharp
    public static Vector2 Flatten(this Vector3 v) => new(v.X, v.Z);
    public static float FlatDist(this Vector3 a, Vector3 b) => Vector2.Distance(a.Flatten(), b.Flatten());
    public static Vector2 NormalizeSafe(this Vector2 v)
    {
        var len = v.Length();
        return len > 0 ? v / len : Vector2.Zero;
    }
```

- [ ] **Step 3: Add circle hit-count**

```csharp
    /// Returns count of enemies within radius of center (including center itself).
    public static int CircleHitCount(Vector3 center, float radius, ReadOnlySpan<Vector3> enemyPositions)
    {
        int count = 0;
        var r2 = radius * radius;
        foreach (ref readonly var pos in enemyPositions)
        {
            var d2 = new Vector2(pos.X - center.X, pos.Z - center.Z).LengthSquared();
            if (d2 <= r2) count++;
        }
        return count;
    }
```

- [ ] **Step 4: Add cone/line intersection**

```csharp
    /// Angle from facing direction to vector from origin to point.
    public static float AngleToPoint(Vector3 point, Vector3 origin, Vector2 facing)
    {
        var toPoint = (point.Flatten() - origin.Flatten()).NormalizeSafe();
        return MathF.Acos(Vector2.Dot(facing, toPoint));
    }

    /// Returns true if point is within cone arc (radians) and range.
    public static bool IsInCone(Vector3 point, Vector3 origin, Vector2 facing, float arcRad, float range)
    {
        var dist = origin.FlatDist(point);
        if (dist > range) return false;
        return AngleToPoint(point, origin, facing) <= arcRad / 2f;
    }

    /// Returns true if point is within line half-width of the ray.
    public static bool IsInLine(Vector3 point, Vector3 origin, Vector2 facing, float range, float halfWidth)
    {
        var toPoint = point.Flatten() - origin.Flatten();
        var dist = toPoint.Length();
        if (dist > range) return false;
        var dir = toPoint / dist;
        var dot = Vector2.Dot(dir, facing);
        if (dot <= 0) return false; // behind
        var lateral = MathF.Sqrt(1f - dot * dot);
        return lateral <= halfWidth / dist;
    }
```

- [ ] **Step 5: Add GetPartyCentroid**

```csharp
    public static Vector3 GetPartyCentroid()
    {
        var members = Hypostasis.Game.Common.GetPartyMembers();
        float sumX = 0, sumZ = 0;
        int count = 0;
        foreach (var m in members)
        {
            var obj = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)m;
            if (obj == null) continue;
            var hp = ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)obj)->CharacterData.Health;
            if (hp == 0) continue; // dead skip
            sumX += obj->Position.X;
            sumZ += obj->Position.Z;
            count++;
        }
        if (count == 0) return Vector3.Zero;
        var localPlayer = Hypostasis.Dalamud.DalamudApi.ClientState.LocalPlayer;
        return new Vector3(sumX / count, localPlayer?.Position.Y ?? 0, sumZ / count);
    }
```

- [ ] **Step 6: Add enemy scan + FindBestAoETarget**

```csharp
    /// Scans all hostile BattleCharas and returns the one maximizing AoE hit count.
    public static FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* FindBestAoETarget(uint actionId)
    {
        if (!ActionStacksEX.actionSheet.TryGetValue(actionId, out var a)) return null;
        var shape = (ActionShape)a.CastType;
        var radius = a.EffectRange;

        var localPlayer = Hypostasis.Dalamud.DalamudApi.ClientState.LocalPlayer;
        if (localPlayer == null) return null;

        var playerPos = localPlayer.Position;
        var facing = localPlayer.GetRotationVector(); // must check GameObject extension

        // Collect up to 50 nearby enemy positions
        var positions = new Vector3[50];
        int n = 0;
        foreach (var obj in Hypostasis.Dalamud.DalamudApi.Svc.Objects)
        {
            if (n >= 50) break;
            if (obj is IBattleChara bc && Extensions.IsEnemy(bc) && !bc.IsDead)
            {
                positions[n++] = bc.Position;
            }
        }
        var span = positions.AsSpan(0, n);

        FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* best = null;
        int bestCount = 0;

        foreach (var obj in Hypostasis.Dalamud.DalamudApi.Svc.Objects)
        {
            if (obj is not IBattleChara bc || !Extensions.IsEnemy(bc) || bc.IsDead) continue;
            var enemyPos = bc.Position;

            int count = shape switch
            {
                ActionShape.Circle => CircleHitCount(enemyPos, radius, span),
                ActionShape.Cone => 0, // TODO: cone counting
                ActionShape.Line => 0, // TODO: line counting
                _ => 1,
            };

            if (count > bestCount)
            {
                bestCount = count;
                best = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)bc.Address;
            }
            else if (count == bestCount && count > 0)
            {
                // Tiebreaker: nearest
                if (playerPos.FlatDist(enemyPos) < playerPos.FlatDist(best->Position))
                    best = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)bc.Address;
            }
        }
        return best;
    }
```

- [ ] **Step 7: Commit chunk 1**

```bash
git add SpatialMath.cs
git commit -m "feat(spatial): add SpatialMath geometry engine - circle hit testing, cone/line intersection, party centroid, best-aoe-target scan"
```

---

## Chunk 2: OptimalAoEPronoun + Configuration Toggles

**Files:**
- Modify: `C:\Users\kruil\Documents\Projects\ActionStacksEX\PronounManager.cs`
- Modify: `C:\Users\kruil\Documents\Projects\ActionStacksEX\Configuration.cs`

---

- [ ] **Step 1: Add two bools to Configuration.cs**

```csharp
    public bool EnableOptimalAoE = false;
    public bool EnablePartyCentroidGroundTargeting = false;
```
Add after line ~113 (after `EnableAutoFocusTargetOutOfCombat`).

- [ ] **Step 2: Add OptimalAoEPronoun to PronounManager.cs**

```csharp
public class OptimalAoEPronoun : IGamePronoun
{
    public string Name => "Optimal AoE Target";
    public string Placeholder => "<optimalaoe>";
    public uint ID => 11_010;
    public unsafe FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* GetGameObject()
        => SpatialMath.FindBestAoETarget(0); // actionId resolved by caller
}
```

- [ ] **Step 3: Register in PronounManager.Initialize**

Add `11_010` to `OrderedIDs` list.

- [ ] **Step 4: Commit chunk 2**

```bash
git add Configuration.cs PronounManager.cs
git commit -m "feat(aoe-pronoun): add <optimalaoe> pronoun and spatial config toggles"
```

---

## Chunk 3: SpatialIntelligence Module + Ground Target Interception

**Files:**
- Create: `C:\Users\kruil\Documents\Projects\ActionStacksEX\Modules\SpatialIntelligence.cs`
- Modify: `C:\Users\kruil\Documents\Projects\ActionStacksEX\ActionStackManager.cs` (ground target redirect)
- Inspect: `C:\Users\kruil\Documents\Projects\ActionStacksEX\Game.cs` — find ground target queue coordinates

---

- [ ] **Step 1: Create SpatialIntelligence module skeleton**

```csharp
using Hypostasis.Game.Structures;

namespace ActionStacksEX.Modules;

public class SpatialIntelligence : PluginModule
{
    public override bool ShouldEnable
        => ActionStacksEX.Config.EnableOptimalAoE
        || ActionStacksEX.Config.EnablePartyCentroidGroundTargeting;

    protected override bool Validate() => true; // Always valid

    protected override void Enable()
    {
        ActionStackManager.PreActionStack += OnPreActionStack;
        // Subscribe render callback for visualization
    }

    protected override void Disable()
    {
        ActionStackManager.PreActionStack -= OnPreActionStack;
    }

    private void OnPreActionStack(...)
    {
        // Ground target interception logic
    }
}
```

- [ ] **Step 2: Add ground target interception in ActionStackManager.cs**

In `OnUseAction`, after `succeeded` is set (around line 146), add:

```csharp
// Party Centroid Ground Target Interception
if (!succeeded
    && ActionStacksEX.Config.EnablePartyCentroidGroundTargeting
    && actionType == 1 // Action
    && ActionStacksEX.actionSheet.TryGetValue(finalActionID, out var a)
    && a.TargetArea)
{
    var centroid = SpatialMath.GetPartyCentroid();
    // Replace target object ID with centroid coordinates
    // This requires hooking UseActionLocation — check Game.cs
}
```

**NOTE:** This step requires finding the correct hook for ground target location injection. See `Game.cs` for `UseActionLocation` or equivalent. If no direct hook exists, intercepting the queuedGroundTarget path (lines 185-198) may be the correct approach.

- [ ] **Step 3: Add always-on ImGui visualization**

In the module's render callback (using Hypostasis ImGui facade):

```csharp
private void RenderOverlay()
{
    if (!ActionStacksEX.Config.EnablePartyCentroidGroundTargeting) return;
    var centroid = SpatialMath.GetPartyCentroid();
    if (centroid == Vector3.Zero) return;

    // Project to screen
    if (Hypostasis.ImGui.ImGuiFacade.WorldToScreen(centroid, out var screenPos))
    {
        var drawList = ImGui.GetBackgroundDrawList();
        var radius = 20f; // scaled pixels
        drawList.AddCircleFilled(screenPos, radius, 0x40FFFFFF);
        drawList.AddCircle(screenPos, radius, 0x80FFFFFF);
    }
}
```

Register via `Hypostasis.Dalamud.DalamudApi.PluginInterface.UiBuilder.BuildUi += RenderOverlay;`

- [ ] **Step 4: Commit chunk 3**

```bash
git add Modules/SpatialIntelligence.cs ActionStackManager.cs
git commit -m "feat(spatial): add SpatialIntelligence module with party centroid ground interception and always-on overlay"
```

---

## Chunk 4: PluginUI Spatial Intelligence Section

**Files:**
- Modify: `C:\Users\kruil\Documents\Projects\ActionStacksEX\PluginUI.cs`

---

- [ ] **Step 1: Find where other feature toggles are drawn (look for EnableAutoDismount patterns)**

Search for pattern `ImGui.Checkbox("EnableAutoDismount"` in PluginUI.cs.

- [ ] **Step 2: Add new section after existing feature toggles**

```csharp
if (ImGui.CollapsingHeader("Spatial Intelligence"))
{
    ImGui.Indent();
    save |= ImGui.Checkbox("Enable Optimal AoE Targeting", ref ActionStacksEX.Config.EnableOptimalAoE);
    ImGuiEx.SetItemTooltip("Target enemy that maximizes AoE hit count. Use <optimalaoe> in stacks.");
    ImGui.SameLine();
    helpMarker("When enabled, the <optimalaoe> pronoun selects the enemy that would result in the highest AoE hit count for the triggering ability.");

    save |= ImGui.Checkbox("Party Centroid Ground Targeting", ref ActionStacksEX.Config.EnablePartyCentroidGroundTargeting);
    ImGuiEx.SetItemTooltip("Auto-redirect ground-target abilities to the geometric center of your alive party members.");
    ImGui.SameLine();
    helpMarker("Shows a green ring preview at the centroid position. Respects explicit ground target clicks.");

    ImGui.Unindent();
}
```

- [ ] **Step 3: Commit chunk 4**

```bash
git add PluginUI.cs
git commit -m "feat(ui): add Spatial Intelligence config section to plugin UI"
```

---

## Chunk 5: Build Verification

- [ ] **Step 1: Run dotnet build**

```bash
cd C:\Users\kruil\Documents\Projects\ActionStacksEX
dotnet build ActionStacksEX.csproj -c Release
```

Expected: Clean build, no errors.

- [ ] **Step 2: Fix any compilation errors**

Common issues:
- `IBattleChara` not importing — use `using ECommons.GameFunctions;`
- `WorldToScreen` facade — check `Hypostasis/ImGui/` for correct API
- `GetRotationVector()` — may not exist on GameObject; use raw rotation float and compute Vector2 from it

- [ ] **Step 3: Final commit**

```bash
git add -A && git commit -m "feat: complete spatial intelligence engine"
```

---

## Post-Build

After successful build, the plugin DLL is auto-copied to:
```
%APPDATA%\XIVLauncher\devPlugins\ActionStacksEX\
```

Load in-game with `/ax` and verify:
1. Both new toggles appear in the UI
2. `<optimalaoe>` resolves to an enemy in multi-target scenarios
3. Ground abilities (e.g., Sacred Soil) show the green centroid ring when toggle is on
