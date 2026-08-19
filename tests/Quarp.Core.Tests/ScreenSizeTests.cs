using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// <c>ScreenWidth</c> / <c>ScreenHeight</c> — the two reads that keep a cartridge's layout tied
/// to the console it is running on (work order Р12) instead of to the screen its author happened
/// to have. ADR-021 settled that screen at 160x90 and deleted the second shipped profile, which
/// is what these tests were rewritten against in M4 stage 4.0.
///
/// <para>Deleting the second profile did <em>not</em> delete the claim it existed to prove. The
/// claim is "one build, two answers": the number comes from the attached console at call time,
/// so the very same compiled cartridge lays itself out differently on two consoles. Nothing but
/// a second console can demonstrate that, so the tests below build one — <see cref="Historic"/>,
/// a plain <c>new ConsoleProfile</c>. That the type stays constructible is the whole reason a
/// profile is data and not a set of constants (ARCHITECTURE §2), and QUARP-16 (320x180, M6) will
/// arrive as one more instance rather than as an <c>if</c>.</para>
///
/// <para>The load-bearing one is
/// <see cref="OneCartridgeObjectAnswers160OnProfile8And128OnASecondProfile"/>: <b>one</b>
/// cartridge instance — one type, one compiled method body, one object — attached to two consoles
/// in turn, answering differently each time. Roslyn is not in the picture here
/// (<c>Quarp.Core.Tests</c> does not reference <c>Quarp.CartKit</c>, and must not: the core knows
/// nothing about the cartridge pipeline). It does not need to be. Reusing a single object proves
/// the point more directly than compiling twice would, because nothing was recompiled between the
/// two answers at all. The Roslyn half lives in
/// <c>Quarp.CartKit.Tests.ScreenSizeThroughTheRealPipelineTests</c>.</para>
///
/// <para>And the negative control, without which the whole feature could be decoration:
/// <see cref="AHardcodedCartDrawsTheSameBorderWhicheverConsoleItIsOn"/> shows a cart that spells
/// out 128 and 72 drawing one and the same picture on both consoles — a border around a screen
/// that no longer exists, 32 px left of and 18 px above the real edge of QUARP-8 — while the cart
/// that reads the properties fits both exactly. The property is not a nicer way to say 160; it is
/// the difference between a cartridge that follows its console and one that ignores it.</para>
/// </summary>
public class ScreenSizeTests
{
    private const byte Border = 7;
    private const byte Background = 0;

    /// <summary>
    /// The second console, built here because the repository ships only one (ADR-021). 128x72 is
    /// deliberately the screen QUARP-8 had from M0 to M4 stage 3: a size this project really did
    /// lay itself out for, so "the cart follows the console" is being asked with two sizes that
    /// were both once real, and the hardcoded-cart control below can hardcode the numbers of an
    /// actual past console rather than invented ones.
    /// </summary>
    private static readonly ConsoleProfile Historic = new()
    {
        Name = "QUARP-8 (historic 128x72)",
        Width = 128,
        Height = 72,
    };

    /// <summary>Picks the profile a theory row is about, so the rows read as sizes, not as names.</summary>
    private static ConsoleProfile ProfileOf(int width) =>
        width == ConsoleProfile.Profile8.Width ? ConsoleProfile.Profile8 : Historic;

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
    /// The mistake the properties exist to prevent: a border drawn around "the screen", where
    /// "the screen" is two literals the author measured once. The literals are 128x72 on purpose
    /// — that is what every cart in this repository said before Р12, and what they would all
    /// still be saying, wrongly, after ADR-021.
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
    /// One object, two consoles, two answers. <c>InitCount</c> is asserted as well, because it is
    /// what makes the claim checkable rather than assumed: it can only reach 2 if the very same
    /// instance ran on both profiles. Nothing is rebuilt, reflected over or re-emitted between the
    /// two attachments — the only thing that differs is the console handed to
    /// <see cref="Cartridge.Attach"/>.
    /// </summary>
    [Fact]
    public void OneCartridgeObjectAnswers160OnProfile8And128OnASecondProfile()
    {
        var cart = new ProbeCart();

        var ratified = new VirtualConsole(ConsoleProfile.Profile8);
        ratified.AttachCart(cart);
        int widthOnProfile8 = cart.WidthInInit;
        int heightOnProfile8 = cart.HeightInInit;

        var historic = new VirtualConsole(Historic);
        historic.AttachCart(cart);

        Assert.Equal(2, cart.InitCount);            // the same object really did run twice
        Assert.Equal(160, widthOnProfile8);
        Assert.Equal(90, heightOnProfile8);
        Assert.Equal(128, cart.WidthInInit);
        Assert.Equal(72, cart.HeightInInit);
    }

    /// <summary>
    /// Same instance, same lifecycle, but read from Update and Draw instead of Init — the two
    /// places a real game lays itself out. A cached-at-Init value that went stale, or a property
    /// accidentally bound to something other than the live console, shows up here.
    /// </summary>
    [Fact]
    public void TheSameObjectAlsoSeesBothSizesFromUpdateAndDraw()
    {
        var cart = new ProbeCart();

        var ratified = new VirtualConsole(ConsoleProfile.Profile8);
        ratified.AttachCart(cart);
        ratified.Tick(default);
        Assert.Equal(160, cart.WidthInUpdate);
        Assert.Equal(90, cart.HeightInUpdate);
        Assert.Equal(160, cart.WidthInDraw);
        Assert.Equal(90, cart.HeightInDraw);

        var historic = new VirtualConsole(Historic);
        historic.AttachCart(cart);
        historic.Tick(default);
        Assert.Equal(128, cart.WidthInUpdate);
        Assert.Equal(72, cart.HeightInUpdate);
        Assert.Equal(128, cart.WidthInDraw);
        Assert.Equal(72, cart.HeightInDraw);
    }

    // --- the negative control ---

    /// <summary>
    /// The failure the properties exist to make impossible, shown happening — and shown as the
    /// thing that actually gives it away: a cart that does not ask draws <em>the same picture on
    /// both consoles</em>. Its bottom-right corner lands on (127, 71) either way. On the historic
    /// console that is the corner; on QUARP-8 it is a corner of nothing, 32 px left of and 18 px
    /// above the real edge, and the real edge stays background. This is what "the screen API is
    /// decoration" would look like in pixels: the game does not get bigger, it gets a margin.
    /// </summary>
    [Fact]
    public void AHardcodedCartDrawsTheSameBorderWhicheverConsoleItIsOn()
    {
        var cart = new HardcodedBorderCart();

        var ratified = new VirtualConsole(ConsoleProfile.Profile8);
        ratified.AttachCart(cart);
        ratified.Tick(default);

        Assert.Equal(Border, ratified.Pget(127, 71));       // where 128x72 would have ended
        Assert.Equal(Background, ratified.Pget(159, 89));   // where this screen actually ends
        Assert.Equal(Background, ratified.Pget(159, 0));    // the whole right rail is missing
        Assert.Equal(Background, ratified.Pget(0, 89));     // and so is the whole bottom rail

        var historic = new VirtualConsole(Historic);
        historic.AttachCart(cart);
        historic.Tick(default);

        // One build, one answer: the same object drew its corner in the same place, and only the
        // smaller console makes that place look right.
        Assert.Equal(Border, historic.Pget(127, 71));
        Assert.Equal(Border, historic.Pget(127, 0));
        Assert.Equal(Border, historic.Pget(0, 71));
    }

    /// <summary>
    /// The same cart written against the properties, on both consoles: the border lands on the
    /// real last column and row, and the pixel just inside stays background — i.e. it is a border
    /// at the edge, not a filled rectangle that happens to reach it.
    /// </summary>
    [Theory]
    [InlineData(160, 90)]
    [InlineData(128, 72)]
    public void AProfileReadingCartBordersEitherScreenExactly(int width, int height)
    {
        var console = new VirtualConsole(ProfileOf(width));
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
    [InlineData(160, 90)]
    [InlineData(128, 72)]
    public void ScreenSizeAgreesWithTheProfileAndTheFramebuffer(int width, int height)
    {
        ConsoleProfile profile = ProfileOf(width);
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
    /// Each console is a real screen, not just a pair of numbers on a property: the default clip
    /// covers all of it, so a pixel written at that console's far corner survives, and one written
    /// past it is dropped as softly as everywhere else (API-8 §1). Run on both profiles it is also
    /// the sharpest statement that the clip follows the profile — (128, 71) is a normal interior
    /// pixel on QUARP-8 and must be dropped on the historic console.
    /// </summary>
    [Theory]
    [InlineData(160, 90)]
    [InlineData(128, 72)]
    public void EachProfileRastersToItsOwnLastColumnAndRow(int width, int height)
    {
        var console = new VirtualConsole(ProfileOf(width));
        console.Pset(width - 1, height - 1, 11);
        console.Pset(width, height - 1, 12);
        console.Pset(width - 1, height, 12);

        Assert.Equal(11, console.Pget(width - 1, height - 1));
        Assert.Equal(0, console.Pget(width, height - 1));
        Assert.Equal(0, console.Pget(width - 1, height));
        Assert.Equal(width * height, console.Framebuffer.Pixels.Length);
    }

    /// <summary>
    /// Guards the milestone's anchors from the direction they could plausibly be nudged. The
    /// original form of this test held Profile8 at 128x72 while a second shipped profile was
    /// added beside it; ADR-021 inverted it, and the danger inverted with it: the tests above
    /// construct a 128x72 profile of their own, and the cheapest way to break every golden hash in
    /// the repository at once would be to let those old numbers leak back into the shipped one.
    /// Profile8 is 160x90, it is the only profile the repository ships, and the console the anchors
    /// run on is that one and not a test fixture.
    /// </summary>
    [Fact]
    public void Profile8IsTheRatifiedScreenAndTheTestFixtureDoesNotMoveIt()
    {
        Assert.Equal("QUARP-8", ConsoleProfile.Profile8.Name);
        Assert.Equal(160, ConsoleProfile.Profile8.Width);
        Assert.Equal(90, ConsoleProfile.Profile8.Height);
        Assert.NotSame(ConsoleProfile.Profile8, Historic);
        Assert.NotEqual(ConsoleProfile.Profile8.Width, Historic.Width);
        Assert.NotEqual(ConsoleProfile.Profile8.Height, Historic.Height);
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
