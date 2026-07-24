using System;
using Microsoft.Xna.Framework;
using TIMF.Abstractions;

namespace TimfServerProbe
{
    /// <summary>
    /// Example <see cref="IAuthorityMod"/>: logs activate/deactivate so handshake / host paths are visible.
    /// Handshake-visible Server side with RequiredOnJoin (vanilla clients are kicked when hosting).
    /// Prefer <see cref="IVanillaPlugin"/> for vanilla-compatible host balance mods.
    /// </summary>
    [TimfMod(Id = "TimfServerProbe", Side = TimfSide.Server, RequiredOnJoin = true)]
    public sealed class TimfServerProbeMod : IAuthorityMod, IServerMod
    {
        private ILogger _log;

        public string Name => "TimfServerProbe";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _log = context.Log;
            _log.Info(
                "TimfServerProbe Load (authority path). IsAuthoritative="
                + (context.Authority != null && context.Authority.IsAuthoritative)
                + " ClientServices=" + (context.Client != null ? "present" : "null"));
        }

        public void Unload()
        {
            _log?.Info("TimfServerProbe Unload");
        }

        public void OnServerActivate(IModContext context)
        {
            (context.Log ?? _log)?.Info(
                "TimfServerProbe OnServerActivate authoritative="
                + (context.Authority != null && context.Authority.IsAuthoritative));
        }

        public void OnServerDeactivate()
        {
            _log?.Info("TimfServerProbe OnServerDeactivate");
        }

        public void PostDraw(GameTime gameTime)
        {
            // Authority-only sample: no draw work.
        }
    }
}

