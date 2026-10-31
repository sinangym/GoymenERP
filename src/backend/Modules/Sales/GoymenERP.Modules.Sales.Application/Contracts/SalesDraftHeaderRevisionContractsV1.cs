using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// DEC-SAL-008 / SAT-D86: one instalment of an order's due terms on the wire — its sequence (1..n), the basis it counts from
/// (<see cref="SalesDueBasisNamesV1"/>), the whole calendar days after the basis and its share of the invoice as exact percent
/// text. Strict: every member present.
/// </summary>
public sealed record SalesDueInstalmentV1(int SequenceNo, string Basis, int DaysAfter, string SharePercent);

/// <summary>DEC-SAL-008: an order's due terms — one instalment ("net N gün") or several ("taksit"; bases may differ: "karma").</summary>
public sealed record SalesDueTermsV1(IReadOnlyList<SalesDueInstalmentV1> Instalments);

/// <summary>The wire names of <see cref="SalesDueBasis"/> (exact, case-sensitive).</summary>
public static class SalesDueBasisNamesV1
{
    public const string InvoiceDate = "InvoiceDate";
    public const string ShipmentDate = "ShipmentDate";
    public const string OrderDate = "OrderDate";

    public static IReadOnlyList<string> All { get; } = [InvoiceDate, ShipmentDate, OrderDate];

    public static bool TryParse(string? name, out SalesDueBasis basis)
    {
        basis = name switch
        {
            InvoiceDate => SalesDueBasis.InvoiceDate,
            ShipmentDate => SalesDueBasis.ShipmentDate,
            OrderDate => SalesDueBasis.OrderDate,
            _ => 0,
        };
        return basis != 0;
    }

    public static string Name(SalesDueBasis basis) => basis switch
    {
        SalesDueBasis.InvoiceDate => InvoiceDate,
        SalesDueBasis.ShipmentDate => ShipmentDate,
        SalesDueBasis.OrderDate => OrderDate,
        _ => throw new ArgumentOutOfRangeException(nameof(basis)),
    };
}

/// <summary>
/// API-04 (DEC-SAL-002, DEC-SAL-008; plan §3.1): <c>POST /api/v1/sales/drafts/{id}/header-revision</c> — the expected
/// concurrency version, the reason (required; why the order changes) and the WHOLE new header: the customer (party + account),
/// the currency, the delivery address and contact references (null = none), the sales owner (an Identity user id, null = none)
/// and the due terms (null = none). Strict: every member present (null where allowed), no other member. Unchanged members
/// repeat the current value; a request that changes nothing is refused.
/// </summary>
public sealed record SalesDraftHeaderRevisionRequest(
    string ExpectedVersion,
    string Reason,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    SalesDraftOwnerReferenceV1? DeliveryAddress,
    SalesDraftOwnerReferenceV1? Contact,
    Guid? SalesOwnerUserId,
    SalesDueTermsV1? DueTerms);

/// <summary>
/// API-04: <c>GET /api/v1/sales/drafts/{id}/header</c> under <c>sales.drafts.read</c> — the order's revisable header as it
/// stands (the S-04 header-revision form's starting point): the draft version and revision, the customer, the currency, the
/// delivery references, the sales owner (id + Identity display name when the directory answered, else null) and the due terms.
/// The due terms are commercial terms: shown only to a caller who also holds <c>sales.orders.price.read</c>
/// (<see cref="DueTermsDisclosure"/> <c>Visible</c>), else withheld (<c>Withheld</c>, <c>dueTerms</c> null) — the Sevkiyatçı's
/// deny-first price rule (Sinan §4). <see cref="CustomerOrCurrencyRevisionPending"/>: the current revision names another
/// customer or currency than the last approved one, so nothing ships until it is approved.
/// </summary>
public sealed record SalesDraftHeaderV1(
    string DraftVersion,
    string Revision,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    SalesDraftOwnerReferenceV1? DeliveryAddress,
    SalesDraftOwnerReferenceV1? Contact,
    Guid? SalesOwnerUserId,
    string? SalesOwnerDisplayName,
    string DueTermsDisclosure,
    SalesDueTermsV1? DueTerms,
    bool CustomerOrCurrencyRevisionPending);

public static class SalesDraftHeaderApiV1
{
    public const string HeaderRevisionPath = "/api/v1/sales/drafts/{id}/header-revision";
    public const string HeaderPath = "/api/v1/sales/drafts/{id}/header";

    /// <summary>
    /// The strict body of a header revision: the expected version as on every state command, the reason present (at most
    /// 4000; the domain requires it non-blank), non-empty customer and currency ids, well-formed owner references, a non-empty
    /// sales owner id when given, and due terms of 1..<see cref="SalesDueTerms.MaximumInstalments"/> instalments, each with a
    /// known basis name and plain decimal share text. The domain decides the rest (sequence, ranges, the shares' sum, no-change).
    /// </summary>
    public static SalesDraftHeaderRevisionRequest Read(string json)
    {
        var request = JsonSerializer.Deserialize<SalesDraftHeaderRevisionRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        if (string.IsNullOrEmpty(request.ExpectedVersion) || request.ExpectedVersion.Length > 19
            || !Regex.IsMatch(request.ExpectedVersion, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
        if (request.Reason is null || request.Reason.Length > 4000)
            throw new JsonException("Invalid reason.");
        if (request.PartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty || request.CurrencyId == Guid.Empty
            || request.SalesOwnerUserId == Guid.Empty)
            throw new JsonException("Invalid header identity.");
        foreach (var reference in new[] { request.DeliveryAddress, request.Contact })
        {
            if (reference is not null && (reference.Id == Guid.Empty || string.IsNullOrWhiteSpace(reference.ExpectedVersion)
                    || reference.ExpectedVersion.Length > 128))
                throw new JsonException("Invalid owner reference.");
        }

        if (request.DueTerms is { } terms)
        {
            if (terms.Instalments is null || terms.Instalments.Count is < 1 or > SalesDueTerms.MaximumInstalments)
                throw new JsonException("Invalid due terms.");
            foreach (var instalment in terms.Instalments)
            {
                if (instalment is null || !SalesDueBasisNamesV1.TryParse(instalment.Basis, out _))
                    throw new JsonException("Invalid due instalment.");
                SalesDraftJson.RequireDecimalShape(instalment.SharePercent, positive: true);
            }
        }

        return request;
    }

    /// <summary>The domain's due terms from the wire (shape already checked); a rule refusal names the instalment.</summary>
    public static SalesDueTerms? ToDomain(SalesDueTermsV1? terms)
    {
        if (terms is null)
        {
            return null;
        }

        return SalesDueTerms.Create([.. terms.Instalments.Select(instalment => new SalesDueInstalment(instalment.SequenceNo,
            SalesDueBasisNamesV1.TryParse(instalment.Basis, out var basis) ? basis : 0, instalment.DaysAfter, instalment.SharePercent))]);
    }

    /// <summary>The wire due terms of the domain's.</summary>
    public static SalesDueTermsV1? ToWire(SalesDueTerms? terms) =>
        terms is null
            ? null
            : new SalesDueTermsV1([.. terms.Instalments.Select(instalment => new SalesDueInstalmentV1(instalment.SequenceNo,
                SalesDueBasisNamesV1.Name(instalment.Basis), instalment.DaysAfter, instalment.SharePercent))]);
}
