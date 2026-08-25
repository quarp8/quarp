namespace Quarp.Shell.Desktop;

/// <summary>
/// What an icon-button knows about itself when it is drawn, gathered by the screen that owns
/// the session so the painter never learns which session type it is looking at.
///
/// <para><b>Why it has a file of its own since wave R2.</b> It lived in
/// <see cref="EditorChromeRenderer"/>'s file, which owns a <c>GraphicsDevice</c> and is
/// therefore layer 3. This struct owns nothing — five booleans, no dependencies — and once the
/// console-scale painters appeared (<see cref="ConsoleChromeRenderer"/>,
/// <see cref="SpriteEditorRenderer"/>), both of them layer 2 because they hold no device, a
/// layer-2 file was reading a layer-3 one and <c>scripts/check-modules.sh</c> said so. The fix
/// the instrument's own header prescribes is to move the type, not to rewrite the layer list —
/// the same repair <c>ShellMode</c> and <c>PixelFontMetrics</c> got in the module-split wave.
/// Not one operator moved with it.</para>
/// </summary>
public readonly record struct EditorButtonState(
    bool Active, bool Hovered, bool Dirty, bool CanUndo, bool CanRedo);
