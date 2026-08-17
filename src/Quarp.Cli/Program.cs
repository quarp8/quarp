using Quarp.Cli;
using Quarp.Core;
using Quarp.Shell.Desktop;

string command = args.Length == 0 ? "run" : args[0];

switch (command)
{
    case "run":
    {
        using var game = new QuarpGame();
        game.Run();
        return 0;
    }

    case "pattern":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: quarp pattern <out.bmp>");
            return 1;
        }
        var fb = new Framebuffer(ConsoleProfile.Profile8);
        TestPattern.Render(fb);
        BmpWriter.Write(args[1], fb);
        Console.WriteLine($"Wrote {fb.Width}x{fb.Height} test pattern to {args[1]}");
        return 0;
    }

    default:
        Console.WriteLine("QUARP — fantasy console (M0 skeleton)");
        Console.WriteLine("usage:");
        Console.WriteLine("  quarp run              open the console window (test pattern)");
        Console.WriteLine("  quarp pattern <file>   write the test pattern as a .bmp image");
        return command is "--help" or "-h" or "help" ? 0 : 1;
}
