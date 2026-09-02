using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The pointer half of ADR-030 as a cartridge sees it: MouseBtnp's edge (held this tick AND
/// not held on the previous one — Btnp's rule, verbatim), the screen clamp, and the wheel as
/// a per-tick delta. Observed from inside Update during <see cref="VirtualConsole.Tick"/>,
/// like <see cref="InputTests"/> observes the buttons.
/// </summary>
public class MouseInputTests
{
    private sealed class RecordingCart : Cartridge
    {
        public List<int> XLog { get; } = new();
        public List<int> YLog { get; } = new();
        public List<bool> BtnLog { get; } = new();
        public List<bool> BtnpLog { get; } = new();
        public List<int> WheelLog { get; } = new();
        public int XDuringInit { get; private set; } = -1;
        public bool BtnpDuringInit { get; private set; }

        public override void Init()
        {
            XDuringInit = MouseX;
            BtnpDuringInit = MouseBtnp(MouseButton.Left);
        }

        public override void Update()
        {
            XLog.Add(MouseX);
            YLog.Add(MouseY);
            BtnLog.Add(MouseBtn(MouseButton.Left));
            BtnpLog.Add(MouseBtnp(MouseButton.Left));
            WheelLog.Add(MouseWheel);
        }
    }

    private static RecordingCart Run(params InputState[] ticks)
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        var cart = new RecordingCart();
        console.AttachCart(cart);
        foreach (InputState input in ticks)
        {
            console.Tick(input);
        }
        return cart;
    }

    private static InputState Mouse(int x, int y, bool left = false, int wheel = 0) =>
        default(InputState).WithMouse(x, y, left ? (byte)(1 << (int)MouseButton.Left) : (byte)0, wheel);

    // Break recipe: make VirtualConsole.MouseBtnp read only _input (drop the !_previous half) —
    // the two held ticks below both report a press, this goes red, and so would every cart
    // that counts clicks.
    [Fact]
    public void MouseBtnpFiresOnTheEdgeOnly()
    {
        RecordingCart cart = Run(
            Mouse(10, 10),
            Mouse(10, 10, left: true),
            Mouse(10, 10, left: true),
            Mouse(10, 10),
            Mouse(10, 10, left: true));

        Assert.Equal(new[] { false, true, true, false, true }, cart.BtnLog);
        Assert.Equal(new[] { false, true, false, false, true }, cart.BtnpLog);
    }

    [Fact]
    public void CoordinatesAreClampedToTheScreen()
    {
        // ADR-030 п.6: the API's value is clamped to 0..ScreenWidth-1 — and the clamp lives in
        // the simulation, so a replay whose log carries an off-screen coordinate (a script
        // said 250 on a 160-wide screen) still reads back deterministically.
        RecordingCart cart = Run(Mouse(250, 200), Mouse(159, 89), Mouse(0, 0));

        Assert.Equal(new[] { 159, 159, 0 }, cart.XLog);
        Assert.Equal(new[] { 89, 89, 0 }, cart.YLog);
    }

    [Fact]
    public void TheWheelIsAPerTickDelta()
    {
        RecordingCart cart = Run(Mouse(0, 0, wheel: 2), Mouse(0, 0), Mouse(0, 0, wheel: -1));

        Assert.Equal(new[] { 2, 0, -1 }, cart.WheelLog);
    }

    [Fact]
    public void InitSeesTheNeutralPointer()
    {
        // Tick 0 is Init and no input has arrived yet: the pointer is parked, no edges fire.
        RecordingCart cart = Run(Mouse(50, 40, left: true));

        Assert.Equal(0, cart.XDuringInit);
        Assert.False(cart.BtnpDuringInit);
        // And the first real tick sees the press as an edge against that neutral snapshot.
        Assert.Equal(new[] { true }, cart.BtnpLog);
    }

    [Fact]
    public void AnUnknownMouseButtonReadsFalse()
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        var cart = new RecordingCart();
        console.AttachCart(cart);
        console.Tick(default(InputState).WithMouse(0, 0, 0xff, 0));

        Assert.False(console.MouseBtn((MouseButton)5));
        Assert.False(console.MouseBtnp((MouseButton)(-1)));
        // 0xff was masked to the three real buttons on the way in, so all three read held...
        Assert.True(console.MouseBtn(MouseButton.Left));
        Assert.True(console.MouseBtn(MouseButton.Middle));
    }
}
