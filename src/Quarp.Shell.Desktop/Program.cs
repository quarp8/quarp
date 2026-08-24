using Quarp.CartKit;
using Quarp.Shell.Desktop;

// No argument: the boot menu (M9 stage 4, ADR-028 — intro, then library / load cart / create
// game). With one: run a cart folder or .quarp8 file directly, no intro — the F5 loop.
string? cartPath = args.Length > 0 ? args[0] : null;

QuarpGame game;
try
{
    game = new QuarpGame(cartPath);
}
catch (CartLoadException e)
{
    Console.Error.WriteLine($"quarp: {e.Message}");
    return 1;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException)
{
    // The cart files were unreadable at startup (locked by an editor, denied by ACLs):
    // a plain message, not a runtime stack trace.
    Console.Error.WriteLine($"quarp: cannot read the cartridge: {e.Message}");
    return 1;
}

using (game)
{
    game.Run();
}
return 0;
