namespace Quarp.Cli;

/// <summary>The src/main.cs that <c>quarp new</c> writes: the smallest playable cartridge.</summary>
public static class CartTemplate
{
    public const string MainCs = """
        using Quarp.Api;

        namespace MyCart;

        public sealed class MyCart : Cartridge
        {
            private int _x = 60;
            private int _y = 40;

            public override void Update()
            {
                // 60 times per game second. Only int and Fix here — no float (SPEC-8 §7).
                if (Btn(Button.Left))
                {
                    _x--;
                }
                if (Btn(Button.Right))
                {
                    _x++;
                }
                if (Btn(Button.Up))
                {
                    _y--;
                }
                if (Btn(Button.Down))
                {
                    _y++;
                }
            }

            public override void Draw()
            {
                Cls(0);
                Print("HELLO QUARP-8", 38, 8, 3);
                RectFill(_x, _y, 8, 8, 7);
            }
        }
        """;
}
