using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace ActionStacksEX.Modules;

public class SpellAutoAttacks : PluginModule
{
    public override bool ShouldEnable => ActionStacksEX.Config.EnableSpellAutoAttacks && !ActionStacksEX.Config.EnableSpellAutoAttacksOutOfCombat;

    protected override bool Validate() => Game.spellAutoAttackPatch.IsValid;
    protected override void Enable() => DalamudApi.Framework.Update += Update;
    protected override void Disable() => DalamudApi.Framework.Update -= Update;

    private static void Update(IFramework framework)
    {
        if (ActionStacksEX.Config.EnableSpellAutoAttacks)
        {
            if (Game.spellAutoAttackPatch.IsEnabled != DalamudApi.Condition[ConditionFlag.InCombat])
                Game.spellAutoAttackPatch.Toggle();
        }
        else
        {
            Game.spellAutoAttackPatch.Disable();
        }
    }
}
