using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace TIMF.UI
{
    /// <summary>
    /// Lightweight pure-managed immediate-mode UI for TIMF mods.
    /// Designed for overlay config panels — not a full Dear ImGui port.
    /// </summary>
    internal sealed class ImmediateModeUi : IImmediateModeUi, IUiHost
    {
        private readonly ILogger _log;
        private Texture2D _pixel;
        private bool _loggedFontFail;
        private bool _loggedDrawFail;

        // Font via reflection (embedded ReLogic fonts)
        private object _fontAsset;
        private PropertyInfo _assetValue;
        private MethodInfo _measure;
        private MethodInfo _drawString;
        private int _drawArgCount;
        private bool _fontResolved;
        private bool _fontFailed;

        // Cursor redraw via reflection (Main.DrawCursor / DrawThickCursor)
        private bool _cursorResolved;
        private MethodInfo _drawCursor;
        private MethodInfo _drawThickCursor;
        private PropertyInfo _uiScaleMatrixProp;
        private SamplerState _cursorSampler;
        private bool _loggedCursorFail;

        private readonly Dictionary<string, WindowState> _windows = new Dictionary<string, WindowState>();
        private readonly List<DrawCmd> _cmds = new List<DrawCmd>(256);

        private WindowState _cur;
        private float _cursorX;
        private float _cursorY;
        private float _lineStartX;
        private float _lineHeight;
        private float _contentMaxX;
        private bool _sameLine;
        private float _sameLineSpacing;

        private MouseState _mouse;
        private MouseState _prevMouse;
        private KeyboardState _keyboard;
        private KeyboardState _prevKeyboard;
        private bool _wantCapture;
        private bool _lmbClick;
        private bool _lmbDown;
        private bool _lmbReleased;
        private string _activeId;
        private string _hotId;
        private int _windowStack;
        private float _nextWindowPosX = 40f;
        private float _nextWindowPosY = 80f;

        private const float Pad = 10f;
        private const float TitleH = 26f;
        private const float RowH = 22f;
        private const float WidgetW = 200f;

        private static readonly Color ColWinBg = new Color(18, 18, 24, 220);
        private static readonly Color ColTitle = new Color(40, 44, 70, 240);
        private static readonly Color ColBorder = new Color(90, 100, 150, 255);
        private static readonly Color ColText = new Color(230, 230, 240, 255);
        private static readonly Color ColBtn = new Color(55, 60, 95, 255);
        private static readonly Color ColBtnHot = new Color(80, 90, 140, 255);
        private static readonly Color ColBtnActive = new Color(100, 120, 190, 255);
        private static readonly Color ColSliderBg = new Color(30, 32, 48, 255);
        private static readonly Color ColSliderFill = new Color(90, 120, 210, 255);
        private static readonly Color ColCheck = new Color(70, 160, 90, 255);

        public ImmediateModeUi(ILogger log)
        {
            _log = log;
        }

        public bool IsReady => _fontResolved && !_fontFailed && _pixel != null;
        public Vector2 MousePosition => new Vector2(_mouse.X, _mouse.Y);
        public bool IsMouseClicked => _lmbClick;
        public bool WantCaptureMouse => _wantCapture;

        public void NewFrame(GameTime gameTime)
        {
            EnsureResources();
            _cmds.Clear();
            _cur = null;
            _windowStack = 0;
            _wantCapture = false;
            _hotId = null;

            _prevMouse = _mouse;
            _prevKeyboard = _keyboard;
            try
            {
                _mouse = Mouse.GetState();
                _keyboard = Keyboard.GetState();
            }
            catch
            {
                // keep previous
            }

            _lmbDown = _mouse.LeftButton == ButtonState.Pressed;
            _lmbClick = _lmbDown && _prevMouse.LeftButton == ButtonState.Released;
            _lmbReleased = !_lmbDown && _prevMouse.LeftButton == ButtonState.Pressed;

            if (!_lmbDown)
                _activeId = null;
        }

        public void Render()
        {
            if (_cmds.Count == 0 || Main.spriteBatch == null)
                return;

            try
            {
                EnsureResources();
                if (_pixel == null)
                    return;

                var sb = Main.spriteBatch;
                sb.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    Matrix.Identity);
                try
                {
                    for (var i = 0; i < _cmds.Count; i++)
                    {
                        var c = _cmds[i];
                        if (c.Kind == DrawKind.Rect)
                        {
                            sb.Draw(_pixel, c.Rect, c.Color);
                        }
                        else if (c.Kind == DrawKind.Text)
                        {
                            DrawText(sb, c.Text, c.Pos, c.Color);
                        }
                    }
                }
                finally
                {
                    sb.End();
                }

                // The game draws the mouse cursor as the last step of Main.Draw; our overlay runs
                // in OnPostDraw (after that), so it would cover the cursor. We produced draw
                // commands this frame (a window is visible), so re-draw the cursor on top.
                DrawCursorOnTop();
            }
            catch (Exception ex)
            {
                if (!_loggedDrawFail)
                {
                    _loggedDrawFail = true;
                    _log.Error("TIMF.UI Render failed", ex);
                }
            }
        }

        /// <summary>
        /// Re-draw the vanilla mouse cursor above our overlay, mirroring
        /// Main.DrawInterface_36_Cursor(): its own SpriteBatch with UIScaleMatrix,
        /// then Main.DrawCursor(Main.DrawThickCursor(false), false).
        /// All via reflection to stay independent of compile-time ReLogic/XNA identities.
        /// </summary>
        private void DrawCursorOnTop()
        {
            try
            {
                if (Main.gameMenu)
                    return; // vanilla menu already keeps its cursor on top

                if (!ResolveCursorReflection())
                    return;

                var sb = Main.spriteBatch;
                var scaleMatrix = _uiScaleMatrixProp != null
                    ? (Matrix)_uiScaleMatrixProp.GetValue(null, null)
                    : Matrix.Identity;
                var sampler = _cursorSampler ?? SamplerState.PointClamp;

                sb.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    sampler,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    null,
                    scaleMatrix);
                try
                {
                    var bonus = _drawThickCursor.Invoke(null, new object[] { false });
                    _drawCursor.Invoke(null, new[] { bonus, (object)false });
                }
                finally
                {
                    sb.End();
                }
            }
            catch (Exception ex)
            {
                if (!_loggedCursorFail)
                {
                    _loggedCursorFail = true;
                    _log.Error("TIMF.UI cursor redraw failed", ex);
                }
            }
        }

        private bool ResolveCursorReflection()
        {
            if (_cursorResolved)
                return _drawCursor != null && _drawThickCursor != null;

            _cursorResolved = true;
            try
            {
                var mainType = typeof(Main);
                _drawThickCursor = mainType.GetMethod(
                    "DrawThickCursor",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(bool) },
                    null);
                _drawCursor = mainType.GetMethod(
                    "DrawCursor",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Vector2), typeof(bool) },
                    null);

                _uiScaleMatrixProp = mainType.GetProperty(
                    "UIScaleMatrix", BindingFlags.Public | BindingFlags.Static);

                var samplerField = mainType.GetField(
                    "SamplerStateForCursor", BindingFlags.Public | BindingFlags.Static);
                if (samplerField != null)
                    _cursorSampler = samplerField.GetValue(null) as SamplerState;

                if (_drawCursor == null || _drawThickCursor == null)
                    _log.Warn("TIMF.UI: DrawCursor/DrawThickCursor not found; cursor may render under UI");

                return _drawCursor != null && _drawThickCursor != null;
            }
            catch (Exception ex)
            {
                _log.Error("TIMF.UI cursor reflection failed", ex);
                return false;
            }
        }

        public bool Begin(string title)
        {
            bool open = true;
            return Begin(title, ref open);
        }

        public bool Begin(string title, ref bool open)
        {
            if (string.IsNullOrEmpty(title))
                title = "Window";

            if (!open)
            {
                // Still push a dummy so End() balances.
                _windowStack++;
                _cur = null;
                return false;
            }

            WindowState st;
            if (!_windows.TryGetValue(title, out st))
            {
                st = new WindowState
                {
                    Title = title,
                    X = _nextWindowPosX,
                    Y = _nextWindowPosY,
                    W = 320f,
                    H = 200f,
                    Collapsed = false,
                };
                _nextWindowPosX += 28f;
                _nextWindowPosY += 28f;
                if (_nextWindowPosX > Main.screenWidth * 0.5f)
                    _nextWindowPosX = 40f;
                if (_nextWindowPosY > Main.screenHeight * 0.5f)
                    _nextWindowPosY = 80f;
                _windows[title] = st;
            }

            _cur = st;
            _windowStack++;
            st.Open = true;

            // Title bar drag
            var titleRect = new Rectangle((int)st.X, (int)st.Y, (int)st.W, (int)TitleH);
            var idTitle = title + "##title";
            if (Hit(titleRect))
            {
                _hotId = idTitle;
                _wantCapture = true;
                if (_lmbClick)
                    _activeId = idTitle;
            }

            if (_activeId == idTitle && _lmbDown)
            {
                st.X += _mouse.X - _prevMouse.X;
                st.Y += _mouse.Y - _prevMouse.Y;
                st.X = MathHelper.Clamp(st.X, 0, Math.Max(0, Main.screenWidth - 40));
                st.Y = MathHelper.Clamp(st.Y, 0, Math.Max(0, Main.screenHeight - 20));
                _wantCapture = true;
            }

            // Collapse toggle on title double-area click edge
            var collapseRect = new Rectangle((int)st.X + 4, (int)st.Y + 4, 18, 18);
            if (Hit(collapseRect) && _lmbClick)
            {
                st.Collapsed = !st.Collapsed;
                _wantCapture = true;
            }

            // Close button
            var closeRect = new Rectangle((int)(st.X + st.W - 22), (int)st.Y + 4, 18, 18);
            if (Hit(closeRect) && _lmbClick)
            {
                open = false;
                st.Open = false;
                _wantCapture = true;
                _cur = null;
                return false;
            }

            // Background body
            var bodyH = st.Collapsed ? TitleH : Math.Max(TitleH + Pad, st.H);
            var winRect = new Rectangle((int)st.X, (int)st.Y, (int)st.W, (int)bodyH);
            if (Hit(winRect))
                _wantCapture = true;

            PushRect(winRect, ColWinBg);
            PushRect(titleRect, ColTitle);
            // border
            PushRect(new Rectangle(winRect.X, winRect.Y, winRect.Width, 1), ColBorder);
            PushRect(new Rectangle(winRect.X, winRect.Bottom - 1, winRect.Width, 1), ColBorder);
            PushRect(new Rectangle(winRect.X, winRect.Y, 1, winRect.Height), ColBorder);
            PushRect(new Rectangle(winRect.Right - 1, winRect.Y, 1, winRect.Height), ColBorder);

            PushText((st.Collapsed ? "► " : "▼ ") + title,
                new Vector2(st.X + 26, st.Y + 5), ColText);
            PushText("×", new Vector2(st.X + st.W - 18, st.Y + 4), ColText);

            if (st.Collapsed)
            {
                _cur = null;
                return false;
            }

            _cursorX = st.X + Pad;
            _cursorY = st.Y + TitleH + Pad;
            _lineStartX = _cursorX;
            _lineHeight = RowH;
            _contentMaxX = st.X + Pad;
            _sameLine = false;
            return true;
        }

        public void End()
        {
            if (_windowStack > 0)
                _windowStack--;

            if (_cur != null)
            {
                // Auto-size height to content
                var needed = (_cursorY + Pad) - _cur.Y;
                if (needed > TitleH + 40)
                    _cur.H = MathHelper.Lerp(_cur.H, needed, 0.35f);
                var neededW = (_contentMaxX + Pad) - _cur.X;
                if (neededW > 200)
                    _cur.W = Math.Max(_cur.W, neededW);
            }

            _cur = null;
            _sameLine = false;
        }

        public void Text(string text)
        {
            TextColored(text, ColText);
        }

        public void TextColored(string text, Color color)
        {
            if (_cur == null || text == null)
                return;
            AdvanceLine();
            PushText(text, new Vector2(_cursorX, _cursorY), color);
            var sz = Measure(text);
            _contentMaxX = Math.Max(_contentMaxX, _cursorX + sz.X);
            _lineHeight = Math.Max(_lineHeight, Math.Max(RowH, sz.Y + 4));
            _cursorY += _lineHeight;
            _cursorX = _lineStartX;
            _sameLine = false;
            _lineHeight = RowH;
        }

        public void Separator()
        {
            if (_cur == null)
                return;
            AdvanceLine();
            var y = (int)_cursorY + 4;
            PushRect(new Rectangle((int)_cur.X + 6, y, (int)_cur.W - 12, 1), ColBorder * 0.7f);
            _cursorY += 12;
            _cursorX = _lineStartX;
            _sameLine = false;
        }

        public void Spacing(float pixels = 6f)
        {
            if (_cur == null)
                return;
            _cursorY += Math.Max(0, pixels);
            _cursorX = _lineStartX;
            _sameLine = false;
        }

        public void SameLine(float spacing = 8f)
        {
            _sameLine = true;
            _sameLineSpacing = spacing;
        }

        public bool Button(string label)
        {
            if (_cur == null)
                return false;
            AdvanceLine();
            var text = label ?? "Button";
            var sz = Measure(text);
            var w = Math.Max(80f, sz.X + 20f);
            var h = Math.Max(RowH, sz.Y + 8f);
            var rect = new Rectangle((int)_cursorX, (int)_cursorY, (int)w, (int)h);
            var id = _cur.Title + "##btn##" + label;

            var hot = Hit(rect);
            if (hot)
            {
                _hotId = id;
                _wantCapture = true;
                if (_lmbClick)
                    _activeId = id;
            }

            var col = ColBtn;
            if (_activeId == id) col = ColBtnActive;
            else if (hot) col = ColBtnHot;

            PushRect(rect, col);
            PushText(text, new Vector2(rect.X + (w - sz.X) * 0.5f, rect.Y + (h - sz.Y) * 0.5f), ColText);

            var clicked = hot && _lmbReleased && (_activeId == id || _hotId == id);
            _contentMaxX = Math.Max(_contentMaxX, _cursorX + w);
            _lineHeight = Math.Max(_lineHeight, h + 2);
            _cursorX += w;
            if (!_sameLine)
            {
                _cursorY += _lineHeight;
                _cursorX = _lineStartX;
                _lineHeight = RowH;
            }

            _sameLine = false;
            return clicked;
        }

        public bool Selectable(string label, bool selected)
        {
            if (_cur == null)
                return false;
            AdvanceLine();
            var text = label ?? "";
            var sz = Measure(text);
            var h = Math.Max(RowH, sz.Y + 6f);
            // Full available width inside the window content area.
            var w = Math.Max(60f, (_cur.X + _cur.W - Pad) - _cursorX);
            var rect = new Rectangle((int)_cursorX, (int)_cursorY, (int)w, (int)h);
            var id = _cur.Title + "##sel##" + label;

            var hot = Hit(rect);
            if (hot)
            {
                _hotId = id;
                _wantCapture = true;
                if (_lmbClick)
                    _activeId = id;
            }

            if (selected)
                PushRect(rect, ColBtnActive);
            else if (hot)
                PushRect(rect, ColBtnHot * 0.6f);

            PushText(text, new Vector2(rect.X + 6, rect.Y + (h - sz.Y) * 0.5f), ColText);

            var clicked = hot && _lmbReleased && (_activeId == id || _hotId == id);
            _contentMaxX = Math.Max(_contentMaxX, _cursorX + w);
            _cursorY += h + 1;
            _cursorX = _lineStartX;
            _lineHeight = RowH;
            _sameLine = false;
            return clicked;
        }

        public bool Checkbox(string label, ref bool value)
        {
            if (_cur == null)
                return false;
            AdvanceLine();
            var box = 16;
            var text = label ?? "";
            var sz = Measure(text);
            var totalW = box + 8 + sz.X;
            var rect = new Rectangle((int)_cursorX, (int)_cursorY + 2, box, box);
            var hit = new Rectangle((int)_cursorX, (int)_cursorY, (int)totalW + 4, (int)Math.Max(RowH, sz.Y + 4));
            var id = _cur.Title + "##chk##" + label;

            var hot = Hit(hit);
            if (hot)
            {
                _hotId = id;
                _wantCapture = true;
                if (_lmbClick)
                    _activeId = id;
            }

            var changed = false;
            if (hot && _lmbReleased && (_activeId == id || _hotId == id))
            {
                value = !value;
                changed = true;
            }

            PushRect(rect, ColSliderBg);
            PushRect(new Rectangle(rect.X, rect.Y, rect.Width, 1), ColBorder);
            PushRect(new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), ColBorder);
            PushRect(new Rectangle(rect.X, rect.Y, 1, rect.Height), ColBorder);
            PushRect(new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), ColBorder);
            if (value)
                PushRect(new Rectangle(rect.X + 3, rect.Y + 3, box - 6, box - 6), ColCheck);

            PushText(text, new Vector2(_cursorX + box + 8, _cursorY + 2), ColText);

            _contentMaxX = Math.Max(_contentMaxX, _cursorX + totalW);
            _lineHeight = Math.Max(_lineHeight, Math.Max(RowH, sz.Y + 6));
            _cursorY += _lineHeight;
            _cursorX = _lineStartX;
            _lineHeight = RowH;
            _sameLine = false;
            return changed;
        }

        public bool SliderFloat(string label, ref float value, float min, float max)
        {
            if (_cur == null)
                return false;
            if (max < min)
            {
                var t = min;
                min = max;
                max = t;
            }

            AdvanceLine();
            var text = (label ?? "") + ": " + value.ToString("0.###");
            PushText(text, new Vector2(_cursorX, _cursorY), ColText);
            var tsz = Measure(text);
            _cursorY += Math.Max(RowH - 4, tsz.Y + 2);

            var track = new Rectangle((int)_cursorX, (int)_cursorY + 4, (int)WidgetW, 10);
            var id = _cur.Title + "##sld##" + label;
            var hot = Hit(new Rectangle(track.X - 2, track.Y - 4, track.Width + 4, track.Height + 8));
            if (hot)
            {
                _hotId = id;
                _wantCapture = true;
                if (_lmbClick)
                    _activeId = id;
            }

            var changed = false;
            if (_activeId == id && _lmbDown)
            {
                var u = MathHelper.Clamp((_mouse.X - track.X) / (float)Math.Max(1, track.Width), 0f, 1f);
                var nv = min + u * (max - min);
                if (Math.Abs(nv - value) > 1e-6f)
                {
                    value = nv;
                    changed = true;
                }

                _wantCapture = true;
            }

            PushRect(track, ColSliderBg);
            var fillW = (int)(track.Width * MathHelper.Clamp((value - min) / Math.Max(1e-6f, max - min), 0f, 1f));
            if (fillW > 0)
                PushRect(new Rectangle(track.X, track.Y, fillW, track.Height), ColSliderFill);
            var knobX = track.X + fillW - 3;
            PushRect(new Rectangle(knobX, track.Y - 3, 6, track.Height + 6), ColBtnHot);

            _contentMaxX = Math.Max(_contentMaxX, _cursorX + WidgetW);
            _cursorY += 22;
            _cursorX = _lineStartX;
            _sameLine = false;
            return changed;
        }

        public bool InputFloat(string label, ref float value, float step = 0.1f)
        {
            // Minimal: label + [-] value [+] buttons
            if (_cur == null)
                return false;
            AdvanceLine();
            var changed = false;
            PushText((label ?? "") + ":", new Vector2(_cursorX, _cursorY + 2), ColText);
            var lx = _cursorX + Measure((label ?? "") + ":").X + 8;

            var oldX = _cursorX;
            var oldY = _cursorY;
            _cursorX = lx;
            _sameLine = false;

            // Inline mini buttons without full line advance hacks
            var minus = new Rectangle((int)_cursorX, (int)_cursorY, 24, (int)RowH);
            var plus = new Rectangle((int)_cursorX + 28 + 70, (int)_cursorY, 24, (int)RowH);
            var valRect = new Rectangle((int)_cursorX + 28, (int)_cursorY, 66, (int)RowH);

            if (MiniButton(minus, "-", _cur.Title + "##if-##" + label))
            {
                value -= step;
                changed = true;
            }

            PushRect(valRect, ColSliderBg);
            var vs = value.ToString("0.###");
            var vsz = Measure(vs);
            PushText(vs, new Vector2(valRect.X + (valRect.Width - vsz.X) * 0.5f, valRect.Y + 3), ColText);

            if (MiniButton(plus, "+", _cur.Title + "##if+##" + label))
            {
                value += step;
                changed = true;
            }

            _contentMaxX = Math.Max(_contentMaxX, plus.Right);
            _cursorX = oldX;
            _cursorY = oldY + RowH + 4;
            _sameLine = false;
            return changed;
        }

        private bool MiniButton(Rectangle rect, string text, string id)
        {
            var hot = Hit(rect);
            if (hot)
            {
                _hotId = id;
                _wantCapture = true;
                if (_lmbClick)
                    _activeId = id;
            }

            var col = hot ? ColBtnHot : ColBtn;
            if (_activeId == id) col = ColBtnActive;
            PushRect(rect, col);
            var sz = Measure(text);
            PushText(text, new Vector2(rect.X + (rect.Width - sz.X) * 0.5f, rect.Y + 3), ColText);
            return hot && _lmbReleased && (_activeId == id || _hotId == id);
        }

        private void AdvanceLine()
        {
            if (!_sameLine)
                return;
            // SameLine: keep Y, advance X from previous widget end — approximate with spacing
            _cursorX += _sameLineSpacing;
            _sameLine = false;
        }

        private bool Hit(Rectangle r)
        {
            return r.Contains(_mouse.X, _mouse.Y);
        }

        private void PushRect(Rectangle r, Color c)
        {
            _cmds.Add(new DrawCmd { Kind = DrawKind.Rect, Rect = r, Color = c });
        }

        private void PushText(string text, Vector2 pos, Color c)
        {
            if (string.IsNullOrEmpty(text))
                return;
            _cmds.Add(new DrawCmd { Kind = DrawKind.Text, Text = text, Pos = pos, Color = c });
        }

        private Vector2 Measure(string text)
        {
            try
            {
                if (_fontResolved && _measure != null && _fontAsset != null)
                {
                    var font = _assetValue.GetValue(_fontAsset, null);
                    if (font != null)
                        return (Vector2)_measure.Invoke(font, new object[] { text ?? "" });
                }
            }
            catch { /* ignore */ }

            // Fallback approximate
            return new Vector2((text ?? "").Length * 8f, 16f);
        }

        private void DrawText(SpriteBatch sb, string text, Vector2 pos, Color color)
        {
            try
            {
                if (!_fontResolved || _drawString == null || _fontAsset == null)
                    return;
                var font = _assetValue.GetValue(_fontAsset, null);
                if (font == null)
                    return;

                if (_drawArgCount >= 12)
                {
                    _drawString.Invoke(null, new object[]
                    {
                        sb, font, text, pos, color, 0f, Vector2.Zero, 1f,
                        SpriteEffects.None, 0f, null, null
                    });
                }
                else
                {
                    _drawString.Invoke(null, new object[]
                    {
                        sb, font, text, pos, color, 0f, Vector2.Zero, 1f,
                        SpriteEffects.None, 0f
                    });
                }
            }
            catch (Exception ex)
            {
                if (!_loggedFontFail)
                {
                    _loggedFontFail = true;
                    _log.Error("TIMF.UI DrawText failed", ex);
                }
            }
        }

        private void EnsureResources()
        {
            if (_pixel == null || _pixel.IsDisposed)
            {
                try
                {
                    GraphicsDevice device = null;
                    if (Main.instance != null)
                        device = Main.instance.GraphicsDevice;
                    if (device == null && Main.graphics != null)
                        device = Main.graphics.GraphicsDevice;
                    if (device != null)
                    {
                        _pixel = new Texture2D(device, 1, 1);
                        _pixel.SetData(new[] { Color.White });
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("TIMF.UI pixel create failed", ex);
                }
            }

            if (!_fontResolved && !_fontFailed)
                ResolveFont();
        }

        private void ResolveFont()
        {
            try
            {
                var terrariaAsm = typeof(Main).Assembly;
                var fontAssetsType = terrariaAsm.GetType("Terraria.GameContent.FontAssets");
                if (fontAssetsType == null)
                    throw new InvalidOperationException("FontAssets not found");

                var field = fontAssetsType.GetField("MouseText", BindingFlags.Public | BindingFlags.Static);
                if (field == null)
                    throw new InvalidOperationException("MouseText not found");

                _fontAsset = field.GetValue(null);
                if (_fontAsset == null)
                    return; // not ready yet; retry next frame

                _assetValue = _fontAsset.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                var fontProbe = _assetValue?.GetValue(_fontAsset, null);
                if (fontProbe == null)
                    return;

                _measure = fontProbe.GetType().GetMethod(
                    "MeasureString",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string) },
                    null);

                MethodInfo draw = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type ext = null;
                    try { ext = asm.GetType("ReLogic.Graphics.DynamicSpriteFontExtensionMethods"); }
                    catch { continue; }
                    if (ext == null) continue;

                    foreach (var m in ext.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name != "DrawString") continue;
                        var ps = m.GetParameters();
                        if (ps.Length >= 10
                            && ps[0].ParameterType == typeof(SpriteBatch)
                            && ps[2].ParameterType == typeof(string))
                        {
                            draw = m;
                            break;
                        }
                    }

                    if (draw != null)
                        break;
                }

                if (draw == null || _measure == null)
                    throw new InvalidOperationException("font measure/draw not found");

                _drawString = draw;
                _drawArgCount = draw.GetParameters().Length;
                _fontResolved = true;
                _log.Info("TIMF.UI font resolved");
            }
            catch (Exception ex)
            {
                // Don't permanently fail if assets not ready
                if (ex is InvalidOperationException)
                {
                    _fontFailed = true;
                    _log.Error("TIMF.UI font resolve failed permanently", ex);
                }
            }
        }

        public void DisposeResources()
        {
            try
            {
                if (_pixel != null && !_pixel.IsDisposed)
                    _pixel.Dispose();
            }
            catch { /* ignore */ }

            _pixel = null;
        }

        private enum DrawKind { Rect, Text }

        private struct DrawCmd
        {
            public DrawKind Kind;
            public Rectangle Rect;
            public Vector2 Pos;
            public Color Color;
            public string Text;
        }

        private sealed class WindowState
        {
            public string Title;
            public float X, Y, W, H;
            public bool Collapsed;
            public bool Open = true;
        }
    }
}
