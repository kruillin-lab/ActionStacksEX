using System;
using System.Numerics;
using Hypostasis.Game.Structures;
using ActionStacksEX.Modules;

namespace ActionStacksEX.Modules;

public class SpatialIntelligence : PluginModule
{
    public override bool ShouldEnable
        => ActionStacksEX.Config.EnableOptimalAoE
        || ActionStacksEX.Config.EnablePartyCentroidGroundTargeting;

    protected override bool Validate() => true;

    protected override unsafe void Enable()
    {
        ActionStackManager.PreActionStack += OnPreActionStack;
    }

    protected override unsafe void Disable()
    {
        ActionStackManager.PreActionStack -= OnPreActionStack;
    }

    private unsafe void OnPreActionStack(
        Hypostasis.Game.Structures.ActionManager* actionManager,
        ref uint actionType, ref uint actionID, ref uint adjustedActionID,
        ref ulong targetObjectID, ref uint param, uint useType, ref int pvp,
        out bool? ret)
    {
        ret = null;

        if (!ActionStacksEX.Config.EnablePartyCentroidGroundTargeting) return;
        if (actionType != 1) return;
        if (!ActionStacksEX.actionSheet.TryGetValue(adjustedActionID, out var actionData)) return;
        if (!actionData.TargetArea) return;

        var centroid = SpatialMath.GetPartyCentroid();
        if (centroid == Vector3.Zero) return;

        ActionStackManager.SetPendingGroundTargetCentroid(centroid);
    }
}
