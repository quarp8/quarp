#:project ../../../src/Quarp.CartKit/Quarp.CartKit.csproj

// Generates golden.input — the button track behind carts/snake/replays/golden.qrpr.
//
// Run it from the repository root:
//
//     dotnet run carts/snake/replays/plan-golden.cs
//
// This is a fixture generator, not engine code, and it is committed for one reason: without
// it "regenerate the golden" is not an instruction anyone can follow. It is a .NET 10
// file-based app (`#:project` above), so it needs no csproj of its own and is not part of
// the solution. It also lives outside carts/snake/src, so it is not part of the cartridge:
// the loader reads only src/**/*.cs and the cart identity hashes only those plus the assets.
//
// --- what it plans -------------------------------------------------------------------
//
// The snake follows a Hamiltonian cycle of the 16x8 field: rows are swept right on even
// rows and left on odd ones, and column 0 is the return lane from the bottom back to the
// top. That tour visits all 8 + 15*8 = 128 cells and closes, so the snake can never hit a
// wall and can never hit its own body while the body is shorter than the field — the run
// stays alive for as long as you ask, and eats every apple that lands in front of it. That
// is the whole point: an idle recording of this cartridge is dead by tick 64 and its frames
// stop depending on the simulation (see ci.yml and README.md).
//
// --- why it reads private fields -------------------------------------------------------
//
// The track cannot be written blind. A turn has to be pressed while the snake sits on a
// corner cell, and which tick that is depends on the step interval, which shortens with
// every apple, which depends on the RNG. So the planner runs the real cartridge and, once
// per tick, reads its head position, direction and pending turn by reflection. That couples
// this file to four field names in carts/snake/src/main.cs; if they are renamed, this file
// fails loudly on startup rather than producing a track that quietly kills the snake.

using System.Globalization;
using System.Reflection;
using System.Text;
using Quarp.Api;
using Quarp.CartKit;
using Quarp.Core;

const int GridW = 16;
const int GridH = 8;
const int DirLeft = 0, DirRight = 1, DirUp = 2, DirDown = 3, DirNone = -1;

string cartPath = "carts/snake";
string outPath = "carts/snake/replays/golden.input";
int ticks = 3000;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--cart" when i + 1 < args.Length: cartPath = args[++i]; break;
        case "-o" or "--out" when i + 1 < args.Length: outPath = args[++i]; break;
        case "--ticks" when i + 1 < args.Length: ticks = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        default:
            Console.Error.WriteLine("usage: dotnet run plan-golden.cs [--cart <path>] [-o <file>] [--ticks N]");
            return 1;
    }
}

CartData data = CartSource.Load(cartPath);
CartCompileResult result = CartCompiler.Compile(data);
foreach (string warning in result.Warnings)
{
    Console.Error.WriteLine(warning);
}
if (!result.Success)
{
    foreach (string diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine(diagnostic);
    }
    return 1;
}

using CartHost host = CartHost.Load(result.AssemblyBytes);
Cartridge cart = host.Cartridge;
// Same starting conditions as `quarp sim` and `quarp replay record`: seed 0, persistent
// memory zeroed, save.dat untouched. A track planned against a different start would not
// reproduce the recording it is supposed to describe.
var console = new VirtualConsole(ConsoleProfile.Profile8, data.Gfx, data.Map, data.Flags);
console.AttachCart(cart, seed: 0);

Type type = cart.GetType();
// IL2075: reflecting over a type obtained at run time. This is a fixture generator that is
// never trimmed or published, and a missing field is reported below as a plain message.
#pragma warning disable IL2075
FieldInfo Field(string name) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException(
        $"{type.FullName} has no field '{name}'. carts/snake changed shape; update this planner "
        + "before regenerating the golden track.");

FieldInfo bodyXField = Field("_bodyX");
FieldInfo bodyYField = Field("_bodyY");
FieldInfo headSlotField = Field("_headSlot");
FieldInfo dirField = Field("_dir");
FieldInfo pendingField = Field("_pendingDir");
FieldInfo stateField = Field("_state");
FieldInfo scoreField = Field("_score");
FieldInfo lengthField = Field("_length");
#pragma warning restore IL2075

int Int(FieldInfo field) => (int)field.GetValue(cart)!;
bool Playing() => Int(stateField) == 0;   // GameState.Playing

var entries = new List<string>();
byte held = 0;
int aliveThrough = 0;

for (int tick = 1; tick <= ticks; tick++)
{
    byte mask = 0;
    if (Playing())
    {
        int slot = Int(headSlotField);
        int headX = ((int[])bodyXField.GetValue(cart)!)[slot];
        int headY = ((int[])bodyYField.GetValue(cart)!)[slot];
        int want = NextDir(headX, headY);
        // One turn is buffered at a time and consumed by the next step, so pressing while a
        // turn is already pending would be thrown away — and holding the button would stop
        // the next Btnp edge from registering at all.
        if (want != Int(dirField) && Int(pendingField) == DirNone)
        {
            mask = MaskOf(want);
        }
    }
    if (mask != held)
    {
        entries.Add($"{tick}:{LettersOf(mask)}");
        held = mask;
    }
    console.Tick(new InputState(mask, 0));
    if (Playing())
    {
        aliveThrough = tick;
    }
}

int score = Int(scoreField);
int length = Int(lengthField);
if (aliveThrough != ticks)
{
    Console.Error.WriteLine(
        $"plan-golden: the snake died at tick {aliveThrough + 1} of {ticks}. The planned track is not "
        + "a live-gameplay golden; fix the cycle or shorten --ticks before recording.");
    return 1;
}

var text = new StringBuilder();
text.Append(CultureInfo.InvariantCulture, $"""
    # carts/snake — golden replay input track. GENERATED by plan-golden.cs; do not hand-edit.
    #
    #     dotnet run carts/snake/replays/plan-golden.cs --ticks {ticks}
    #
    # The snake walks a Hamiltonian cycle of the 16x8 field: rows swept right on even rows
    # and left on odd ones, column 0 the return lane back to the top. A closed tour of all
    # 128 cells cannot hit a wall and cannot hit its own body while the body is shorter than
    # the field, so this run is alive from the first tick to the last and eats every apple
    # that lands in front of it. Over {ticks} ticks it scores {score} and grows from 3 to {length}.
    #
    # Grammar (quarp replay record --input-file): tick:buttons, entries separated by commas
    # or newlines, '#' starts a comment. Every turn is a *tap* — press on one tick, release
    # on the next — because the cartridge turns on Btnp, "pressed this tick and not the
    # last". The release entries are therefore not noise: without them a single press would
    # turn once and then sit held forever, and no further turn would ever register.

    """);
for (int i = 0; i < entries.Count; i += 12)
{
    text.AppendJoin(',', entries.Skip(i).Take(12));
    text.Append(i + 12 < entries.Count ? ",\n" : "\n");
}

string? folder = Path.GetDirectoryName(Path.GetFullPath(outPath));
if (!string.IsNullOrEmpty(folder))
{
    Directory.CreateDirectory(folder);
}
// UTF-8 without BOM and LF line endings, so the file is byte-identical wherever it is
// regenerated — the same reason .qrpr is little-endian everywhere (REPLAY-FORMAT §1).
File.WriteAllText(outPath, text.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));

Console.WriteLine($"wrote {outPath}: {entries.Count} entries ({entries.Count / 2} turns) over {ticks} ticks");
Console.WriteLine($"snake alive through tick {aliveThrough}, score {score}, length {length}");
Console.WriteLine($"final frame {FrameHash.Of(console.Framebuffer)}");
Console.WriteLine("now record it:");
Console.WriteLine(
    $"  quarp replay record {cartPath} -o carts/snake/replays/golden.qrpr --ticks {ticks} "
    + $"--input-file {outPath} --every 20");
return 0;

// The cycle. Every cell has exactly one successor, and following them from any cell walks
// all 128 and returns to the start.
static int NextDir(int x, int y)
{
    if (x == 0)
    {
        return y == 0 ? DirRight : DirUp;               // the return lane, and its top corner
    }
    if ((y & 1) == 0)
    {
        return x < GridW - 1 ? DirRight : DirDown;      // even rows sweep right
    }
    if (x > 1)
    {
        return DirLeft;                                 // odd rows sweep left
    }
    return y == GridH - 1 ? DirLeft : DirDown;          // last odd row steps into column 0
}

static byte MaskOf(int dir) => (byte)(1 << (int)(dir switch
{
    DirLeft => Button.Left,
    DirRight => Button.Right,
    DirUp => Button.Up,
    DirDown => Button.Down,
    _ => throw new ArgumentOutOfRangeException(nameof(dir)),
}));

static string LettersOf(byte mask)
{
    var letters = new StringBuilder(4);
    if ((mask & (1 << (int)Button.Left)) != 0) letters.Append('L');
    if ((mask & (1 << (int)Button.Right)) != 0) letters.Append('R');
    if ((mask & (1 << (int)Button.Up)) != 0) letters.Append('U');
    if ((mask & (1 << (int)Button.Down)) != 0) letters.Append('D');
    return letters.ToString();
}
