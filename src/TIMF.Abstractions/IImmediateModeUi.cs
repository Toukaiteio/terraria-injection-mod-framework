using Microsoft.Xna.Framework;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Immediate-mode UI surface provided by the TIMF.UI library mod.
    /// Call from <see cref="IMod.PostDraw"/> after resolving via <c>context.Services</c>.
    /// </summary>
    public interface IImmediateModeUi
    {
        /// <summary>True if the library is ready (texture/font resolved).</summary>
        bool IsReady { get; }

        /// <summary>Begin a floating window. Returns false if collapsed/closed (still call End).</summary>
        bool Begin(string title);

        /// <summary>Begin a window with open state. When open becomes false, window is hidden until set true.</summary>
        bool Begin(string title, ref bool open);

        void End();

        /// <summary>
        /// Begin a scrollable child region of fixed height inside the current window.
        /// Returns false if the parent window is closed/collapsed (still call EndChild).
        /// </summary>
        bool BeginChild(string id, float height, float width = 0f);

        void EndChild();

        void Text(string text);
        void TextColored(string text, Color color);
        void Separator();
        void Spacing(float pixels = 6f);
        void SameLine(float spacing = 8f);

        bool Button(string label);

        /// <summary>Full-width selectable row (for lists). Highlighted when <paramref name="selected"/>.</summary>
        bool Selectable(string label, bool selected);

        bool Checkbox(string label, ref bool value);
        bool SliderFloat(string label, ref float value, float min, float max);
        bool InputFloat(string label, ref float value, float step = 0.1f);

        /// <summary>
        /// Single-line text field. Returns true when the text changed this frame.
        /// Uses the game's input path when focused so Chinese IME works; Ctrl+V pastes.
        /// While focused it captures typing (see <see cref="WantCaptureKeyboard"/>).
        /// </summary>
        bool InputText(string label, ref string value, int maxLength = 64);

        /// <summary>Screen-space mouse position this frame (UI-logical coords).</summary>
        Vector2 MousePosition { get; }

        bool IsMouseClicked { get; }
        bool WantCaptureMouse { get; }

        /// <summary>True when a text field is focused and consuming keyboard input this frame.</summary>
        bool WantCaptureKeyboard { get; }

        /// <summary>True when any TIMF window is open this frame (visible content was submitted).</summary>
        bool AnyWindowOpen { get; }

        /// <summary>True when the game window currently has OS focus.</summary>
        bool IsGameFocused { get; }
    }
}
