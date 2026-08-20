using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace TIMF.Bridge
{
    /// <summary>
    /// Drives the TIMF host on tModLoader's lifecycle:
    ///   PostSetupContent      → discover + Load every hosted TIMF client mod (all mods loaded by now)
    ///   UpdateUI              → capture GameTime + poll keybinds
    ///   ModifyInterfaceLayers → insert a UI-space layer (under the cursor) that runs one UI frame
    ///   PostDrawFullscreenMap → dispatch map-overlay hooks in fullscreen-map space
    ///   OnModUnload           → Unload hosted mods and dispose UI resources
    /// </summary>
    public sealed class TimfBridgeSystem : ModSystem
    {
        /// <summary>Shared host, exposed so the bridge ModPlayer can dispatch info-accessory hooks.</summary>
        internal static TimfHost Host { get; private set; }

        private TimfHost _host;

        public override void PostSetupContent()
        {
            if (Main.dedServ)
                return; // client-only bridge

            _host = new TimfHost(new BridgeLogger(Mod.Logger, "Bridge"));
            _host.Start(Mod, Mod.Logger);
            Host = _host;
        }

        public override void OnModUnload()
        {
            try { _host?.Stop(); }
            finally
            {
                _host = null;
                Host = null;
            }
        }

        public override void UpdateUI(GameTime gameTime)
        {
            if (_host == null)
                return;
            _host.SetGameTime(gameTime);
            _host.PollKeybinds();
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            if (_host == null || Main.dedServ)
                return;

            // Draw TIMF UI in UI space, just under the vanilla cursor so panels sit beneath the pointer.
            var layer = new LegacyGameInterfaceLayer(
                "TimfBridge: UI",
                delegate
                {
                    _host.RunUiPass();
                    return true;
                },
                InterfaceScaleType.UI);

            var idx = layers.FindIndex(l => l.Name == "Vanilla: Cursor");
            if (idx < 0)
                layers.Add(layer);
            else
                layers.Insert(idx, layer);
        }

        public override void PostDrawFullscreenMap(ref string mouseText)
        {
            _host?.RunMapOverlay(ref mouseText);
        }
    }
}
