using Terraria;
using Terraria.ModLoader;

namespace TIMF.Bridge
{
    /// <summary>
    /// Dispatches TIMF info-accessory hooks. PostUpdateEquips runs right after vanilla rebuilds the
    /// local player's info-accessory flags, so hooks can grant watch / compass / detection displays.
    /// </summary>
    public sealed class TimfBridgePlayer : ModPlayer
    {
        public override void PostUpdateEquips()
        {
            if (Main.dedServ)
                return;
            if (Player == null || Player.whoAmI != Main.myPlayer)
                return;
            TimfBridgeSystem.Host?.DispatchInfoAccessories(Player);
        }
    }
}
