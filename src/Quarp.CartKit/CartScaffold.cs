using System.Globalization;
using System.Text.Json;

namespace Quarp.CartKit;

/// <summary>
/// Writes a brand-new cartridge to disk — the one engine behind the CLI's <c>quarp new</c>
/// and the shell menu's CREATE GAME (M9 stage 4, the boot-menu order of 2026-08-24).
///
/// <para><b>Why this is not a private method of the CLI anymore.</b> The template writer
/// lived in <c>Quarp.Cli</c>'s top-level <c>Program.cs</c>, and the CLI references the shell,
/// so the shell could not call it back without a reference cycle. The menu needs the very
/// same files the CLI writes — a template that drifted between the two entrances would mean
/// "the cart you make in the console" and "the cart you make in the terminal" quietly stop
/// being the same thing. So the logic moved down to CartKit, which both already reference,
/// the same "one owner both callers name" move that produced <c>EditorSheetStep</c>
/// (M9 stage 2 lesson: a copy is not a seam).</para>
///
/// <para><b>What it deliberately does not do:</b> print. Callers own their surfaces — the
/// CLI writes to the terminal, the menu to its footer line — so every method here reports
/// through return values and <c>out</c> warnings, and the warning strings are exactly the
/// lines the CLI printed before the move (QuarpNewTests pins them through the child
/// process).</para>
/// </summary>
public static class CartScaffold
{
    /// <summary>Longest cartridge name the menu's entry field accepts; fits the library row and every filesystem.</summary>
    public const int MaxNameLength = 24;

    /// <summary>
    /// True for a name that is safe as a folder on every platform the console targets:
    /// lowercase letters, digits, <c>-</c> and <c>_</c>, 1..24 characters. Deliberately
    /// stricter than what the OS would accept — a cart folder is also a name in the library,
    /// an argument to <c>quarp run</c>, and one day a file someone shares; the strict set
    /// never needs quoting anywhere. The CLI keeps accepting whatever folder name the author
    /// typed (their terminal, their rules); this gate is for names born in the menu.
    /// </summary>
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return false;
        }
        foreach (char c in name)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// True when <paramref name="root"/> already holds a cartridge — the refusal
    /// <c>quarp new</c> has always made, now askable before any file is written. The check
    /// is on <c>manifest.json</c> rather than on the folder being empty, so scaffolding into
    /// a directory with a README still works, but overwriting somebody's <c>src/main.cs</c>
    /// is a data loss no message makes up for.
    /// </summary>
    public static bool CartridgeExists(string root) =>
        File.Exists(Path.Combine(root, "manifest.json"));

    /// <summary>
    /// Writes the cartridge proper — <c>manifest.json</c> and <c>src/main.cs</c> — and
    /// returns the cart's name (the folder's own name, which is what the library shows and
    /// the packer stamps). Throws <see cref="IOException"/> when the folder already holds a
    /// cartridge; the caller decides what that refusal looks like on its surface. IO and
    /// permission errors propagate as themselves — both callers already route that family
    /// into their message lines.
    /// </summary>
    public static string Create(string root)
    {
        string full = Path.GetFullPath(root);
        if (CartridgeExists(full))
        {
            throw new IOException($"{full} already contains a cartridge (manifest.json exists).");
        }
        string name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Length == 0)
        {
            name = "my-cart";
        }

        Directory.CreateDirectory(Path.Combine(full, "src"));
        string manifest = "{\n"
            + $"    \"name\": {JsonSerializer.Serialize(name)},\n"
            + "    \"author\": \"\",\n"
            + "    \"profile\": 8\n"
            + "}\n";
        File.WriteAllText(Path.Combine(full, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(full, "src", "main.cs"), CartTemplate.MainCs);
        return name;
    }

    /// <summary>
    /// Writes <c>.quarp/cart.csproj</c>, the dev-only project that makes an IDE show the same
    /// determinism diagnostics <c>CartCompiler</c> enforces (M2 work order; API-8 §12).
    /// It is not part of the cartridge: the loader reads only <c>src/**/*.cs</c>, the packer
    /// writes only the named files, the watcher ignores it, and the code budget never sees it.
    ///
    /// <para>Skipped, with a warning rather than a failure, when the analyzer is not sitting
    /// next to the tools — a cartridge that exists is worth more than an editor integration,
    /// and a csproj pointing at a missing analyzer would produce silence rather than an
    /// error. The warning text is the exact line the CLI printed before the move.</para>
    /// </summary>
    public static bool TryWriteDevProject(string root, out string? warning)
    {
        // Both DLLs are published next to the quarp tools; BaseDirectory ends with a separator.
        string toolsDir = AppContext.BaseDirectory;
        string analyzer = Path.Combine(toolsDir, "Quarp.Analyzers.dll");
        string api = Path.Combine(toolsDir, "Quarp.Api.dll");
        if (!File.Exists(analyzer) || !File.Exists(api))
        {
            warning = $"quarp: skipped {CartTemplate.DevFolder}/{CartTemplate.DevProjectFile} — "
                + $"Quarp.Api.dll or Quarp.Analyzers.dll is missing from {toolsDir}.";
            return false;
        }

        string devFolder = Path.Combine(root, CartTemplate.DevFolder);
        Directory.CreateDirectory(devFolder);
        File.WriteAllText(
            Path.Combine(devFolder, CartTemplate.DevProjectFile),
            string.Format(CultureInfo.InvariantCulture, CartTemplate.DevProjectFormat, toolsDir));
        warning = null;
        return true;
    }

    /// <summary>
    /// Writes <c>.vscode/launch.json</c> and <c>.vscode/tasks.json</c> so that opening the
    /// cartridge folder in VS Code and pressing F5 runs the cart under the .NET debugger
    /// (ADR-019; M4 work order, stage 1). Dev-only in the same four senses <c>.quarp/</c> is.
    ///
    /// <para>Skipped with a warning rather than a failure when the <c>quarp</c> executable
    /// cannot be located: a launch configuration pointing at nothing is worse than no launch
    /// configuration, and the cartridge itself is perfectly usable without one.</para>
    /// </summary>
    public static bool TryWriteVsCodeFiles(string root, out string? warning)
    {
        string? exePath = FindQuarpExecutable();
        if (exePath is null)
        {
            warning = $"quarp: skipped {CartTemplate.VsCodeFolder}/ — could not find the quarp executable "
                + $"in {AppContext.BaseDirectory}.";
            return false;
        }

        // JSON, not string interpolation: a Windows path is backslashes all the way down and
        // each one has to be escaped. Serialize gives back the quoted, escaped literal, so the
        // token in the template carries its quotes and is replaced whole.
        string quotedPath = JsonSerializer.Serialize(exePath);
        string vsCodeFolder = Path.Combine(root, CartTemplate.VsCodeFolder);
        Directory.CreateDirectory(vsCodeFolder);
        File.WriteAllText(
            Path.Combine(vsCodeFolder, CartTemplate.LaunchFile),
            CartTemplate.LaunchJson.Replace(CartTemplate.ToolPathToken, quotedPath, StringComparison.Ordinal));
        File.WriteAllText(
            Path.Combine(vsCodeFolder, CartTemplate.TasksFile),
            CartTemplate.TasksJson.Replace(CartTemplate.ToolPathToken, quotedPath, StringComparison.Ordinal));
        warning = null;
        return true;
    }

    /// <summary>
    /// The absolute path an IDE should launch. The apphost next to the tools is preferred
    /// over <see cref="Environment.ProcessPath"/> because that is the same folder the dev
    /// csproj already anchors on; the process path is the fallback for a layout where the
    /// apphost was not published.
    /// </summary>
    public static string? FindQuarpExecutable()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "quarp.exe" : "quarp");
        if (File.Exists(beside))
        {
            return beside;
        }
        string? processPath = Environment.ProcessPath;
        return processPath is not null && File.Exists(processPath) ? processPath : null;
    }
}
