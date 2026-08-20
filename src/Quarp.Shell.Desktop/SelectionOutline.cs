namespace Quarp.Shell.Desktop;

/// <summary>
/// One dash of the marching-ants selection outline, in canvas-local window pixels — a
/// ready-to-draw rectangle plus which of the two inks it takes. A plain record struct so the
/// renderer's whole job is <c>batch.Draw(white, rect, Bright ? light : dark)</c> and the
/// tests compare dashes by value.
/// </summary>
public readonly record struct AntDash(int X, int Y, int Width, int Height, bool Bright);

/// <summary>
/// The marching-ants geometry (M9 stage 2.5, wave 2g — the owner's third review: the white
/// bounding box and the blue fill are gone; the selection is a dashed animated frame on the
/// MASK BOUNDARY, like Photoshop/Aseprite). Headless like <see cref="ToolbarFlyout"/> and for
/// the same reason: which edges are boundary, how they chain into loops, and where each dark
/// and light dash falls at a given moment are all provable without a window — including the
/// animation itself, whose named negative control is "freeze the phase and the phase tests
/// go red". The renderer only turns the rectangles into quads.
///
/// <para><b>Boundary, not bounding box.</b> An edge belongs to the outline exactly when it
/// separates a selected pixel from an unselected one — the region border counts as unselected
/// because the mask predicate answers false out of range — so a brush- or wand-shaped mask
/// shows its true silhouette, holes included. Edges are oriented clockwise (interior on the
/// right of travel, screen y down) and chained into closed loops; where two selected pixels
/// touch only diagonally the walk prefers the right turn, which resolves the shared corner
/// into two separate loops instead of a figure-eight.</para>
///
/// <para><b>Dashes and time.</b> The pattern alternates a light and a dark segment of
/// <c>dashLength</c> window pixels — the classic answer to "readable on any background":
/// whatever pixels sit under the outline, half the dashes contrast with them. The phase (how
/// far the pattern has marched) is a pure function of the wall-clock seconds the shell's Draw
/// already receives; the ants are host chrome like the tooltip clock, so no simulation state
/// and no frame hash can ever see them. Each loop's pattern is anchored at its own first
/// edge, so the seam where a loop's length is not a whole number of periods sits still at
/// that anchor — exactly as it does in the classics — while the dashes march along the
/// travel direction.</para>
/// </summary>
public static class SelectionOutline
{
    /// <summary>Seconds for the pattern to march one full period (a light plus a dark dash) past a point — slow enough to read, fast enough to be visibly alive.</summary>
    public const double PeriodSeconds = 0.8;

    /// <summary>
    /// The march made a number: the pattern's offset at a moment, in window pixels
    /// 0..2*dashLength-1. The one owner of the animation's speed — <see cref="Collect"/> takes
    /// the result rather than the time so tests can pin exact dash layouts at exact phases.
    /// </summary>
    public static int Phase(double timeSeconds, int dashLength) =>
        (int)(timeSeconds / PeriodSeconds % 1.0 * (2 * dashLength));

    /// <summary>Corner steps per travel direction: 0 → +x (interior below), 1 → +y (interior left), 2 → -x (interior above), 3 → -y (interior right).</summary>
    private static readonly (int Dx, int Dy)[] _step = { (1, 0), (0, 1), (-1, 0), (0, -1) };

    /// <summary>Right turn first, then straight, then left — the tie-break that keeps diagonal touches as two loops.</summary>
    private static readonly int[] _turnPreference = { 1, 0, 3 };

    /// <summary>
    /// Computes a mask's outline as dash rectangles. <paramref name="selected"/> answers
    /// region-local pixel coordinates and must answer false outside 0..side-1 — exactly what
    /// <see cref="SpriteEditorSession.IsSelected"/> does; a floating move is outlined by
    /// asking through the offset, which is the caller's one line. <paramref name="scale"/> is
    /// window pixels per region pixel and <paramref name="thickness"/> the dash's cross size;
    /// dashes are inset into the selected side of the boundary, so the outline never leaves
    /// the canvas rectangle. <paramref name="output"/> is caller-owned and cleared here — a
    /// per-frame caller reuses one list instead of allocating sixty a second.
    /// </summary>
    public static void Collect(
        Func<int, int, bool> selected, int side, int scale, int dashLength, int thickness,
        int phase, List<AntDash> output)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();

        // Every boundary edge in one deterministic row-major scan. Determinism matters beyond
        // testability: the loop anchors (and with them the dash seams) must not jump between
        // frames, or the outline would flicker instead of march.
        var edges = new List<(int X, int Y, int Direction)>();
        var starts = new Dictionary<(int X, int Y), List<int>>();
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                if (!selected(x, y))
                {
                    continue;
                }
                if (!selected(x, y - 1))
                {
                    AddEdge(edges, starts, x, y, 0);            // top side, travelling right
                }
                if (!selected(x + 1, y))
                {
                    AddEdge(edges, starts, x + 1, y, 1);        // right side, travelling down
                }
                if (!selected(x, y + 1))
                {
                    AddEdge(edges, starts, x + 1, y + 1, 2);    // bottom side, travelling left
                }
                if (!selected(x - 1, y))
                {
                    AddEdge(edges, starts, x, y + 1, 3);        // left side, travelling up
                }
            }
        }

        var used = new bool[edges.Count];
        for (int first = 0; first < edges.Count; first++)
        {
            if (used[first])
            {
                continue;
            }
            // Walk one closed loop, cutting each edge into dashes as the arc length runs.
            int run = 0;
            int current = first;
            do
            {
                used[current] = true;
                (int cornerX, int cornerY, int direction) = edges[current];
                EmitEdgeDashes(output, cornerX, cornerY, direction, scale, dashLength, thickness, phase, run);
                run += scale;
                current = NextEdge(edges, starts, used, current);
            }
            while (current >= 0);
        }
    }

    private static void AddEdge(
        List<(int X, int Y, int Direction)> edges,
        Dictionary<(int X, int Y), List<int>> starts,
        int x, int y, int direction)
    {
        if (!starts.TryGetValue((x, y), out List<int>? at))
        {
            at = new List<int>(2);      // 2 is the maximum: only a diagonal touch shares a start corner
            starts[(x, y)] = at;
        }
        at.Add(edges.Count);
        edges.Add((x, y, direction));
    }

    /// <summary>
    /// The loop's next edge: the unused one leaving the current edge's end corner, preferring
    /// the right turn, then straight, then left. Every boundary corner has as many exits as
    /// entries (each selected-unselected flip contributes one of each), so -1 means the loop
    /// closed — the walk is back at an edge already spent.
    /// </summary>
    private static int NextEdge(
        List<(int X, int Y, int Direction)> edges,
        Dictionary<(int X, int Y), List<int>> starts,
        bool[] used, int current)
    {
        (int cornerX, int cornerY, int direction) = edges[current];
        var end = (cornerX + _step[direction].Dx, cornerY + _step[direction].Dy);
        if (!starts.TryGetValue(end, out List<int>? candidates))
        {
            return -1;
        }
        foreach (int turn in _turnPreference)
        {
            int want = (direction + turn) % 4;
            foreach (int index in candidates)
            {
                if (!used[index] && edges[index].Direction == want)
                {
                    return index;
                }
            }
        }
        return -1;
    }

    /// <summary>
    /// One edge (scale window pixels of loop arc) cut into dashes: the ink flips every
    /// <paramref name="dashLength"/> pixels of arc, and subtracting the phase shifts the whole
    /// pattern forward along the travel direction as time grows — the march itself.
    /// </summary>
    private static void EmitEdgeDashes(
        List<AntDash> output, int cornerX, int cornerY, int direction,
        int scale, int dashLength, int thickness, int phase, int run)
    {
        int period = 2 * dashLength;
        int shift = ((phase % period) + period) % period;   // tolerate any caller's phase, not just Phase()'s
        int offset = 0;
        while (offset < scale)
        {
            int position = (run + offset - shift + period) % period;
            bool bright = position < dashLength;
            int length = Math.Min(scale - offset, (bright ? dashLength : period) - position);
            output.Add(DashRect(cornerX, cornerY, direction, scale, offset, length, thickness, bright));
            offset += length;
        }
    }

    /// <summary>The dash's rectangle in canvas-local window pixels, inset into the interior (right of travel) so it never leaves the canvas.</summary>
    private static AntDash DashRect(
        int cornerX, int cornerY, int direction, int scale, int offset, int length, int thickness, bool bright)
    {
        int px = cornerX * scale;
        int py = cornerY * scale;
        return direction switch
        {
            0 => new AntDash(px + offset, py, length, thickness, bright),                       // → interior below
            1 => new AntDash(px - thickness, py + offset, thickness, length, bright),           // ↓ interior left
            2 => new AntDash(px - offset - length, py - thickness, length, thickness, bright),  // ← interior above
            _ => new AntDash(px, py - offset - length, thickness, length, bright),              // ↑ interior right
        };
    }
}
