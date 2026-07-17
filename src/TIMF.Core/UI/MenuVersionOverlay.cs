using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TIMF.Abstractions;

namespace TIMF.Core.UI
{
    /// <summary>
    /// Draws TIMF version above the vanilla game version on the main menu.
    /// Prefer <see cref="DrawInMenuBatch"/> (Harmony postfix after DrawVersionNumber)
    /// so we share the vanilla SpriteBatch state and avoid layer/compositing mismatch.
    /// </summary>
    internal sealed class MenuVersionOverlay
    {
        private readonly ILogger _log;
        private bool _loggedError;
        private bool _resolved;
        private bool _resolveFailed;
        private object _mouseTextAsset;
        private PropertyInfo _assetValueProp;
        private MethodInfo _measureString;
        private MethodInfo _drawString;
        private int _drawStringArgCount;

        public MenuVersionOverlay(ILogger log)
        {
            _log = log;
        }

        /// <summary>
        /// Called from Harmony postfix while Main.spriteBatch is still the menu batch.
        /// Do NOT Begin/End here.
        /// </summary>
        public void DrawInMenuBatch(Color menuColor, float upBump)
        {
            try
            {
                if (!Main.gameMenu || Main.dedServ)
                    return;
                if (Main.spriteBatch == null)
                    return;

                try
                {
                    if (WorldGen.drunkWorldGen)
                        return;
                }
                catch { /* ignore */ }

                if (!_resolved && !_resolveFailed)
                    ResolveFonts();
                if (_resolveFailed || _mouseTextAsset == null)
                    return;

                var font = _assetValueProp.GetValue(_mouseTextAsset, null);
                if (font == null)
                    return;

                var gameVersion = Main.versionNumber ?? "";
                var timfText = TimfInfo.MenuVersionText;

                var gameSize = (Vector2)_measureString.Invoke(font, new object[] { gameVersion });
                var timfSize = (Vector2)_measureString.Invoke(font, new object[] { timfText });

                const float leftPad = 10f;
                const float bottomPad = 2f;
                // Sit tightly above the game version line (same origin style as vanilla).
                const float gap = 2f;

                // Vanilla DrawVersionNumber:
                //   pos = (halfW + 10 + ox, screenHeight - halfH - 2 - upBump + oy), origin = half size
                var gameCenterY = Main.screenHeight - gameSize.Y * 0.5f - bottomPad - upBump;
                var gameTop = gameCenterY - gameSize.Y * 0.5f;

                var timfCenter = new Vector2(
                    leftPad + timfSize.X * 0.5f,
                    gameTop - gap - timfSize.Y * 0.5f);
                var timfOrigin = timfSize * 0.5f;

                DrawOutlinedLikeVanilla(font, timfText, timfCenter, timfOrigin, menuColor);
            }
            catch (Exception ex)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    _log.Error("MenuVersionOverlay.DrawInMenuBatch failed", ex);
                }
            }
        }

        /// <summary>
        /// Fallback path (OnPostDraw) — opens its own batch. Prefer Harmony path.
        /// </summary>
        public void Draw()
        {
            try
            {
                if (!Main.gameMenu || Main.dedServ)
                    return;
                if (Main.spriteBatch == null)
                    return;

                try
                {
                    if (WorldGen.drunkWorldGen)
                        return;
                }
                catch { /* ignore */ }

                if (!_resolved && !_resolveFailed)
                    ResolveFonts();
                if (_resolveFailed || _mouseTextAsset == null)
                    return;

                var font = _assetValueProp.GetValue(_mouseTextAsset, null);
                if (font == null)
                    return;

                var uiMatrix = Matrix.Identity;
                try
                {
                    var prop = typeof(Main).GetProperty("UIScaleMatrix", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null)
                        uiMatrix = (Matrix)prop.GetValue(null, null);
                }
                catch { /* ignore */ }

                var upBump = 0f;
                try
                {
                    if (Main.menuMode == 0)
                        upBump = 32f;
                }
                catch { /* ignore */ }

                Main.spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    null,
                    uiMatrix);
                try
                {
                    DrawInMenuBatch(GetMenuVersionColor(), upBump);
                }
                finally
                {
                    Main.spriteBatch.End();
                }
            }
            catch (Exception ex)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    _log.Error("MenuVersionOverlay.Draw failed", ex);
                }
            }
        }

        private static Color GetMenuVersionColor()
        {
            try
            {
                var tile = Main.tileColor;
                var b = (byte)((255 + tile.R * 2) / 3);
                return new Color(b, b, b, 255);
            }
            catch
            {
                return new Color(200, 200, 200, 255);
            }
        }

        private void DrawOutlinedLikeVanilla(
            object font,
            string text,
            Vector2 position,
            Vector2 origin,
            Color menuColor)
        {
            for (var i = 0; i < 5; i++)
            {
                var color = Color.Black;
                if (i == 4)
                {
                    // Exact vanilla brighten path (including G/B from updated R).
                    color = menuColor;
                    color.R = (byte)((255 + color.R) / 2);
                    color.G = (byte)((255 + color.R) / 2);
                    color.B = (byte)((255 + color.R) / 2);
                }

                color.A = (byte)(color.A * 0.3f);

                var ox = 0;
                var oy = 0;
                if (i == 0) ox = -2;
                if (i == 1) ox = 2;
                if (i == 2) oy = -2;
                if (i == 3) oy = 2;

                InvokeDrawString(font, text, position + new Vector2(ox, oy), color, origin);
            }
        }

        private void InvokeDrawString(object font, string text, Vector2 pos, Color color, Vector2 origin)
        {
            if (_drawStringArgCount >= 12)
            {
                _drawString.Invoke(null, new object[]
                {
                    Main.spriteBatch,
                    font,
                    text,
                    pos,
                    color,
                    0f,
                    origin,
                    1f,
                    SpriteEffects.None,
                    0f,
                    null,
                    null
                });
            }
            else
            {
                _drawString.Invoke(null, new object[]
                {
                    Main.spriteBatch,
                    font,
                    text,
                    pos,
                    color,
                    0f,
                    origin,
                    1f,
                    SpriteEffects.None,
                    0f
                });
            }
        }

        private void ResolveFonts()
        {
            try
            {
                var terrariaAsm = typeof(Main).Assembly;
                var fontAssetsType = terrariaAsm.GetType("Terraria.GameContent.FontAssets");
                if (fontAssetsType == null)
                    throw new InvalidOperationException("FontAssets type not found");

                var field = fontAssetsType.GetField("MouseText", BindingFlags.Public | BindingFlags.Static);
                if (field == null)
                    throw new InvalidOperationException("MouseText field not found on FontAssets");

                _mouseTextAsset = field.GetValue(null);
                if (_mouseTextAsset == null)
                    return;

                _assetValueProp = _mouseTextAsset.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                if (_assetValueProp == null)
                    throw new InvalidOperationException("Asset.Value property not found");

                var fontProbe = _assetValueProp.GetValue(_mouseTextAsset, null);
                if (fontProbe == null)
                    return;

                var fontType = fontProbe.GetType();
                _measureString = fontType.GetMethod(
                    "MeasureString",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string) },
                    null);
                if (_measureString == null)
                    throw new InvalidOperationException("MeasureString not found");

                MethodInfo draw = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type ext = null;
                    try { ext = asm.GetType("ReLogic.Graphics.DynamicSpriteFontExtensionMethods"); }
                    catch { continue; }
                    if (ext == null)
                        continue;

                    foreach (var m in ext.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name != "DrawString")
                            continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 12
                            && ps[0].ParameterType == typeof(SpriteBatch)
                            && ps[2].ParameterType == typeof(string)
                            && ps[3].ParameterType == typeof(Vector2)
                            && ps[5].ParameterType == typeof(float)
                            && ps[7].ParameterType == typeof(float))
                        {
                            draw = m;
                            break;
                        }
                    }

                    if (draw == null)
                    {
                        foreach (var m in ext.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (m.Name != "DrawString")
                                continue;
                            var ps = m.GetParameters();
                            if (ps.Length == 10
                                && ps[0].ParameterType == typeof(SpriteBatch)
                                && ps[2].ParameterType == typeof(string)
                                && ps[5].ParameterType == typeof(float)
                                && ps[7].ParameterType == typeof(float))
                            {
                                draw = m;
                                break;
                            }
                        }
                    }

                    if (draw != null)
                        break;
                }

                if (draw == null)
                    throw new InvalidOperationException("DynamicSpriteFontExtensionMethods.DrawString not found");

                _drawString = draw;
                _drawStringArgCount = draw.GetParameters().Length;
                _resolved = true;
                _log.Info("MenuVersionOverlay font resolved: " + fontType.FullName);
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException && ex.Message != null && ex.Message.Contains("null"))
                    return;

                _resolveFailed = true;
                _log.Error("MenuVersionOverlay resolve failed", ex);
            }
        }
    }
}
