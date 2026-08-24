namespace Quarp.Shell.Desktop;

/// <summary>
/// The faces of the one console window (ADR-026: library ↔ game ↔ editor). Since M9 stage 2
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
}
