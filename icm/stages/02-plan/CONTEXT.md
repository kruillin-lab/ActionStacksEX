---
tags:
  - type/context
  - project/actionstacksex
  - status/active
  - workflow/icm
  - stage/plan
type: context
project: actionstacksex
status: active
aliases: []
---
# Plan Stage

Turn discovery into a focused implementation and verification plan.

## Inputs

| Source | File/Location | Section/Scope | Why |
| --- | --- | --- | --- |
| Router | `../../CONTEXT.md` | Task Routing | Confirm routing |
| Project profile | `../../_config/project-profile.md` | Full file | Stable project facts |
| Task policy | `../../_config/task-policy.md` | Full file | Operating constraints |

## Process

1. Confirm the user request belongs in this stage.
2. Load only the listed inputs plus files required by the user request.
3. Perform the stage work.
4. Capture durable findings in `output/` when useful.

## Outputs

| Artifact | Location | Format |
| --- | --- | --- |
| Stage notes | `output/[task-slug]-02-plan.md` | Markdown |

## Audit

| Check | Pass Condition |
| --- | --- |
| Scope | Work stayed within this stage's job |
| Context | Only relevant references and artifacts were loaded |
