using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Documents.UnitTests;

/// <summary>
/// DEC-DOC-009's allowlist and MIME validation, DEC-DOC-004's safe user-visible name and the owner link shape, as pure
/// decisions. SENTETIK / NOT_ENV.
/// </summary>
public sealed class DocumentContentRulesTests
{
    private const long Bound = 1024;
    internal static readonly byte[] Pdf = [.. "%PDF-1.7\n%SENTETIK\n"u8.ToArray()];
    internal static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];
    internal static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    [Theory]
    [InlineData("fatura.pdf", "application/pdf", DocumentFileKind.Pdf)]
    [InlineData("FATURA.PDF", "application/pdf; name=x", DocumentFileKind.Pdf)]
    [InlineData("tarama.pdf", "application/octet-stream", DocumentFileKind.Pdf)]
    [InlineData("tarama.pdf", "", DocumentFileKind.Pdf)]
    [InlineData("foto.png", "image/png", DocumentFileKind.Png)]
    [InlineData("foto.jpg", "image/jpeg", DocumentFileKind.Jpeg)]
    [InlineData("foto.jpeg", "image/pjpeg", DocumentFileKind.Jpeg)]
    public void An_allowlisted_kind_proven_by_its_signature_is_accepted_with_its_canonical_type(string name, string declared, DocumentFileKind kind)
    {
        var decision = DocumentContentPolicy.Classify(Name(name), declared, Content(kind), Bound);

        Assert.True(decision.IsAccepted);
        Assert.Equal(kind, decision.Kind!.Kind);
        Assert.Equal(DocumentContentPolicy.Info(kind).ContentType, decision.Kind.ContentType);
    }

    [Theory]
    [InlineData("setup.exe", "application/octet-stream")]
    [InlineData("sayfa.html", "text/html")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("arsiv.zip", "application/zip")]
    [InlineData("makro.docm", "application/vnd.ms-word.document.macroEnabled.12")]
    [InlineData("betik.js", "text/javascript")]
    [InlineData("uzantisiz", "application/pdf")]
    public void A_kind_off_the_allowlist_is_refused_whatever_its_bytes(string name, string declared)
    {
        var decision = DocumentContentPolicy.Classify(Name(name), declared, Pdf, Bound);

        Assert.False(decision.IsAccepted);
        Assert.Equal(DocumentRuleCodes.TypeNotAllowed, decision.RefusalCode);
    }

    [Fact]
    public void Bytes_that_are_not_the_named_kind_or_a_contradicting_declared_type_are_refused_as_a_mismatch()
    {
        // An image renamed to .pdf, a PDF renamed to .png, an HTML page named .pdf.
        Assert.Equal(DocumentRuleCodes.ContentMismatch, DocumentContentPolicy.Classify(Name("a.pdf"), "application/pdf", Png, Bound).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, DocumentContentPolicy.Classify(Name("a.png"), "image/png", Pdf, Bound).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            DocumentContentPolicy.Classify(Name("a.pdf"), "application/pdf", "<html><script>x</script>"u8.ToArray(), Bound).RefusalCode);
        // An allowed name and bytes, but the client declared a dangerous type: the declaration must agree or be "unknown".
        Assert.Equal(DocumentRuleCodes.ContentMismatch, DocumentContentPolicy.Classify(Name("a.pdf"), "text/html", Pdf, Bound).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, DocumentContentPolicy.Classify(Name("a.jpg"), "image/png", Jpeg, Bound).RefusalCode);
    }

    [Fact]
    public void An_empty_file_and_one_over_the_bound_are_refused_before_the_kind_is_looked_at()
    {
        Assert.Equal(DocumentRuleCodes.Empty, DocumentContentPolicy.Classify(Name("a.pdf"), "application/pdf", [], Bound).RefusalCode);
        var large = new byte[Bound + 1];
        Pdf.CopyTo(large, 0);
        Assert.Equal(DocumentRuleCodes.TooLarge, DocumentContentPolicy.Classify(Name("a.pdf"), "application/pdf", large, Bound).RefusalCode);
        Assert.True(DocumentContentPolicy.Classify(Name("a.pdf"), "application/pdf", large.AsSpan(0, (int)Bound), Bound).IsAccepted);
    }

    [Fact]
    public void The_file_name_keeps_the_last_segment_and_turkish_letters_and_converts_what_a_file_system_refuses()
    {
        Assert.Equal("Sözleşme İmzalı.pdf", DocumentFileName.TryCreate("C:\\Users\\x\\Belgeler\\Sözleşme İmzalı.pdf")!.Value);
        Assert.Equal("çğıöşü ÇĞİÖŞÜ.png", DocumentFileName.TryCreate("../../çğıöşü ÇĞİÖŞÜ.png")!.Value);
        Assert.Equal("a_b_c_d_.pdf", DocumentFileName.TryCreate("a<b>c:d?.pdf")!.Value);
        // Right-to-left override and control characters cannot disguise the extension.
        var spoofed = DocumentFileName.TryCreate("fatura\u202Efdp.exe")!;
        Assert.Equal("fatura_fdp.exe", spoofed.Value);
        Assert.Equal(".exe", spoofed.Extension);
        Assert.Equal("a_b.pdf", DocumentFileName.TryCreate("a\u0000b.pdf")!.Value);
        // NFC: a decomposed "ş" is stored composed.
        Assert.Equal("kaş.pdf", DocumentFileName.TryCreate("kas\u0327.pdf")!.Value);
        Assert.Equal(".pdf", DocumentFileName.TryCreate("  Rapor.PDF  ")!.Extension);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("dizin/")]
    [InlineData("???")]
    public void A_name_with_nothing_usable_left_is_refused(string? raw) => Assert.Null(DocumentFileName.TryCreate(raw));

    [Fact]
    public void A_long_name_is_cut_in_its_stem_and_keeps_its_extension()
    {
        var name = DocumentFileName.TryCreate(new string('ş', 500) + ".pdf")!;

        Assert.Equal(DocumentFileName.MaximumLength, name.Value.Length);
        Assert.EndsWith(".pdf", name.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_content_hash_is_the_lower_case_sha256_of_the_exact_bytes()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", DocumentContentHash.Compute([]));
        Assert.True(DocumentContentHash.IsWellFormed(DocumentContentHash.Compute(Pdf)));
        Assert.False(DocumentContentHash.IsWellFormed("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"));
        Assert.False(DocumentContentHash.IsWellFormed("abc"));
    }

    [Theory]
    [InlineData("counterparty.party", true)]
    [InlineData("sales.order-line", true)]
    [InlineData("counterparty", false)]
    [InlineData("Counterparty.Party", false)]
    [InlineData("counterparty..party", false)]
    [InlineData("counterparty.party/../x", false)]
    [InlineData("", false)]
    public void An_owner_link_kind_is_a_dotted_lower_case_name_and_the_owner_id_is_not_empty(string kind, bool valid)
    {
        Assert.Equal(valid, DocumentOwnerLink.TryCreate(kind, Guid.NewGuid()) is not null);
        Assert.Null(DocumentOwnerLink.TryCreate("counterparty.party", Guid.Empty));
    }

    private static DocumentFileName Name(string value) => DocumentFileName.TryCreate(value)!;

    private static byte[] Content(DocumentFileKind kind) => kind switch
    {
        DocumentFileKind.Pdf => Pdf,
        DocumentFileKind.Png => Png,
        _ => Jpeg,
    };
}
