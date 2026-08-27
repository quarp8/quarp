using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The four data-bank calls of ADR-035 as a cartridge sees them, including the clipping rules —
/// the part a port will lean on at every level boundary, and the part that has to behave
/// identically on every machine rather than throwing somewhere deep in a shell.
/// </summary>
public class DataBankApiTests
{
    /// <summary>Runs one action against the console from inside Update, where a cart lives.</summary>
    private sealed class ActionCart : Cartridge
    {
        private readonly Action<ActionCart> _update;

        public ActionCart(Action<ActionCart> update) => _update = update;

        public int Length(int bank) => DataLength(bank);

        public byte Get(int bank, int offset) => DataGet(bank, offset);

        public void ToGfx(int bank, int offset, int pixel, int count) =>
            DataToGfx(bank, offset, pixel, count);

        public void ToMap(int bank, int offset, int cell, int count) =>
            DataToMap(bank, offset, cell, count);

        /// <summary>The sheet and the map are protected on Cartridge; the test reads them
        /// through the cart, which is the only vantage point a cartridge actually has.</summary>
        public byte Sheet(int x, int y) => Sget(x, y);

        public byte MapAt(int x, int y) => Mget(x, y);

        public override void Update() => _update(this);

        public override void Draw()
        {
        }
    }

    private static VirtualConsole ConsoleWith(params (int Bank, byte[] Bytes)[] banks)
    {
        var set = new byte[VirtualConsole.DataBankCount][];
        for (int i = 0; i < set.Length; i++)
        {
            set[i] = Array.Empty<byte>();
        }
        foreach ((int bank, byte[] bytes) in banks)
        {
            set[bank] = bytes;
        }
        return new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, null, null, set);
    }

    private static void Run(VirtualConsole console, Action<ActionCart> body)
    {
        var cart = new ActionCart(body);
        console.AttachCart(cart);
        console.Tick(default);
    }

    [Fact]
    public void LengthAndGetReadTheBank()
    {
        VirtualConsole console = ConsoleWith((2, new byte[] { 10, 20, 30 }));
        int length = -1;
        byte first = 0, last = 0;

        Run(console, cart =>
        {
            length = cart.Length(2);
            first = cart.Get(2, 0);
            last = cart.Get(2, 2);
        });

        Assert.Equal(3, length);
        Assert.Equal(10, first);
        Assert.Equal(30, last);
    }

    /// <summary>Soft geometry, the same answer Mget and Sget give outside the field.</summary>
    [Fact]
    public void ReadsOutsideTheBankAreZeroNotAnError()
    {
        VirtualConsole console = ConsoleWith((0, new byte[] { 7 }));
        byte past = 1, negative = 1, noBank = 1, badBank = 1;
        int lengthOfNothing = -1;

        Run(console, cart =>
        {
            past = cart.Get(0, 1);
            negative = cart.Get(0, -1);
            noBank = cart.Get(5, 0);
            badBank = cart.Get(999, 0);
            lengthOfNothing = cart.Length(999);
        });

        Assert.Equal(0, past);
        Assert.Equal(0, negative);
        Assert.Equal(0, noBank);
        Assert.Equal(0, badBank);
        Assert.Equal(0, lengthOfNothing);
    }

    [Fact]
    public void ToGfxCopiesIntoTheSheetAndSgetSeesIt()
    {
        VirtualConsole console = ConsoleWith((1, new byte[] { 3, 4, 5, 6 }));
        byte atZero = 0, atOne = 0, untouched = 9;

        Run(console, cart =>
        {
            cart.ToGfx(1, 0, 0, 4);
            atZero = cart.Sheet(0, 0);
            atOne = cart.Sheet(1, 0);
            untouched = cart.Sheet(4, 0);
        });

        Assert.Equal(3, atZero);
        Assert.Equal(4, atOne);
        Assert.Equal(0, untouched);
    }

    [Fact]
    public void ToMapCopiesIntoTheMapAndMgetSeesIt()
    {
        VirtualConsole console = ConsoleWith((0, new byte[] { 11, 12 }));
        byte cell0 = 0, cell1 = 0;

        Run(console, cart =>
        {
            cart.ToMap(0, 0, 0, 2);
            cell0 = cart.MapAt(0, 0);
            cell1 = cart.MapAt(1, 0);
        });

        Assert.Equal(11, cell0);
        Assert.Equal(12, cell1);
    }

    /// <summary>
    /// A copy that runs off the end of the bank writes what exists and stops — the destination
    /// past the bank's last byte is left alone rather than filled with zeros, because a
    /// cartridge paging a partial tile must not silently erase what was already there.
    /// </summary>
    [Fact]
    public void ACopyLongerThanTheBankIsClippedNotPadded()
    {
        VirtualConsole console = ConsoleWith((0, new byte[] { 1, 2 }));
        byte written = 0, beyond = 9;

        Run(console, cart =>
        {
            cart.ToGfx(0, 0, 10, 1000);
            written = cart.Sheet(10, 0);
            beyond = cart.Sheet(12, 0);
        });

        Assert.Equal(1, written);
        Assert.Equal(0, beyond);
    }

    /// <summary>
    /// A negative source offset shifts the destination by the same amount, so the byte at bank
    /// offset 0 still lands where the caller's arithmetic put it. Anything else would make a
    /// cartridge scrolling a texture window off the left edge tear.
    /// </summary>
    [Fact]
    public void ANegativeOffsetShiftsTheDestinationInstead()
    {
        VirtualConsole console = ConsoleWith((0, new byte[] { 1, 2, 3 }));
        byte atFour = 0, atThree = 9;

        Run(console, cart =>
        {
            cart.ToGfx(0, -1, 3, 3);
            atThree = cart.Sheet(3, 0);
            atFour = cart.Sheet(4, 0);
        });

        Assert.Equal(0, atThree);
        Assert.Equal(1, atFour);
    }

    [Fact]
    public void CopiesFromAMissingBankDoNothing()
    {
        VirtualConsole console = ConsoleWith();
        byte sheet = 9, map = 9;

        Run(console, cart =>
        {
            cart.ToGfx(4, 0, 0, 16);
            cart.ToMap(4, 0, 0, 16);
            sheet = cart.Sheet(0, 0);
            map = cart.MapAt(0, 0);
        });

        Assert.Equal(0, sheet);
        Assert.Equal(0, map);
    }

    /// <summary>
    /// Rewind correctness: the banks are read-only, so a resimulation replays the cartridge's
    /// own copies out of them and lands on exactly the same sheet.
    /// </summary>
    [Fact]
    public void AResimulationReproducesWhatTheCartridgePagedIn()
    {
        var banks = new byte[VirtualConsole.DataBankCount][];
        for (int i = 0; i < banks.Length; i++)
        {
            banks[i] = Array.Empty<byte>();
        }
        banks[0] = new byte[] { 5, 6, 7, 8 };

        byte[] Play()
        {
            var console = new VirtualConsole(
                ConsoleProfile.Profile8, null, null, null, null, null, banks);
            var cart = new ActionCart(c => c.ToGfx(0, 0, 0, 4));
            console.AttachCart(cart);
            for (int i = 0; i < 5; i++)
            {
                console.Tick(default);
            }
            return new[] { cart.Sheet(0, 0), cart.Sheet(1, 0), cart.Sheet(2, 0), cart.Sheet(3, 0) };
        }

        Assert.Equal(Play(), Play());
    }
}
