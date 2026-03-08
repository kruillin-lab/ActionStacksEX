# ActionStacksEX

[![Dalamud API](https://img.shields.io/badge/Dalamud%20API-14-blue)](https://dalamud.dev/)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

**Enhanced battle system QoL for Final Fantasy XIV**

ActionStacksEX is a Dalamud plugin that enhances the FFXIV battle system with advanced action stacking, custom targeting pronouns, and quality-of-life features. It's an enhanced standalone version of ReActionEX, decoupled from ParseLord.

## ✨ Features

### 🎯 Action Stacks
Redirect actions to different targets based on customizable conditions:
- **Trigger Actions** - Define which abilities activate a stack
- **Stack Items** - Set up target redirection rules with conditions (HP%, status, range, cooldown)
- **Modifier Keys** - Use Shift, Ctrl, or Alt to activate specific stacks
- **Export/Import** - Share your stacks as compressed JSON (`ASEX_...`)

### 🏷️ Custom Pronouns
Extended target placeholder system beyond the game's defaults:

| Type | Examples |
|------|----------|
| **Standard** | `<t>`, `<me>`, `<f>`, `<mo>`, `<tt>`, `<pt>` |
| **HP-based** | `<lowhpparty>`, `<lowhptank>`, `<lowhphealer>`, `<lowhpdps>` |
| **Distance** | `<nearparty>`, `<farparty>`, `<nearenemy>`, `<faremeny>` |
| **Job-specific** | `<pld>`, `<war>`, `<drk>`, `<gnb>`, `<whm>`, `<sch>`, `<ast>`, `<sge>`, etc. |
| **Special** | `<dead>` - First dead party member without raise status |

### 🔧 Quality of Life Modules

| Feature | Description |
|---------|-------------|
| **AutoDismount** | Automatically dismount when using actions |
| **AutoCastCancel** | Cancel casting when target dies |
| **AutoTarget** | Auto-target nearest enemy |
| **AutoFocusTarget** | Auto-set focus target |
| **CameraRelativeActions** | Camera-relative directional abilities |
| **Decombos** | Remove combo actions (Sundering style) |
| **QueueAdjustments** | Custom queue threshold system |
| **QueueMore** | Enable queuing for items and Limit Breaks |
| **SpellAutoAttacks** | Enable auto-attacks during spell casting |
| **TurboHotbars** | Turbo hotbar keybinds for rapid casting |

## 📦 Installation

### Requirements
- Final Fantasy XIV with Dalamud installed
- XIVLauncher

### Dev Build (Current)
1. Install the Dalamud dev hooks (XIVLauncher → Settings → Dalamud → Dev)
2. Clone this repository
3. Build in **Release | x64**
4. The post-build target automatically copies to `%APPDATA%\XIVLauncher\devPlugins\ActionStacksEX\`
5. Add the dev plugin directory to Dalamud (`/xlsettings → Experimental → Dev Plugins`)

### From Plugin Repository (When Available)
Search for "ActionStacksEX" in the Dalamud plugin installer.

## 🎮 Usage

### Commands

| Command | Alias | Description |
|---------|-------|-------------|
| `/actionstacksex` | `/ax` | Opens/closes the config window |
| `/asmacroqueue` | `/asmqueue` | Toggle `/ac` queueing in macros (on/off) |

### Setting Up Action Stacks

1. Open the config window (`/ax`)
2. Click "New Stack"
3. Add trigger actions (the abilities that will be redirected)
4. Add stack items with target conditions:
   - Set HP threshold (e.g., `< 50%`)
   - Add status requirements
   - Choose target pronoun
5. Optional: Set modifier keys for conditional activation
6. Test in-game!

### Example Use Cases

**Healer Stack:**
- **Trigger:** Cure
- **Condition:** Target lowest HP party member under 50%
- **Pronoun:** `<lowhpparty>`

**Tank Stack:**
- **Trigger:** Provoke
- **Condition:** Target highest aggro enemy not targeting you
- **Pronoun:** `<tt>` (target of target)

## 🛠️ Building from Source

### Requirements
- Visual Studio 2022+ (or `dotnet` CLI)
- .NET 10 SDK
- Dalamud.NET.Sdk 14.0.1

### Build
```bash
dotnet build ActionStacksEX.csproj -c Release
```

The build automatically copies to your devPlugins folder.

## 🏗️ Architecture

### Plugin Structure
```
ActionStacksEX/
├── ActionStacksEX.cs          # Main plugin entry
├── ActionStackManager.cs      # Core action stacking logic
├── Configuration.cs           # Settings & data structures
├── PluginUI.cs                # ImGui configuration UI
├── Game.cs                    # Game hooks & patches
├── PronounManager.cs          # Custom pronoun system
├── Hypostasis/                # Plugin framework
└── Modules/                   # Feature modules (14 files)
```

### Module System
Each feature is implemented as a `PluginModule` with:
- `ShouldEnable` - Condition to enable the module
- `Validate()` - Check if game signatures are found
- `Enable()` - Activate hooks/patches
- `Disable()` - Deactivate hooks/patches

## 📋 Configuration

All settings available in the ImGui config window (`/ax`):

### Core Features
- EnableEnhancedAutoFaceTarget
- EnableAutoDismount
- EnableGroundTargetQueuing
- EnableAutoCastCancel
- EnableAutoTarget
- EnableSpellAutoAttacks
- EnableCameraRelativeDashes
- EnableFrameAlignment
- EnableTurboHotbars
- And more...

### Threshold Settings
- **QueueThreshold** - Queue window threshold (default: 0.5s)
- **QueueLockThreshold** - Queue lock threshold
- **TurboHotbarInterval** - Turbo interval (default: 400ms)

## 🤝 Contributing

Contributions welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Follow existing code style (nullable enabled, 4-space indentation)
4. Test your changes in-game
5. Submit a PR with clear description

## 📚 Resources

- [Dalamud Documentation](https://dalamud.dev/)
- [FFXIV Client Structs](https://github.com/aers/FFXIVClientStructs)
- [ECommons](https://github.com/NightmareXIV/ECommons)

## ⚠️ Disclaimer

This plugin modifies game behavior. Use at your own risk. The author is not responsible for any disciplinary action taken by Square Enix.

## 📝 License

MIT License - See [LICENSE](LICENSE) for details

---

**Author:** Maomi Gato  
**Version:** 1.0.0.0  
**Dalamud API:** 14 (FFXIV Patch 7.x)
