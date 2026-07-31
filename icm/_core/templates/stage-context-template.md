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
# {{STAGE_NAME}} Stage

## Inputs

| Source | File/Location | Section/Scope | Why |
| --- | --- | --- | --- |
| Router | `../../CONTEXT.md` | Task Routing | Confirm this is the correct stage |

## Process

1. Confirm the task belongs in this stage.
2. Load only listed inputs.
3. Complete the stage work.
4. Run the audit if present.
5. Save durable artifacts to `output/` when useful.

## Outputs

| Artifact | Location | Format |
| --- | --- | --- |
| Stage notes | `output/{{TASK_SLUG}}-{{STAGE_SLUG}}.md` | Markdown |
