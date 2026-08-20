using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
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

        // UI scale handling (Main.UIScaleMatrix space) so we align with the vanilla cursor.
        private float _uiScale = 1f;
        private Vector2 _mouseUi;
        private Vector2 _prevMouseUi;
        private bool _uiScaleResolved;
        private MemberInfo _uiScaleField;
        private PropertyInfo _uiMatrixProp;
        private bool _uiMatrixTried;
        private bool _loggedMatrixFail;

        private readonly Dictionary<string, WindowState> _windows = new Dictionary<string, WindowState>();
        private readonly List<DrawCmd> _cmds = new List<DrawCmd>(256);
        // Window rects for THIS frame (filled during Begin) and LAST frame (used by EarlyBlockGameInput).
        private readonly List<Rectangle> _frameWindowRects = new List<Rectangle>(8);
        private readonly List<Rectangle> _blockWindowRects = new List<Rectangle>(8);
        private RasterizerState _scissorRaster;
        private bool _loggedScissorFail;

        private WindowState _cur;
        private ChildState _child;
        private float _cursorX;
        private float _cursorY;
        private float _lineStartX;
        private float _lineHeight;
        private float _contentMaxX;
        private bool _sameLine;
        private float _sameLineSpacing;
        private float _lastItemMaxX;
        private float _lastItemY;
        private float _lastItemH;
        private bool _hasLastItem;

        // Saved layout when entering a child region.
        private float _savedCursorX;
        private float _savedCursorY;
        private float _savedLineStartX;
        private float _savedLineHeight;
        private float _savedContentMaxX;
        private bool _savedSameLine;
        private float _savedSameLineSpacing;

        private MouseState _mouse;
        private MouseState _prevMouse;
        private KeyboardState _keyboard;
        private KeyboardState _prevKeyboard;
        private bool _wantCapture;
        private bool _wantCaptureKeyboard;
        private bool _anyWindowOpen;
        private bool _lmbClick;
        private bool _lmbDown;
        private bool _lmbReleased;
        private string _activeId;
        private string _hotId;
        private string _focusedInputId;
        // Per-widget text buffer for InputFloat's editable middle field (persists while focused,
        // keyed by the same widget id InputText uses).
        private readonly Dictionary<string, string> _floatEditBuffers = new Dictionary<string, string>();
        // Track game flags we set so we can release them when UI is gone.
        // Main.blockInput is sticky: Player.Update skips CopyInto while true → no movement/use.
        // Main.blockMouse is also sticky in-world (vanilla only clears it in a few UI paths).
        private bool _ownedBlockInput;
        private bool _wantBlockInputThisFrame;
        private bool _ownedBlockMouse;
        // PlayerInput.WritingText gates Main.HandleIME() → IImeService.Enable/Disable.
        // Without it the keyInt/keyString buffer stays empty and GetInputText returns unchanged text.
        private bool _ownedWritingText;
        private bool _wantWritingTextThisFrame;
        private int _inputDiagCounter;
        private double _keyRepeatTimer;
        private Keys _lastRepeatKey = Keys.None;
        private double _frameSeconds = 1.0 / 60.0;
        private double _caretBlink;
        private int _windowStack;
        private float _nextWindowPosX = 40f;
        private float _nextWindowPosY = 80f;

        private const float Pad = 10f;
        private const float TitleH = 26f;
        private const float RowH = 24f;
        private const float WidgetW = 200f;
        private const float DefaultWindowW = 380f;
        private const float ScrollbarW = 10f;
        private const float ChildPad = 4f;
        private const float CollapseBtnSize = 16f;
        private const float CloseBtnSize = 16f;
        // Terraria MouseText MeasureString overstates height (extra line padding), so pure
        // (boxH - measureH)/2 looks optically high. Use a capped optical height + slight nudge.
        private const float TextOpticalMaxH = 16f;
        private const float TextOpticalMinH = 12f;
        private const float TextOpticalScale = 0.70f;
        private const float TextOpticalNudgeY = 1f;
        private const float BtnH = 24f;
        private const float TabH = 24f;
        private const float HeaderH = 26f;

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
        private static readonly Color ColChildBg = new Color(12, 12, 18, 180);
        private static readonly Color ColScrollBg = new Color(25, 26, 36, 220);
        private static readonly Color ColScrollThumb = new Color(90, 100, 150, 255);
        private static readonly Color ColTabIdle = new Color(40, 44, 68, 255);
        private static readonly Color ColTabHot = new Color(70, 80, 130, 255);
        private static readonly Color ColTabActive = new Color(90, 120, 210, 255);
        private static readonly Color ColTabUnderline = new Color(140, 170, 255, 255);
        private static readonly Color ColHeader = new Color(36, 40, 62, 255);
        private static readonly Color ColHeaderHot = new Color(52, 58, 90, 255);
        private static readonly Color ColHeaderOpen = new Color(48, 56, 88, 255);

        public ImmediateModeUi(ILogger log)
        {
            _log = log;
        }

        public bool IsReady => _fontResolved && !_fontFailed && _pixel != null;
        public Vector2 MousePosition => _mouseUi;
        public bool IsMouseClicked => _lmbClick;
        public bool WantCaptureMouse => _wantCapture;
        public bool WantCaptureKeyboard => _wantCaptureKeyboard;
        public bool AnyWindowOpen => _anyWindowOpen;
        public bool IsGameFocused => UiNative.IsOurProcessFocused();

        public void NewFrame(GameTime gameTime)
        {
            EnsureResources();
            _cmds.Clear();
            _frameWindowRects.Clear();
            _cur = null;
            _child = null;
            _windowStack = 0;
            _wantCapture = false;
            _wantCaptureKeyboard = false;
            _anyWindowOpen = false;
            _hotId = null;
            // InputText re-asserts these each frame while focused; if nothing does, we release.
            _wantBlockInputThisFrame = false;
            _wantWritingTextThisFrame = false;

            // Discard OS char buffer if no input was focused last frame (stale keystrokes).
            if (_focusedInputId == null)
                UiNative.ClearChars();

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

            _frameSeconds = gameTime != null ? gameTime.ElapsedGameTime.TotalSeconds : 1.0 / 60.0;

            // The game renders UI (and its cursor) under Main.UIScaleMatrix = CreateScale(uiScale).
            // We draw in that same space so our windows line up with the vanilla cursor at any UI
            // scale. XNA Mouse is in physical pixels, so convert to UI-logical coords by / uiScale.
            _uiScale = ResolveUiScale();
            var inv = _uiScale > 0.01f ? 1f / _uiScale : 1f;
            _mouseUi = new Vector2(_mouse.X * inv, _mouse.Y * inv);
            _prevMouseUi = new Vector2(_prevMouse.X * inv, _prevMouse.Y * inv);

            _lmbDown = _mouse.LeftButton == ButtonState.Pressed;
            _lmbClick = _lmbDown && _prevMouse.LeftButton == ButtonState.Released;
            _lmbReleased = !_lmbDown && _prevMouse.LeftButton == ButtonState.Pressed;

            if (!_lmbDown)
                _activeId = null;
        }

        /// <summary>
        /// Flush recorded draw commands. Prefer calling from a Harmony prefix on
        /// Main.DrawCursor so the vanilla cursor stays on top (no custom cursor).
        /// Safely Ends any open batch first — interface layers often leave SpriteBatch begun.
        /// Child regions use GPU scissor so scrolled rows cannot paint past the viewport.
        /// </summary>
        public void Render()
        {
            // Snapshot window rects for next-frame early blocking (DrawMenu runs before DrawCursor).
            _blockWindowRects.Clear();
            for (var i = 0; i < _frameWindowRects.Count; i++)
                _blockWindowRects.Add(_frameWindowRects[i]);

            // After all Begin/widgets this frame: drop focus if every window is closed, and
            // release any sticky Main.blockInput / blockMouse we own.
            FinalizeInputOwnership();

            if (_wantCapture)
                ApplyInputBlock();
            else
                ReleaseOwnedBlockMouse();

            if (_cmds.Count == 0 || Main.spriteBatch == null)
                return;

            try
            {
                EnsureResources();
                if (_pixel == null)
                    return;

                var sb = Main.spriteBatch;
                GraphicsDevice device = null;
                try
                {
                    if (Main.instance != null)
                        device = Main.instance.GraphicsDevice;
                    if (device == null && Main.graphics != null)
                        device = Main.graphics.GraphicsDevice;
                }
                catch { /* ignore */ }

                // Previous UI interface layer may still have Begin active.
                try { sb.End(); }
                catch (InvalidOperationException) { /* not begun */ }
                catch { /* ignore */ }

                EnsureScissorRaster();
                var matrix = GetUiMatrix();
                var scale = Math.Max(0.01f, _uiScale);

                Rectangle? prevScissor = null;
                try
                {
                    if (device != null)
                        prevScissor = device.ScissorRectangle;
                }
                catch { /* ignore */ }

                // Group consecutive commands that share the same clip rect so we only
                // re-Begin the batch when the scissor region actually changes.
                var i = 0;
                while (i < _cmds.Count)
                {
                    var clip = _cmds[i].Clip;
                    var j = i + 1;
                    while (j < _cmds.Count && ClipEquals(_cmds[j].Clip, clip))
                        j++;

                    var useScissor = clip.HasValue && device != null && _scissorRaster != null;
                    try
                    {
                        if (useScissor)
                        {
                            // Clip is stored in UI-logical coords; GPU scissor is physical pixels.
                            var uiClip = clip.Value;
                            var phys = new Rectangle(
                                (int)Math.Floor(uiClip.X * scale),
                                (int)Math.Floor(uiClip.Y * scale),
                                Math.Max(1, (int)Math.Ceiling(uiClip.Width * scale)),
                                Math.Max(1, (int)Math.Ceiling(uiClip.Height * scale)));
                            // Intersect with the backbuffer so XNA doesn't throw.
                            var vp = device.Viewport;
                            var bounds = new Rectangle(vp.X, vp.Y, vp.Width, vp.Height);
                            phys = Rectangle.Intersect(phys, bounds);
                            if (phys.Width <= 0 || phys.Height <= 0)
                            {
                                i = j;
                                continue;
                            }

                            sb.Begin(
                                SpriteSortMode.Deferred,
                                BlendState.AlphaBlend,
                                SamplerState.PointClamp,
                                DepthStencilState.None,
                                _scissorRaster,
                                null,
                                matrix);
                            device.ScissorRectangle = phys;
                        }
                        else
                        {
                            sb.Begin(
                                SpriteSortMode.Deferred,
                                BlendState.AlphaBlend,
                                SamplerState.PointClamp,
                                DepthStencilState.None,
                                RasterizerState.CullNone,
                                null,
                                matrix);
                        }

                        for (var k = i; k < j; k++)
                        {
                            var c = _cmds[k];
                            if (c.Kind == DrawKind.Rect)
                                sb.Draw(_pixel, c.Rect, c.Color);
                            else if (c.Kind == DrawKind.Text)
                                DrawText(sb, c.Text, c.Pos, c.Color);
                        }
                    }
                    finally
                    {
                        try { sb.End(); }
                        catch { /* ignore */ }
                    }

                    i = j;
                }

                // Restore previous scissor so later game draws aren't clipped.
                if (device != null && prevScissor.HasValue)
                {
                    try { device.ScissorRectangle = prevScissor.Value; }
                    catch { /* ignore */ }
                }
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

        private void EnsureScissorRaster()
        {
            if (_scissorRaster != null)
                return;
            try
            {
                _scissorRaster = new RasterizerState
                {
                    CullMode = CullMode.None,
                    ScissorTestEnable = true,
                };
            }
            catch (Exception ex)
            {
                if (!_loggedScissorFail)
                {
                    _loggedScissorFail = true;
                    _log.Error("TIMF.UI scissor RasterizerState create failed", ex);
                }
                _scissorRaster = null;
            }
        }

        private static bool ClipEquals(Rectangle? a, Rectangle? b)
        {
            if (!a.HasValue && !b.HasValue)
                return true;
            if (!a.HasValue || !b.HasValue)
                return false;
            return a.Value.Equals(b.Value);
        }

        private Rectangle? CurrentChildClip()
        {
            if (_child == null)
                return null;
            // Outer child rect (includes scrollbar gutter) — content must not paint outside it.
            return new Rectangle(
                (int)_child.OuterX,
                (int)_child.OuterY,
                Math.Max(1, (int)_child.OuterW),
                Math.Max(1, (int)_child.OuterH));
        }

        /// <summary>
        /// Block vanilla click-through for windows that were open last frame.
        /// Call before the game consumes the click (DrawMenu Prefix / early Update).
        /// Main-menu items read mouseLeft during DrawMenu — after DrawCursor is too late.
        /// </summary>
        public void EarlyBlockGameInput()
        {
            if (_blockWindowRects.Count == 0)
                return;

            try
            {
                // Same coordinate space as NewFrame widgets (physical mouse / UIScale).
                var scale = ResolveUiScale();
                var inv = scale > 0.01f ? 1f / scale : 1f;
                float mx, my;
                try
                {
                    var m = Mouse.GetState();
                    mx = m.X * inv;
                    my = m.Y * inv;
                }
                catch
                {
                    // Fallback: Main.mouseX/Y are usually already in UI-space after PlayerInput.
                    mx = Main.mouseX;
                    my = Main.mouseY;
                }

                var over = false;
                for (var i = 0; i < _blockWindowRects.Count; i++)
                {
                    if (_blockWindowRects[i].Contains((int)mx, (int)my))
                    {
                        over = true;
                        break;
                    }
                }

                if (!over)
                    return;

                ApplyInputBlock();
            }
            catch
            {
                // Never break the game loop.
            }
        }

        /// <summary>
        /// Block game click-through while the pointer is over our UI.
        /// Called from Render when WantCaptureMouse is set, and from EarlyBlockGameInput.
        /// </summary>
        private void ApplyInputBlock()
        {
            try
            {
                if (Main.LocalPlayer != null)
                    Main.LocalPlayer.mouseInterface = true;
            }
            catch
            {
                // ignore
            }

            try
            {
                // Main menu (DrawMenu) keys off mouseLeft + mouseLeftRelease, not mouseInterface.
                // These are re-sampled next frame by PlayerInput — safe to clear for this frame only.
                Main.mouseLeft = false;
                Main.mouseLeftRelease = false;
                Main.mouseRight = false;
                Main.mouseRightRelease = false;
            }
            catch
            {
                // ignore
            }

            try
            {
                // Used by some menu widgets (color sliders, inventory bits). Sticky in-world —
                // we own the set so Finalize/Release can clear it when UI no longer captures.
                Main.blockMouse = true;
                _ownedBlockMouse = true;
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Release sticky game input flags we set, and drop text focus when no window is open.
        /// Without this, closing a window that had a focused InputText leaves Main.blockInput=true
        /// forever and Player.Update never copies movement/use triggers.
        /// </summary>
        private void FinalizeInputOwnership()
        {
            if (!_anyWindowOpen)
            {
                _focusedInputId = null;
                _wantCaptureKeyboard = false;
            }

            // Only keep blockInput while a field re-requested it this frame.
            if (_ownedBlockInput && !_wantBlockInputThisFrame)
                TrySetBlockInput(false);

            // Same for WritingText (gates IME service).
            if (_ownedWritingText && !_wantWritingTextThisFrame)
                TrySetWritingText(false);
        }

        private void ReleaseOwnedBlockMouse()
        {
            if (!_ownedBlockMouse)
                return;
            try
            {
                Main.blockMouse = false;
            }
            catch
            {
                // ignore
            }
            _ownedBlockMouse = false;
        }

        /// <summary>
        /// UI-logical screen size (physical / uiScale). Sized from the device viewport: it is
        /// always physical pixels, whereas Main.screenWidth/Height flip to UI-logical values
        /// around PlayerInput.SetZoom_UI — dividing those again would double-scale and clamp
        /// window drags to ~1/UIScale² of the screen.
        /// </summary>
        private float ScreenWidthUi
        {
            get
            {
                var w = (float)PhysicalScreenSize(true);
                return _uiScale > 0.01f ? w / _uiScale : w;
            }
        }

        private float ScreenHeightUi
        {
            get
            {
                var h = (float)PhysicalScreenSize(false);
                return _uiScale > 0.01f ? h / _uiScale : h;
            }
        }

        private static int PhysicalScreenSize(bool width)
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
                    var vp = device.Viewport;
                    var v = width ? vp.Width : vp.Height;
                    if (v > 0)
                        return v;
                }
            }
            catch { /* fall through */ }

            return width ? Main.screenWidth : Main.screenHeight;
        }

        private Matrix GetUiMatrix()
        {
            try
            {
                if (_uiMatrixProp == null && !_uiMatrixTried)
                {
                    _uiMatrixTried = true;
                    _uiMatrixProp = typeof(Main).GetProperty(
                        "UIScaleMatrix", BindingFlags.Public | BindingFlags.Static);
                }

                if (_uiMatrixProp != null)
                    return (Matrix)_uiMatrixProp.GetValue(null, null);
            }
            catch (Exception ex)
            {
                if (!_loggedMatrixFail)
                {
                    _loggedMatrixFail = true;
                    _log.Error("TIMF.UI UIScaleMatrix read failed; using scale fallback", ex);
                }
            }

            // Fallback: build from resolved scale.
            return Matrix.CreateScale(_uiScale, _uiScale, 1f);
        }

        private float ResolveUiScale()
        {
            try
            {
                if (!_uiScaleResolved)
                {
                    _uiScaleResolved = true;
                    _uiScaleField = typeof(Main).GetField(
                        "UIScale", BindingFlags.Public | BindingFlags.Static)
                        ?? typeof(Main).GetProperty("UIScale", BindingFlags.Public | BindingFlags.Static) as MemberInfo;
                }

                if (_uiScaleField is FieldInfo fi)
                {
                    var v = fi.GetValue(null);
                    if (v is float f && f > 0.01f) return f;
                }
                else if (_uiScaleField is PropertyInfo pi)
                {
                    var v = pi.GetValue(null, null);
                    if (v is float f && f > 0.01f) return f;
                }
            }
            catch
            {
                // fall through
            }

            return 1f;
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
                    W = DefaultWindowW,
                    H = 200f,
                    Collapsed = false,
                };
                _nextWindowPosX += 28f;
                _nextWindowPosY += 28f;
                if (_nextWindowPosX > ScreenWidthUi * 0.5f)
                    _nextWindowPosX = 40f;
                if (_nextWindowPosY > ScreenHeightUi * 0.5f)
                    _nextWindowPosY = 80f;
                _windows[title] = st;
            }

            _cur = st;
            _child = null;
            _windowStack++;
            st.Open = true;
            _anyWindowOpen = true;

            // Title chrome layout — collapse/close hitboxes match drawn icons.
            var titleRect = new Rectangle((int)st.X, (int)st.Y, (int)st.W, (int)TitleH);
            var collapseSize = (int)CollapseBtnSize;
            var closeSize = (int)CloseBtnSize;
            var collapseRect = new Rectangle(
                (int)st.X + 6,
                (int)(st.Y + (TitleH - collapseSize) * 0.5f),
                collapseSize,
                collapseSize);
            var closeRect = new Rectangle(
                (int)(st.X + st.W - closeSize - 4),
                (int)(st.Y + (TitleH - closeSize) * 0.5f),
                closeSize,
                closeSize);

            var hitCollapse = Hit(collapseRect);
            var hitClose = Hit(closeRect);

            // Collapse / close first so they don't start a title drag on the same click.
            if (hitCollapse)
            {
                _hotId = title + "##collapse";
                _wantCapture = true;
                if (_lmbClick)
                {
                    st.Collapsed = !st.Collapsed;
                    _activeId = null;
                }
            }
            else if (hitClose)
            {
                _hotId = title + "##close";
                _wantCapture = true;
                if (_lmbClick)
                {
                    open = false;
                    st.Open = false;
                    _activeId = null;
                    _cur = null;
                    return false;
                }
            }
            else
            {
                // Title bar drag (exclude collapse / close hitboxes).
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
                    st.X += _mouseUi.X - _prevMouseUi.X;
                    st.Y += _mouseUi.Y - _prevMouseUi.Y;
                    st.X = MathHelper.Clamp(st.X, 0, Math.Max(0, ScreenWidthUi - 40));
                    st.Y = MathHelper.Clamp(st.Y, 0, Math.Max(0, ScreenHeightUi - 20));
                    _wantCapture = true;

                    // Drag moves the window — recompute chrome rects for this frame's draw.
                    titleRect = new Rectangle((int)st.X, (int)st.Y, (int)st.W, (int)TitleH);
                    collapseRect = new Rectangle(
                        (int)st.X + 6,
                        (int)(st.Y + (TitleH - collapseSize) * 0.5f),
                        collapseSize,
                        collapseSize);
                    closeRect = new Rectangle(
                        (int)(st.X + st.W - closeSize - 4),
                        (int)(st.Y + (TitleH - closeSize) * 0.5f),
                        closeSize,
                        closeSize);
                }
            }

            // Background body
            var bodyH = st.Collapsed ? TitleH : Math.Max(TitleH + Pad, st.H);
            var winRect = new Rectangle((int)st.X, (int)st.Y, (int)st.W, (int)bodyH);
            _frameWindowRects.Add(winRect);
            if (Hit(winRect))
                _wantCapture = true;

            DrawWindowChrome(st, title, titleRect, collapseRect, closeRect);

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
            _hasLastItem = false;
            return true;
        }

        public void End()
        {
            if (_windowStack > 0)
                _windowStack--;

            // Auto-close unmatched child (defensive).
            if (_child != null)
                EndChild();

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
            _child = null;
            _sameLine = false;
        }

        public bool BeginChild(string id, float height, float width = 0f)
        {
            if (_cur == null)
                return false;
            if (_child != null)
            {
                // Nested children not supported; keep balance by stacking one level only.
                EndChild();
            }

            if (string.IsNullOrEmpty(id))
                id = "child";

            AdvanceLine();

            var availW = Math.Max(40f, (_cur.X + _cur.W - Pad) - _cursorX);
            var childW = width > 1f ? Math.Min(width, availW) : availW;
            var childH = Math.Max(24f, height);
            var childX = _cursorX;
            var childY = _cursorY;

            var viewW = Math.Max(20f, childW - ScrollbarW - 2f);
            var viewRect = new Rectangle((int)childX, (int)childY, (int)childW, (int)childH);
            var contentView = new Rectangle((int)childX, (int)childY, (int)viewW, (int)childH);

            float scroll;
            if (!_cur.ChildScroll.TryGetValue(id, out scroll))
                scroll = 0f;

            var hovering = HitRaw(viewRect);
            if (hovering)
            {
                _wantCapture = true;
                var wheelDelta = _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
                if (wheelDelta != 0)
                {
                    // 120 units per notch typically.
                    scroll -= (wheelDelta / 120f) * 28f;
                }
            }

            // Child chrome (always drawn; not subject to self-clip).
            PushRectRaw(viewRect, ColChildBg);
            PushRectRaw(new Rectangle(viewRect.X, viewRect.Y, viewRect.Width, 1), ColBorder * 0.7f);
            PushRectRaw(new Rectangle(viewRect.X, viewRect.Bottom - 1, viewRect.Width, 1), ColBorder * 0.7f);
            PushRectRaw(new Rectangle(viewRect.X, viewRect.Y, 1, viewRect.Height), ColBorder * 0.7f);
            PushRectRaw(new Rectangle(viewRect.Right - 1, viewRect.Y, 1, viewRect.Height), ColBorder * 0.7f);

            // Save parent layout.
            _savedCursorX = _cursorX;
            _savedCursorY = _cursorY;
            _savedLineStartX = _lineStartX;
            _savedLineHeight = _lineHeight;
            _savedContentMaxX = _contentMaxX;
            _savedSameLine = _sameLine;
            _savedSameLineSpacing = _sameLineSpacing;

            _child = new ChildState
            {
                Id = id,
                ViewX = contentView.X,
                ViewY = contentView.Y,
                ViewW = contentView.Width,
                ViewH = contentView.Height,
                OuterX = childX,
                OuterY = childY,
                OuterW = childW,
                OuterH = childH,
                ScrollY = scroll,
                ContentStartY = childY + ChildPad - scroll,
            };

            _cursorX = childX + ChildPad;
            _cursorY = _child.ContentStartY;
            _lineStartX = _cursorX;
            _lineHeight = RowH;
            _contentMaxX = _cursorX;
            _sameLine = false;
            return true;
        }

        public void EndChild()
        {
            if (_child == null || _cur == null)
            {
                _child = null;
                return;
            }

            var ch = _child;
            // Content height measured from the scrolled content start (pre-scroll top + pad).
            var contentBottom = _cursorY + (_sameLine ? 0 : 0);
            var unscrolledTop = ch.OuterY + ChildPad;
            var contentH = Math.Max(0f, (contentBottom - ch.ContentStartY));
            // Also account for padding at bottom of content.
            contentH += ChildPad;

            var maxScroll = Math.Max(0f, contentH - ch.ViewH);
            var scroll = MathHelper.Clamp(ch.ScrollY, 0f, maxScroll);
            _cur.ChildScroll[ch.Id] = scroll;

            // Scrollbar track on the right of the outer child.
            var trackX = (int)(ch.OuterX + ch.OuterW - ScrollbarW - 1);
            var trackY = (int)ch.OuterY + 2;
            var trackH = Math.Max(4, (int)ch.OuterH - 4);
            PushRectRaw(new Rectangle(trackX, trackY, (int)ScrollbarW, trackH), ColScrollBg);

            if (maxScroll > 0.5f)
            {
                var thumbH = Math.Max(16f, trackH * (ch.ViewH / Math.Max(ch.ViewH, contentH)));
                var t = maxScroll > 0.001f ? (scroll / maxScroll) : 0f;
                var thumbY = trackY + (trackH - thumbH) * t;
                PushRectRaw(new Rectangle(trackX + 1, (int)thumbY, (int)ScrollbarW - 2, (int)thumbH), ColScrollThumb);

                // Drag scrollbar thumb.
                var thumbRect = new Rectangle(trackX, trackY, (int)ScrollbarW, trackH);
                var sid = _cur.Title + "##scroll##" + ch.Id;
                if (HitRaw(thumbRect))
                {
                    _hotId = sid;
                    _wantCapture = true;
                    if (_lmbClick)
                        _activeId = sid;
                }
                if (_activeId == sid && _lmbDown)
                {
                    var rel = MathHelper.Clamp((_mouseUi.Y - trackY) / Math.Max(1f, trackH), 0f, 1f);
                    scroll = rel * maxScroll;
                    _cur.ChildScroll[ch.Id] = scroll;
                    _wantCapture = true;
                }
            }

            // Restore parent layout below the child region.
            _contentMaxX = Math.Max(_savedContentMaxX, ch.OuterX + ch.OuterW);
            _cursorX = _savedLineStartX;
            _cursorY = ch.OuterY + ch.OuterH + 4f;
            _lineStartX = _savedLineStartX;
            _lineHeight = RowH;
            _sameLine = false;
            _child = null;
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
            var startX = _cursorX;
            var startY = _cursorY;
            PushText(text, new Vector2(_cursorX, _cursorY), color);
            var sz = Measure(text);
            var h = Math.Max(RowH, sz.Y + 4);
            _lineHeight = Math.Max(_lineHeight, h);
            MarkLastItem(startX, startY, sz.X, h);
            _cursorX = startX + sz.X;
            if (!_sameLine)
            {
                _cursorY += _lineHeight;
                _cursorX = _lineStartX;
                _lineHeight = RowH;
            }
            _sameLine = false;
        }

        public void Separator()
        {
            if (_cur == null)
                return;
            AdvanceLine();
            var y = (int)_cursorY + 4;
            var left = _child != null ? (int)_child.ViewX + 2 : (int)_cur.X + 6;
            var width = _child != null ? (int)_child.ViewW - 4 : (int)_cur.W - 12;
            PushRect(new Rectangle(left, y, width, 1), ColBorder * 0.7f);
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
            if (!_hasLastItem || _cur == null)
            {
                _sameLine = true;
                _sameLineSpacing = spacing;
                return;
            }

            // Stay on the previous item's row: restore its Y and place after its right edge.
            _cursorY = _lastItemY;
            _cursorX = _lastItemMaxX + spacing;
            _lineHeight = Math.Max(_lineHeight, _lastItemH);
            _sameLine = true;
            _sameLineSpacing = 0f; // already applied
        }

        private void MarkLastItem(float x, float y, float w, float h)
        {
            _lastItemMaxX = x + w;
            _lastItemY = y;
            _lastItemH = h;
            _hasLastItem = true;
            _contentMaxX = Math.Max(_contentMaxX, _lastItemMaxX);
        }

        public bool Button(string label)
        {
            if (_cur == null)
                return false;
            AdvanceLine();
            var text = label ?? "Button";
            var sz = Measure(text);
            var w = Math.Max(80f, sz.X + 20f);
            var h = BtnH;
            var rect = new Rectangle((int)_cursorX, (int)_cursorY, (int)w, (int)h);
            var id = _cur.Title + "##btn##" + label + ChildIdSuffix();

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
            PushTextCentered(text, rect, ColText);

            var clicked = hot && _lmbReleased && (_activeId == id || _hotId == id);
            _lineHeight = Math.Max(_lineHeight, h + 2);
            MarkLastItem(_cursorX, _cursorY, w, h + 2);
            if (!_sameLine)
            {
                _cursorY += _lineHeight;
                _cursorX = _lineStartX;
                _lineHeight = RowH;
            }
            else
            {
                _cursorX += w;
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
            var h = Math.Max(RowH, TextOpticalH(sz.Y) + 8f);
            // Full available width inside the window / child content area.
            float right;
            if (_child != null)
                right = _child.ViewX + _child.ViewW - ChildPad;
            else
                right = _cur.X + _cur.W - Pad;
            var w = Math.Max(60f, right - _cursorX);
            var rect = new Rectangle((int)_cursorX, (int)_cursorY, (int)w, (int)h);
            var id = _cur.Title + "##sel##" + label + ChildIdSuffix();

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

            PushText(
                text,
                new Vector2(rect.X + 6, TextYInBox(rect.Y, rect.Height, sz.Y)),
                ColText);

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
            var rowH = Math.Max(RowH, Math.Max(box + 4f, TextOpticalH(sz.Y) + 8f));
            var totalW = box + 8 + sz.X;
            var boxY = (int)(_cursorY + (rowH - box) * 0.5f);
            var rect = new Rectangle((int)_cursorX, boxY, box, box);
            var hit = new Rectangle((int)_cursorX, (int)_cursorY, (int)totalW + 4, (int)rowH);
            var id = _cur.Title + "##chk##" + label + ChildIdSuffix();

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

            PushText(
                text,
                new Vector2(_cursorX + box + 8, TextYInBox(_cursorY, rowH, sz.Y)),
                ColText);

            _contentMaxX = Math.Max(_contentMaxX, _cursorX + totalW);
            _lineHeight = rowH;
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
            var id = _cur.Title + "##sld##" + label + ChildIdSuffix();
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
                var u = MathHelper.Clamp((_mouseUi.X - track.X) / (float)Math.Max(1, track.Width), 0f, 1f);
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

        public bool TabBar(string id, string[] labels, ref int selectedIndex)
        {
            if (_cur == null)
                return false;
            if (labels == null || labels.Length == 0)
                return false;

            if (selectedIndex < 0)
                selectedIndex = 0;
            if (selectedIndex >= labels.Length)
                selectedIndex = labels.Length - 1;

            AdvanceLine();

            float right;
            if (_child != null)
                right = _child.ViewX + _child.ViewW - ChildPad;
            else
                right = _cur.X + _cur.W - Pad;

            var availW = Math.Max(40f, right - _cursorX);
            var startX = _cursorX;
            var x = startX;
            var y = _cursorY;
            var rowH = TabH;
            var tabPadX = 14f;
            var gap = 4f;
            var changed = false;
            var maxRight = startX;
            var barId = string.IsNullOrEmpty(id) ? "tabs" : id;

            for (var i = 0; i < labels.Length; i++)
            {
                var text = labels[i] ?? "";
                var sz = Measure(text);
                var tw = Math.Max(40f, sz.X + tabPadX * 2f);
                // Wrap to next row when the tab would overflow (keep at least one tab per row).
                if (x > startX && x + tw > startX + availW)
                {
                    x = startX;
                    y += rowH + 2f;
                }

                var rect = new Rectangle((int)x, (int)y, (int)tw, (int)rowH);
                var tid = _cur.Title + "##tab##" + barId + "##" + i + ChildIdSuffix();
                var selected = i == selectedIndex;
                var hot = Hit(rect);
                if (hot)
                {
                    _hotId = tid;
                    _wantCapture = true;
                    if (_lmbClick)
                        _activeId = tid;
                }

                var col = selected ? ColTabActive : (hot ? ColTabHot : ColTabIdle);
                PushRect(rect, col);
                // Bottom accent for the active tab.
                if (selected)
                    PushRect(new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), ColTabUnderline);
                PushRect(new Rectangle(rect.X, rect.Y, rect.Width, 1), ColBorder * 0.6f);
                PushRect(new Rectangle(rect.X, rect.Y, 1, rect.Height), ColBorder * 0.6f);
                PushRect(new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), ColBorder * 0.6f);

                PushTextCentered(text, rect, ColText);

                if (hot && _lmbReleased && (_activeId == tid || _hotId == tid) && !selected)
                {
                    selectedIndex = i;
                    changed = true;
                }

                x += tw + gap;
                if (rect.Right > maxRight)
                    maxRight = rect.Right;
            }

            _contentMaxX = Math.Max(_contentMaxX, maxRight);
            _cursorY = y + rowH + 6f;
            _cursorX = _lineStartX;
            _lineHeight = RowH;
            _sameLine = false;
            _hasLastItem = false;
            return changed;
        }

        public bool CollapsingHeader(string label, ref bool open)
        {
            if (_cur == null)
                return false;

            AdvanceLine();

            float right;
            if (_child != null)
                right = _child.ViewX + _child.ViewW - ChildPad;
            else
                right = _cur.X + _cur.W - Pad;

            var text = label ?? "";
            var sz = Measure(text);
            var h = HeaderH;
            var w = Math.Max(60f, right - _cursorX);
            var rect = new Rectangle((int)_cursorX, (int)_cursorY, (int)w, (int)h);
            var id = _cur.Title + "##hdr##" + label + ChildIdSuffix();

            var hot = Hit(rect);
            if (hot)
            {
                _hotId = id;
                _wantCapture = true;
                if (_lmbClick)
                    _activeId = id;
            }

            if (hot && _lmbReleased && (_activeId == id || _hotId == id))
                open = !open;

            var col = open ? ColHeaderOpen : (hot ? ColHeaderHot : ColHeader);
            PushRect(rect, col);
            PushRect(new Rectangle(rect.X, rect.Y, rect.Width, 1), ColBorder * 0.55f);
            PushRect(new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), ColBorder * 0.55f);

            // Chevron box on the left, optically centered.
            var chev = new Rectangle(rect.X + 6, rect.Y + (int)((h - 14) * 0.5f), 14, 14);
            DrawCollapseChevron(chev, !open, hot ? ColBtnHot : ColText, raw: false);

            var textX = chev.Right + 6;
            PushText(
                text,
                new Vector2(textX, TextYInBox(rect.Y, rect.Height, sz.Y)),
                ColText);

            _contentMaxX = Math.Max(_contentMaxX, _cursorX + w);
            _cursorY += h + 3;
            _cursorX = _lineStartX;
            _lineHeight = RowH;
            _sameLine = false;
            _hasLastItem = false;
            return open;
        }

        public bool InputFloat(string label, ref float value, float step = 0.1f)
        {
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

            var id = _cur.Title + "##if##" + label + ChildIdSuffix();

            // Editable middle field: click to focus, type a number directly, click away to commit.
            string buffer;
            if (!_floatEditBuffers.TryGetValue(id, out buffer) || !IsFocusedInput(id))
                buffer = FormatFloat(value);
            _floatEditBuffers[id] = buffer;

            var hot = Hit(valRect);
            if (hot)
                _wantCapture = true;

            if (_lmbClick)
            {
                if (hot)
                    _focusedInputId = id;
                else if (_focusedInputId == id)
                    _focusedInputId = null;
            }

            var focused = _focusedInputId == id;
            if (focused)
            {
                _wantCaptureKeyboard = true;
                TrySetBlockInput(true);
                TrySetWritingText(true);
                if (ProcessTextInput(ref buffer, 12))
                {
                    _floatEditBuffers[id] = buffer;
                    float parsed;
                    if (float.TryParse(buffer, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    {
                        if (parsed != value)
                        {
                            value = parsed;
                            changed = true;
                        }
                    }
                }
            }

            if (MiniButton(minus, "-", _cur.Title + "##if-##" + label + ChildIdSuffix()))
            {
                value -= step;
                changed = true;
                _floatEditBuffers[id] = FormatFloat(value);
            }

            PushRect(valRect, ColSliderBg);
            var border = focused ? ColSliderFill : ColBorder;
            PushRect(new Rectangle(valRect.X, valRect.Y, valRect.Width, 1), border);
            PushRect(new Rectangle(valRect.X, valRect.Bottom - 1, valRect.Width, 1), border);
            PushRect(new Rectangle(valRect.X, valRect.Y, 1, valRect.Height), border);
            PushRect(new Rectangle(valRect.Right - 1, valRect.Y, 1, valRect.Height), border);

            var shown = focused ? buffer : FormatFloat(value);
            if (focused && ((int)(_caretBlink * 2) % 2 == 0))
                shown += "|";
            if (!string.IsNullOrEmpty(shown))
            {
                var tsz = Measure(shown);
                PushText(shown, new Vector2(valRect.X + 6, TextYInBox(valRect.Y, valRect.Height, tsz.Y)), ColText);
            }

            if (MiniButton(plus, "+", _cur.Title + "##if+##" + label + ChildIdSuffix()))
            {
                value += step;
                changed = true;
                _floatEditBuffers[id] = FormatFloat(value);
            }

            _contentMaxX = Math.Max(_contentMaxX, plus.Right);
            _cursorX = oldX;
            _cursorY = oldY + RowH + 4;
            _sameLine = false;
            return changed;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private bool IsFocusedInput(string id)
        {
            return _focusedInputId == id;
        }

        public bool InputText(string label, ref string value, int maxLength = 64)
        {
            if (_cur == null)
                return false;
            AdvanceLine();

            value = value ?? "";
            var id = _cur.Title + "##txt##" + label + ChildIdSuffix();

            var lbl = (label ?? "").Trim();
            var hasLabel = lbl.Length > 0 && !lbl.StartsWith("##");
            if (hasLabel)
            {
                PushText(lbl, new Vector2(_cursorX, _cursorY + 3), ColText);
                var lsz = Measure(lbl);
                _cursorY += Math.Max(RowH - 6, lsz.Y + 2);
            }

            float right;
            if (_child != null)
                right = _child.ViewX + _child.ViewW - ChildPad;
            else
                right = _cur.X + _cur.W - Pad;
            var boxW = Math.Max(120f, right - _cursorX);
            var boxH = RowH + 2;
            var rect = new Rectangle((int)_cursorX, (int)_cursorY, (int)boxW, (int)boxH);

            var hot = Hit(rect);
            if (hot)
                _wantCapture = true;

            // Click to focus / click-away to blur.
            if (_lmbClick)
            {
                if (hot)
                    _focusedInputId = id;
                else if (_focusedInputId == id)
                    _focusedInputId = null;
            }

            var focused = _focusedInputId == id;
            var changed = false;
            if (focused)
            {
                _wantCaptureKeyboard = true;
                TrySetBlockInput(true);
                TrySetWritingText(true);
                changed = ProcessTextInput(ref value, maxLength);
            }

            // Box background + border (highlight when focused).
            PushRect(rect, ColSliderBg);
            var border = focused ? ColSliderFill : ColBorder;
            PushRect(new Rectangle(rect.X, rect.Y, rect.Width, 1), border);
            PushRect(new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), border);
            PushRect(new Rectangle(rect.X, rect.Y, 1, rect.Height), border);
            PushRect(new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), border);

            // Text (with blinking caret when focused). Empty → nothing.
            var shown = value;
            if (focused && ((int)(_caretBlink * 2) % 2 == 0))
                shown += "|";
            if (!string.IsNullOrEmpty(shown))
            {
                var tsz = Measure(shown);
                PushText(shown, new Vector2(rect.X + 6, TextYInBox(rect.Y, rect.Height, tsz.Y)), ColText);
            }

            _contentMaxX = Math.Max(_contentMaxX, _cursorX + boxW);
            _cursorY += boxH + 4;
            _cursorX = _lineStartX;
            _lineHeight = RowH;
            _sameLine = false;
            return changed;
        }

        private void TrySetBlockInput(bool value)
        {
            try
            {
                if (value)
                {
                    Main.blockInput = true;
                    _ownedBlockInput = true;
                    _wantBlockInputThisFrame = true;
                }
                else if (_ownedBlockInput)
                {
                    // Only clear if we were the ones who set it (don't clobber chat/sign/chest).
                    Main.blockInput = false;
                    _ownedBlockInput = false;
                    _wantBlockInputThisFrame = false;
                }
            }
            catch
            {
                // Field may not exist on some builds.
            }
        }

        // --- Reflection cache: FocusHelper.IsSelectedApplication (gates AllowUIInputs → GetInputText) ---
        private static FieldInfo _isSelectedField;
        private static bool _focusResolved;
        // --- Reflection cache: IImeService.Enable() ---
        private static object _imeService;
        private static MethodInfo _imeEnable;
        private static MethodInfo _imeDisable;
        private static bool _imeResolved;

        private static void ResolveFocusHelper()
        {
            if (_focusResolved) return;
            _focusResolved = true;
            try
            {
                var t = typeof(Main).Assembly.GetType("Terraria.FocusHelper");
                if (t != null)
                    _isSelectedField = t.GetField("IsSelectedApplication",
                        BindingFlags.Public | BindingFlags.Static);
            }
            catch { /* ignore */ }
        }

        private static void ResolveImeService()
        {
            if (_imeResolved) return;
            _imeResolved = true;
            try
            {
                // Platform.Get<IImeService>() — ReLogic.OS.Platform
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var platType = asm.GetType("ReLogic.OS.Platform");
                    if (platType == null) continue;
                    var getMethod = platType.GetMethod("Get",
                        BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (getMethod == null)
                    {
                        // Generic: Platform.Get<T>()
                        foreach (var m in platType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (m.Name == "Get" && m.IsGenericMethodDefinition
                                && m.GetParameters().Length == 0)
                            {
                                getMethod = m;
                                break;
                            }
                        }
                    }
                    if (getMethod == null) continue;

                    // Find IImeService type
                    Type imeType = null;
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        imeType = a.GetType("ReLogic.Localization.IME.IImeService");
                        if (imeType != null) break;
                    }
                    if (imeType == null) continue;

                    if (getMethod.IsGenericMethodDefinition)
                        getMethod = getMethod.MakeGenericMethod(imeType);

                    _imeService = getMethod.Invoke(null, null);
                    if (_imeService != null)
                    {
                        _imeEnable = imeType.GetMethod("Enable");
                        _imeDisable = imeType.GetMethod("Disable");
                    }
                    break;
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Manage PlayerInput.WritingText AND directly enable IImeService.
        /// WritingText gates Main.HandleIME() → IImeService.Enable/Disable.
        /// We also call IImeService.Enable() directly to bypass the HandleIME toggle
        /// (which runs in DrawPlayerChat layer 35, before our DrawCursor layer 37).
        /// </summary>
        private void TrySetWritingText(bool value)
        {
            try
            {
                if (value)
                {
                    PlayerInput.WritingText = true;
                    _ownedWritingText = true;
                    _wantWritingTextThisFrame = true;

                    // Directly enable IME service (don't wait for HandleIME toggle).
                    ResolveImeService();
                    try { _imeEnable?.Invoke(_imeService, null); }
                    catch { /* ignore */ }
                }
                else if (_ownedWritingText)
                {
                    // Only clear if we own it and vanilla chat/sign/chest is not active.
                    if (!Main.drawingPlayerChat && !Main.editSign && !Main.editChest)
                    {
                        PlayerInput.WritingText = false;
                        try { _imeDisable?.Invoke(_imeService, null); }
                        catch { /* ignore */ }
                    }
                    _ownedWritingText = false;
                    _wantWritingTextThisFrame = false;
                }
            }
            catch
            {
                // Property may not exist on some builds.
            }
        }

        /// <summary>
        /// Apply typed keys to the focused field.
        /// Primary: Main.GetInputText — the vanilla text input pipeline. Handles keyInt buffer
        /// (IME-composed Chinese + English), backspace with repeat, Ctrl+V/Z/X/C, and Escape.
        /// We bypass FocusHelper.AllowUIInputs by setting IsSelectedApplication=true via reflection
        /// (in the injection context the game window focus detection may fail).
        /// Fallback: ProcessKeyFallback (ASCII) + clipboard paste, if GetInputText is unavailable.
        /// </summary>
        private bool ProcessTextInput(ref string value, int maxLength)
        {
            _caretBlink += _frameSeconds;
            if (maxLength < 1)
                maxLength = 1;

            var changed = false;
            var usedGetInputText = false;

            // --- Primary: Main.GetInputText (full vanilla input pipeline) ---
            try
            {
                // Bypass FocusHelper.AllowUIInputs guard (returns IsSelectedApplication).
                ResolveFocusHelper();
                if (_isSelectedField != null)
                    _isSelectedField.SetValue(null, true);

                var before = value ?? "";
                var result = Main.GetInputText(before, false);

                // GetInputText sets inputTextEscape when Escape is pressed.
                if (Main.inputTextEscape)
                {
                    _focusedInputId = null;
                    TrySetBlockInput(false);
                    TrySetWritingText(false);
                    Main.inputTextEscape = false;
                }

                // Enter — ignore for single-line field (don't blur, just consume).
                if (Main.inputTextEnter)
                    Main.inputTextEnter = false;

                if (result != null && result.Length > maxLength)
                    result = result.Substring(0, maxLength);

                if (!string.Equals(result, before, StringComparison.Ordinal))
                {
                    value = result ?? "";
                    changed = true;
                }

                usedGetInputText = true;
            }
            catch
            {
                // GetInputText unavailable — fall through to fallback.
            }

            // --- Fallback: ProcessKeyFallback (ASCII) + clipboard paste ---
            if (!usedGetInputText)
            {
                if (ProcessKeyFallback(ref value, maxLength))
                    changed = true;

                // Ctrl+V paste.
                var ctrl = _keyboard.IsKeyDown(Keys.LeftControl) || _keyboard.IsKeyDown(Keys.RightControl);
                var vDown = _keyboard.IsKeyDown(Keys.V);
                var vFresh = vDown && _prevKeyboard.IsKeyUp(Keys.V);
                if (ctrl && vFresh && !changed)
                {
                    var clip = UiNative.GetClipboardText();
                    if (!string.IsNullOrEmpty(clip))
                    {
                        clip = clip.Replace("\r", "").Replace("\n", " ");
                        var merged = (value ?? "") + clip;
                        if (merged.Length > maxLength)
                            merged = merged.Substring(0, maxLength);
                        if (!string.Equals(merged, value, StringComparison.Ordinal))
                        {
                            value = merged;
                            changed = true;
                        }
                    }
                }

                // Escape blurs.
                if (_keyboard.IsKeyDown(Keys.Escape) && _prevKeyboard.IsKeyUp(Keys.Escape))
                {
                    _focusedInputId = null;
                    TrySetBlockInput(false);
                    TrySetWritingText(false);
                }
            }

            // --- Diagnostic: log input state every 300 frames (~5s at 60fps) ---
            if (++_inputDiagCounter >= 300)
            {
                _inputDiagCounter = 0;
                try
                {
                    _log.Info("[InputDiag] focused=" + (_focusedInputId != null)
                        + " getInputText=" + usedGetInputText
                        + " keyCount=" + Main.keyCount
                        + " writingText=" + PlayerInput.WritingText
                        + " blockInput=" + Main.blockInput
                        + " valLen=" + (value ?? "").Length);
                }
                catch { /* ignore */ }
            }

            return changed;
        }

        /// <summary>Fallback key path when GetInputText is unavailable. Handles backspace + printable ASCII.</summary>
        private bool ProcessKeyFallback(ref string value, int maxLength)
        {
            var changed = false;
            var keys = _keyboard.GetPressedKeys();
            var shift = _keyboard.IsKeyDown(Keys.LeftShift) || _keyboard.IsKeyDown(Keys.RightShift);
            var ctrl = _keyboard.IsKeyDown(Keys.LeftControl) || _keyboard.IsKeyDown(Keys.RightControl);

            foreach (var k in keys)
            {
                // Edge or repeat gating.
                var freshPress = _prevKeyboard.IsKeyUp(k);
                var isRepeat = false;
                if (!freshPress)
                {
                    if (k == _lastRepeatKey)
                    {
                        _keyRepeatTimer -= _frameSeconds;
                        if (_keyRepeatTimer <= 0)
                        {
                            isRepeat = true;
                            _keyRepeatTimer = 0.04; // fast repeat once held
                        }
                    }
                    if (!isRepeat)
                        continue;
                }
                else
                {
                    _lastRepeatKey = k;
                    _keyRepeatTimer = 0.4; // initial delay before repeat
                }

                if (k == Keys.Back)
                {
                    if (value.Length > 0)
                    {
                        value = value.Substring(0, value.Length - 1);
                        changed = true;
                    }
                    continue;
                }

                if (k == Keys.Escape || k == Keys.Enter || k == Keys.Tab)
                    continue;

                // Skip Ctrl combos (paste handled separately).
                if (ctrl)
                    continue;

                var ch = KeyToChar(k, shift);
                if (ch != '\0' && value.Length < maxLength)
                {
                    value += ch;
                    changed = true;
                }
            }

            // Reset repeat when the tracked key is released.
            if (_lastRepeatKey != Keys.None && _keyboard.IsKeyUp(_lastRepeatKey))
                _lastRepeatKey = Keys.None;

            return changed;
        }

        private static char KeyToChar(Keys k, bool shift)
        {
            // Letters
            if (k >= Keys.A && k <= Keys.Z)
            {
                var c = (char)('a' + (k - Keys.A));
                return shift ? char.ToUpperInvariant(c) : c;
            }

            // Top-row digits
            if (k >= Keys.D0 && k <= Keys.D9)
            {
                if (!shift)
                    return (char)('0' + (k - Keys.D0));
                switch (k)
                {
                    case Keys.D1: return '!';
                    case Keys.D2: return '@';
                    case Keys.D3: return '#';
                    case Keys.D4: return '$';
                    case Keys.D5: return '%';
                    case Keys.D6: return '^';
                    case Keys.D7: return '&';
                    case Keys.D8: return '*';
                    case Keys.D9: return '(';
                    case Keys.D0: return ')';
                }
            }

            // Numpad digits
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9)
                return (char)('0' + (k - Keys.NumPad0));

            switch (k)
            {
                case Keys.Space: return ' ';
                case Keys.OemMinus: return shift ? '_' : '-';
                case Keys.OemPlus: return shift ? '+' : '=';
                case Keys.OemPeriod: return shift ? '>' : '.';
                case Keys.OemComma: return shift ? '<' : ',';
                case Keys.OemQuestion: return shift ? '?' : '/';
                case Keys.OemSemicolon: return shift ? ':' : ';';
                case Keys.OemQuotes: return shift ? '"' : '\'';
                default: return '\0';
            }
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
            PushTextCentered(text, rect, ColText);
            return hot && _lmbReleased && (_activeId == id || _hotId == id);
        }

        /// <summary>
        /// Optical height of Terraria MouseText — MeasureString returns extra line padding.
        /// </summary>
        private static float TextOpticalH(float measureH)
        {
            if (measureH < 0.1f)
                return TextOpticalMinH;
            return MathHelper.Clamp(measureH * TextOpticalScale, TextOpticalMinH, TextOpticalMaxH);
        }

        /// <summary>Top Y for text so glyphs sit optically centered in a box.</summary>
        private static float TextYInBox(float boxY, float boxH, float measureH)
        {
            var oh = TextOpticalH(measureH);
            return boxY + (boxH - oh) * 0.5f + TextOpticalNudgeY;
        }

        private void PushTextCentered(string text, Rectangle rect, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;
            var sz = Measure(text);
            PushText(
                text,
                new Vector2(rect.X + (rect.Width - sz.X) * 0.5f, TextYInBox(rect.Y, rect.Height, sz.Y)),
                color);
        }

        private void AdvanceLine()
        {
            if (!_sameLine)
                return;
            // SameLine() already restored Y and advanced X; clear the flag only.
            // If SameLine was called without a prior item, apply spacing fallback.
            if (_sameLineSpacing > 0f)
                _cursorX += _sameLineSpacing;
            _sameLine = false;
            _sameLineSpacing = 0f;
        }

        private string ChildIdSuffix()
        {
            return _child != null ? "##c##" + _child.Id : "";
        }

        /// <summary>Hit test that respects active child viewport (Y clip + mouse must be over child).</summary>
        private bool Hit(Rectangle r)
        {
            if (_child != null)
            {
                var view = new Rectangle((int)_child.ViewX, (int)_child.ViewY, (int)_child.ViewW, (int)_child.ViewH);
                if (!view.Contains((int)_mouseUi.X, (int)_mouseUi.Y))
                    return false;
                // Widget must intersect the viewport in Y.
                if (r.Bottom < view.Y || r.Y > view.Bottom)
                    return false;
            }
            return r.Contains((int)_mouseUi.X, (int)_mouseUi.Y);
        }

        /// <summary>Raw hit test without child clipping (chrome, scrollbar, window frame).</summary>
        private bool HitRaw(Rectangle r)
        {
            return r.Contains((int)_mouseUi.X, (int)_mouseUi.Y);
        }

        private void DrawWindowChrome(
            WindowState st,
            string title,
            Rectangle titleRect,
            Rectangle collapseRect,
            Rectangle closeRect)
        {
            var bodyH = st.Collapsed ? TitleH : Math.Max(TitleH + Pad, st.H);
            var winRect = new Rectangle((int)st.X, (int)st.Y, (int)st.W, (int)bodyH);

            PushRectRaw(winRect, ColWinBg);
            PushRectRaw(titleRect, ColTitle);
            // border
            PushRectRaw(new Rectangle(winRect.X, winRect.Y, winRect.Width, 1), ColBorder);
            PushRectRaw(new Rectangle(winRect.X, winRect.Bottom - 1, winRect.Width, 1), ColBorder);
            PushRectRaw(new Rectangle(winRect.X, winRect.Y, 1, winRect.Height), ColBorder);
            PushRectRaw(new Rectangle(winRect.Right - 1, winRect.Y, 1, winRect.Height), ColBorder);

            // Collapse chevron (pixel triangle) — hitbox == collapseRect.
            var triColor = Hit(collapseRect) ? ColBtnHot : ColText;
            DrawCollapseChevron(collapseRect, st.Collapsed, triColor);

            // Title text starts after the collapse button so it never overlaps the icon.
            var titleX = collapseRect.Right + 6;
            var titleSz = Measure(title);
            PushText(title, new Vector2(titleX, TextYInBox(st.Y, TitleH, titleSz.Y)), ColText);

            // Close "×" optically centered in closeRect.
            var closeColor = Hit(closeRect) ? new Color(255, 120, 120, 255) : ColText;
            PushTextCentered("×", closeRect, closeColor);
        }

        /// <summary>
        /// Pixel-art chevron centered in <paramref name="btn"/>.
        /// Collapsed → ► (point right); expanded → ▼ (point down). Same 7×7 bounds either way
        /// so the icon never "jumps" relative to the hitbox.
        /// </summary>
        private void DrawCollapseChevron(Rectangle btn, bool collapsed, Color color, bool raw = true)
        {
            const int s = 7;
            var ox = btn.X + (btn.Width - s) / 2;
            var oy = btn.Y + (btn.Height - s) / 2;

            if (collapsed)
            {
                // ► right-pointing filled triangle (widths 1,2,3,4,3,2,1)
                for (var row = 0; row < s; row++)
                {
                    var w = row <= 3 ? row + 1 : (s - row);
                    var r = new Rectangle(ox + 1, oy + row, w, 1);
                    if (raw) PushRectRaw(r, color); else PushRect(r, color);
                }
            }
            else
            {
                // ▼ down-pointing filled triangle
                for (var row = 0; row < 4; row++)
                {
                    var w = s - row * 2;
                    if (w <= 0)
                        break;
                    var r = new Rectangle(ox + row, oy + 1 + row, w, 1);
                    if (raw) PushRectRaw(r, color); else PushRect(r, color);
                }
            }
        }

        private void PushRect(Rectangle r, Color c)
        {
            if (_child != null)
            {
                // Coarse reject fully-outside rows; partial overflow is handled by GPU scissor.
                var top = _child.ViewY;
                var bottom = _child.ViewY + _child.ViewH;
                if (r.Bottom < top || r.Y > bottom)
                    return;
            }
            _cmds.Add(new DrawCmd { Kind = DrawKind.Rect, Rect = r, Color = c, Clip = CurrentChildClip() });
        }

        private void PushRectRaw(Rectangle r, Color c)
        {
            // Chrome (child border/bg/scrollbar) is never scissored by the child itself.
            _cmds.Add(new DrawCmd { Kind = DrawKind.Rect, Rect = r, Color = c, Clip = null });
        }

        private void PushText(string text, Vector2 pos, Color c)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (_child != null)
            {
                var top = _child.ViewY;
                var bottom = _child.ViewY + _child.ViewH;
                // Coarse reject; GPU scissor trims partial overflow at top/bottom edges.
                if (pos.Y + RowH < top || pos.Y > bottom)
                    return;
            }
            _cmds.Add(new DrawCmd { Kind = DrawKind.Text, Text = text, Pos = pos, Color = c, Clip = CurrentChildClip() });
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
            /// <summary>UI-logical scissor rect; null = no clip for this command.</summary>
            public Rectangle? Clip;
        }

        private sealed class WindowState
        {
            public string Title;
            public float X, Y, W, H;
            public bool Collapsed;
            public bool Open = true;
            public readonly Dictionary<string, float> ChildScroll = new Dictionary<string, float>();
        }

        private sealed class ChildState
        {
            public string Id;
            public float ViewX, ViewY, ViewW, ViewH;
            public float OuterX, OuterY, OuterW, OuterH;
            public float ScrollY;
            public float ContentStartY;
        }
    }
}
