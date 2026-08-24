using System.Runtime.InteropServices;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The OS file-picker behind the menu's LOAD CART (owner's decision on the boot-menu order,
/// 2026-08-24: both roads now — the dialog <em>and</em> drag-and-drop — because the phone
/// ports will need a picker-shaped road anyway). This is a deliberate departure from the
/// niche: none of the three reference consoles opens an OS dialog on desktop (the scouted
/// intersection is LOAD commands and, on PICO-8, dropping the file into the window), and
/// ADR-028 records whose call that was.
///
/// <para>Windows only, by one straight P/Invoke into <c>comdlg32</c> — no packages, per the
/// owner's standing rule on third parties. Elsewhere (the linux-arm64 CI leg, the eventual
/// uConsole) it declines with the sentence the menu shows, and the drop road is the road.
/// The owner handle is deliberately <see cref="IntPtr.Zero"/>: under DesktopGL the game
/// window's <c>Handle</c> is an SDL pointer, not an HWND, and a wrong owner is worse than
/// none. The dialog still blocks the update loop, so the shell cannot be driven while it is
/// up.</para>
/// </summary>
public static class FilePicker
{
    /// <summary>What the menu says on a platform without the dialog; the drop road always works.</summary>
    public const string DropInstead = "NO FILE DIALOG HERE - DROP A CART INTO THE WINDOW";

    private const int MaxPath = 32768;

    /// <summary>
    /// Opens the picker over .quarp8 files. False with a null <paramref name="refusal"/> is
    /// a plain cancel; false with text is "no dialog on this platform" or a dialog error —
    /// either way the text is exactly what the menu's message line should say.
    /// </summary>
    public static bool TryPick(out string path, out string? refusal)
    {
        path = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            refusal = DropInstead;
            return false;
        }
        return TryPickWindows(out path, out refusal);
    }

    private static bool TryPickWindows(out string path, out string? refusal)
    {
        path = string.Empty;
        IntPtr buffer = Marshal.AllocHGlobal(MaxPath * sizeof(char));
        try
        {
            // The buffer must arrive zeroed: the dialog reads it as the initial file name.
            for (int i = 0; i < 4; i++)
            {
                Marshal.WriteInt16(buffer, i * sizeof(char), 0);
            }
            // Every field is assigned, the unused ones to their explicit zeros: the compiler
            // cannot see comdlg32 writing this struct, and a partially initialized one would
            // draw CS0649 on every field the dialog owns.
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = IntPtr.Zero,
                hInstance = IntPtr.Zero,
                // Pairs of display\0pattern\0, closed by the marshaller's own terminator.
                lpstrFilter = "Quarp cartridge (*.quarp8)\0*.quarp8\0All files (*.*)\0*.*\0",
                lpstrCustomFilter = IntPtr.Zero,
                nMaxCustFilter = 0,
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = MaxPath,
                lpstrFileTitle = IntPtr.Zero,
                nMaxFileTitle = 0,
                lpstrInitialDir = null,
                lpstrTitle = "Load cartridge",
                // MUSTEXIST: the menu loads what the author picked, not what they typo'd.
                // NOCHANGEDIR: the default dialog changes the process's working directory,
                // and the library's cwd-relative carts/ root must not move under the shell.
                Flags = OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir | OfnExplorer | OfnHideReadOnly,
                nFileOffset = 0,
                nFileExtension = 0,
                lpstrDefExt = IntPtr.Zero,
                lCustData = IntPtr.Zero,
                lpfnHook = IntPtr.Zero,
                lpTemplateName = IntPtr.Zero,
                pvReserved = IntPtr.Zero,
                dwReserved = 0,
                FlagsEx = 0,
            };
            if (GetOpenFileNameW(ref ofn))
            {
                path = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                refusal = null;
                return path.Length > 0;
            }
            uint error = CommDlgExtendedError();
            refusal = error == 0 ? null : $"FILE DIALOG FAILED (0x{error:X})";
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int OfnHideReadOnly = 0x4;
    private const int OfnNoChangeDir = 0x8;
    private const int OfnPathMustExist = 0x800;
    private const int OfnFileMustExist = 0x1000;
    private const int OfnExplorer = 0x80000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();
}
