---
tags:
  - type/instruction
  - project/actionstacksex
  - status/active
type: instruction
project: actionstacksex
status: active
aliases:
  - ActionStacksEX full agent guide (on demand)
---
# ActionStacksEX - AI Agent Guide

> Load when editing this repo. Slim pointer: `../AGENTS.md`.

## Project Overview

**ActionStacksEX** is a Dalamud plugin for Final Fantasy XIV (FFXIV) that provides enhanced battle system quality-of-life features. It is an enhanced standalone version of ReActionEX, decoupled from ParseLord.

- **Target Game**: FFXIV Patch 7.x (Dawntrail)
- **Framework**: .NET 10 / Dalamud API 14
- **Language**: C# 12
- **Version**: 1.0.0.0
- **Author**: Maomi Gato

## Architecture

### Core Components

```
ActionStacksEX.cs        - Main plugin entry point
Configuration.cs         - Plugin settings and data structures
PluginUI.cs              - ImGui configuration interface
ActionStackManager.cs    - Core action stacking logic
Game.cs                  - Game hooks and function pointers
PronounManager.cs        - Custom target pronoun/placeholder system
Extensions.cs            - Extension methods for game objects
JobRole.cs               - Job role enumeration
```

### Hypostasis Framework

```
Hypostasis/
├── Hypostasis.cs              - Framework initialization
├── PluginModule.cs            - Base class for plugin modules
├── PluginModuleManager.cs     - Module lifecycle management
├── Dalamud/
│   ├── DalamudApi.cs          - Dalamud service access
│   ├── DalamudPlugin.cs       - Plugin base class
│   ├── PluginCommandManager.cs - Command handling
│   └── PluginConfiguration.cs - Config base class
├── Game/
│   ├── AsmPatch.cs            - Assembly patching utilities
│   ├── Common.cs              - Shared game structures
│   ├── GameFunction.cs        - Game function handling
│   ├── VirtualFunction.cs     - Virtual function hooks
│   └── Structures/
└── ImGui/
```

### Modules (`Modules/`)

ActionStacks, AutoCastCancel, AutoDismount, AutoFocusTarget, AutoRefocusTarget, AutoTarget, CameraRelativeActions, Decombos, EnhancedAutoFaceTarget, ExtendedSlidecast, FrameAlignment, QueueAdjustments, QueueMore, SpellAutoAttacks, TurboHotbars.

## Key Design Patterns

### PluginModule System

```csharp
public class MyModule : PluginModule
{
    public override bool ShouldEnable => Config.EnableMyFeature;
    protected override bool Validate() => SomeGameFunction.IsValid;
    protected override void Enable() { }
    protected override void Disable() { }
}
```

### Action Stacks System

1. **Action**: trigger (e.g., Cure)
2. **Stack Item**: redirect rules (HP%, status, range, cooldown)
3. **Modifier Keys**: optional activation keys

Evaluation: match action → modifier keys → stack items top-to-bottom → first valid target wins.

### Custom Pronouns

`IGamePronoun` with `ID >= 10000`. Built-ins: `<t>`, `<me>`, `<f>`, HP/distance/job variants, `<dead>`.

### AsmPatch

Signature pattern + replacement bytes. Patches: queueGroundTargets, spellAutoAttack, waitSyntaxDecimal, queueACCommand, allowUnassignableActions.

## Configuration

`ActionStack`, `ActionStackItem` — see source `Configuration.cs`. Export prefix: `ASEX_H4sI...`

## Build

- VS 2022+, .NET 10, Dalamud.NET.Sdk 14.0.1, `net10.0-windows10.0.26100.0`, x64, unsafe
- `dotnet build ActionStacksEX.csproj -c Release`
- Output: `%APPDATA%\XIVLauncher\devPlugins\ActionStacksEX\`
- Deps: ECommons 3.1.0.13, FFXIVClientStructs

## Code Style

4 spaces, file-scoped namespaces, K&R braces, nullable enabled, `DalamudApi.LogError` in catch (never empty catch).

## Common Tasks

**New module:** `Modules/MyFeature.cs` → config in `Configuration.cs` → checkbox in `PluginUI.cs` `DrawOtherSettings()`.

**New pronoun:** implement `IGamePronoun`, register in `PronounManager.Initialize()` `OrderedIDs` if needed.

**New hook:** Hypostasis signature injection + detour calling `Original`.

## Safety / Performance

Signatures break on patches; null-check unsafe pointers; hooks must stay fast; `RunOnFrameworkThread()` for game mutations. `IObjectTable` / `ClientState.LocalPlayer` are framework-thread-only — never read them from `PluginModule.Enable()` (Hypostasis Toggle runs off-thread and `ToggleOrInvalidateModule` kills the module). Extended Slidecast cannot extend past server ActionEffect on live 7.56 — do not spoof `CastInfo.ResponseSpellId` or swallow `OnCastCancelled` as a lock.

## File Reference

| File | Purpose |
|------|---------|
| ActionStacksEX.cs | Entry |
| Configuration.cs | Settings |
| PluginUI.cs | ImGui UI |
| ActionStackManager.cs | Stack logic |
| Game.cs | Hooks |
| PronounManager.cs | Pronouns |

## External Resources

- https://dalamud.dev/
- https://github.com/aers/FFXIVClientStructs
- https://github.com/NightmareXIV/ECommons

## ICM

`icm/CONTEXT.md` + stage `CONTEXT.md` when applicable.
