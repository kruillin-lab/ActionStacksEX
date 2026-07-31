---
tags:
  - type/context
  - project/actionstacksex
  - status/active
  - workflow/icm
type: context
project: actionstacksex
status: active
aliases: []
---
# CONTEXT.md - ActionStacksEX ICM Router

## Task Routing

| If task involves... | Go to stage |
| --- | --- |
| discovery, Understand the task, inspect the project, research, and identify constraints. | `stages/01-discovery/` |
| plan, Turn discovery into a focused implementation and verification plan. | `stages/02-plan/` |
| implement, Make scoped edits while following project rules. | `stages/03-implement/` |
| verify, Run builds, tests, lint, audits, screenshots, or manual checks. | `stages/04-verify/` |
| handoff, Summarize outcomes, update durable logs, and prepare commit or PR notes. | `stages/05-handoff/` |

## How to Start Any Task

1. Read this router.
2. Select the single best stage for the current task.
3. Open that stage's `CONTEXT.md`.
4. Load only the files named in the stage Inputs table.
5. Execute the process and write durable artifacts to `output/` when useful.

## Shared Resources

- Stable project facts: `_config/project-profile.md`
- Task policy: `_config/task-policy.md`
- Project index: `shared/project-index.md`
- Decisions: `shared/decisions.md`
