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

    // Track when each stack was last successfully executed to prevent multi-casting
    private static readonly Dictionary<string, DateTime> lastStackExecution = new();
    private static readonly TimeSpan stackExecutionWindow = TimeSpan.FromMilliseconds(3000); // 3 second window

    // Track the last executed action to prevent duplicate casts
    private static uint lastExecutedAction = 0;
    private static DateTime lastExecutionTime = DateTime.MinValue;

    // Track currently executing stack to prevent multi-item casting
    private static string currentlyExecutingStack = "";
    private static DateTime stackExecutionStart = DateTime.MinValue;
    private static uint lockedTriggerAction = 0; // Track which specific trigger action is locked

    public static Bool OnUseAction(ActionManager* actionManager, uint actionType, uint actionID, ulong targetObjectID, uint param, uint useType, int pvp, bool* isGroundTarget)
    {
        try
        {
            if (DalamudApi.ObjectTable.LocalPlayer == null) return 0;

            var tryStack = useType is 0 or 1;
            if (useType == 100)
            {
                useType = 0;
                tryStack = true;
            }

            // DEBUG: Log every action use attempt
            DalamudApi.LogDebug($"[ActionStacksEX] OnUseAction called: Type={actionType}, ID={actionID}, Target={targetObjectID:X}, useType={useType}, pvp={pvp}, tryStack={tryStack}");

            var adjustedActionID = actionType == 1 ? actionManager->CS.GetAdjustedActionId(actionID) : actionID;

            // Check if we're in the middle of executing a stack (prevent multi-item casting)
            if (!string.IsNullOrEmpty(currentlyExecutingStack))
            {
                var timeSinceExec = DateTime.Now - stackExecutionStart;
                if (timeSinceExec < stackExecutionWindow)
                {
                    // Check if this is the same trigger action that started the stack
                    if (adjustedActionID == lockedTriggerAction)
                    {
                        DalamudApi.LogDebug($"[ActionStacksEX] Blocking trigger {adjustedActionID} - stack '{currentlyExecutingStack}' is executing ({timeSinceExec.TotalMilliseconds:F0}ms ago)");
                        return 0; // Block the trigger action completely
                    }
                    DalamudApi.LogDebug($"[ActionStacksEX] Stack '{currentlyExecutingStack}' is executing, but {adjustedActionID} is different trigger. Allowing.");
                }
                else
                {
                    // Window expired, clear the flag
                    DalamudApi.LogDebug($"[ActionStacksEX] Stack '{currentlyExecutingStack}' window expired. Clearing lock.");
                    currentlyExecutingStack = "";
                    lockedTriggerAction = 0;
                }
            }

            DalamudApi.LogDebug($"[ActionStacksEX] Original ID={actionID}, Adjusted ID={adjustedActionID}");

            PreUseAction?.Invoke(actionManager, ref actionType, ref actionID, ref targetObjectID, ref param, ref useType, ref pvp);

            bool? ret = null;
            PreActionStack?.Invoke(actionManager, ref actionType, ref actionID, ref adjustedActionID, ref targetObjectID, ref param, useType, ref pvp, out ret);
            if (ret.HasValue)
                return ret.Value;

            var succeeded = false;
            uint finalActionID = adjustedActionID;
            bool stackMatched = false;
            if (PluginModuleManager.GetModule<Modules.ActionStacks>().IsValid && tryStack && actionType == 1 && ActionStacksEX.actionSheet.TryGetValue(adjustedActionID, out var a))
            {
                var modifierKeys = GetModifierKeys();
                foreach (var stack in ActionStacksEX.Config.ActionStacks)
                {
                    var exactMatch = (stack.ModifierKeys & 8) != 0;
                    if (exactMatch ? stack.ModifierKeys != modifierKeys : (stack.ModifierKeys & modifierKeys) != stack.ModifierKeys) continue;

                    // Check if this stack has a trigger action set
                    if (stack.TriggerAction == 0) continue;

                    // Check if the used action matches the trigger
                    var triggerToCheck = stack.UseAdjustedTrigger
                        ? actionManager->CS.GetAdjustedActionId(stack.TriggerAction)
                        : stack.TriggerAction;

                    if (triggerToCheck != adjustedActionID) continue;

                    stackMatched = true;
                    DalamudApi.LogDebug($"[ActionStacksEX] Stack '{stack.Name}' triggered by action {adjustedActionID}");

                    // Check if this stack was recently executed (prevent multi-casting)
                    if (lastStackExecution.TryGetValue(stack.Name, out var lastExec))
                    {
                        var timeSinceLastExec = DateTime.Now - lastExec;
                        if (timeSinceLastExec < stackExecutionWindow)
                        {
                            DalamudApi.LogDebug($"[ActionStacksEX] Stack '{stack.Name}' was executed {timeSinceLastExec.TotalMilliseconds:F0}ms ago, within {stackExecutionWindow.TotalMilliseconds:F0}ms window. Allowing original action.");
                            // Don't block - let the original action execute
                            break;
                        }
                    }

                    // Check for duplicate rapid calls of the same trigger
                    var timeSinceLastAction = DateTime.Now - lastExecutionTime;
                    if (adjustedActionID == lastExecutedAction && timeSinceLastAction < TimeSpan.FromMilliseconds(100))
                    {
                        DalamudApi.LogDebug($"[ActionStacksEX] DUPLICATE: Trigger {adjustedActionID} was executed {timeSinceLastAction.TotalMilliseconds:F0}ms ago. Allowing original.");
                        // Don't block rotation - just don't process stack this time
                        break;
                    }

                    if (!CheckActionStack(actionManager, adjustedActionID, stack, useType, out var newAction, out var newTarget))
                    {
                        if (stack.BlockOriginal)
                        {
                            DalamudApi.LogDebug($"[ActionStacksEX] Stack '{stack.Name}' failed and BlockOriginal is true - blocking action");
                            return 0;
                        }
                        // Stack matched but couldn't execute - don't try other stacks, let original action through
                        DalamudApi.LogDebug($"[ActionStacksEX] Stack '{stack.Name}' failed but BlockOriginal is false - allowing original action {actionID}");
                        break;
                    }

                    actionID = newAction;
                    finalActionID = newAction;
                    targetObjectID = newTarget;
                    succeeded = true;
                    // Record execution time to prevent multi-casting
                    lastStackExecution[stack.Name] = DateTime.Now;
                    lastExecutedAction = adjustedActionID; // Track trigger action
                    lastExecutionTime = DateTime.Now;
                    // Set global lock to prevent other items from this stack executing
                    currentlyExecutingStack = stack.Name;
                    lockedTriggerAction = adjustedActionID; // Record which trigger action started this
                    stackExecutionStart = DateTime.Now;
                    DalamudApi.LogDebug($"[ActionStacksEX] Stack '{stack.Name}' SUCCESS: Executing action {newAction}. LOCKED trigger {adjustedActionID} for {stackExecutionWindow.TotalMilliseconds:F0}ms.");
                    break;
                }

                if (!stackMatched)
                {
                    DalamudApi.LogDebug($"[ActionStacksEX] No stack matched for action {adjustedActionID}");
                }
            }

            PostActionStack?.Invoke(actionManager, actionType, actionID, finalActionID, ref targetObjectID, param, useType, pvp);

            DalamudApi.LogDebug($"[ActionStacksEX] Executing action: Type={actionType}, ID={actionID}, Target={targetObjectID}, Succeeded={succeeded}");
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

            DalamudApi.LogDebug($"[ActionStacksEX] OnUseAction returning: result={result}, succeeded={succeeded}");

            // Track execution only when a stack succeeded (prevents duplicate stack casts, not normal rotation)
            if (succeeded && result != 0)
            {
                lastExecutedAction = adjustedActionID; // Track the ORIGINAL trigger action
                lastExecutionTime = DateTime.Now;
                DalamudApi.LogDebug($"[ActionStacksEX] Tracked stack execution: Trigger={adjustedActionID}, Executed={actionID}");
            }

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
        DalamudApi.LogDebug($"[ActionStacksEX] Checking stack '{stack.Name}' with {stack.Items.Count} items");
        foreach (var item in stack.Items)
        {
            if (!item.Enabled)
            {
                DalamudApi.LogDebug($"[ActionStacksEX] Item {item.ID} is disabled, skipping");
                continue;
            }

            var newID = item.ID != 0 ? actionManager->CS.GetAdjustedActionId(item.ID) : id;
            DalamudApi.LogDebug($"[ActionStacksEX] Checking item: AdjustedID={newID}, TargetID={item.TargetID}, Enabled={item.Enabled}");
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
                DalamudApi.LogDebug($"[ActionStacksEX] Item {newID}: No target found for TargetID {item.TargetID}, continuing to next item");
                continue;
            }

            if (!ActionStacksEX.actionSheet.TryGetValue(newID, out var actionData)) continue;

            // Check if player is high enough level for this action (handles level sync dungeons)
            var localPlayer = DalamudApi.ObjectTable.LocalPlayer;
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
                            bool isCasting = DalamudApi.ObjectTable.LocalPlayer!.IsCasting;

                            // If we are casting, we generally cannot use oGCDs (unless it's late weave? But game rejects "UseAction" for oGCD during cast bar).
                            // If !isGCD and isCasting, we should likely SKIP this action to let the stack find something else, or let the original GCD queue.
                            if (!isGCD && isCasting)
                            {
                                DalamudApi.LogDebug($"[ActionStacksEX] Item {newID} (oGCD) skipped - player is casting. Will try next item.");
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
            DalamudApi.LogDebug($"[ActionStacksEX] Found valid item: Action={newID}, Target={target}");
            return true;
        }

        DalamudApi.LogDebug($"[ActionStacksEX] Stack '{stack.Name}': All {stack.Items.Count} items checked, none valid. Stack will not execute.");
        return false;
    }



    private static void SetInstantGroundTarget(ActionManager* actionManager, uint actionType, uint useType)
    {
        if ((ActionStacksEX.Config.EnableBlockMiscInstantGroundTargets && actionType == 11) || useType == 2 && actionType == 1 || actionType == 15) return;
        actionManager->activateGroundTarget = 1;
    }
}
