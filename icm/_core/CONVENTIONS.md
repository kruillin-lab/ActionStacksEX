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
# ICM Conventions

## Layers

- Layer 0: project `AGENTS.md` and this ICM entry.
- Layer 1: `icm/CONTEXT.md` routes tasks.
- Layer 2: `icm/stages/*/CONTEXT.md` defines stage contracts.
- Layer 3: `_config/`, `shared/`, `references/`, and `skills/` hold durable context.
- Layer 4: `output/` holds working artifacts.

## Rules

- One stage, one job.
- Load minimal context for the selected stage.
- Keep durable rules out of `output/`.
- Keep run-specific work out of `_config/`.
- Do not move project source into `icm/` without an explicit migration plan.
- Preserve existing project instructions and build commands.
