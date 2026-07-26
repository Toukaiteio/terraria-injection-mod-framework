using System;
using Microsoft.Xna.Framework;
using TIMF.Abstractions;

namespace TimfServerProbe
{
    /// <summary>
    /// Example <see cref="IAuthorityMod"/>: logs activate/deactivate so handshake / host paths are visible.
    ///
    /// Opts into <see cref="TimfNetProfile.Required"/>, so the host advertises this mod on the
    /// handshake and kicks peers that lack it. This is the strict end of the ladder — plain
    /// host-side balance mods should stay on the default <see cref="TimfNetProfile.Vanilla"/>
    /// so pure vanilla clients can still join (see the LootRates example).
    /// </summary>
    [TimfMod(Id = "TimfServerProbe", Net = TimfNetProfile.Required)]
    public sealed class TimfServerProbeMod : IAuthorityMod, IAuthorityLifecycle
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

        public void OnAuthorityActivate(IModContext context)
        {
            (context.Log ?? _log)?.Info(
                "TimfServerProbe OnAuthorityActivate authoritative="
                + (context.Authority != null && context.Authority.IsAuthoritative));
        }

        public void OnAuthorityDeactivate()
        {
            _log?.Info("TimfServerProbe OnAuthorityDeactivate");
        }

        public void PostDraw(GameTime gameTime)
        {
            // Authority-only sample: no draw work.
        }
    }
}

