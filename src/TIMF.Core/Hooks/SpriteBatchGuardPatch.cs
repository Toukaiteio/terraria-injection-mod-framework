using System;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TIMF.Abstractions;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Closes any SpriteBatch still open when a frame begins, and reports that it had to.
    ///
    /// <c>Main.DoDraw</c> starts by walking <c>ContentThatNeedsRenderTargets</c>, and those
    /// PrepareRenderTarget calls open their own batch. If anything leaked a batch from the
    /// previous frame, the first of them dies with "Begin cannot be called again until End has
    /// been successfully called" — and the reported call stack points at that innocent
    /// renderer rather than at whatever actually leaked, one frame earlier.
    ///
    /// Closing the stray batch here turns a hard crash into a logged warning and, more
    /// usefully, tells us how often the leak happens and whether it correlates with a
    /// particular action.
    /// </summary>
    internal static class SpriteBatchGuardPatch
    {
        private static ILogger _log;
        private static long _closed;

        internal static void Install(Harmony harmony, ILogger log)
        {
            _log = log;
            try
            {
                var target = AccessTools.Method(typeof(Main), "DoDraw", new[] { typeof(Microsoft.Xna.Framework.GameTime) });
                if (target == null)
                {
                    log.Warn("SpriteBatch guard: Main.DoDraw(GameTime) not found; guard not installed");
                    return;
                }

                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(SpriteBatchGuardPatch), nameof(BeforeDoDraw)));
                log.Info("SpriteBatch guard installed on Main.DoDraw");
            }
            catch (Exception ex)
            {
                log.Error("SpriteBatch guard install failed", ex);
            }
        }

        /// <summary>Total number of frames that began with a leaked batch.</summary>
        internal static long ClosedCount => _closed;

        private static void BeforeDoDraw()
        {
            SpriteBatch sb;
            try { sb = Main.spriteBatch; }
            catch { return; }
            if (sb == null)
                return;

            // There is no public "is a batch open" query, so probe by ending one: End throws
            // InvalidOperationException when none is active, and succeeds when one leaked.
            try
            {
                sb.End();
            }
            catch (InvalidOperationException)
            {
                return;   // nothing was open, which is the normal case
            }
            catch
            {
                return;
            }

            _closed++;

            // Loud for the first few, then rare, so a per-frame leak cannot flood the log.
            if (_closed <= 5 || _closed % 600 == 0)
            {
                _log?.Warn("SpriteBatch guard: a SpriteBatch was still open at the start of DoDraw "
                           + "and has been closed (occurrence #" + _closed + "). Something leaked a "
                           + "batch during the previous frame; without this guard the frame would "
                           + "have crashed in PrepareRenderTarget.");
            }
        }
    }
}
