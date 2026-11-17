namespace GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

/// <summary>Evidence of the issued source, pinned to a Documents version. ETTN alone is not evidence.</summary>
public sealed record DispatchKilogramDocument(Guid Ettn, string Kind, Guid DocumentId, int DocumentVersion, string Sha256);

/// <summary>Frozen dispatch quantities carried to invoices; no monetary amounts or invoice-level reallocation.</summary>
public sealed record DispatchKilogramSnapshot(IReadOnlyList<DispatchKilogramAllocatedLine> Lines,
    DispatchKilogramDocument? FinalizedDocument)
{
    public static DispatchKilogramSnapshot Theoretical(IReadOnlyList<DispatchKilogramSourceLine> lines,
        IReadOnlyList<DispatchKilogramProductTotal> totals) =>
        new(Array.AsReadOnly(DispatchKilogramAllocation.Calculate(lines, totals).ToArray()), null);

    public static DispatchKilogramSnapshot Finalized(IReadOnlyList<DispatchKilogramSourceLine> lines,
        IReadOnlyList<DispatchKilogramProductTotal> totals, IReadOnlyList<DispatchKilogramDeclaredLine> declared,
        DispatchKilogramDocument document)
    {
        RequireDocument(document);
        return new(Array.AsReadOnly(DispatchKilogramAllocation.CopyFinalizedDocument(lines, totals, declared).ToArray()), document);
    }

    public DispatchKilogramSnapshot ValidateAndCopy(IReadOnlyList<WeighingDifferenceProductResult> products)
    {
        if (Lines is null || Lines.Any(line => line is null)) throw new ArgumentException("İrsaliye kg dağılımı okunamıyor.");
        var sources = Lines.Select(line => new DispatchKilogramSourceLine(line.LineNo, line.ShipmentId, line.ProductId, line.TheoreticalKg)).ToArray();
        var totals = products.Select(product => new DispatchKilogramProductTotal(product.ProductId, product.ScaleKg.Value)).ToArray();
        if (products.Any(product => Lines.Where(line => line.ProductId == product.ProductId).Sum(line => line.TheoreticalKg) != product.NoteKg.Value))
            throw new ArgumentException("İrsaliye kg dağılımının kaynak miktarları eşleşmiyor.");
        var expected = FinalizedDocument is null ? Theoretical(sources, totals) : Finalized(sources, totals,
            Lines.Select(line => new DispatchKilogramDeclaredLine(line.LineNo, line.ShipmentId, line.ProductId, line.Kilograms)).ToArray(), FinalizedDocument);
        if (!expected.Lines.SequenceEqual(Lines.OrderBy(line => line.LineNo)))
            throw new ArgumentException("İrsaliye kg dağılımı kaynak kararıyla eşleşmiyor.");
        return expected;
    }

    private static void RequireDocument(DispatchKilogramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Ettn == Guid.Empty || document.DocumentId == Guid.Empty || document.DocumentVersion < 1
            || document.Kind is not ("ElectronicDispatch" or "ElectronicInvoice")
            || document.Sha256 is not { Length: 64 } || !document.Sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Kesinleşmiş belge kanıtı okunamıyor.");
    }
}
