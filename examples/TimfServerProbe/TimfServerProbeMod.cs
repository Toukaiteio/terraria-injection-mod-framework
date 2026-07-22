using System;
using Microsoft.Xna.Framework;
using TIMF.Abstractions;

namespace TimfServerProbe
{
    /// <summary>
    /// Minimal server-side probe: logs activate/deactivate so handshake / SP host paths are visible.
    /// </summary>
    [TimfMod(Id = "TimfServerProbe", Side = TimfSide.Server, RequiredOnJoin = true)]
    public sealed class TimfServerProbeMod : IMod, IServerMod
    {
        private ILogger _log;

        public string Name => "TimfServerProbe";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _log = context.Log;
            _log.Info("TimfServerProbe Load (server path)");
        }

        public void Unload()
        {
            _log?.Info("TimfServerProbe Unload");
        }

        public void OnServerActivate(IModContext context)
        {
            (context.Log ?? _log)?.Info("TimfServerProbe OnServerActivate");
        }

        public void OnServerDeactivate()
        {
            _log?.Info("TimfServerProbe OnServerDeactivate");
        }

        public void PostDraw(GameTime gameTime)
        {
            // Server-only: no draw. Keep empty for IMod.
        }
    }
}

