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
