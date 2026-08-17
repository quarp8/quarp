using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// Btn/Btnp edge semantics (M1 work order: Btnp = held this tick AND not held on the
/// previous tick), observed the way a real cartridge sees them — from inside Update
/// during <see cref="VirtualConsole.Tick"/>.
/// </summary>
public class InputTests
{
    /// <summary>Records Btn/Btnp for one button+player on every Update tick.</summary>
    private sealed class RecordingCart : Cartridge
    {
        private readonly Button _button;
        private readonly int _player;

        public RecordingCart(Button button, int player)
        {
            _button = button;
            _player = player;
        }

        public List<bool> BtnLog { get; } = new();
        public List<bool> BtnpLog { get; } = new();
        public bool BtnDuringInit { get; private set; }
        public bool BtnpDuringInit { get; private set; }

        public override void Init()
        {
            BtnDuringInit = Btn(_button, _player);
            BtnpDuringInit = Btnp(_button, _player);
        }

        public override void Update()
        {
            BtnLog.Add(Btn(_button, _player));
            BtnpLog.Add(Btnp(_button, _player));
        }
    }

    private static RecordingCart Run(Button button, int player, params InputState[] ticks)
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        var cart = new RecordingCart(button, player);
        console.AttachCart(cart);
        foreach (InputState input in ticks)
        {
            console.Tick(input);
        }
        return cart;
    }

    private static InputState P0(params Button[] held)
    {
        var state = default(InputState);
        foreach (Button b in held)
        {
            state = state.With(0, b, true);
        }
        return state;
    }

    [Fact]
    public void BtnpFiresOnlyOnThePressEdge()
    {
        // held: X _ X X X _ X
        var cart = Run(Button.O, 0,
            P0(Button.O), P0(), P0(Button.O), P0(Button.O), P0(Button.O), P0(), P0(Button.O));
        Assert.Equal(new[] { true, false, true, true, true, false, true }, cart.BtnLog);
        Assert.Equal(new[] { true, false, true, false, false, false, true }, cart.BtnpLog);
    }

    [Fact]
    public void BtnpTrueOnVeryFirstTickWhenAlreadyHeld()
    {
        // AttachCart resets previous input; a button held on tick 1 is a fresh press.
        var cart = Run(Button.Start, 0, P0(Button.Start));
        Assert.Equal(new[] { true }, cart.BtnLog);
        Assert.Equal(new[] { true }, cart.BtnpLog);
    }

    [Fact]
    public void ReleaseIsNeverAPress()
    {
        var cart = Run(Button.Left, 0, P0(Button.Left), P0(), P0());
        Assert.Equal(new[] { true, false, false }, cart.BtnLog);
        Assert.Equal(new[] { true, false, false }, cart.BtnpLog);
    }

    [Fact]
    public void InputIsClearDuringInit()
    {
        var cart = Run(Button.O, 0, P0(Button.O));
        Assert.False(cart.BtnDuringInit);
        Assert.False(cart.BtnpDuringInit);
    }

    [Fact]
    public void PlayersAreIndependent()
    {
        var p1Down = default(InputState).With(1, Button.X, true);
        var cartP0 = Run(Button.X, 0, p1Down, p1Down);
        Assert.Equal(new[] { false, false }, cartP0.BtnLog);
        var cartP1 = Run(Button.X, 1, p1Down, p1Down);
        Assert.Equal(new[] { true, true }, cartP1.BtnLog);
        Assert.Equal(new[] { true, false }, cartP1.BtnpLog);
    }

    [Fact]
    public void ButtonsAreIndependentBits()
    {
        var cart = Run(Button.Right, 0,
            P0(Button.Right, Button.O), P0(Button.O), P0(Button.Right, Button.O));
        Assert.Equal(new[] { true, false, true }, cart.BtnLog);
        Assert.Equal(new[] { true, false, true }, cart.BtnpLog);
    }

    [Fact]
    public void UnknownPlayersReadAsNotHeld()
    {
        var cart = Run(Button.O, 5, P0(Button.O), P0(Button.O));
        Assert.Equal(new[] { false, false }, cart.BtnLog);
        Assert.Equal(new[] { false, false }, cart.BtnpLog);
    }

    [Fact]
    public void InputStateWithSetsAndClearsBits()
    {
        var state = default(InputState)
            .With(0, Button.Left, true)
            .With(0, Button.X, true)
            .With(1, Button.Start, true);
        Assert.True(state.IsDown(0, Button.Left));
        Assert.True(state.IsDown(0, Button.X));
        Assert.False(state.IsDown(0, Button.Start));
        Assert.True(state.IsDown(1, Button.Start));
        state = state.With(0, Button.Left, false);
        Assert.False(state.IsDown(0, Button.Left));
        Assert.True(state.IsDown(0, Button.X));     // clearing one bit keeps the others
    }

    [Fact]
    public void TicksCountFromInitAsTickZero()
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        var cart = new RecordingCart(Button.O, 0);
        console.AttachCart(cart);
        Assert.Equal(0, console.Ticks);             // Init was tick 0
        console.Tick(default);
        Assert.Equal(1, console.Ticks);
        console.Tick(default);
        Assert.Equal(2, console.Ticks);
    }

    [Fact]
    public void TickWithoutCartThrows()
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        Assert.Throws<InvalidOperationException>(() => console.Tick(default));
    }
}
