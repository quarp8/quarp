using Xunit;

namespace Quarp.Analyzers.Tests;

/// <summary>QRP1003 — foreach over a hash-ordered collection in cartridge code.</summary>
public sealed class UnorderedIterationTests
{
    private static Task VerifyAsync(string source) => CartVerifier.VerifyAsync<UnorderedIterationAnalyzer>(source);

    // --- fires ---

    /// <summary>The span covers the collection expression, which is the thing to change.</summary>
    [Fact]
    public Task ForeachOverADictionary() => VerifyAsync(CartVerifier.Cart("""
            private readonly Dictionary<string, int> _scores = new Dictionary<string, int>();

            public override void Update()
            {
                int total = 0;
                foreach (KeyValuePair<string, int> entry in {|QRP1003:_scores|})
                {
                    total += entry.Value;
                }
                _ = total;
            }
        """));

    [Fact]
    public Task ForeachOverAHashSet() => VerifyAsync(CartVerifier.Cart("""
            private readonly HashSet<int> _seen = new HashSet<int>();

            public override void Update()
            {
                foreach (int cell in {|QRP1003:_seen|})
                {
                    Pset(cell, 0, 7);
                }
            }
        """));

    /// <summary>Iterating the key collection has exactly the same hash order problem.</summary>
    [Fact]
    public Task ForeachOverDictionaryKeys() => VerifyAsync(CartVerifier.Cart("""
            private readonly Dictionary<string, int> _scores = new Dictionary<string, int>();

            public override void Update()
            {
                foreach (string name in {|QRP1003:_scores.Keys|})
                {
                    _ = name;
                }
            }
        """));

    [Fact]
    public Task ForeachOverDictionaryValues() => VerifyAsync(CartVerifier.Cart("""
            private readonly Dictionary<string, int> _scores = new Dictionary<string, int>();

            public override void Update()
            {
                foreach (int score in {|QRP1003:_scores.Values|})
                {
                    _ = score;
                }
            }
        """));

    /// <summary>A deconstructing foreach is the natural way to walk a dictionary, and is a separate syntax node.</summary>
    [Fact]
    public Task DeconstructingForeachOverADictionary() => VerifyAsync(CartVerifier.Cart("""
            private readonly Dictionary<string, int> _scores = new Dictionary<string, int>();

            public override void Update()
            {
                foreach ((string name, int score) in {|QRP1003:_scores|})
                {
                    _ = name;
                    _ = score;
                }
            }
        """));

    // --- does not fire ---

    [Fact]
    public Task ForeachOverAListOrArrayIsFine() => VerifyAsync(CartVerifier.Cart("""
            private static readonly int[] DirDx = { 0, 1, 0, -1 };
            private readonly List<int> _body = new List<int>();

            public override void Update()
            {
                foreach (int dx in DirDx)
                {
                    _ = dx;
                }
                foreach (int cell in _body)
                {
                    _ = cell;
                }
            }
        """));

    /// <summary>
    /// The sorted collections enumerate in comparer order, which is reproducible — they are
    /// the recommended fix, so warning on them would send the author in circles.
    /// </summary>
    [Fact]
    public Task ForeachOverSortedCollectionsIsFine() => VerifyAsync(CartVerifier.Cart("""
            private readonly SortedDictionary<string, int> _scores = new SortedDictionary<string, int>();
            private readonly SortedSet<int> _cells = new SortedSet<int>();

            public override void Update()
            {
                foreach (KeyValuePair<string, int> entry in _scores)
                {
                    _ = entry.Value;
                }
                foreach (int cell in _cells)
                {
                    _ = cell;
                }
            }
        """));

    /// <summary>Holding a dictionary is fine; only walking it in an undefined order is not.</summary>
    [Fact]
    public Task LookupWithoutIterationIsFine() => VerifyAsync(CartVerifier.Cart("""
            private readonly Dictionary<string, int> _scores = new Dictionary<string, int>();

            public override void Update()
            {
                _scores["player"] = Ticks;
                if (_scores.TryGetValue("player", out int score))
                {
                    _ = score;
                }
            }
        """));

    /// <summary>The engine iterates dictionaries wherever it likes: it is not a simulation.</summary>
    [Fact]
    public Task ForeachOverADictionaryOutsideACartIsFine() => VerifyAsync(CartVerifier.NotACart("""
            private readonly Dictionary<string, int> _scores = new Dictionary<string, int>();
            private readonly HashSet<int> _seen = new HashSet<int>();

            public int Total()
            {
                int total = 0;
                foreach (KeyValuePair<string, int> entry in _scores)
                {
                    total += entry.Value;
                }
                foreach (int cell in _seen)
                {
                    total += cell;
                }
                return total;
            }
        """));
}
