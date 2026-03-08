using Hypostasis.Game.Structures;

namespace ActionStacksEX.Modules;

public class ActionStacks : PluginModule
{
    protected override bool Validate() => Common.getGameObjectFromPronounID.IsValid && ActionManager.canUseActionOnGameObject.IsValid;
}
