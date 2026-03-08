# ActionStacksEX - Project Summary

## Overview

**ActionStacksEX** is a Dalamud plugin for Final Fantasy XIV (FFXIV) that provides enhanced battle system quality-of-life features. It is an enhanced standalone version of ReActionEX, decoupled from ParseLord.

| Property | Value |
|----------|-------|
| **Author** | Maomi Gato |
| **Version** | 1.0.0.0 |
| **Target Game** | FFXIV Patch 7.x (Dawntrail) |
| **Framework** | .NET 10 / Dalamud API 14 |
| **Language** | C# 12 |
| **Category** | Jobs / Battle System |

## Description

An enhanced version of ReActionEX with more features, providing:
- Action stacking with conditional target redirection
- Custom pronoun/placeholder system for targeting
- Auto-dismount, auto-target, and auto-cancel features
- Queue adjustments and frame alignment
- Camera-relative actions
- Turbo hotbar functionality
- Spell auto-attacks

## Project Structure

```
ActionStacksEX/
├── ActionStacksEX.cs              # Main plugin entry point (91 lines)
├── ActionStacksEX.csproj          # Project file (.NET 10, Dalamud SDK)
├── ActionStacksEX.json            # Plugin manifest
├── Configuration.cs               # Settings & data structures (143 lines)
├── PluginUI.cs                    # ImGui configuration interface (854 lines)
├── ActionStackManager.cs          # Core action stacking logic (308 lines)
├── Game.cs                        # Game hooks and function pointers (173 lines)
├── PronounManager.cs              # Custom target pronoun system (513 lines)
├── Extensions.cs                  # Extension methods for game objects (66 lines)
├── JobRole.cs                     # Job role enumeration (37 lines)
├── AGENTS.md                      # AI agent guide (372 lines)
├── bin/                           # Build output
├── obj/                           # Object files
├── Hypostasis/                    # Plugin framework
│   ├── Hypostasis.cs              # Framework initialization
│   ├── PluginModule.cs            # Base class for plugin modules
│   ├── PluginModuleManager.cs     # Module lifecycle management
│   ├── Dalamud/                   # Dalamud service wrappers
│   └── Game/                      # Game structures and hooks
└── Modules/                       # Feature modules (14 files)
    ├── ActionStacks.cs            # Core action stack validation
    ├── AutoCastCancel.cs          # Auto-cancel casting on target death
    ├── AutoDismount.cs            # Auto-dismount on action use
    ├── AutoFocusTarget.cs         # Auto-set focus target
    ├── AutoRefocusTarget.cs       # Restore focus target in duties
    ├── AutoTarget.cs              # Auto-target nearest enemy
    ├── CameraRelativeActions.cs   # Camera-relative directional actions
    ├── Decombos.cs                # Remove combo actions
    ├── EnhancedAutoFaceTarget.cs  # Enhanced auto-face behavior
    ├── FrameAlignment.cs          # Frame timing alignment
    ├── QueueAdjustments.cs        # Custom queue threshold system
    ├── QueueMore.cs               # Enable queuing for items/LBs
    ├── SpellAutoAttacks.cs        # Auto-attacks on spells
    └── TurboHotbars.cs            # Turbo hotbar keybinds
```

## Key Features

### 1. Action Stacks System
Action stacks allow redirecting actions to different targets based on conditions:
- **Trigger Action**: The action that activates the stack (e.g., Cure)
- **Stack Items**: Target redirect rules with conditions (HP%, status, range, cooldown)
- **Modifier Keys**: Optional keybinds to activate the stack (Shift, Ctrl, Alt)
- Export/Import: Stacks can be shared as compressed JSON (`ASEX_...`)

### 2. Custom Pronouns
Extended target placeholder system:
- **Standard**: `<t>`, `<me>`, `<f>`, `<mo>`, `<tt>`, `<pt>`
- **HP-based**: `<lowhpparty>`, `<lowhptank>`, `<lowhphealer>`, `<lowhpdps>`
- **Distance**: `<nearparty>`, `<farparty>`, `<nearemeny>`, `<faremeny>`
- **Job-specific**: `<pld>`, `<war>`, `<drk>`, `<gnb>`, `<whm>`, `<sch>`, `<ast>`, `<sge>`, etc.
- **Special**: `<dead>` (first dead party member without raise status)

### 3. Module Features

| Module | Description |
|--------|-------------|
| AutoDismount | Automatically dismount when using actions |
| AutoCastCancel | Cancel casting when target dies |
| AutoTarget | Auto-target nearest enemy |
| AutoFocusTarget | Auto-set focus target |
| AutoRefocusTarget | Restore focus target in duties |
| CameraRelativeActions | Camera-relative directional abilities |
| Decombos | Remove combo actions (Sundering) |
| EnhancedAutoFaceTarget | Enhanced auto-face behavior |
| FrameAlignment | Frame timing alignment for inputs |
| QueueAdjustments | Custom queue threshold system |
| QueueMore | Enable queuing for items and Limit Breaks |
| SpellAutoAttacks | Enable auto-attacks during spell casting |
| TurboHotbars | Turbo hotbar keybinds for rapid casting |

## Configuration Options

```csharp
// Core Features
EnableEnhancedAutoFaceTarget    # Enhanced auto-face target
EnableAutoDismount              # Auto-dismount on action
EnableGroundTargetQueuing       # Queue ground-target abilities
EnableInstantGroundTarget       # Instant ground-target placement
EnableAutoCastCancel            # Cancel cast on target death
EnableAutoTarget                # Auto-target enemies
EnableSpellAutoAttacks          # Auto-attacks on spells
EnableCameraRelativeDashes      # Camera-relative dash abilities
EnableQueuingMore               # Queue items/LBs
EnableFrameAlignment            # Frame timing alignment
EnableAutoRefocusTarget         # Auto-refocus in duties
EnableMacroQueue                # /ac queueing in macros
EnableQueueAdjustments          # Custom queue thresholds
EnableRequeuing                 # Action requeuing
EnableTurboHotbars              # Turbo hotbar mode
EnableCameraRelativeDirectionals # Camera-relative directional actions

// Decombo Options
EnableDecomboMeditation         # Separate Meditation stacks
EnableDecomboBunshin            # Separate Bunshin
EnableDecomboWanderersMinuet    # Separate WM
EnableDecomboLiturgy            # Separate Liturgy
EnableDecomboEarthlyStar        # Separate Earthly Star
EnableDecomboMinorArcana        # Separate Minor Arcana
EnableDecomboGeirskogul         # Separate Geirskogul

// Threshold Settings
QueueThreshold                  # Queue window threshold (0.5 default)
QueueLockThreshold              # Queue lock threshold
QueueActionLockout              # Action lockout time
TurboHotbarInterval             # Turbo interval in ms (400 default)
```

## Build System

### Requirements
- Visual Studio 2022+ (with .NET 10 SDK)
- XIVLauncher with Dalamud dev environment
- Dalamud.NET.Sdk 14.0.1

### Dependencies
- **ECommons** (3.1.0.13): Shared utility library
- **Dalamud**: Core framework (via SDK)
- **FFXIVClientStructs**: Game structure definitions

### Post-Build
Build automatically copies to:
```
%APPDATA%\XIVLauncher\devPlugins\ActionStacksEX\
```

## Commands

| Command | Alias | Description |
|---------|-------|-------------|
| `/actionstacksex` | `/ax` | Opens/closes the config window |
| `/asmacroqueue` | `/asmqueue` | Toggle /ac queueing in macros (on/off) |

## Technical Details

### Architecture
- **Hypostasis Framework**: Plugin module system with lifecycle management
- **PluginModule Pattern**: Each feature is a module with Enable/Disable/Validate methods
- **AsmPatch System**: Assembly patches for game code modification
- **Signature Injection**: Game function hooks via pattern matching

### Code Style
- **Indentation**: 4 spaces
- **Namespaces**: File-scoped (C# 10+)
- **Braces**: K&R style
- **unsafe**: Required for FFXIVClientStructs access
- **Nullable**: Reference types enabled

## External Resources

- [Dalamud Documentation](https://dalamud.dev/)
- [FFXIV Client Structs](https://github.com/aers/FFXIVClientStructs)
- [ECommons](https://github.com/NightmareXIV/ECommons)
- Lumina: Game data extraction library

## File Statistics

| File | Lines | Purpose |
|------|-------|---------|
| ActionStacksEX.cs | 91 | Plugin entry point |
| Configuration.cs | 143 | Settings & data structures |
| PluginUI.cs | 854 | ImGui configuration UI |
| ActionStackManager.cs | 308 | Action stacking logic |
| Game.cs | 173 | Game hooks & patches |
| PronounManager.cs | 513 | Custom pronoun system |
| Extensions.cs | 66 | Game object extensions |
| JobRole.cs | 37 | Job role enum |
| AGENTS.md | 372 | AI agent guide |
| **Total** | **~2,500+** | **Core plugin files** |

---

*Generated: March 2026*
*Plugin Manifest: ActionStacksEX.json*
