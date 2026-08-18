using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The breakpoint contract (M4 stage 1). Not "a PDB was emitted" — that was always true while
/// breakpoints silently refused to bind — but the three things a debugger actually checks
/// before it binds one, read back out of the emitted image with
/// <c>System.Reflection.Metadata</c>:
/// <list type="number">
///   <item>the document names a file that exists on disk (it used to say <c>src/main.cs</c>,
///     which is not a path anything can open);</item>
///   <item>the document's checksum equals the hash of that file's bytes, under the algorithm
///     the document itself names — <c>requireExactSource</c> is on by default, so a mismatch
///     means "the source does not match the code" and the breakpoint stays hollow;</item>
///   <item>the cartridge's <c>Update</c> has sequence points on its statement lines, which is
///     what a line number is resolved through.</item>
/// </list>
///
/// <para><b>What breaks what.</b> Stated plainly, because "there are negative controls in this
/// file" is a claim worth being able to check:</para>
/// <list type="bullet">
///   <item><description>Put <c>CartCompiler.ReadSourceText</c> back to hashing the in-memory
///   string (<c>SourceText.From(string, Encoding.UTF8)</c>, which hashes the encoding's
///   three-byte preamble followed by the text) and the checksum stops being the hash of the
///   file: <see cref="FolderCartDocumentChecksumMatchesTheBytesOnDisk"/> goes red, so does the
///   binding check that opens
///   <see cref="AFileChangedByOneCharacterStopsMatchingTheChecksum"/>, so do the two
///   <see cref="EveryFileShapeKeepsTheChecksumContract"/> cases without a BOM — and
///   <see cref="TheFileShapeChangesTheChecksumButNotTheCartridge"/> goes red for the sharper
///   reason that the old scheme gives a BOM file and a plain file the <em>same</em> checksum,
///   collapsing four shapes onto two. The two BOM cases stay green, which is exactly the point:
///   the old scheme was right for one shape by accident and wrong for the others in
///   silence.</description></item>
///   <item><description>Remove the <c>SourceFileResolver</c> and the document goes back to
///   naming <c>src/main.cs</c>, a path nothing can open:
///   <see cref="FolderCartDocumentPointsAtTheFileOnDisk"/>,
///   <see cref="FolderCartExceptionStillNamesTheSourceLine"/> and
///   <see cref="ASourceEditedAfterLoadingCompilesTheTextThatWasLoaded"/> go red, and so does
///   every test that runs <c>AssertBindsToTheFileOnDisk</c>, on its first
///   line.</description></item>
/// </list>
///
/// <para><b>One thing this file does not claim.</b> The second half of
/// <see cref="AFileChangedByOneCharacterStopsMatchingTheChecksum"/> — a file edited by one byte
/// hashes differently — is a property of SHA-256 and cannot fail. It is there as the frame
/// around the two assertions that <em>can</em>: the binding check before the edit, and the same
/// check again after the bytes are put back, which together say the recorded checksum tracks the
/// file rather than merely being some 32 bytes that happen to differ from something.</para>
///
/// <para>The rest of the class guards the borders this fix is not allowed to cross: the
/// cartridge identity and the code budget must not notice that a disk path exists, a package
/// cartridge must keep working with no disk path at all, diagnostics must keep printing the
/// cart-relative path, and a cart exception must keep naming its line — the M1 criterion, which
/// <c>PipelineTests.CartExceptionPropagatesWithSourceLineNumber</c> already guards for an
/// in-memory cart and which is re-checked here for the folder cart, the shape whose stack-trace
/// path this change alters.</para>
/// </summary>
public sealed class DebugSymbolsTests : IDisposable
{
    // The two hash algorithms a portable PDB document may name (portable PDB spec, "Document").
    private static readonly Guid Sha1AlgorithmId = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid Sha256AlgorithmId = new("8829d00f-11b8-4213-878b-770e8597ac16");

    /// <summary>U+FEFF, written as a number so that it cannot hide in this file as itself.</summary>
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>
    /// The cart under the microscope. Line numbers are load-bearing — <c>_t++</c> is on line 9,
    /// the <c>if</c> on line 10, <c>_t = 0</c> on line 12 — so edits above them break the
    /// sequence-point expectations on purpose.
    /// </summary>
    private const string DebugCart = """
        using Quarp.Api;

        public sealed class DebugCart : Cartridge
        {
            private int _t;

            public override void Update()
            {
                _t++;
                if (_t > 10)
                {
                    _t = 0;
                }
            }

            public override void Draw()
            {
                Cls(1);
            }
        }
        """;

    /// <summary>A cart that throws from line 8, for the M1 stack-trace criterion.</summary>
    private const string CrashCart = """
        using Quarp.Api;

        public sealed class CrashCart : Cartridge
        {
            public override void Update()
            {
                int zero = Ticks - Ticks;
                _ = 1 / zero;
            }
        }
        """;

    private readonly string _root;

    public DebugSymbolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-debug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void FolderCartDocumentPointsAtTheFileOnDisk()
    {
        string folder = MakeCart("named", DebugCart);
        PdbDocument document = SingleDocument(CompileFolder(folder));

        Assert.True(Path.IsPathFullyQualified(document.Name), $"document path is not absolute: {document.Name}");
        Assert.True(File.Exists(document.Name), $"document does not exist on disk: {document.Name}");
        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "src", "main.cs")), document.Name);
    }

    [Fact]
    public void FolderCartDocumentChecksumMatchesTheBytesOnDisk()
    {
        string folder = MakeCart("checksum", DebugCart);
        AssertBindsToTheFileOnDisk(SingleDocument(CompileFolder(folder)));
    }

    /// <summary>
    /// Where a breakpoint can land. The full set measured for this cart is 8, 9, 10, 11, 12, 13,
    /// 14 (the method's braces, its three statements, and the braces of the <c>if</c> block);
    /// only the statement lines and the method's own braces are asserted, because whether a
    /// nested block's braces get their own point is the compiler's business and not the
    /// contract.
    /// </summary>
    [Fact]
    public void UpdateHasSequencePointsOnItsStatementLines()
    {
        string folder = MakeCart("seqpoints", DebugCart);
        int[] lines = SequencePointLines(CompileFolder(folder), "DebugCart", "Update");

        Assert.Contains(8, lines);      // the opening brace of Update
        Assert.Contains(9, lines);      // _t++;
        Assert.Contains(10, lines);     // if (_t > 10)
        Assert.Contains(12, lines);     // _t = 0;
        Assert.Contains(14, lines);     // its closing brace
        Assert.All(lines, line => Assert.InRange(line, 7, 14));
    }

    /// <summary>
    /// The checksum tracks the file, in both directions: it matches the bytes as loaded, stops
    /// matching the moment one byte is appended, and matches again once the byte is taken back
    /// off. The middle step is a property of SHA-256 and could not fail; the two around it are
    /// about our code, and they are why this is a test and not a comment. The state it walks
    /// through — a document whose checksum no longer matches the file — is exactly the state in
    /// which a debugger refuses to bind, and the round trip says the pipeline can be in that
    /// state and out of it for the right reasons.
    /// </summary>
    [Fact]
    public void AFileChangedByOneCharacterStopsMatchingTheChecksum()
    {
        string folder = MakeCart("tamper", DebugCart);
        PdbDocument document = SingleDocument(CompileFolder(folder));
        AssertBindsToTheFileOnDisk(document);
        byte[] original = File.ReadAllBytes(document.Name);

        byte[] edited = [.. original, (byte)' '];
        File.WriteAllBytes(document.Name, edited);
        Assert.NotEqual(SHA256.HashData(File.ReadAllBytes(document.Name)), document.Checksum);

        File.WriteAllBytes(document.Name, original);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(document.Name)), document.Checksum);
    }

    /// <summary>
    /// The sharpest control in the file: the shapes the same source can have on disk. The old
    /// scheme hashed "UTF-8 preamble + text", so it was right for the BOM file by accident and
    /// wrong for the plain one and for a checkout made with <c>core.autocrlf=true</c> —
    /// silently, in both directions. Whatever the shape, the answer must be defined, and the
    /// defined answer is "the checksum is the hash of the bytes on disk". Restore the old scheme
    /// and the two cases without a BOM go red while the two with one stay green, which is the
    /// old bug's signature written out.
    /// </summary>
    [Theory]
    [InlineData(false, "\n")]       // what every source in this repository looks like
    [InlineData(true, "\n")]        // saved by an editor that writes a BOM
    [InlineData(false, "\r\n")]     // cloned on a machine with core.autocrlf=true
    [InlineData(true, "\r\n")]
    public void EveryFileShapeKeepsTheChecksumContract(bool bom, string newline)
    {
        string folder = MakeCart($"shape-{bom}-{newline.Length}", DebugCart, bom, newline);
        CartData cart = CartSource.LoadFolder(folder);

        AssertBindsToTheFileOnDisk(SingleDocument(CompileOk(cart)));
        // And the BOM never reaches the text, so it never reaches the cartridge identity.
        Assert.False(cart.Sources[0].Text.StartsWith(ByteOrderMark));
    }

    /// <summary>
    /// The same four shapes at once, which is the sharpest way to state the border: the checksum
    /// follows the bytes (four shapes, four different checksums — it has to, or it would not be
    /// the hash of the file), while the cartridge is one and the same cartridge in all four.
    /// </summary>
    [Fact]
    public void TheFileShapeChangesTheChecksumButNotTheCartridge()
    {
        var checksums = new List<string>();
        var identities = new List<string>();
        foreach ((bool bom, string newline) in new[] { (false, "\n"), (true, "\n"), (false, "\r\n"), (true, "\r\n") })
        {
            string folder = MakeCart($"same-{bom}-{newline.Length}", DebugCart, bom, newline);
            CartData cart = CartSource.LoadFolder(folder);
            checksums.Add(Convert.ToHexString(SingleDocument(CompileOk(cart)).Checksum));
            identities.Add(Convert.ToHexString(CartIdentity.Compute(cart)));
        }

        Assert.Equal(4, checksums.Distinct().Count());
        Assert.Single(identities.Distinct());
    }

    /// <summary>
    /// The disk path is not part of what a cartridge is: the same sources with and without it
    /// hash the same and measure the same. If this ever fails, the golden replays and
    /// <c>regen_sha256</c> in CI are already broken with it.
    /// </summary>
    [Fact]
    public void DiskPathIsInvisibleToIdentityAndBudget()
    {
        string folder = MakeCart("identity", DebugCart);
        CartData onDisk = CartSource.LoadFolder(folder);
        CartSourceFile source = Assert.Single(onDisk.Sources);
        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "src", "main.cs")), source.DiskPath);

        var inMemory = new CartData
        {
            Manifest = onDisk.Manifest,
            Sources = new[] { new CartSourceFile(source.RelativePath, source.Text) },
            Gfx = onDisk.Gfx,
            Map = onDisk.Map,
            Flags = onDisk.Flags,
            Sfx = onDisk.Sfx,
            Music = onDisk.Music,
        };
        Assert.Null(inMemory.Sources[0].DiskPath);
        Assert.Equal(CartIdentity.Compute(onDisk), CartIdentity.Compute(inMemory));
        Assert.Equal(CodeBudget.Measure(onDisk.Sources), CodeBudget.Measure(inMemory.Sources));
    }

    /// <summary>
    /// A <c>.quarp8</c> has no files on disk, so it gets no disk path, no resolver and the old
    /// relative document — the known limitation recorded in the work order (M4 Р1), pinned here
    /// so that "unpack to a temp folder" cannot sneak in unnoticed. It is also the proof that
    /// the folder-cart machinery stayed off the package path: same cartridge, same identity.
    /// </summary>
    [Fact]
    public void PackagedCartHasNoDiskPathAndKeepsTheRelativeDocument()
    {
        string folder = MakeCart("packaged", DebugCart);
        string package = Path.Combine(_root, "packaged.quarp8");
        Quarp8Package.Pack(folder, package);

        CartData cart = CartSource.LoadPackage(package);
        Assert.Null(Assert.Single(cart.Sources).DiskPath);
        PdbDocument document = SingleDocument(CompileOk(cart));
        Assert.Equal("src/main.cs", document.Name);
        Assert.Equal(CartIdentity.Compute(CartSource.LoadFolder(folder)), CartIdentity.Compute(cart));
    }

    /// <summary>
    /// The M1 criterion on the path this change touches. The line number is the criterion and it
    /// survives; the file name in the trace does change for a folder cart — it is now the
    /// absolute path, with the platform's separators, instead of <c>src/main.cs</c> — which is
    /// asserted here so that nobody discovers it from a bug report.
    /// </summary>
    [Fact]
    public void FolderCartExceptionStillNamesTheSourceLine()
    {
        string folder = MakeCart("crash", CrashCart);
        using var host = CartHost.Load(CompileFolder(folder));
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(host.Cartridge);

        var thrown = Assert.Throws<DivideByZeroException>(() => console.Tick(default));
        Assert.Contains("line 8", thrown.StackTrace);       // the division sits on line 8
        Assert.Contains(Path.GetFullPath(Path.Combine(folder, "src", "main.cs")), thrown.StackTrace);
    }

    /// <summary>
    /// What the cartridge author reads when the build fails must not become a machine-local
    /// absolute path. The resolver was chosen over an absolute <c>path:</c> on the syntax tree
    /// precisely because it moves the PDB document and nothing else.
    /// </summary>
    [Fact]
    public void DiagnosticsStillNameTheCartRelativePath()
    {
        string folder = MakeCart("broken", """
            using Quarp.Api;

            public sealed class BrokenCart : Cartridge
            {
                public override void Update()
                {
                    int x = ;
                }
            }
            """);

        CartCompileResult result = CartCompiler.Compile(CartSource.LoadFolder(folder));
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Contains("src/main.cs(7,"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains(folder, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The rule when the file and the loaded text disagree, which happens whenever the author
    /// saves between load and compile: the loaded text wins. Here the file on disk is replaced
    /// by something that is not C# at all — the compile still succeeds, which is only possible
    /// if the loaded text was the one compiled, and the checksum is then honestly the in-memory
    /// one, so the debugger declines to bind instead of stopping on a line that is no longer
    /// there. Identity before convenience: the alternative would compile bytes
    /// <c>CartIdentity</c> never saw.
    /// </summary>
    [Fact]
    public void ASourceEditedAfterLoadingCompilesTheTextThatWasLoaded()
    {
        string folder = MakeCart("raced", DebugCart);
        CartData cart = CartSource.LoadFolder(folder);
        string main = Path.GetFullPath(Path.Combine(folder, "src", "main.cs"));
        File.WriteAllText(main, "this is not C# at all");

        PdbDocument document = SingleDocument(CompileOk(cart));
        Assert.Equal(main, document.Name);
        Assert.NotEqual(SHA256.HashData(File.ReadAllBytes(main)), document.Checksum);
    }

    /// <summary>
    /// Determinism is untouched: reading the source from the file instead of from a string is
    /// still the same source, so two compiles of one folder are still byte-identical.
    /// </summary>
    [Fact]
    public void FolderCartStillCompilesToIdenticalBytes()
    {
        string folder = MakeCart("deterministic", DebugCart);
        Assert.Equal(CompileFolder(folder), CompileFolder(folder));
    }

    // --- helpers ---

    private string MakeCart(string name, string source, bool bom = false, string newline = "\n")
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"),
            "{\"name\":\"debugcart\",\"author\":\"\",\"profile\":8}");

        // Written as bytes, not as text: the whole point of these tests is which bytes end up on
        // disk, and File.WriteAllText would decide the BOM question for us.
        string text = source.Replace("\r\n", "\n").Replace("\n", newline);
        byte[] body = Encoding.UTF8.GetBytes(text);
        byte[] bytes = bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
        File.WriteAllBytes(Path.Combine(folder, "src", "main.cs"), bytes);
        return folder;
    }

    /// <summary>
    /// The binding contract in one place, in the order a debugger asks it: is there a file, does
    /// the document say how it was hashed, and is that hash the hash of the file's bytes.
    /// </summary>
    private static void AssertBindsToTheFileOnDisk(PdbDocument document)
    {
        Assert.True(File.Exists(document.Name), $"the PDB names a file that is not on disk: {document.Name}");
        Assert.Equal(Sha256AlgorithmId, document.HashAlgorithm);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(document.Name)), document.Checksum);
    }

    private static byte[] CompileFolder(string folder) => CompileOk(CartSource.LoadFolder(folder));

    private static byte[] CompileOk(CartData cart)
    {
        CartCompileResult result = CartCompiler.Compile(cart);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return result.AssemblyBytes;
    }

    private sealed record PdbDocument(string Name, Guid HashAlgorithm, byte[] Checksum);

    private static PdbDocument SingleDocument(byte[] assembly) => Assert.Single(ReadDocuments(assembly));

    private static List<PdbDocument> ReadDocuments(byte[] assembly)
    {
        using var peReader = new PEReader(new MemoryStream(assembly, writable: false));
        using MetadataReaderProvider provider = OpenEmbeddedPdb(peReader);
        MetadataReader pdb = provider.GetMetadataReader();

        var documents = new List<PdbDocument>();
        foreach (DocumentHandle handle in pdb.Documents)
        {
            Document document = pdb.GetDocument(handle);
            Guid algorithm = pdb.GetGuid(document.HashAlgorithm);
            Assert.True(
                algorithm == Sha256AlgorithmId || algorithm == Sha1AlgorithmId,
                $"the document names hash algorithm {algorithm}, which is neither SHA-1 nor SHA-256");
            documents.Add(new PdbDocument(
                pdb.GetString(document.Name),
                algorithm,
                pdb.GetBlobBytes(document.Hash)));
        }
        return documents;
    }

    /// <summary>
    /// The non-hidden sequence-point lines of one method, sorted and deduplicated. Hidden points
    /// (line 0xFEEFEE) are compiler-generated glue with no source line and no breakpoint.
    /// </summary>
    private static int[] SequencePointLines(byte[] assembly, string typeName, string methodName)
    {
        using var peReader = new PEReader(new MemoryStream(assembly, writable: false));
        MetadataReader metadata = peReader.GetMetadataReader();
        using MetadataReaderProvider provider = OpenEmbeddedPdb(peReader);
        MetadataReader pdb = provider.GetMetadataReader();

        // A MethodDebugInformation row lines up with the MethodDefinition row of the same number.
        MethodDefinitionHandle method = FindMethod(metadata, typeName, methodName);
        MethodDebugInformation debugInformation = pdb.GetMethodDebugInformation(
            MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber((EntityHandle)method)));

        var lines = new List<int>();
        foreach (SequencePoint point in debugInformation.GetSequencePoints())
        {
            if (!point.IsHidden)
            {
                lines.Add(point.StartLine);
            }
        }
        lines.Sort();
        return lines.Distinct().ToArray();
    }

    private static MethodDefinitionHandle FindMethod(MetadataReader metadata, string typeName, string methodName)
    {
        foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
            if (metadata.GetString(type.Name) != typeName)
            {
                continue;
            }
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                if (metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name) == methodName)
                {
                    return methodHandle;
                }
            }
        }
        throw new InvalidOperationException($"{typeName}.{methodName} is not in the emitted assembly");
    }

    private static MetadataReaderProvider OpenEmbeddedPdb(PEReader peReader)
    {
        foreach (DebugDirectoryEntry entry in peReader.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                return peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
            }
        }
        throw new InvalidOperationException(
            "the cartridge assembly carries no embedded portable PDB — cart stack traces have just lost their line numbers");
    }
}
