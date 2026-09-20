---
tags:
  - type/doc
  - project/actionstacksex
  - status/active
  - workflow/icm
type: doc
project: actionstacksex
status: active
aliases: []
---
# Decisions

Record durable decisions here when they should survive the current task.

| Date | Decision | Reason | Source |
| --- | --- | --- | --- |
| 2026-09-20 | Extended slidecast uses `CastInfo.Interruptible` + `ActionManager.OnCastCancelled` + `GameMain.ExecuteCommand(105)`, not a patched 0.5f compare and not ReAction queueing | CS documents ActionEffect (`ResponseSpellId`) as the stock slidecast lock; OnCastCancelled is the move-interrupt path; OmenTools CancelCast is command 105. Dead/invalid targets are never protected so AutoCastCancel still fires. | Luis Rivera AX feature request |
| 2026-09-20 | PR #3 no-op at 2.5s: movement cancel is on `GameObject.SetPosition` / `PositionModified` / `BattleChara.Update`, not OnCastCancelled; dead-target gate uses `IsDead()` not `CanUseActionOnGameObject` | Luis confirmed slider was maxed. OnCastCancelled is cleanup; CanUseActionOnGameObject fails on range/facing as soon as you move and would drop protection. Interruptible is applied on the CS position-write path before the native cancel check. | Luis Rivera AX live report |
| 2026-09-20 | ExtendedSlidecast.Enable must not read ObjectTable.LocalPlayer; Hypostasis Toggle is off-thread and invalidates the module | Luis 01:10:37 `Not on main thread!` at CacheLocalPlayer → ToggleOrInvalidateModule. Zero `/xllog` ExtendedSlidecast lines. Resolve player via CS `Control.GetLocalPlayer()`, cache on Framework.Update, arm move hooks even when LocalPlayer is null. | Luis Rivera AX dalamud.log |
