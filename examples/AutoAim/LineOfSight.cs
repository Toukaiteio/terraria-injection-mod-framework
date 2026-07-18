using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;

namespace AutoAim
{
    /// <summary>
    /// Wraps the vanilla line-of-sight test Collision.CanHit(Vector2,int,int,Vector2,int,int)
    /// via reflection, so "don't ignore walls" uses the game's own tile check rather than a
    /// hand-rolled ray.
    /// </summary>
    internal sealed class LineOfSight
    {
        private readonly ILogger _log;
        private MethodInfo _canHit;
        private bool _resolved;

        public LineOfSight(ILogger log)
        {
            _log = log;
        }

        public bool CanReach(Vector2 pos1, int w1, int h1, Vector2 pos2, int w2, int h2)
        {
            if (!Resolve())
                return true; // if we cannot test, don't block targeting

            try
            {
                var result = _canHit.Invoke(null, new object[] { pos1, w1, h1, pos2, w2, h2 });
                return result is bool b && b;
            }
            catch (Exception ex)
            {
                _log.Error("Collision.CanHit invoke failed", ex);
                return true;
            }
        }

        private bool Resolve()
        {
            if (_resolved)
                return _canHit != null;
            _resolved = true;

            try
            {
                var collision = typeof(Main).Assembly.GetType("Terraria.Collision");
                if (collision == null)
                {
                    _log.Warn("Terraria.Collision type not found; wall checks disabled");
                    return false;
                }

                _canHit = collision.GetMethod(
                    "CanHit",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(Vector2), typeof(int), typeof(int),
                        typeof(Vector2), typeof(int), typeof(int)
                    },
                    null);

                if (_canHit == null)
                    _log.Warn("Collision.CanHit(Vector2,int,int,Vector2,int,int) not found; wall checks disabled");

                return _canHit != null;
            }
            catch (Exception ex)
            {
                _log.Error("Collision.CanHit reflection failed", ex);
                return false;
            }
        }
    }
}
