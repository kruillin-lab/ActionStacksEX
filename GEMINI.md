# ActionStacksEX - Project Context

## Project Overview
**ActionStacksEX** is a standalone Final Fantasy XIV (FFXIV) plugin built using the Dalamud framework. Its primary purpose is to provide advanced action transformation and targeting capabilities, essentially allowing players to create "stacks" of conditional logic for actions.

### Key Features
*   **Action Stacking:** Define sequences of actions and targets that are evaluated when a "trigger" action is used.
*   **Conditional Logic:** Supports HP ratio checks, status effect (buff/debuff) checks, and range/cooldown validation.
*   **Advanced Targeting:** Includes custom pronouns for targeting specific party roles (Tank, Healer, DPS), lowest HP members, and nearest/furthest entities.
*   **Auto-Rotation Compatibility:** Intercepts both manual keypresses (`useType 0`) and automated execution calls (`useType 1`) to ensure seamless integration with external rotation tools.
*   **Beneficial Redirection:** Automatically redirects beneficial actions (like heals) to the player if an enemy is targeted.
*   **Enhanced Queuing:** Relaxes strict game checks during animation locks or active casts to allow GCDs and oGCDs to queue more reliably.

### Technologies
*   **Language:** C# 13.0
*   **Framework:** .NET 10.0 (Targeting Windows 10.0.26100.0)
*   **Libraries:**
    *   [Dalamud](https://github.com/goatcorp/Dalamud): Core plugin framework.
    *   [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs): Direct interfacing with game memory and structures.
    *   [ECommons](https://github.com/NightmareXIV/ECommons): Shared utilities and common plugin logic.
    *   [Lumina](https://github.com/Reslog/Lumina): Accessing game data files (Excel sheets).
    *   [Hypostasis](https://github.com/daemitus/Hypostasis): Foundation architecture for hooks, memory management, and UI.

## Building and Running

### Build Prerequisites
*   .NET 10 SDK
*   Dalamud.NET.Sdk

### Key Commands
*   **Build:** `dotnet build "ActionStacksEX.csproj"`
*   **Deploy:** The project is configured to automatically copy the resulting `.dll` and `.json` manifest to `$(APPDATA)\XIVLauncher\devPlugins\ActionStacksEX` after a successful build.

## Project Structure
*   `ActionStacksEX.cs`: Main plugin entry point and initialization logic.
*   `ActionStackManager.cs`: Core logic for intercepting `UseAction` and evaluating stack conditions.
*   `PronounManager.cs`: Handles custom targeting logic and pronoun resolution.
*   `PluginUI.cs`: ImGui-based configuration interface.
*   `Configuration.cs`: Data structures for plugin settings and action stacks.
*   `Extensions.cs`: Helper methods for interfacing with `FFXIVClientStructs` objects.
*   `Game.cs`: Manages hooks and memory patches.
*   `Modules/`: Contains optional sub-modules for features like `AutoTarget`, `TurboHotbars`, and `Decombos`.

## Development Conventions

### Hooking Strategy
The plugin primarily operates by hooking `ActionManager.UseAction`. It modifies parameters (actionID, targetObjectID) in real-time based on the evaluated stack logic before passing the call to the original game function.

### Target Validation
Standard game checks like `CanUseActionOnGameObject` are often bypassed or supplemented with manual checks (in `Extensions.cs`) because they return `false` during active GCDs, which would block legitimate queuing.

### Error Handling
ECommons initialization is wrapped in `try-catch` to handle assembly load conflicts common in multi-plugin environments. Direct memory access (via pointers) uses raw offsets where property definitions are unstable across game versions.

### UI Style
The configuration UI uses Material Design principles and `ImGuiEx` utilities for a consistent and searchable interface (e.g., `ExcelSheetCombo` for action and status selection).
