# ActionStacksEX - AI Agent Guide

This file provides essential information for AI coding agents working on the ActionStacksEX project.

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

The project uses the Hypostasis framework for plugin architecture:

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
│   └── Structures/            - Game structure definitions
│       ├── ActionManager.cs   - Action manager structures
│       ├── Bool.cs            - Boolean type for interop
│       └── ...
└── ImGui/                     - ImGui helper utilities
```

### Modules

All feature modules are in the `Modules/` folder:

```
Modules/
├── ActionStacks.cs            - Core action stack validation
├── AutoCastCancel.cs          - Auto-cancel casting on target death
├── AutoDismount.cs            - Auto-dismount on action use
├── AutoFocusTarget.cs         - Auto-set focus target
├── AutoRefocusTarget.cs       - Restore focus target in duties
├── AutoTarget.cs              - Auto-target nearest enemy
├── CameraRelativeActions.cs   - Camera-relative directional actions
├── Decombos.cs                - Remove combo actions (Sundering)
├── EnhancedAutoFaceTarget.cs  - Enhanced auto-face behavior
├── FrameAlignment.cs          - Frame timing alignment
├── QueueAdjustments.cs        - Custom queue threshold system
├── QueueMore.cs               - Enable queuing for items/LBs
├── SpellAutoAttacks.cs        - Auto-attacks on spells
└── TurboHotbars.cs            - Turbo hotbar keybinds
```

## Key Design Patterns

### PluginModule System

Each feature is implemented as a `PluginModule` with:

```csharp
public class MyModule : PluginModule
{
    public override bool ShouldEnable => Config.EnableMyFeature;
    
    protected override bool Validate() 
        => SomeGameFunction.IsValid; // Check if signatures found
    
    protected override void Enable() { /* Hook activation */ }
    protected override void Disable() { /* Hook deactivation */ }
}
```

### Action Stacks System

Action stacks allow redirecting actions to different targets:

1. **Action**: The trigger action (e.g., Cure)
2. **Stack Item**: Target redirect rules with conditions (HP%, status, range, cooldown)
3. **Modifier Keys**: Optional keybinds to activate the stack

Stack evaluation order:
1. Match action against stack's action list
2. Check modifier keys
3. Iterate through stack items (top to bottom)
4. First valid target wins

### Custom Pronouns

Custom pronouns extend the game's target placeholder system:

```csharp
public class MyPronoun : IGamePronoun
{
    public string Name => "My Target <my>";
    public string Placeholder => "<my>";
    public uint ID => 10_000; // Must be >= 10000
    public unsafe GameObject* GetGameObject() { /* Logic */ }
}
```

Built-in pronouns include:
- Standard: `<t>`, `<me>`, `<f>`, `<mo>`, `<tt>`, `<pt>`
- HP-based: `<lowhpparty>`, `<lowhptank>`, `<lowhphealer>`, `<lowhpdps>`
- Distance: `<nearparty>`, `<farparty>`, `<nearemeny>`, `<faremeny>`
- Job-specific: `<pld>`, `<war>`, `<drk>`, `<gnb>`, `<whm>`, `<sch>`, `<ast>`, `<sge>`, etc.
- Special: `<dead>` (first dead party member without raise status)

### AsmPatch System

Assembly patches modify game code directly:

```csharp
public static readonly AsmPatch myPatch = new(
    "SignatureBytes ?? ?? ??",  // Pattern to find
    "90 90 90",                 // Replacement bytes (nop)
    startEnabled: false
);
```

Used patches:
- `queueGroundTargetsPatch`: Enable ground target queuing
- `spellAutoAttackPatch`: Enable auto-attacks on spells
- `waitSyntaxDecimalPatch`: Allow decimal wait times
- `queueACCommandPatch`: Enable /ac queueing in macros
- `allowUnassignableActionsPatch`: Allow normally unavailable actions

## Configuration System

### Data Structures

```csharp
public class ActionStack
{
    public string Name;
    public List<Action> Actions;        // Trigger actions
    public List<ActionStackItem> Items; // Target redirects
    public uint ModifierKeys;           // Shift=1, Ctrl=2, Alt=4, Exact=8
    public bool BlockOriginal;          // Block if stack fails
    public bool CheckRange;             // Fail if out of range
    public bool CheckCooldown;          // Fail if on cooldown
}

public class ActionStackItem
{
    public uint ID;              // Override action ID (0 = same)
    public uint TargetID;        // Pronoun ID for target
    public bool Enabled;
    public float HpRatio;        // Max HP% threshold
    public uint StatusID;        // Required status check
    public bool MissingStatus;   // Check for missing instead of present
}
```

### Export/Import

Stacks can be exported/imported as compressed JSON:
```
ASEX_H4sIAAAAAAAAC...
```

## Build System

### Requirements

- **Visual Studio 2022+** (with .NET 10 SDK support)
- **XIVLauncher** with Dalamud dev environment
- **Dalamud.NET.Sdk** 14.0.1

### Project Configuration

```xml
<Project Sdk="Dalamud.NET.Sdk/14.0.1">
    <PropertyGroup>
        <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
        <PlatformTarget>x64</PlatformTarget>
        <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    </PropertyGroup>
</Project>
```

### Dependencies

- **ECommons** (3.1.0.13): Shared utility library
- **Dalamud**: Core framework (via SDK)
- **FFXIVClientStructs**: Game structure definitions

### Post-Build

Build automatically copies to devPlugins:
```
%APPDATA%\XIVLauncher\devPlugins\ActionStacksEX\
```

## Code Style Guidelines

### Formatting

- **Indentation**: 4 spaces
- **Namespaces**: File-scoped (C# 10+)
- **Braces**: K&R style
- **unsafe**: Required for FFXIVClientStructs access

### Naming Conventions

```csharp
// Private fields: camelCase
private static bool isRequeuing;

// Public properties: PascalCase
public string Name => "ActionStacksEX";

// Constants: PascalCase or UPPER_CASE
private const uint MinimumCustomPronounID = 10_000;

// Config properties: Descriptive with feature prefix
public bool EnableAutoDismount { get; set; }
public bool EnableDecomboMeditation { get; set; }
```

### Nullability

- Nullable reference types enabled
- Unsafe pointers use `*` notation
- Null-check Dalamud services before use

### Error Handling

```csharp
try
{
    // Hook or game operation
}
catch (Exception e)
{
    DalamudApi.LogError($"Failed to do something\n{e}");
    return null; // or safe fallback
}
```

## Common Tasks

### Adding a New Module

1. Create `Modules/MyFeature.cs`:
```csharp
public class MyFeature : PluginModule
{
    public override bool ShouldEnable => ActionStacksEX.Config.EnableMyFeature;
    
    protected override bool Validate() => SomeGameFunction.IsValid;
    
    protected override void Enable()
    {
        // Create hooks or enable patches
    }
    
    protected override void Disable()
    {
        // Disable hooks/patches
    }
}
```

2. Add config property to `Configuration.cs`:
```csharp
public bool EnableMyFeature { get; set; } = false;
```

3. Add UI control to `PluginUI.cs` in `DrawOtherSettings()`:
```csharp
save |= ImGui.Checkbox("Enable My Feature", ref ActionStacksEX.Config.EnableMyFeature);
ImGuiEx.SetItemTooltip("Description of feature.");
```

### Adding a New Pronoun

1. Create class implementing `IGamePronoun`:
```csharp
public class MyPronoun : IGamePronoun
{
    public string Name => "My Target <my>";
    public string Placeholder => "<my>";
    public uint ID => 10_XXX; // Unique ID >= 10000
    public unsafe GameObject* GetGameObject() { /* Logic */ }
}
```

2. Add ID to `OrderedIDs` in `PronounManager.Initialize()` if needed.

### Adding a Game Hook

```csharp
private delegate ReturnType MyDelegate(PtrType* ptr, Args...);
[HypostasisSignatureInjection("Signature pattern", Required = true)]
[HypostasisClientStructsInjection(typeof(Type.MemberFunctionPointers))]
private static Hook<MyDelegate> MyHook;

private static ReturnType MyDetour(PtrType* ptr, Args...)
{
    // Custom logic
    return MyHook.Original(ptr, args...);
}
```

## Important Considerations

### Game Version Compatibility

- Dalamud API 14 is for FFXIV Patch 7.x (Dawntrail)
- Signature patterns may break between patches
- Always test after game updates

### Memory Safety

- FFXIVClientStructs uses unsafe pointers
- Always null-check before dereferencing
- Use `try/catch` around hook detours

### Performance

- Hooks are called frequently; keep detours efficient
- Use `DalamudApi.LogDebug()` sparingly in hot paths
- Cache Excel sheet lookups when possible

### Thread Safety

- Dalamud framework update is not thread-safe
- Use `DalamudApi.Framework.RunOnFrameworkThread()` for game state modifications

## File Reference

| File | Purpose | Lines |
|------|---------|-------|
| ActionStacksEX.cs | Plugin entry point | 88 |
| Configuration.cs | Settings & data structures | 143 |
| PluginUI.cs | ImGui configuration UI | 854 |
| ActionStackManager.cs | Action stacking logic | 262 |
| Game.cs | Game hooks & patches | 173 |
| PronounManager.cs | Custom pronoun system | 513 |
| Extensions.cs | Game object extensions | 66 |
| JobRole.cs | Job role enum | 37 |

## External Resources

- **Dalamud Docs**: https://dalamud.dev/
- **FFXIV Client Structs**: https://github.com/aers/FFXIVClientStructs
- **Lumina**: Game data extraction library
- **ECommons**: https://github.com/NightmareXIV/ECommons
