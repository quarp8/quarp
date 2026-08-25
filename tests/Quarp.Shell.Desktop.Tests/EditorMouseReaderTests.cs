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
        Assert.True(pick.RightDown);
        Assert.False(pick.LeftPressed);
        Assert.False(heldPick.RightPressed);    // one press, one gesture — not one per frame
        Assert.True(heldPick.RightDown);        // but the hold is reported: the map's erase drag rides it

        EditorMouse up = reader.Read(State(5, 5, ButtonState.Released, ButtonState.Released));
        Assert.True(up.RightReleased);          // and its release commits the erase as one undo step
        Assert.False(up.RightDown);
        Assert.False(reader.Read(State(5, 5, ButtonState.Released, ButtonState.Released)).RightReleased);
    }

    /// <summary>
    /// The middle button, added in wave 3d for the map's tile eyedropper (TIC-80
    /// <c>processMouseDrawMode</c>): one edge per physical press, and independent of the other
    /// two. Break recipe: report it from <c>LeftButton</c> in <see cref="EditorMouseReader"/>
    /// and the independence assertions go red.
    /// </summary>
    [Fact]
    public void TheMiddleButtonEdgesIndependently()
    {
        var reader = new EditorMouseReader();
        static MouseState Middle(ButtonState middle) =>
            new(7, 8, 0, ButtonState.Released, middle, ButtonState.Released,
                ButtonState.Released, ButtonState.Released);

        EditorMouse sample = reader.Read(Middle(ButtonState.Pressed));
        EditorMouse held = reader.Read(Middle(ButtonState.Pressed));

        Assert.True(sample.MiddlePressed);
        Assert.False(sample.LeftPressed);
        Assert.False(sample.RightPressed);
        Assert.False(held.MiddlePressed);       // sampling happens once, on the press
        Assert.False(reader.Read(Middle(ButtonState.Released)).MiddlePressed);
    }

    [Fact]
    public void PositionsPassThroughUntouched()
    {
        var reader = new EditorMouseReader();

        EditorMouse mouse = reader.Read(State(123, 456, ButtonState.Released, ButtonState.Released));

        Assert.Equal((123, 456), (mouse.X, mouse.Y));
    }

    /// <summary>
    /// The wheel arrives as a delta, not the cumulative value MonoGame reports (wave 2h —
    /// the sheet scroll consumes it): one notch shows once, a still wheel shows zero.
    /// </summary>
    [Fact]
    public void TheWheelReportsPerFrameDeltas()
    {
        var reader = new EditorMouseReader();
        static MouseState Wheeled(int value) =>
            new(0, 0, value, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released);

        Assert.Equal(0, reader.Read(Wheeled(0)).WheelDelta);
        Assert.Equal(120, reader.Read(Wheeled(120)).WheelDelta);    // one notch up
        Assert.Equal(0, reader.Read(Wheeled(120)).WheelDelta);      // wheel at rest — no repeat
        Assert.Equal(-240, reader.Read(Wheeled(-120)).WheelDelta);  // two notches down
    }
}
