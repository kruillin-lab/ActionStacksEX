using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Hypostasis.Game.Structures;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;

namespace ActionStacksEX;

public static class PluginUI
{
    private static bool isVisible = false;
    private static int selectedStack = -1;
    private static int hotbar = 0;
    private static int hotbarSlot = 0;
    private static int commandType = 1;
    private static uint commandID = 0;

    public static bool IsVisible
    {
        get => isVisible;
        set => isVisible = value;
    }

    private static Configuration.ActionStack CurrentStack => 0 <= selectedStack && selectedStack < ActionStacksEX.Config.ActionStacks.Count ? ActionStacksEX.Config.ActionStacks[selectedStack] : null;

    public static void Draw()
    {
        if (!isVisible) return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(700, 600) * ImGuiHelpers.GlobalScale, new Vector2(9999));
        ImGui.Begin("ActionStacksEX Configuration", ref isVisible);
        ImGuiEx.AddDonationHeader();

        if (ImGui.BeginTabBar("ActionStacksEXTabs"))
        {
            if (ImGui.BeginTabItem("Stacks"))
            {
                DrawStackList();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Pronoun Forge"))
            {
                DrawPronounForge();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Other Settings"))
            {
                ImGui.BeginChild("OtherSettings");
                DrawOtherSettings();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Custom Placeholders"))
            {
                DrawCustomPlaceholders();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("X-Ray"))
            {
                DrawXRay();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Help"))
            {
                DrawStackHelp();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private static void DrawStackList()
    {
        var currentStack = CurrentStack;
        var hasSelectedStack = currentStack != null;

        ImGui.PushFont(UiBuilder.IconFont);

        var buttonSize = ImGui.CalcTextSize(FontAwesomeIcon.SignOutAlt.ToIconString()) + ImGui.GetStyle().FramePadding * 2;

        if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), buttonSize))
        {
            ActionStacksEX.Config.ActionStacks.Add(new() { Name = "New Stack" });
            ActionStacksEX.Config.Save();
        }

        ImGui.SameLine();

        if (ImGui.Button(FontAwesomeIcon.SignOutAlt.ToIconString(), buttonSize) && hasSelectedStack)
            ImGui.SetClipboardText(Configuration.ExportActionStack(CurrentStack));
        ImGui.PopFont();
        ImGuiEx.SetItemTooltip("Export stack to clipboard.");
        ImGui.PushFont(UiBuilder.IconFont);

        ImGui.SameLine();

        if (ImGui.Button(FontAwesomeIcon.SignInAlt.ToIconString(), buttonSize))
        {
            try
            {
                var stack = Configuration.ImportActionStack(ImGui.GetClipboardText());
                ActionStacksEX.Config.ActionStacks.Add(stack);
                ActionStacksEX.Config.Save();
            }
            catch (Exception e)
            {
                DalamudApi.PrintError($"Failed to import stack from clipboard!\n{e.Message}");
            }
        }
        ImGui.PopFont();
        ImGuiEx.SetItemTooltip("Import stack from clipboard.");
        ImGui.PushFont(UiBuilder.IconFont);

        ImGui.SameLine();

        if (ImGui.Button(FontAwesomeIcon.ArrowUp.ToIconString(), buttonSize) && hasSelectedStack)
        {
            var preset = CurrentStack;
            ActionStacksEX.Config.ActionStacks.RemoveAt(selectedStack);

            selectedStack = Math.Max(selectedStack - 1, 0);

            ActionStacksEX.Config.ActionStacks.Insert(selectedStack, preset);
            ActionStacksEX.Config.Save();
        }

        ImGui.SameLine();

        if (ImGui.Button(FontAwesomeIcon.ArrowDown.ToIconString(), buttonSize) && hasSelectedStack)
        {
            var preset = CurrentStack;
            ActionStacksEX.Config.ActionStacks.RemoveAt(selectedStack);

            selectedStack = Math.Min(selectedStack + 1, ActionStacksEX.Config.ActionStacks.Count);

            ActionStacksEX.Config.ActionStacks.Insert(selectedStack, preset);
            ActionStacksEX.Config.Save();
        }

        ImGui.PopFont();

        ImGui.SameLine();

        if (ImGuiEx.DeleteConfirmationButton(buttonSize) && hasSelectedStack)
        {
            ActionStacksEX.Config.ActionStacks.RemoveAt(selectedStack);
            selectedStack = Math.Min(selectedStack, ActionStacksEX.Config.ActionStacks.Count - 1);
            currentStack = CurrentStack;
            hasSelectedStack = currentStack != null;
            ActionStacksEX.Config.Save();
        }

        var firstColumnWidth = 250 * ImGuiHelpers.GlobalScale;
        ImGui.PushStyleColor(ImGuiCol.Border, ImGui.GetColorU32(ImGuiCol.TabActive));
        ImGui.BeginChild("ActionStacksEXPresetList", new Vector2(firstColumnWidth, ImGui.GetContentRegionAvail().Y / 2), true);
        ImGui.PopStyleColor();

        for (int i = 0; i < ActionStacksEX.Config.ActionStacks.Count; i++)
        {
            ImGui.PushID(i);

            var preset = ActionStacksEX.Config.ActionStacks[i];

            if (ImGui.Selectable(preset.Name, selectedStack == i))
                selectedStack = i;

            ImGui.PopID();
        }

        ImGui.EndChild();

        if (!hasSelectedStack) return;

        var lastCursorPos = ImGui.GetCursorPos();
        ImGui.SameLine();
        var nextLineCursorPos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(lastCursorPos);

        ImGui.BeginChild("ActionStacksEXStackEditorMain", new Vector2(firstColumnWidth, ImGui.GetContentRegionAvail().Y), true);
        DrawStackEditorMain(currentStack);
        ImGui.EndChild();

        ImGui.SetCursorPos(nextLineCursorPos);
        ImGui.BeginChild("ActionStacksEXStackEditorLists", ImGui.GetContentRegionAvail(), false);
        DrawStackEditorLists(currentStack);
        ImGui.EndChild();
    }

    private static void DrawStackEditorMain(Configuration.ActionStack stack)
    {
        var save = false;

        save |= ImGui.InputText("Name", ref stack.Name, 64);
        save |= ImGui.CheckboxFlags("##Shift", ref stack.ModifierKeys, 1u);
        ImGuiEx.SetItemTooltip("Shift");
        ImGui.SameLine();
        save |= ImGui.CheckboxFlags("##Ctrl", ref stack.ModifierKeys, 2u);
        ImGuiEx.SetItemTooltip("Control");
        ImGui.SameLine();
        save |= ImGui.CheckboxFlags("##Alt", ref stack.ModifierKeys, 4u);
        ImGuiEx.SetItemTooltip("Alt");
        ImGui.SameLine();
        save |= ImGui.CheckboxFlags("##Exact", ref stack.ModifierKeys, 8u);
        ImGuiEx.SetItemTooltip("Match exactly these modifiers. E.g. Shift + Control ticked will match Shift + Control held, but not Shift + Control + Alt held.");
        ImGui.SameLine();
        ImGui.TextUnformatted("Modifier Keys");
        save |= ImGui.Checkbox("Block Original on Stack Fail", ref stack.BlockOriginal);
        save |= ImGui.Checkbox("Fail if Out of Range", ref stack.CheckRange);
        save |= ImGui.Checkbox("Fail if On Cooldown", ref stack.CheckCooldown);
        ImGuiEx.SetItemTooltip("Will fail if the action would fail to queue due to cooldown. Which is either" +
            "\n> 0.5s left on the cooldown, or < 0.5s since the last use (Charges / GCD).");

        if (save)
            ActionStacksEX.Config.Save();
    }

    private static void DrawStackEditorLists(Configuration.ActionStack stack)
    {
        DrawTriggerEditor(stack);
        DrawItemEditor(stack);
    }

    private static string FormatActionRow(Action a) => a.RowId switch
    {
        0 => "All Actions",
        1 => "All Harmful Actions",
        2 => "All Beneficial Actions",
        _ => $"[#{a.RowId} {a.ClassJob.ValueNullable?.Abbreviation}{(a.IsPvP ? " PVP" : string.Empty)}] {a.Name}"
    };

    private static readonly ImGuiEx.ExcelSheetComboOptions<Action> actionComboOptions = new()
    {
        FormatRow = FormatActionRow,
        FilteredSheet = DalamudApi.DataManager.GetExcelSheet<Action>()?.Take(3).Concat(ActionStacksEX.actionSheet.Select(kv => kv.Value))
    };

    private static readonly ImGuiEx.ExcelSheetPopupOptions<Action> actionPopupOptions = new()
    {
        FormatRow = FormatActionRow,
        FilteredSheet = actionComboOptions.FilteredSheet
    };

    private static readonly ImGuiEx.ExcelSheetComboOptions<Status> statusComboOptions = new()
    {
        FormatRow = r => $"[#{r.RowId}] {r.Name}",
        FilteredSheet = ActionStacksEX.statusSheet.Select(kv => kv.Value)
    };

    private static string FormatStatusName(uint id)
        => ActionStacksEX.statusSheet.TryGetValue(id, out var s) ? s.Name.ToString() : $"#{id}";

    private static void DrawTriggerEditor(Configuration.ActionStack stack)
    {
        var contentRegion = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("ActionStacksEXTriggerEditor", contentRegion with { Y = contentRegion.Y / 2 }, true);

        ImGui.Text("Trigger Action");
        ImGuiEx.SetItemTooltip("When this action is used, the stack will execute and try each item below.");

        var triggerActionId = stack.TriggerAction;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 120);
        if (ImGuiEx.ExcelSheetCombo("##TriggerAction", ref triggerActionId, actionComboOptions))
        {
            stack.TriggerAction = triggerActionId;
            ActionStacksEX.Config.Save();
        }

        ImGui.SameLine();

        if (ImGui.Checkbox("Adjust##Trigger", ref stack.UseAdjustedTrigger))
            ActionStacksEX.Config.Save();
        var detectedAdjustment = false;
        unsafe
        {
            if (!stack.UseAdjustedTrigger && triggerActionId != 0 && (detectedAdjustment = Common.ActionManager->CS.GetAdjustedActionId(triggerActionId) != triggerActionId))
                ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), 0x2000FF30, ImGui.GetStyle().FrameRounding);
        }
        ImGuiEx.SetItemTooltip("Allows the trigger to match any action it transforms into."
            +"\nE.g. If Cure is set as trigger, it will also trigger when Cure II is used (if Cure upgrades to Cure II)."
            + (detectedAdjustment ? "\n\nThis action is currently adjusted. Enabling this is recommended." : string.Empty));

        ImGui.EndChild();
    }

    private static string FormatOverrideActionRow(Action a) => a.RowId switch
    {
        0 => "Same Action",
        _ => $"[#{a.RowId} {a.ClassJob.ValueNullable?.Abbreviation}{(a.IsPvP ? " PVP" : string.Empty)}] {a.Name}"
    };

    private static readonly ImGuiEx.ExcelSheetComboOptions<Action> actionOverrideComboOptions = new()
    {
        FormatRow = FormatOverrideActionRow,
        FilteredSheet = DalamudApi.DataManager.GetExcelSheet<Action>().Take(1).Concat(ActionStacksEX.actionSheet.Select(kv => kv.Value))
    };

    private static void DrawItemEditor(Configuration.ActionStack stack)
    {
        ImGui.BeginChild("ActionStacksEXItemEditor", ImGui.GetContentRegionAvail(), true);

        var buttonWidth = ImGui.GetContentRegionAvail().X / 4;
        var buttonIndent = 0f;
        for (int i = 0; i < stack.Items.Count; i++)
        {
            using var _ = ImGuiEx.IDBlock.Begin(i);
            var item = stack.Items[i];

            ImGui.Button("≡");
            if (ImGuiEx.IsItemDraggedDelta(item, ImGuiMouseButton.Left, ImGui.GetFrameHeightWithSpacing(), false, out var dt) && dt.Y != 0)
                stack.Items.Shift(i, dt.Y);

            if (i == 0)
                buttonIndent = ImGui.GetItemRectSize().X + ImGui.GetStyle().ItemSpacing.X;

            ImGui.SameLine();

            if (ImGui.Checkbox("##Enabled", ref item.Enabled))
                ActionStacksEX.Config.Save();
            ImGuiEx.SetItemTooltip("Enable or disable this stack item.");

            ImGui.SameLine();

            ImGui.SetNextItemWidth(buttonWidth);
            if (DrawTargetTypeCombo("##TargetType", ref item.TargetID))
                ActionStacksEX.Config.Save();

            ImGui.SameLine();

            ImGui.SetNextItemWidth(buttonWidth);
            if (ImGuiEx.ExcelSheetCombo("##ActionOverride", ref item.ID, actionOverrideComboOptions))
                ActionStacksEX.Config.Save();

            ImGui.SameLine();

            ImGui.SetNextItemWidth(buttonWidth / 2);
            if (ImGui.SliderFloat("##HpRatio", ref item.HpRatio, 0.0f, 1.0f, "%.2f"))
                ActionStacksEX.Config.Save();
            ImGuiEx.SetItemTooltip("HP Ratio threshold. Item will be skipped if target HP% is above this.");

            ImGui.SameLine();

            ImGui.SetNextItemWidth(buttonWidth / 2);
            var statusLabel = item.StatusIDs.Count switch
            {
                0 => "No status",
                1 => FormatStatusName(item.StatusIDs[0]),
                _ => $"{item.StatusIDs.Count} statuses"
            };
            if (ImGui.Button($"{statusLabel}##StatusList", new Vector2(buttonWidth / 2, 0)))
                ImGui.OpenPopup("StatusList");
            ImGuiEx.SetItemTooltip("Statuses this item checks, matched any-of. Click to edit; empty disables the check.\n" +
                "List the level-scaled forms of one buff (e.g. Aspected Helios and Helios Conjunction)\n" +
                "to guard correctly at every sync level.");

            if (ImGui.BeginPopup("StatusList"))
            {
                for (var s = 0; s < item.StatusIDs.Count; s++)
                {
                    using var statusId = ImGuiEx.IDBlock.Begin(s);
                    if (ImGui.Button("x"))
                    {
                        item.StatusIDs.RemoveAt(s);
                        ActionStacksEX.Config.Save();
                        break; // list mutated, resume next frame
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted(FormatStatusName(item.StatusIDs[s]));
                }

                if (item.StatusIDs.Count > 0)
                    ImGui.Separator();

                ImGui.TextUnformatted("Add");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(320 * ImGuiHelpers.GlobalScale);
                var statusToAdd = 0u;
                if (ImGuiEx.ExcelSheetCombo("##AddStatus", ref statusToAdd, statusComboOptions)
                    && statusToAdd != 0 && !item.StatusIDs.Contains(statusToAdd))
                {
                    item.StatusIDs.Add(statusToAdd);
                    ActionStacksEX.Config.Save();
                }

                ImGui.EndPopup();
            }

            ImGui.SameLine();

            if (ImGui.Checkbox("##MissingStatus", ref item.MissingStatus))
                ActionStacksEX.Config.Save();
            ImGuiEx.SetItemTooltip("Ticked: the item only triggers if the target has NONE of the listed statuses.\n" +
                "Unticked: it only triggers if the target has at least ONE of them.");

            ImGui.SameLine();

            if (!ImGuiEx.DeleteConfirmationButton()) continue;
            stack.Items.RemoveAt(i);
            ActionStacksEX.Config.Save();
        }

        using (ImGuiEx.IndentBlock.Begin(buttonIndent))
        {
            if (ImGuiEx.FontButton(FontAwesomeIcon.Plus.ToIconString(), UiBuilder.IconFont, new Vector2(buttonWidth, 0)))
            {
                stack.Items.Add(new());
                ActionStacksEX.Config.Save();
            }
        }

        ImGui.EndChild();
    }

    private static bool DrawTargetTypeCombo(string label, ref uint currentSelection)
    {
        if (!ImGui.BeginCombo(label, PronounManager.GetPronounName(currentSelection))) return false;

        var ret = false;
        foreach (var id in PronounManager.OrderedIDs)
        {
            if (!ImGui.Selectable(PronounManager.GetPronounName(id), id == currentSelection)) continue;
            currentSelection = id;
            ret = true;
            break;
        }

        ImGui.EndCombo();
        return ret;
    }

    private static void DrawOtherSettings()
    {
        var save = false;

        if (ImGuiEx.BeginGroupBox("Actions", 0.5f))
        {
            save |= ImGui.DragInt("Stack Re-entrancy Window", ref ActionStacksEX.Config.StackReentrancyWindow, 0.5f, 0, 3000, "%d ms");
            ImGuiEx.SetItemTooltip("How long a successful stack suppresses re-evaluation of the same trigger.\n" +
                "Stops one keypress firing several stack items, so keep it well under a GCD.\n" +
                "Set too high (the old 3000ms default) and genuine repeat presses are swallowed:\n" +
                "the stack is skipped entirely and its conditions - HP, status, range - never run.");

            save |= ImGui.Checkbox("Enable Turbo Hotbar Keybinds", ref ActionStacksEX.Config.EnableTurboHotbars);
            ImGuiEx.SetItemTooltip("Allows you to hold hotbar keybinds (no controller support).\nWARNING: Text macros may be spammed.");

            using (ImGuiEx.DisabledBlock.Begin(!ActionStacksEX.Config.EnableTurboHotbars))
            {
                ImGuiEx.Prefix(false);
                save |= ImGui.DragInt("Interval", ref ActionStacksEX.Config.TurboHotbarInterval, 0.5f, 0, 1000, "%d ms");

                ImGuiEx.Prefix(false);
                save |= ImGui.DragInt("Start Delay", ref ActionStacksEX.Config.InitialTurboHotbarInterval, 0.5f, 0, 1000, "%d ms");

                ImGuiEx.Prefix(false);
                save |= ImGui.Checkbox("Enable Out of Combat##Turbo", ref ActionStacksEX.Config.EnableTurboHotbarsOutOfCombat);

                ImGuiEx.Prefix(true);
                save |= ImGui.Checkbox($"Toggle Hold Mode", ref ActionStacksEX.Config.ToggleTurboMode);
            }

            save |= ImGui.Checkbox("Enable Instant Ground Targets", ref ActionStacksEX.Config.EnableInstantGroundTarget);
            ImGuiEx.SetItemTooltip("Ground targets will immediately place themselves at your current cursor position when a stack does not override the target.");

            using (ImGuiEx.DisabledBlock.Begin(!ActionStacksEX.Config.EnableInstantGroundTarget))
            {
                ImGuiEx.Prefix(true);
                save |= ImGui.Checkbox("Block Miscellaneous Ground Targets", ref ActionStacksEX.Config.EnableBlockMiscInstantGroundTargets);
                ImGuiEx.SetItemTooltip("Disables the previous option from activating on actions such as placing pets.");
            }

            save |= ImGui.Checkbox("Enable Enhanced Auto Face Target", ref ActionStacksEX.Config.EnableEnhancedAutoFaceTarget);
            ImGuiEx.SetItemTooltip("Actions that don't require facing a target will no longer automatically face the target, such as healing.");

            save |= ImGui.Checkbox("Enable Camera Relative Directional Actions", ref ActionStacksEX.Config.EnableCameraRelativeDirectionals);
            ImGuiEx.SetItemTooltip("Changes channeled and directional actions, such as Passage of Arms or Surpanakha,\nto be relative to the direction your camera is facing, rather than your character.");

            save |= ImGui.Checkbox("Enable Camera Relative Dashes", ref ActionStacksEX.Config.EnableCameraRelativeDashes);
            ImGuiEx.SetItemTooltip("Changes dashes, such as En Avant and Elusive Jump, to be relative\nto the direction your camera is facing, rather than your character.");

            using (ImGuiEx.DisabledBlock.Begin(!ActionStacksEX.Config.EnableCameraRelativeDashes))
            {
                ImGuiEx.Prefix(false);
                save |= ImGui.Checkbox("Block Backward Dashes", ref ActionStacksEX.Config.EnableNormalBackwardDashes);
                ImGuiEx.SetItemTooltip("Disables the previous option for any backward dash, such as Elusive Jump.");

                if (ActionStacksEX.Config.EnableNormalBackwardDashes && save)
                    ActionStacksEX.Config.EnableReverseBackwardDashes = false;

                ImGuiEx.Prefix(true);
                save |= ImGui.Checkbox($"Reverse Backward Dashes", ref ActionStacksEX.Config.EnableReverseBackwardDashes);
                ImGuiEx.SetItemTooltip($"Dashes such as Elusive Jump will dash forward relative to the camera instead of backwards.");

                if (ActionStacksEX.Config.EnableReverseBackwardDashes && save)
                    ActionStacksEX.Config.EnableNormalBackwardDashes = false;

            }

            ImGuiEx.EndGroupBox();
        }

        ImGui.SameLine();

        if (ImGuiEx.BeginGroupBox("Auto", 0.5f))
        {
            save |= ImGui.Checkbox("Enable Auto Dismount", ref ActionStacksEX.Config.EnableAutoDismount);
            ImGuiEx.SetItemTooltip("Automatically dismounts when an action is used, prior to using the action.");

            save |= ImGui.Checkbox("Enable Auto Cast Cancel", ref ActionStacksEX.Config.EnableAutoCastCancel);
            ImGuiEx.SetItemTooltip("Automatically cancels casting when the target dies.");

            save |= ImGui.Checkbox("Enable Auto Target", ref ActionStacksEX.Config.EnableAutoTarget);
            ImGuiEx.SetItemTooltip("Automatically targets the closest enemy when no target is specified for a targeted attack.");

            using (ImGuiEx.DisabledBlock.Begin(!ActionStacksEX.Config.EnableAutoTarget))
            {
                ImGuiEx.Prefix(false);
                save |= ImGui.Checkbox("Enable Auto Change Target", ref ActionStacksEX.Config.EnableAutoChangeTarget);
                ImGuiEx.SetItemTooltip("Additionally targets the closest enemy when your main target is incorrect for a targeted attack.");

                ImGuiEx.Prefix(true);
                save |= ImGui.Checkbox("Ignore Camera", ref ActionStacksEX.Config.IgnoreCamera);
                ImGuiEx.SetItemTooltip("Picks a target even if they're not in camera view.");
            }

            var _ = ActionStacksEX.Config.AutoFocusTargetID != 0;
            if (ImGui.Checkbox("Enable Auto Focus Target", ref _))
            {
                ActionStacksEX.Config.AutoFocusTargetID = _ ? PronounManager.OrderedIDs.First() : 0;
                save = true;
            }
            ImGuiEx.SetItemTooltip("Automatically sets the focus target to the selected target type when possible.");

            using (ImGuiEx.DisabledBlock.Begin(!_))
            {
                ImGuiEx.Prefix(false);
                save |= DrawTargetTypeCombo("##AutoFocusTargetID", ref ActionStacksEX.Config.AutoFocusTargetID);

                ImGuiEx.Prefix(true);
                save |= ImGui.Checkbox("Enable Out of Combat##AutoFocusTarget", ref ActionStacksEX.Config.EnableAutoFocusTargetOutOfCombat);
            }

            save |= ImGui.Checkbox("Enable Auto Refocus Target", ref ActionStacksEX.Config.EnableAutoRefocusTarget);
            ImGuiEx.SetItemTooltip("While in duties, attempts to focus target whatever was previously focus targeted if the focus target is lost.");

            save |= ImGui.Checkbox("Enable Auto Attacks on Spells", ref ActionStacksEX.Config.EnableSpellAutoAttacks);
            ImGuiEx.SetItemTooltip("Causes spells (and some other actions) to start using auto attacks just like weaponskills.");

            using (ImGuiEx.DisabledBlock.Begin(!ActionStacksEX.Config.EnableSpellAutoAttacks))
            {
                ImGuiEx.Prefix(true);
                if (ImGui.Checkbox("Enable Out of Combat##SpellAutos", ref ActionStacksEX.Config.EnableSpellAutoAttacksOutOfCombat))
                {
                    if (ActionStacksEX.Config.EnableSpellAutoAttacksOutOfCombat)
                        Game.spellAutoAttackPatch.Enable();
                    else
                        Game.spellAutoAttackPatch.Disable();
                    save = true;
                }
                ImGuiEx.SetItemTooltip("WARNING: This can cause early pulls on certain bosses!");
            }

            ImGuiEx.EndGroupBox();
        }

        if (ImGuiEx.BeginGroupBox("Queuing", 0.5f))
        {
            if (ImGui.Checkbox("Enable Ground Target Queuing", ref ActionStacksEX.Config.EnableGroundTargetQueuing))
            {
                Game.queueGroundTargetsPatch.Toggle();
                save = true;
            }
            ImGuiEx.SetItemTooltip("Ground targets will insert themselves into the action queue,\ncausing them to immediately be used as soon as possible, like other OGCDs.");

            save |= ImGui.Checkbox("Enable Queuing More", ref ActionStacksEX.Config.EnableQueuingMore);
            ImGuiEx.SetItemTooltip("Allows all items and LBs to be queued.");

            save |= ImGui.Checkbox("Always Queue Macros", ref ActionStacksEX.Config.EnableMacroQueue);
            ImGuiEx.SetItemTooltip("All macros will behave as if /macroqueue was used.");

            save |= ImGui.Checkbox("Enable Queue Adjustments (BETA)", ref ActionStacksEX.Config.EnableQueueAdjustments);
            ImGuiEx.SetItemTooltip("Changes how the game handles queuing actions.\nThis is a beta feature, please let me know if anything is not working as expected.");

            using (ImGuiEx.DisabledBlock.Begin(!ActionStacksEX.Config.EnableQueueAdjustments))
            using (ImGuiEx.ItemWidthBlock.Begin(ImGui.CalcItemWidth() / 2))
            {
                ImGuiEx.Prefix(false);
                save |= ImGui.Checkbox("##Enable GCD Adjusted Threshold", ref ActionStacksEX.Config.EnableGCDAdjustedQueueThreshold);
                ImGuiEx.SetItemTooltip("Modifies the threshold based on the current GCD.");

                ImGui.SameLine();
                save |= ImGui.SliderFloat("Queue Threshold", ref ActionStacksEX.Config.QueueThreshold, 0.1f, 2.5f, "%.1f");
                ImGuiEx.SetItemTooltip("Time remaining on an action's cooldown to allow the game\nto queue up the next one when pressed early. Default: 0.5."
                    + (ActionStacksEX.Config.EnableGCDAdjustedQueueThreshold ? $"\nGCD Adjusted Threshold: {ActionStacksEX.Config.QueueThreshold * ActionManager.GCDRecast / 2500f}" : string.Empty));

                ImGui.BeginGroup();
                ImGuiEx.Prefix(false);
                save |= ImGui.Checkbox("##Enable Requeuing", ref ActionStacksEX.Config.EnableRequeuing);
                using (ImGuiEx.DisabledBlock.Begin(!ActionStacksEX.Config.EnableRequeuing))
                {
                    ImGui.SameLine();
                    save |= ImGui.SliderFloat("Queue Lock Threshold", ref ActionStacksEX.Config.QueueLockThreshold, 0.1f, 2.5f, "%.1f");
                }
                ImGui.EndGroup();
                ImGuiEx.SetItemTooltip("When enabled, allows requeuing until the queued action's cooldown is below this value.");

                ImGuiEx.Prefix(true);
                save |= ImGui.SliderFloat("Action Lockout", ref ActionStacksEX.Config.QueueActionLockout, 0, 2.5f, "%.1f");
                ImGuiEx.SetItemTooltip("Blocks the same action from being queued again if it has been on cooldown for less than this value.");
            }

            ImGuiEx.EndGroupBox();
        }

        ImGui.SameLine();

        if (ImGuiEx.BeginGroupBox("Sunderings", 0.5f))
        {
            save |= ImGui.Checkbox("Sunder Meditation", ref ActionStacksEX.Config.EnableDecomboMeditation);
            ImGuiEx.SetItemTooltip("Removes the Meditation <-> Steel Peak / Forbidden Chakra combo. You will need to use\nthe hotbar feature below to place one of them on your hotbar in order to use them again.\nSteel Peak ID: 25761\nForbidden Chakra ID: 3547");

            save |= ImGui.Checkbox("Sunder Bunshin", ref ActionStacksEX.Config.EnableDecomboBunshin);
            ImGuiEx.SetItemTooltip("Removes the Bunshin <-> Phantom Kamaitachi combo. You will need to use\nthe hotbar feature below to place it on your hotbar in order to use it again.\nPhantom Kamaitachi ID: 25774");

            save |= ImGui.Checkbox("Sunder Wanderer's Minuet", ref ActionStacksEX.Config.EnableDecomboWanderersMinuet);
            ImGuiEx.SetItemTooltip("Removes the Wanderer's Minuet -> Pitch Perfect combo. You will need to use\nthe hotbar feature below to place it on your hotbar in order to use it again.\nPitch Perfect ID: 7404");

            save |= ImGui.Checkbox("Sunder Liturgy of the Bell", ref ActionStacksEX.Config.EnableDecomboLiturgy);
            ImGuiEx.SetItemTooltip("Removes the Liturgy of the Bell combo. You will need to use the hotbar\nfeature below to place it on your hotbar in order to use it again.\nLiturgy of the Bell (Detonate) ID: 28509");

            save |= ImGui.Checkbox("Sunder Earthly Star", ref ActionStacksEX.Config.EnableDecomboEarthlyStar);
            ImGuiEx.SetItemTooltip("Removes the Earthly Star combo. You will need to use the hotbar\nfeature below to place it on your hotbar in order to use it again.\nStellar Detonation ID: 8324");

            save |= ImGui.Checkbox("Sunder Minor Arcana", ref ActionStacksEX.Config.EnableDecomboMinorArcana);
            ImGuiEx.SetItemTooltip("Removes the Minor Arcana -> Lord / Lady of Crowns combo. You will need to use the\nhotbar feature below to place one of them on your hotbar in order to use them again.\nLord of Crowns ID: 7444\nLady of Crowns ID: 7445");

            save |= ImGui.Checkbox("Sunder Geirskogul", ref ActionStacksEX.Config.EnableDecomboGeirskogul);
            ImGuiEx.SetItemTooltip("Removes the Geirskogul -> Nastrond combo. You will need to use the\nhotbar feature below to place it on your hotbar in order to use it again.\nNastrond ID: 7400");

            ImGuiEx.EndGroupBox();
        }

        if (ImGuiEx.BeginGroupBox("Misc", 0.5f))
        {
            save |= ImGui.Checkbox("Enable Frame Alignment", ref ActionStacksEX.Config.EnableFrameAlignment);
            ImGuiEx.SetItemTooltip("Aligns the game's frames with the GCD and animation lock.\nNote: This option will cause an almost unnoticeable stutter when either of these timers ends.");

            if (ImGui.Checkbox("Enable Decimal Waits (Fractionality)", ref ActionStacksEX.Config.EnableFractionality))
            {
                Game.waitSyntaxDecimalPatch.Toggle();
                Game.waitCommandDecimalPatch.Toggle();
                save = true;
            }
            ImGuiEx.SetItemTooltip("Allows decimals in wait commands and removes the 60 seconds cap (e.g. <wait.0.5> or /wait 0.5).");

            if (ImGui.Checkbox("Enable Unassignable Actions in Commands", ref ActionStacksEX.Config.EnableUnassignableActions))
            {
                Game.allowUnassignableActionsPatch.Toggle();
                save = true;
            }
            ImGuiEx.SetItemTooltip("Allows using normally unavailable actions in \"/ac\", such as The Forbidden Chakra or Stellar Detonation.");

            save |= ImGui.Checkbox("Enable Player Names in Commands", ref ActionStacksEX.Config.EnablePlayerNamesInCommands);
            ImGuiEx.SetItemTooltip("Allows using the \"First Last@World\" syntax for any command requiring a target.");

            ImGuiEx.EndGroupBox();
        }

        ImGui.SameLine();

        if (ImGuiEx.BeginGroupBox("Place on Hotbar (HOVER ME FOR INFORMATION)", 0.5f, new ImGuiEx.GroupBoxOptions
        {
            HeaderTextAction = () => ImGuiEx.SetItemTooltip(
                "This will allow you to place various things on the hotbar that you can't normally."
                +"\nIf you don't know what this can be used for, don't touch it."
                +"\nSome examples of things you can do:"
                +"\n\tPlace a certain action on the hotbar to be used with one of the \"Sundering\" features. The IDs are in each setting's tooltip."
                +"\n\tPlace a certain doze and sit emote on the hotbar (Emote, 88 and 95)."
                +"\n\tPlace a currency (Item, 1-99) on the hotbar to see how much you have without opening the currency menu."
                +"\n\tRevive flying mount roulette (GeneralAction, 24).")
        }))
        {
            ImGui.Combo("Bar", ref hotbar, "1\02\03\04\05\06\07\08\09\010\0XHB 1\0XHB 2\0XHB 3\0XHB 4\0XHB 5\0XHB 6\0XHB 7\0XHB 8");
            ImGui.Combo("Slot", ref hotbarSlot, "1\02\03\04\05\06\07\08\09\010\011\012\013\014\015\016");
            var hotbarSlotType = Enum.GetName(typeof(RaptureHotbarModule.HotbarSlotType), commandType) ?? commandType.ToString();
            if (ImGui.BeginCombo("Type", hotbarSlotType))
            {
                for (int i = 1; i <= 32; i++)
                {
                    if (!ImGui.Selectable($"{Enum.GetName(typeof(RaptureHotbarModule.HotbarSlotType), i) ?? i.ToString()}##{i}", commandType == i)) continue;
                    commandType = i;
                }
                ImGui.EndCombo();
            }

            DrawHotbarIDInput((RaptureHotbarModule.HotbarSlotType)commandType);

            if (ImGui.Button("Execute"))
                Game.SetHotbarSlot(hotbar, hotbarSlot, (byte)commandType, commandID);
            ImGuiEx.EndGroupBox();
        }

        if (save)
            ActionStacksEX.Config.Save();
    }

    public static void DrawHotbarIDInput(RaptureHotbarModule.HotbarSlotType slotType)
    {
        switch ((RaptureHotbarModule.HotbarSlotType)commandType)
        {
            case RaptureHotbarModule.HotbarSlotType.Action:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<Action> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.Item:
                const int hqID = 1_000_000;
                var _ = commandID >= hqID ? commandID - hqID : commandID;
                if (ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref _, new ImGuiEx.ExcelSheetComboOptions<Item> { FormatRow = r => $"[#{r.RowId}] {r.Name}" }))
                    commandID = commandID >= hqID ? _ + hqID : _;
                var hq = commandID >= hqID;
                if (ImGui.Checkbox("HQ", ref hq))
                    commandID = hq ? commandID + hqID : commandID - hqID;
                break;
            case RaptureHotbarModule.HotbarSlotType.EventItem:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<EventItem> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.Emote:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<Emote> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.Marker:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<Marker> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.CraftAction:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<CraftAction> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.GeneralAction:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<GeneralAction> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.BuddyAction:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<BuddyAction> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.MainCommand:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<MainCommand> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.Companion:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<Companion> { FormatRow = r => $"[#{r.RowId}] {r.Singular}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.PetAction:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<PetAction> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.Mount:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<Mount> { FormatRow = r => $"[#{r.RowId}] {r.Singular}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.FieldMarker:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<FieldMarker> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.Recipe:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<Recipe> { FormatRow = r => $"[#{r.RowId}] {r.ItemResult.Value.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.ChocoboRaceAbility:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<ChocoboRaceAbility> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.ChocoboRaceItem:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<ChocoboRaceItem> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.ExtraCommand:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<ExtraCommand> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.PvPQuickChat:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<QuickChat> { FormatRow = r => $"[#{r.RowId}] {r.NameAction}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.PvPCombo:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<ActionComboRoute> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.BgcArmyAction:
                // Sheet is BgcArmyAction, but it doesn't appear to be in Lumina
                var __ = (int)commandID;
                if (ImGui.Combo("ID", ref __, "[#0]\0[#1] Engage\0[#2] Disengage\0[#3] Re-engage\0[#4] Execute Limit Break\0[#5] Display Order Hotbar"))
                    commandID = (uint)__;
                break;
            case RaptureHotbarModule.HotbarSlotType.PerformanceInstrument:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<Perform> { FormatRow = r => $"[#{r.RowId}] {r.Instrument}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.McGuffin:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<McGuffin> { FormatRow = r => $"[#{r.RowId}] {r.UIData.Value.Name}" });
                break;
            case RaptureHotbarModule.HotbarSlotType.Ornament:
                ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<Ornament> { FormatRow = r => $"[#{r.RowId}] {r.Singular}" });
                break;
            // Doesn't appear to have a sheet
            //case HotbarSlotType.LostFindsItem:
            //    ImGuiEx.ExcelSheetCombo($"ID##{commandType}", ref commandID, new ImGuiEx.ExcelSheetComboOptions<> { FormatRow = r => $"[#{r.RowId}] {r.Name}" });
            //    break;
            default:
                var ___ = (int)commandID;
                if (ImGui.InputInt("ID", ref ___))
                    commandID = (uint)___;
                break;
        }
    }

    private static unsafe void DrawCustomPlaceholders()
    {
        if (!ImGui.BeginTable("CustomPronounInfoTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("Placeholder");
        ImGui.TableSetupColumn("Current Target");
        ImGui.TableHeadersRow();

        foreach (var (placeholder, pronoun) in PronounManager.CustomPlaceholders)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            ImGui.TextUnformatted(pronoun.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(placeholder);

            var p = pronoun.GetGameObject();
            if (p == null) continue;
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(p->NameString);
        }

        ImGui.EndTable();
    }

    private static ForgedPronounDef selectedForgedPronoun;

    private static unsafe void DrawPronounForge()
    {
        var config = ActionStacksEX.Config;
        var save = false;

        ImGui.BeginChild("ForgedPronounList", new Vector2(200 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().Y), true);

        ImGui.PushFont(UiBuilder.IconFont);
        var buttonSize = ImGui.CalcTextSize(FontAwesomeIcon.SignOutAlt.ToIconString()) + ImGui.GetStyle().FramePadding * 2;

        if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), buttonSize))
        {
            var def = new ForgedPronounDef { ID = config.NextForgedPronounID++ };
            def.Placeholder = $"<forge{def.ID - PronounForge.MinimumForgedID + 1}>";
            config.ForgedPronouns.Add(def);
            selectedForgedPronoun = def;
            save = true;
        }
        ImGui.PopFont();
        ImGuiEx.SetItemTooltip("Create a new forged pronoun.");
        ImGui.PushFont(UiBuilder.IconFont);

        ImGui.SameLine();

        if (ImGui.Button(FontAwesomeIcon.SignOutAlt.ToIconString(), buttonSize) && selectedForgedPronoun != null)
            ImGui.SetClipboardText(Configuration.ExportForgedPronoun(selectedForgedPronoun));
        ImGui.PopFont();
        ImGuiEx.SetItemTooltip("Export forged pronoun to clipboard.");
        ImGui.PushFont(UiBuilder.IconFont);

        ImGui.SameLine();

        if (ImGui.Button(FontAwesomeIcon.SignInAlt.ToIconString(), buttonSize))
        {
            try
            {
                var def = Configuration.ImportForgedPronoun(ImGui.GetClipboardText());
                if (def != null)
                {
                    def.ID = config.NextForgedPronounID++;
                    config.ForgedPronouns.Add(def);
                    selectedForgedPronoun = def;
                    save = true;
                }
            }
            catch (Exception e)
            {
                DalamudApi.PrintError($"Failed to import forged pronoun from clipboard!\n{e.Message}");
            }
        }
        ImGui.PopFont();
        ImGuiEx.SetItemTooltip("Import forged pronoun from clipboard.");

        ImGui.Separator();

        foreach (var def in config.ForgedPronouns)
        {
            if (ImGui.Selectable($"{def.Name}##Forged{def.ID}", selectedForgedPronoun == def))
                selectedForgedPronoun = def;
        }

        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("ForgedPronounEditor", ImGui.GetContentRegionAvail(), true);

        if (selectedForgedPronoun is { } cur && config.ForgedPronouns.Contains(cur))
        {
            save |= ImGui.InputText("Name", ref cur.Name, 64);

            save |= ImGui.InputText("Placeholder", ref cur.Placeholder, 32);
            ImGuiEx.SetItemTooltip("Text placeholder usable in macros and text commands, e.g. <myheal>.\nAlso selectable as a stack item target.");
            if (!string.IsNullOrEmpty(cur.Placeholder) && (!cur.Placeholder.StartsWith('<') || !cur.Placeholder.EndsWith('>')))
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), "Placeholder should look like <name>");
            else if (!string.IsNullOrEmpty(cur.Placeholder)
                && PronounManager.CustomPlaceholders.TryGetValue(cur.Placeholder, out var owner)
                && (owner is not ForgedPronoun f || f.Def != cur))
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Placeholder is already used by \"{owner.Name}\"");

            ImGui.Separator();

            save |= ImGui.Combo("Pool", ref cur.Pool, "Party\0Enemies");
            ImGuiEx.SetItemTooltip("Candidate pool to search.");

            if (cur.Pool == 0)
            {
                save |= ImGui.Combo("Role", ref cur.Role, "Any\0Tank\0Healer\0DPS");

                var job = (int)cur.JobID;
                if (ImGui.InputInt("Job ID", ref job))
                {
                    cur.JobID = (uint)Math.Max(job, 0);
                    save = true;
                }
                ImGuiEx.SetItemTooltip("ClassJob row ID filter, 0 = any job.");
                if (cur.JobID != 0)
                {
                    var jobSheet = DalamudApi.DataManager.GetExcelSheet<ClassJob>();
                    var jobName = jobSheet?.GetRowOrDefault(cur.JobID)?.Name.ToString();
                    ImGui.SameLine();
                    ImGui.TextUnformatted(string.IsNullOrEmpty(jobName) ? "(unknown)" : $"({jobName})");
                }

                save |= ImGui.Checkbox("Exclude self", ref cur.ExcludeSelf);
            }

            save |= ImGui.Combo("Life", ref cur.LifeFilter, "Alive only\0Dead only\0Any");

            save |= ImGui.Checkbox("##UseHpFilter", ref cur.UseHpFilter);
            ImGui.SameLine();
            if (!cur.UseHpFilter) ImGui.BeginDisabled();
            save |= ImGui.SliderFloat("Max HP %", ref cur.MaxHpPercent, 0.0f, 1.0f, "%.2f");
            if (!cur.UseHpFilter) ImGui.EndDisabled();
            ImGuiEx.SetItemTooltip("Only match candidates at or below this HP fraction.");

            var status = (int)cur.StatusID;
            if (ImGui.InputInt("Status ID", ref status))
            {
                cur.StatusID = (uint)Math.Max(status, 0);
                save = true;
            }
            ImGuiEx.SetItemTooltip("Status effect filter, 0 = ignore.");
            if (cur.StatusID != 0)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted(ActionStacksEX.statusSheet.TryGetValue(cur.StatusID, out var statusRow) ? $"({statusRow.Name})" : "(unknown)");
                save |= ImGui.Checkbox("Match only when status is missing", ref cur.MissingStatus);
            }

            save |= ImGui.SliderFloat("Max Distance", ref cur.MaxDistance, 0.0f, 55.0f, cur.MaxDistance > 0 ? "%.0f yalms" : "Any");

            save |= ImGui.Combo("Sort", ref cur.Sort, "First found\0Lowest HP %\0Highest HP %\0Nearest\0Farthest");
            ImGuiEx.SetItemTooltip("How to pick among multiple matching candidates.");

            ImGui.Separator();

            var match = PronounForge.Resolve(cur);
            if (match != null)
            {
                var io = DalamudApi.ObjectTable.FirstOrDefault(o => o.Address == (nint)match);
                ImGui.TextColored(new Vector4(0.3f, 1, 0.3f, 1), $"Current match: {io?.Name.TextValue ?? "Unknown"} ({PronounHelpers.GetHPPercent((nint)match) * 100:F0}% HP)");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0.65f, 0.3f, 1), "Current match: none");
            }

            ImGui.Separator();

            if (ImGuiEx.DeleteConfirmationButton())
            {
                config.ForgedPronouns.Remove(cur);
                selectedForgedPronoun = null;
                save = true;
            }
        }
        else
        {
            ImGui.TextWrapped("Forge your own targeting pronouns: pick a candidate pool, add filters (role, job, HP, status, distance), and choose how ties are sorted."
                + " Forged pronouns can be used as stack item targets and as text placeholders in macros."
                + "\n\nSelect or create a pronoun on the left to begin.");
        }

        ImGui.EndChild();

        if (save)
        {
            config.Save();
            PronounManager.ReloadForged();
        }
    }

    private static void DrawStackHelp()
    {
        ImGui.Text("Creating a Stack");
        ImGui.Indent();
        ImGui.TextWrapped("To start, click the + button in the top left corner, this will create a new stack that you can begin adding actions and functionality to.");
        ImGui.Unindent();

        ImGui.Separator();

        ImGui.Text("Editing a Stack");
        ImGui.Indent();
        ImGui.TextWrapped("Click on a stack from the top left list to display the editing panes for that it. The bottom left pane is where the " +
            "main settings reside, these will change the base functionality for the stack itself.");
        ImGui.Unindent();

        ImGui.Separator();

        ImGui.Text("Setting the Trigger Action");
        ImGui.Indent();
        ImGui.TextWrapped("The top right pane is where you set the trigger action. This is the single action that will activate the stack when used. " +
            "When you use this action in-game, the stack will execute and try each item in the stack (bottom pane) in order until one succeeds. " +
            "You can also enable \"Adjust\" to make the trigger match upgraded versions of the action (e.g., Cure will also trigger when Cure II is used).");
        ImGui.Unindent();

        ImGui.Separator();

        ImGui.Text("Editing a Stack's Functionality");
        ImGui.Indent();
        ImGui.TextWrapped("The bottom right pane is where you can change the functionality of the selected actions, by setting a list of targets to " +
            "extend or replace the game's. When the action is used, the plugin will attempt to determine, from top to bottom, which target is a valid choice. " +
            "This will execute before the game's own target priority system and only allow it to continue if not blocked by the stack. If any of the targets " +
            "are valid choices, the plugin will change the action's target to the new one and, additionally, replace the action with the override if set.");
        ImGui.Unindent();

        ImGui.Separator();

        ImGui.Text("Stack Priority");
        ImGui.Indent();
        ImGui.TextWrapped("The executed stack will depend on which one, from top to bottom, first matches the trigger action being used and has its modifier " +
            "keys held. Each stack only needs ONE trigger action set. When that action is used, the stack activates and tries each item in order. " +
            "If no item in the stack can be executed, the original trigger action will be used normally.");
        ImGui.Unindent();
    }

    private static readonly Vector4 xrayPassColor = new(0.35f, 1f, 0.45f, 1f);
    private static readonly Vector4 xrayFailColor = new(1f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 xrayInfoColor = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 xrayRedirectColor = new(1f, 0.85f, 0.3f, 1f);

    private static Vector4 XRayColor(XRay.StepKind kind) => kind switch
    {
        XRay.StepKind.Pass => xrayPassColor,
        XRay.StepKind.Fail => xrayFailColor,
        XRay.StepKind.Redirect => xrayRedirectColor,
        _ => xrayInfoColor
    };

    private static void DrawXRay()
    {
        ImGui.Checkbox("Capture", ref XRay.Capturing);
        ImGuiEx.SetItemTooltip("Records a decision trace for every stack evaluation while enabled. Zero overhead when off.");
        ImGui.SameLine();
        using (ImGuiEx.DisabledBlock.Begin(!XRay.Capturing))
            ImGui.Checkbox("Dry run", ref XRay.DryRun);
        ImGuiEx.SetItemTooltip("Evaluate stacks and record the trace, but do NOT redirect actions. Requires capture.");
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            XRay.Clear();
        ImGui.SameLine();
        if (ImGui.Button("Copy Trace"))
            ImGui.SetClipboardText(XRay.Dump());
        ImGuiEx.SetItemTooltip("Copies the whole buffer as plain text for bug reports or sharing.");
        ImGui.SameLine();
        ImGui.TextDisabled($"{XRay.Buffer.Count}/{XRay.Capacity} evaluations (newest first)");

        ImGui.Separator();

        if (XRay.Buffer.Count == 0)
        {
            ImGui.TextWrapped(XRay.Capturing
                ? "Waiting for stack evaluations... use a trigger action in-game."
                : "Enable Capture, then use your trigger actions in-game. Each press shows exactly which stacks were considered, which conditions passed or failed (with observed values), and why the action was redirected, blocked, or passed through.");
            return;
        }

        ImGui.BeginChild("XRayList");
        var i = 0;
        foreach (var e in XRay.Buffer)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, XRayColor(e.OutcomeKind));
            var open = ImGui.TreeNodeEx($"[{e.Time:HH:mm:ss.fff}] {e.TriggerName} — {e.Outcome}{(e.DryRun ? " [DRY-RUN]" : string.Empty)}###xray{i}");
            ImGui.PopStyleColor();
            if (open)
            {
                ImGui.TextColored(xrayInfoColor, $"Modifiers held: {XRay.Mods(e.ModifierKeys)}");
                foreach (var s in e.Steps)
                {
                    ImGui.TextColored(XRayColor(s.Kind), $"  [{s.Kind}]");
                    ImGui.SameLine();
                    ImGui.TextWrapped(string.IsNullOrEmpty(s.Detail) ? s.Label : $"{s.Label}: {s.Detail}");
                }
                if (e.Steps.Count == 0)
                    ImGui.TextColored(xrayInfoColor, "  (no steps recorded)");
                ImGui.TreePop();
            }
            i++;
        }
        ImGui.EndChild();
    }
}
