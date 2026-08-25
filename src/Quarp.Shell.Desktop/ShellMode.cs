namespace Quarp.Shell.Desktop;

/// <summary>
/// The faces of the one console window (ADR-026: library ↔ game ↔ editor; ADR-028 adds the
/// boot menu in front — <c>quarp</c> without arguments now lands there, and the library is
/// its first door). Since M9 stage 2
/// the editor is real: it holds a <see cref="SpriteEditorSession"/> for the cart the library's
/// bar was on. Since stage 3 the editor has two faces of its own — the sprite sheet and the
/// map of the SAME cart, two modes rather than one with a flag, because the window draws a
/// different screen and routes different input for each, and <c>QuarpGame</c>'s update and
/// draw switches are where that difference has to be visible.
///
/// <para><b>Why it sits in its own file and not next to <see cref="ShellModeMachine"/></b>
/// (M9, the module-boundary wave). This enum is vocabulary — four names, no dependencies, no
/// behaviour — while the machine is wiring: it owns transitions and the
/// <see cref="CartSession"/> lifetime. Keeping them in one file made every reader of the
/// vocabulary a reader of the wiring, and <see cref="EditorIcons.TabTarget"/> — a view-layer
/// table saying which tab means which face — was exactly such a reader. That was the shell's
/// only reference pointing from the view up into the wiring, and it was an artefact of
/// file placement, not of the code. Splitting the file removes it without moving a statement:
/// the enum is now the bottom rung the machine and the view both read down into.
/// <c>scripts/check-modules.sh</c> is what keeps it that way.</para>
/// </summary>
public enum ShellMode
{
    Library,
    Game,
    Editor,
    MapEditor,

    /// <summary>The boot screen: intro, then LIBRARY / LOAD CART / CREATE GAME (ADR-028). Appended last so the four original faces keep their values.</summary>
    Menu,

    /// <summary>
    /// The third face of the open cartridge: the text of <c>src/main.cs</c>, behind the CODE
    /// tab (M9, the code-editor screen wave). A mode of its own for the same reason the map is
    /// one — the window draws a different screen and routes different input for it, and
    /// <c>QuarpGame</c>'s update and draw switches are where that difference has to be visible.
    /// Appended after <see cref="Menu"/> so every earlier face keeps its value.
    /// </summary>
    CodeEditor,

    /// <summary>
    /// The fourth face of the open cartridge: the 64 effect slots of <c>sfx.bin</c>, behind the
    /// SOUND tab (M9, the sound-editor screen wave). A mode of its own for the reason the map
    /// and the code screens are — the window draws a different screen and routes different input
    /// for it, and <c>QuarpGame</c>'s update and draw switches are where that difference has to
    /// be visible. It is also the first face whose update has a second half: the preview APU,
    /// which is wiring and not a screen. Appended after <see cref="CodeEditor"/> so every earlier
    /// face keeps its value.
    /// </summary>
    SfxEditor,
}
