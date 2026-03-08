using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using ECommons;

namespace ActionStacksEX;

public class ActionStacksEX(IDalamudPluginInterface pluginInterface) : DalamudPlugin<Configuration>(pluginInterface), IDalamudPlugin
{
    public static Dictionary<uint, Lumina.Excel.Sheets.Action> actionSheet;
    public static Dictionary<uint, Lumina.Excel.Sheets.Action> mountActionsSheet;
    public static Dictionary<uint, Lumina.Excel.Sheets.Status> statusSheet;

    protected override void Initialize()
    {
        Game.Initialize();
        PronounManager.Initialize();
        try
        {
            ECommonsMain.Init(pluginInterface, this, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);
        }
        catch (Exception e)
        {
            DalamudApi.LogError("Failed to initialize ECommons, it might be already loaded by another plugin.", e);
        }

        actionSheet = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.Where(i => !string.IsNullOrEmpty(i.Name.ToString()) && i.RowId > 8).ToDictionary(i => i.RowId, i => i);
        mountActionsSheet = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.Where(i => i.ActionCategory.RowId == 12).ToDictionary(i => i.RowId, i => i);
        statusSheet = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>()?.Where(i => !string.IsNullOrEmpty(i.Name.ToString()) && i.Icon != 0).ToDictionary(i => i.RowId, i => i);
        
        if (actionSheet == null || mountActionsSheet == null || statusSheet == null)
            throw new ApplicationException("Excel sheets failed to load!");

        DalamudApi.LogDebug($"Loaded {actionSheet.Count} actions and {statusSheet.Count} statuses.");
    }

    protected override void ToggleConfig() => PluginUI.IsVisible ^= true;

    [PluginCommand("/actionstacksex", "/ax", HelpMessage = "Opens / closes the config.")]
    private void ToggleConfig(string command, string argument) => ToggleConfig();

    [PluginCommand("/asmacroqueue", "/asmqueue", HelpMessage = "[on|off] - Toggles (with no argument specified), enables or disables /ac queueing in the current macro.")]
    private void OnMacroQueue(string command, string argument)
    {
        if (!Common.IsMacroRunning)
        {
            DalamudApi.PrintError("This command requires a macro to be running.");
            return;
        }

        switch (argument)
        {
            case "on":
                Game.queueACCommandPatch.Enable();
                break;
            case "off":
                Game.queueACCommandPatch.Disable();
                break;
            case "":
                if (!Config.EnableMacroQueue)
                    Game.queueACCommandPatch.Toggle();
                break;
            default:
                DalamudApi.PrintError("Invalid usage.");
                break;
        }
    }

    protected override void Update()
    {
        if (Config.EnableMacroQueue)
        {
            if (!Game.queueACCommandPatch.IsEnabled && !Common.IsMacroRunning)
                Game.queueACCommandPatch.Enable();
        }
        else
        {
            if (Game.queueACCommandPatch.IsEnabled && !Common.IsMacroRunning)
                Game.queueACCommandPatch.Disable();
        }
    }

    protected override void Draw() => PluginUI.Draw();

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        Game.Dispose();
    }
}