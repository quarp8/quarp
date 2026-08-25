using System.Runtime.InteropServices;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The machine's own clipboard, behind <see cref="ITextClipboard"/> — REFERENCES-EDITORS §8
/// item 2 ("системный буфер обмена") for the code editor's Ctrl+X / Ctrl+C / Ctrl+V. All three
/// references use the real one and nothing less: TIC-80 goes through
/// <c>tic_sys_clipboard_set</c> / <c>_get</c> (§1), LIKO-12's code editor uses "системный,
/// plain text" (§4.2), PICO-8's manual is "CTRL-X, C, V to cut copy or paste selected" (§4.3).
/// An editor whose copy cannot be pasted into a browser is not the thing any of them shipped.
///
/// <para><b>Why this file is in the wiring layer and the seam is not.</b> There is no clipboard
/// in MonoGame's public API — not in 3.8.5, not anywhere in the DesktopGL surface — so the only
/// road to it is the SDL2 the DesktopGL backend already loads into this process, reached the way
/// <see cref="FilePicker"/> reaches <c>comdlg32</c>: one straight P/Invoke, no package, per the
/// owner's standing rule on third parties. That makes this type a <b>host device</b>, and host
/// devices live in layer 4 with the window and the input readers. Everything above it —
/// <see cref="CodeEditorSession"/>, <see cref="CodeEditorView"/>, <see cref="CodeEditorInput"/>,
/// <see cref="CodeEditorRenderer"/> — knows only the two-verb interface, which is what keeps the
/// whole editor headless-testable: a test constructs the view with an
/// <see cref="InMemoryTextClipboard"/> and no operating system is involved in "Ctrl+C then
/// Ctrl+V puts the text back".</para>
///
/// <para><b>Bound by hand rather than by <c>DllImport</c>, and that is the point.</b> The
/// library's file name differs per platform (<c>SDL2.dll</c>, <c>libSDL2-2.0.so.0</c>,
/// <c>libSDL2-2.0.0.dylib</c>) and there are hosts where it is simply not there — the test
/// process, first of all, which never opens a window. A <c>DllImport</c> would turn that into a
/// <c>DllNotFoundException</c> at the first Ctrl+C; <see cref="NativeLibrary"/> turns it into a
/// <see cref="Available"/> of false and a clipboard that quietly keeps working inside the
/// editor. The three entry points are looked up once, in the constructor, because a lookup per
/// keystroke would be a syscall per keystroke.</para>
///
/// <para><b>The degradation is named, never disguised.</b> If SDL is missing, or if
/// <c>SDL_SetClipboardText</c> reports a failure (a locked X11 selection, a Wayland compositor
/// that refuses a window without focus), this object stops claiming to be the system's and
/// becomes the in-process buffer for the rest of its life — <see cref="Degraded"/> says so, and
/// the two verbs stay consistent with each other from that moment on rather than reading from
/// one place and writing to another. What it never does is report a paste that did not happen.
/// </para>
///
/// <para><b>No determinism is touched.</b> A cartridge cannot reach this type: nothing in
/// <c>Quarp.Core</c>, <c>Quarp.Api</c> or <see cref="CartSession"/> references it, and the text
/// it moves never enters a framebuffer that <c>FrameHash</c> sees.</para>
/// </summary>
public sealed class SystemTextClipboard : ITextClipboard
{
    /// <summary>
    /// The names DesktopGL's own loader tries, in its order. The plain "SDL2" is last: on the
    /// platforms above it is the wrong name, and on the ones where the runtime resolves it, it
    /// resolves to the same object the specific names would have found.
    /// </summary>
    private static readonly string[] _libraryNames =
    {
        "SDL2.dll", "libSDL2-2.0.so.0", "libSDL2-2.0.0.dylib", "SDL2",
    };

    // Bound once. Null together — a half-bound clipboard would be worse than none, because the
    // half that works would make the half that does not look like a paste of nothing.
    private readonly SdlSetClipboardText? _set;
    private readonly SdlGetClipboardText? _get;
    private readonly SdlFree? _free;

    /// <summary>Where the text goes once this object has stopped being the system's. Also the whole of it when SDL was never found.</summary>
    private readonly InMemoryTextClipboard _fallback = new();

    private bool _degraded;

    /// <summary>Looks SDL up once. Never throws: a host without it gets an in-process buffer and a false <see cref="Available"/>.</summary>
    public SystemTextClipboard()
    {
        if (TryBind(out IntPtr set, out IntPtr get, out IntPtr free))
        {
            _set = Marshal.GetDelegateForFunctionPointer<SdlSetClipboardText>(set);
            _get = Marshal.GetDelegateForFunctionPointer<SdlGetClipboardText>(get);
            _free = Marshal.GetDelegateForFunctionPointer<SdlFree>(free);
        }
    }

    /// <summary>True when all three SDL entry points were found. False on a host without SDL — a test process, most of all.</summary>
    public bool Available => _set is not null;

    /// <summary>
    /// True once this object has given up on the system clipboard — either it was never there
    /// (<see cref="Available"/> is false) or a write came back with an error. From then on it is
    /// an in-process buffer and says so, which is the one thing the report about this wave must
    /// be able to state as a fact rather than a hope.
    /// </summary>
    public bool Degraded => !Available || _degraded;

    /// <summary>
    /// What the machine holds, as text. SDL hands back a buffer it owns and expects
    /// <c>SDL_free</c> back — not <c>Marshal.FreeHGlobal</c>: it was allocated by SDL's
    /// allocator, and freeing it with the wrong one is a heap corruption that shows up
    /// somewhere else entirely.
    /// </summary>
    public string Read()
    {
        if (Degraded)
        {
            return _fallback.Read();
        }
        IntPtr text = _get!();
        if (text == IntPtr.Zero)
        {
            return string.Empty;        // documented as "never null", but a null here is an empty paste, not a crash
        }
        try
        {
            return Marshal.PtrToStringUTF8(text) ?? string.Empty;
        }
        finally
        {
            _free!(text);
        }
    }

    /// <summary>
    /// Puts text on the machine's clipboard as UTF-8. A refusal from SDL is not swallowed and
    /// not thrown either: the object degrades once and keeps the text, so the author's Ctrl+X is
    /// still followed by a working Ctrl+V inside this editor.
    /// </summary>
    public void Write(string text)
    {
        string value = text ?? string.Empty;
        if (Degraded)
        {
            _fallback.Write(value);
            return;
        }
        IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            if (_set!(utf8) != 0)
            {
                _degraded = true;
                _fallback.Write(value);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    /// <summary>
    /// The three symbols, or nothing. Tries each library name in turn and takes the first handle
    /// that carries all three exports; a handle missing one of them is not a usable SDL and is
    /// passed over rather than half-used.
    /// </summary>
    private static bool TryBind(out IntPtr set, out IntPtr get, out IntPtr free)
    {
        set = IntPtr.Zero;
        get = IntPtr.Zero;
        free = IntPtr.Zero;
        foreach (string name in _libraryNames)
        {
            if (!NativeLibrary.TryLoad(name, out IntPtr handle))
            {
                continue;
            }
            if (NativeLibrary.TryGetExport(handle, "SDL_SetClipboardText", out set)
                && NativeLibrary.TryGetExport(handle, "SDL_GetClipboardText", out get)
                && NativeLibrary.TryGetExport(handle, "SDL_free", out free))
            {
                return true;
            }
        }
        set = IntPtr.Zero;
        get = IntPtr.Zero;
        free = IntPtr.Zero;
        return false;
    }

    /// <summary>SDL2: <c>int SDL_SetClipboardText(const char *text)</c> — zero on success.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlSetClipboardText(IntPtr utf8);

    /// <summary>SDL2: <c>char *SDL_GetClipboardText(void)</c> — caller frees with <c>SDL_free</c>.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetClipboardText();

    /// <summary>SDL2: <c>void SDL_free(void *mem)</c>.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlFree(IntPtr memory);
}
