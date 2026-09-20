# CLAUDE.md — ActionStacksEX

FFXIV Dalamud plugin. Enhanced ReAction-style action-stack / battle QoL, decoupled from ParseLord. Author string: "Maomi Gato".

**This is the canonical variant.** On 2026-08-11 it absorbed both sibling forks ("Merge OMP and PA feature sets into the original"). See the warning below before touching ActionStacksOMP or ActionStacksPA.

## Build

```bash
dotnet build ActionStacksEX.csproj -c Release
```

PostBuild target `CopyToDevPlugins` auto-copies the DLL, `ECommons.dll`, and the manifest to `%APPDATA%\XIVLauncher\devPlugins\ActionStacksEX\`.

## Layout

| Path | What |
|---|---|
| `Modules/` | Feature modules (15) — the bulk of the plugin |
| `Hypostasis/` | Vendored framework layer (`Hypostasis/Dalamud/DalamudPlugin.cs` is the plugin base) — don't edit |
| `ActionStacksEX.json` | Dalamud manifest |
| `docs/` | Design notes |

Commands: `/actionstacksex`, `/ax`, `/asmqueue`, `/asmacroqueue`.

## Sibling forks — read before cross-referencing

Three folders exist: `ActionStacksEX`, `ActionStacksOMP`, `ActionStacksPA`. **All three build `ActionStacksEX.csproj` producing `ActionStacksEX.dll`** — only the manifest `Name` and the devPlugins output folder differ. Identical READMEs, so the folder name is the only reliable label.

As of 2026-08-11 EX is ahead of both and contains their features. Treat OMP and PA as superseded unless told otherwise.

## Interference with ParseLord5

This plugin's `PrepareAction` IPC can apply buffs that change [ParseLord5]'s heal thresholds — a documented case had "SCH Tank CD" spam holding Excogitation on a tank, dropping the ST heal threshold from 70 to 60 HP% and presenting as "heals late". If someone is debugging ParseLord5 heal timing, this plugin is a suspect.
