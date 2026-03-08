using System;
using System.Linq;
using ActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ActionManager = Hypostasis.Game.Structures.ActionManager;
using Hypostasis.Game.Structures;

namespace ActionStacksEX;

public static unsafe class ActionStackManager
{
    public delegate void PreUseActionEventDelegate(ActionManager* actionManager, ref uint actionType, ref uint actionID, ref ulong targetObjectID, ref uint param, ref uint useType, ref int pvp);
    public static event PreUseActionEventDelegate PreUseAction;
    public delegate void PreActionStackDelegate(ActionManager* actionManager, ref uint actionType, ref uint actionID, ref uint adjustedActionID, ref ulong targetObjectID, ref uint param, uint useType, ref int pvp, out bool? ret);
    public static event PreActionStackDelegate PreActionStack;
    public delegate void PostActionStackDelegate(ActionManager* actionManager, uint actionType, uint actionID, uint adjustedActionID, ref ulong targetObjectID, uint param, uint useType, int pvp);
    public static event PostActionStackDelegate PostActionStack;
    public delegate void PostUseActionDelegate(ActionManager* actionManager, uint actionType, uint actionID, uint adjustedActionID, ulong targetObjectID, uint param, uint useType, int pvp, bool ret);
    public static event PostUseActionDelegate PostUseAction;

    private static ulong queuedGroundTargetObjectID = 0;

    public static Bool OnUseAction(ActionManager* actionManager, uint actionType, uint actionID, ulong targetObjectID, uint param, uint useType, int pvp, bool* isGroundTarget)
    {
        try
        {
            if (DalamudApi.ClientState.LocalPlayer == null) return 0;

            var tryStack = useType is 0 or 1;
            if (useType == 100)
            {
                useType = 0;
                tryStack = true;
            }

            PreUseAction?.Invoke(actionManager, ref actionType, ref actionID, ref targetObjectID, ref param, ref useType, ref pvp);

            var adjustedActionID = actionType == 1 ? actionManager->CS.GetAdjustedActionId(actionID) : actionID;

            bool? ret = null;
            PreActionStack?.Invoke(actionManager, ref actionType, ref actionID, ref adjustedActionID, ref targetObjectID, ref param, useType, ref pvp, out ret);
            if (ret.HasValue)
                return ret.Value;

            var succeeded = false;
            uint finalActionID = adjustedActionID;
            if (PluginModuleManager.GetModule<Modules.ActionStacks>().IsValid && tryStack && actionType == 1 && ActionStacksEX.actionSheet.TryGetValue(adjustedActionID, out var a))
            {
                var modifierKeys = GetModifierKeys();
                foreach (var stack in ActionStacksEX.Config.ActionStacks)
                {
                    var exactMatch = (stack.ModifierKeys & 8) != 0;
                    if (exactMatch ? stack.ModifierKeys != modifierKeys : (stack.ModifierKeys & modifierKeys) != stack.ModifierKeys) continue;
                    if (!stack.Actions.Any(action
                            => action.ID == 0
                               || action.ID == 1 && a.CanTargetHostile
                               || action.ID == 2 && (a.CanTargetAlly || a.CanTargetParty)
                               || (action.UseAdjustedID ? actionManager->CS.GetAdjustedActionId(action.ID) : action.ID) == adjustedActionID))
                        continue;

                    if (!CheckActionStack(actionManager, adjustedActionID, stack, useType, out var newAction, out var newTarget))
                    {
                        if (stack.BlockOriginal) return 0;
                        break;
                    }

                    actionID = newAction;
                    finalActionID = newAction;
                    targetObjectID = newTarget;
                    succeeded = true;
                    break;
                }
            }

            PostActionStack?.Invoke(actionManager, actionType, actionID, finalActionID, ref targetObjectID, param, useType, pvp);

            var result = Game.UseActionHook.Original(actionManager, actionType, actionID, targetObjectID, param, useType, pvp, isGroundTarget);

            if (succeeded && useType == 0 && result == 0)
            {
                if (ActionStacksEX.actionSheet.TryGetValue(finalActionID, out var finalA))
                {
                    bool isGCD = finalA.ActionCategory.RowId is 1 or 2;
                    if (!isGCD && actionManager->CS.GetActionStatus(ActionType.Action, finalActionID, targetObjectID, false, false) == 0)
                    {
                        result = 1;
                    }
                }
            }

            PostUseAction?.Invoke(actionManager, actionType, actionID, finalActionID, targetObjectID, param, useType, pvp, result);

            if (succeeded && ActionStacksEX.actionSheet[finalActionID].TargetArea)
            {
                actionManager->queuedGroundTargetObjectID = targetObjectID;
                queuedGroundTargetObjectID = targetObjectID;
            }
            else if (useType == 1 && queuedGroundTargetObjectID != 0)
            {
                actionManager->queuedGroundTargetObjectID = queuedGroundTargetObjectID;
                queuedGroundTargetObjectID = 0;
            }
            else
            {
                queuedGroundTargetObjectID = 0;
            }

            if (ActionStacksEX.Config.EnableInstantGroundTarget && !succeeded && queuedGroundTargetObjectID == 0)
                SetInstantGroundTarget(actionManager, actionType, useType);

            return result;
        }
        catch (Exception e)
        {
            DalamudApi.LogError($"Failed to modify action\n{e}");
            return 0;
        }
    }

    private static uint GetModifierKeys()
    {
        var keys = 8u;
        if (DalamudApi.KeyState[16]) keys |= 1;
        if (DalamudApi.KeyState[17]) keys |= 2;
        if (DalamudApi.KeyState[18]) keys |= 4;
        return keys;
    }

    private static bool CheckActionStack(ActionManager* actionManager, uint id, Configuration.ActionStack stack, uint useType, out uint action, out ulong target)
    {
        action = 0;
        target = Game.InvalidObjectID;

        var useRange = stack.CheckRange;
        var useCooldown = stack.CheckCooldown;
        foreach (var item in stack.Items)
        {
            if (!item.Enabled) continue;

            var newID = item.ID != 0 ? actionManager->CS.GetAdjustedActionId(item.ID) : id;
            var newTarget = PronounManager.GetGameObjectFromID(item.TargetID);
            if (newTarget == null)
            {
                // Fallback: If item.TargetID corresponds to Party1..8 (43..50), try GetPartyMembers
                // 43 = PartyMember1 (Self), 44 = PartyMember2, etc.
                if (item.TargetID >= 43 && item.TargetID <= 50)
                {
                    var index = item.TargetID - 43;
                    var members = Hypostasis.Game.Common.GetPartyMembers().ToList();
                    // Debug log suppressed to avoid spam unless critical
                    // DalamudApi.LogDebug($"[ActionStacksEX] TargetID {item.TargetID} (Index {index}) - Party Members Found: {members.Count}");
                    if (index < members.Count)
                    {
                        newTarget = (GameObject*)members[(int)index];
                        // DalamudApi.LogDebug($"[ActionStacksEX] Resolved Target from Fallback: {(nint)newTarget:X}");
                    }
                    else 
                    {
                        // Log only if index is within expected range for a light party (e.g. < 4) to verify visibility
                        if (index < 4) 
                            DalamudApi.LogDebug($"[ActionStacksEX] Index {index} out of range for party list of size {members.Count}");
                    }
                }
            }
            
            if (newTarget == null)
            {
                // Only log failure if it's a specific target type we expect to exist (like Party 1-4)
                // item.TargetID 43-46 are Party 1-4.
                if (item.TargetID >= 43 && item.TargetID <= 46)
                {
                    DalamudApi.LogDebug($"[ActionStacksEX] Failed to find target for item {item.ID} (TargetID: {item.TargetID})");
                }
                continue;
            }

            if (!ActionStacksEX.actionSheet.TryGetValue(newID, out var actionData)) continue;

            // Check if player is high enough level for this action (handles level sync dungeons)
            var localPlayer = DalamudApi.ClientState.LocalPlayer;
            if (localPlayer != null && actionData.ClassJobLevel > localPlayer.Level)
            {
                DalamudApi.LogDebug($"[ActionStacksEX] Skipping {newID} - requires level {actionData.ClassJobLevel}, player is level {localPlayer.Level}");
                continue;
            }

            bool isSelf = newTarget->EntityId == localPlayer!.GameObjectId;
            bool isEnemy = Extensions.IsHostile(newTarget);
            bool canTargetHostile = actionData.CanTargetHostile;
            bool canTargetAlly = actionData.CanTargetAlly || actionData.CanTargetParty;
            bool canTargetSelf = actionData.CanTargetSelf;

            if (Extensions.IsCharacter(newTarget))
            {
                if (((Character*)newTarget)->CharacterData.Health == 0 && actionData.ActionCategory.RowId != 15)
                {
                    continue;
                }
            }

            bool targetValid = (canTargetHostile && isEnemy) || (canTargetAlly && !isEnemy) || (canTargetSelf && isSelf) || actionData.TargetArea;
            if (!targetValid)
            {
                continue;
            }

            if (item.HpRatio < 1.0f)
            {
                var hpRatio = Extensions.GetHealthRatio(newTarget);
                if (hpRatio > item.HpRatio) continue;
            }

            if (item.StatusID != 0)
            {
                var statusManager = Extensions.GetStatusManager(newTarget);
                bool hasStatus = statusManager != null && statusManager->HasStatus(item.StatusID);
                if (item.MissingStatus && hasStatus) continue;
                if (!item.MissingStatus && !hasStatus) continue;
            }

            if (useRange && Game.IsActionOutOfRange(newID, newTarget)) continue;

            // We MUST check recast/casting (true, true) to get status 573/579.
            // If we pass (false, false), GetActionStatus returns 0 (Ready) even if on CD, causing the stack to pick the first item and fail to execute.
            var status = actionManager->CS.GetActionStatus(ActionType.Action, newID, newTarget->EntityId, true, true);
            if (status != 0)
            {
                // 573: Action not yet ready (Recast/AnimLock)
                // 579: Cannot use while casting
                if (status == 573 || status == 579)
                {
                    bool cdCheckPassed = false;
                    try
                    {
                        // Check if we have charges available. If so, we can ignore the Recast timer.
                        // Cast ActionType to uint as per ClientStructs signature.
                        // Assuming GetCurrentCharges takes only ID.
                        var maxCharges = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.GetMaxCharges((uint)ActionType.Action, newID);
                        if (maxCharges > 1 && actionManager->CS.GetCurrentCharges(newID) > 0)
                        {
                            // We have charges, allow it.
                            cdCheckPassed = true;
                        }
                        else
                        {
                            float elapsed = actionManager->CS.GetRecastTimeElapsed(ActionType.Action, newID);
                            float total = actionManager->CS.GetRecastTime(ActionType.Action, newID);
                            float remaining = total - elapsed;

                            bool isGCD = actionData.ActionCategory.RowId is 1 or 2;
                            bool isCasting = DalamudApi.ClientState.LocalPlayer!.IsCasting;

                            // If we are casting, we generally cannot use oGCDs (unless it's late weave? But game rejects "UseAction" for oGCD during cast bar).
                            // If !isGCD and isCasting, we should likely SKIP this action to let the stack find something else, or let the original GCD queue.
                            if (!isGCD && isCasting)
                            {
                                DalamudApi.LogDebug($"[ActionStacksEX] Skipping {newID} (oGCD) because player IsCasting");
                                cdCheckPassed = false;
                            }
                            // If it's a GCD and we're NOT casting, OR it's an oGCD:
                            // Allow queuing if within 0.5s window.
                            else if ((isGCD && isCasting) || remaining <= 0.5f)
                            {
                                cdCheckPassed = true;
                            }
                            else
                            {
                                DalamudApi.LogDebug($"[ActionStacksEX] Skipping {newID} due to CD {remaining:F2}s (Status {status})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DalamudApi.LogError($"[ActionStacksEX] Error checking CD/Charges for {newID}: {ex.Message}");
                        // On error, behave like original: if status != 0, skip.
                        // But since we are here, status IS != 0.
                        // We'll treat it as 'failed check' to be safe.
                        cdCheckPassed = false; 
                    }

                    if (!cdCheckPassed) continue;
                }
                else
                {
                    // Other errors (MP, Range, Status, etc) -> Invalid target/action.
                    DalamudApi.LogDebug($"[ActionStacksEX] Skipping {newID} due to Status {status}");
                    continue;
                }
            }


            action = newID;
            target = Game.GetObjectID(newTarget);
            return true;
        }

        return false;
    }



    private static void SetInstantGroundTarget(ActionManager* actionManager, uint actionType, uint useType)
    {
        if ((ActionStacksEX.Config.EnableBlockMiscInstantGroundTargets && actionType == 11) || useType == 2 && actionType == 1 || actionType == 15) return;
        actionManager->activateGroundTarget = 1;
    }
}
