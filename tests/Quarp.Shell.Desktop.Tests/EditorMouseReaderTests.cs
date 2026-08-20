using Microsoft.Xna.Framework.Input;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// Edge detection for the editor's mouse — the property the pencil's undo granularity hangs
/// on: exactly one <c>LeftPressed</c> per physical press (that is one <c>BeginStroke</c>) and
/// exactly one <c>LeftReleased</c> per physical release (one committed undo step), however
/// many frames the button spends held. Synthetic <see cref="MouseState"/>s, no window — the
/// reason the reader takes states instead of polling a global.
/// </summary>
public class EditorMouseReaderTests
{
    private static MouseState State(int x, int y, ButtonState left, ButtonState right) =>
        new(x, y, 0, left, ButtonState.Released, right, ButtonState.Released, ButtonState.Released);

    [Fact]
    public void APressIsOneEdgeHoweverLongTheButtonIsHeld()
    {
        var reader = new EditorMouseReader();

        EditorMouse first = reader.Read(State(10, 20, ButtonState.Pressed, ButtonState.Released));
        EditorMouse held = reader.Read(State(11, 21, ButtonState.Pressed, ButtonState.Released));

        Assert.True(first.LeftPressed);
        Assert.True(first.LeftDown);
        Assert.False(held.LeftPressed);     // the second frame is a drag, not a second stroke
        Assert.True(held.LeftDown);
    }

    [Fact]
    public void AReleaseIsOneEdge()
    {
        var reader = new EditorMouseReader();
        reader.Read(State(0, 0, ButtonState.Pressed, ButtonState.Released));

        EditorMouse released = reader.Read(State(0, 0, ButtonState.Released, ButtonState.Released));
        EditorMouse idle = reader.Read(State(0, 0, ButtonState.Released, ButtonState.Released));

        Assert.True(released.LeftReleased);
        Assert.False(released.LeftDown);
        Assert.False(idle.LeftReleased);    // one release, one committed stroke
    }

    [Fact]
    public void TheRightButtonEdgesIndependently()
    {
        var reader = new EditorMouseReader();

        EditorMouse pick = reader.Read(State(5, 5, ButtonState.Released, ButtonState.Pressed));
        EditorMouse heldPick = reader.Read(State(5, 5, ButtonState.Released, ButtonState.Pressed));

        Assert.True(pick.RightPressed);
        Assert.False(pick.LeftPressed);
        Assert.False(heldPick.RightPressed);    // the eyedropper samples on the press, not per frame
    }

    [Fact]
    public void PositionsPassThroughUntouched()
    {
        var reader = new EditorMouseReader();

        EditorMouse mouse = reader.Read(State(123, 456, ButtonState.Released, ButtonState.Released));

        Assert.Equal((123, 456), (mouse.X, mouse.Y));
    }
}
