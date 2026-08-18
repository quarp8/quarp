using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// <c>ScreenWidth</c> / <c>ScreenHeight</c> — the two reads M4 needs before it can judge
/// 128x72 against the 160x90 fallback (ADR-005, work order Р12).
///
/// <para>The milestone's verdict is supposed to come from the <em>same</em> demo game seen at
/// two resolutions. That comparison is only worth running if the game can actually change
/// shape, which is why these are properties read from the attached console and not constants:
/// a <c>const</c> is baked into the cartridge's IL, so a cart built when the screen was
/// 128 px wide would still lay itself out for 128 px on a 160 px console, and the spike would
/// compare a wide screen against a narrow game drawn in the corner of it. Every test below
/// exists to hold one half of that sentence up.</para>
///
/// <para>The load-bearing one is
/// <see cref="OneCartridgeObjectAnswers128OnProfile8And160OnProfile8Wide"/>: <b>one</b>
/// cartridge instance — one type, one compiled method body, one object — attached to two
/// consoles in turn, answering differently each time. Roslyn is not in the picture here
/// (<c>Quarp.Core.Tests</c> does not reference <c>Quarp.CartKit</c>, and must not: the core
/// knows nothing about the cartridge pipeline). It does not need to be. "One build, two
/// answers" is a claim about where the number comes from at run time — the attached console,
/// not the callsite — and reusing a single object proves that more directly than compiling
/// twice would, because nothing was recompiled between the two answers at all.</para>
///
/// <para>And the negative control, without which the whole feature could be decoration:
/// <see cref="AHardcodedCartDrawsItsBorderInTheMiddleOfAWideScreen"/> shows a cart that spells
/// out 128 and 72 misplacing its layout on the wide profile, while the cart that reads the
/// properties fits both. The property is not a nicer way to say 128; it is the difference
/// between a measurable spike and a meaningless one.</para>
/// </summary>
public class ScreenSizeTests
{
    private const byte Border = 7;
    private const byte Background = 0;

    /// <summary>Records the screen size at every point in the lifecycle a cart can ask.</summary>
    private sealed class ProbeCart : Cartridge
    {
        public int InitCount { get; private set; }
        public int WidthInInit { get; private set; }
        public int HeightInInit { get; private set; }
        public int WidthInUpdate { get; private set; }
        public int HeightInUpdate { get; private set; }
        public int WidthInDraw { get; private set; }
        public int HeightInDraw { get; private set; }

        public override void Init()
        {
            InitCount++;
            WidthInInit = ScreenWidth;
            HeightInInit = ScreenHeight;
        }

        public override void Update()
        {
            WidthInUpdate = ScreenWidth;
            HeightInUpdate = ScreenHeight;
        }

        public override void Draw()
        {
            WidthInDraw = ScreenWidth;
            HeightInDraw = ScreenHeight;
        }
    }

    /// <summary>
    /// The mistake the properties exist to prevent: a border drawn around "the screen",
    /// where "the screen" is two literals the author measured once.
    /// </summary>
    private sealed class HardcodedBorderCart : Cartridge
    {
        public override void Draw()
        {
            Cls(Background);
            Rect(0, 0, 128, 72, Border);
        }
    }

    /// <summary>The same cart written the supported way. Identical apart from where the numbers come from.</summary>
    private sealed class ProfileReadingBorderCart : Cartridge
    {
        public override void Draw()
        {
            Cls(Background);
            Rect(0, 0, ScreenWidth, ScreenHeight, Border);
        }
    }

    // --- the proof Р12 asks for ---

    /// <summary>
    /// One object, two consoles, two answers. <c>InitCount</c> is asserted as well, because it
    /// is what makes the claim checkable rather than assumed: it can only reach 2 if the very
    /// same instance ran on both profiles. Nothing is rebuilt, reflected over or re-emitted
    /// between the two attachments — the only thing that differs is the console handed to
    /// <see cref="Cartridge.Attach"/>.
    /// </summary>
    [Fact]
    public void OneCartridgeObjectAnswers128OnProfile8And160OnProfile8Wide()
    {
        var cart = new ProbeCart();

        var narrow = new VirtualConsole(ConsoleProfile.Profile8);
        narrow.AttachCart(cart);
        int widthOnProfile8 = cart.WidthInInit;
        int heightOnProfile8 = cart.HeightInInit;

        var wide = new VirtualConsole(ConsoleProfile.Profile8Wide);
        wide.AttachCart(cart);

        Assert.Equal(2, cart.InitCount);            // the same object really did run twice
        Assert.Equal(128, widthOnProfile8);
        Assert.Equal(72, heightOnProfile8);
        Assert.Equal(160, cart.WidthInInit);
        Assert.Equal(90, cart.HeightInInit);
    }

    /// <summary>
    /// Same instance, same lifecycle, but read from Update and Draw instead of Init — the two
    /// places a real game lays itself out. A cached-at-Init value that went stale, or a
    /// property accidentally bound to something other than the live console, shows up here.
    /// </summary>
    [Fact]
    public void TheSameObjectAlsoSeesBothSizesFromUpdateAndDraw()
    {
        var cart = new ProbeCart();

        var narrow = new VirtualConsole(ConsoleProfile.Profile8);
        narrow.AttachCart(cart);
        narrow.Tick(default);
        Assert.Equal(128, cart.WidthInUpdate);
        Assert.Equal(72, cart.HeightInUpdate);
        Assert.Equal(128, cart.WidthInDraw);
        Assert.Equal(72, cart.HeightInDraw);

        var wide = new VirtualConsole(ConsoleProfile.Profile8Wide);
        wide.AttachCart(cart);
        wide.Tick(default);
        Assert.Equal(160, cart.WidthInUpdate);
        Assert.Equal(90, cart.HeightInUpdate);
        Assert.Equal(160, cart.WidthInDraw);
        Assert.Equal(90, cart.HeightInDraw);
    }

    // --- the negative control ---

    /// <summary>
    /// The failure the properties exist to make impossible, shown happening. On the wide
    /// profile the hardcoded cart paints its bottom-right corner at (127, 71) — a corner of
    /// nothing, 32 px left of and 18 px above the actual edge — and the real corner stays
    /// background. This is what "the spike would be meaningless" looks like in pixels: the
    /// game does not get wider, it gets a margin.
    /// </summary>
    [Fact]
    public void AHardcodedCartDrawsItsBorderInTheMiddleOfAWideScreen()
    {
        var wide = new VirtualConsole(ConsoleProfile.Profile8Wide);
        wide.AttachCart(new HardcodedBorderCart());
        wide.Tick(default);

        Assert.Equal(Border, wide.Pget(127, 71));       // where 128x72 would have ended
        Assert.Equal(Background, wide.Pget(159, 89));   // where the screen actually ends
        Assert.Equal(Background, wide.Pget(159, 0));    // the whole right rail is missing
        Assert.Equal(Background, wide.Pget(0, 89));     // and so is the whole bottom rail
    }

    /// <summary>
    /// The same cart written against the properties, on both profiles: the border lands on the
    /// real last column and row, and the pixel just inside stays background — i.e. it is a
    /// border at the edge, not a filled rectangle that happens to reach it.
    /// </summary>
    [Theory]
    [InlineData(128, 72)]
    [InlineData(160, 90)]
    public void AProfileReadingCartBordersEitherScreenExactly(int width, int height)
    {
        ConsoleProfile profile = width == 128 ? ConsoleProfile.Profile8 : ConsoleProfile.Profile8Wide;
        var console = new VirtualConsole(profile);
        console.AttachCart(new ProfileReadingBorderCart());
        console.Tick(default);

        Assert.Equal(Border, console.Pget(width - 1, height - 1));
        Assert.Equal(Border, console.Pget(width - 1, 0));
        Assert.Equal(Border, console.Pget(0, height - 1));
        Assert.Equal(Background, console.Pget(width - 2, height - 2));
    }

    // --- the console side of the contract ---

    /// <summary>
    /// The three places the number lives agree by construction, and the API returns the one the
    /// rasterizer uses. If they could drift, a cart could lay itself out for a screen the clip
    /// rectangle disagreed with, and the drawing would silently vanish at the seam.
    /// </summary>
    [Theory]
    [InlineData(128, 72)]
    [InlineData(160, 90)]
    public void ScreenSizeAgreesWithTheProfileAndTheFramebuffer(int width, int height)
    {
        ConsoleProfile profile = width == 128 ? ConsoleProfile.Profile8 : ConsoleProfile.Profile8Wide;
        var console = new VirtualConsole(profile);

        Assert.Equal(width, profile.Width);
        Assert.Equal(height, profile.Height);
        Assert.Equal(width, console.Framebuffer.Width);
        Assert.Equal(height, console.Framebuffer.Height);
        Assert.Equal(width, console.ScreenWidth);
        Assert.Equal(height, console.ScreenHeight);

        IConsoleApi api = console;
        Assert.Equal(width, api.ScreenWidth);
        Assert.Equal(height, api.ScreenHeight);
    }

    /// <summary>
    /// The wide profile is a real screen, not just a pair of larger numbers: the default clip
    /// covers all of it, so a pixel written at the far corner survives, and one written past it
    /// is dropped as softly as everywhere else (API-8 §1).
    /// </summary>
    [Fact]
    public void TheWideProfileRastersToItsLastColumnAndRow()
    {
        var wide = new VirtualConsole(ConsoleProfile.Profile8Wide);
        wide.Pset(159, 89, 11);
        wide.Pset(160, 89, 12);
        wide.Pset(159, 90, 12);

        Assert.Equal(11, wide.Pget(159, 89));
        Assert.Equal(0, wide.Pget(160, 89));
        Assert.Equal(0, wide.Pget(159, 90));
        Assert.Equal(160 * 90, wide.Framebuffer.Pixels.Length);
    }

    /// <summary>
    /// Guards the milestone's anchors from the direction they could plausibly be nudged: this
    /// change adds a second profile, and the cheapest way to break every golden hash at once
    /// would be to edit the wrong literal while doing it. Profile8 is 128x72 and stays 128x72;
    /// the snake and all four M4 anchors run on it (work order Р6).
    /// </summary>
    [Fact]
    public void Profile8IsUntouchedByTheSpikeProfile()
    {
        Assert.Equal("QUARP-8", ConsoleProfile.Profile8.Name);
        Assert.Equal(128, ConsoleProfile.Profile8.Width);
        Assert.Equal(72, ConsoleProfile.Profile8.Height);
        Assert.NotSame(ConsoleProfile.Profile8, ConsoleProfile.Profile8Wide);
    }

    /// <summary>
    /// Read before the cart is attached — which is where a field initializer or a constructor
    /// runs (API-8 §2) — the screen size throws like every other call on the surface, instead
    /// of quietly answering 0 and letting a game lay itself out on a zero-wide screen.
    /// </summary>
    [Fact]
    public void ScreenSizeIsUnavailableBeforeAttach()
    {
        var cart = new UnattachedCart();
        Assert.Throws<InvalidOperationException>(() => cart.ReadWidth());
        Assert.Throws<InvalidOperationException>(() => cart.ReadHeight());
    }

    private sealed class UnattachedCart : Cartridge
    {
        internal int ReadWidth() => ScreenWidth;

        internal int ReadHeight() => ScreenHeight;
    }
}
