using GoymenERP.BuildingBlocks.Monetary;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Domain.Invoices;

/// <summary>ALS-D7/D44: imported supplier facts. Amounts are from the official document, never recomputed as tax policy.</summary>
public sealed record PurchaseInvoiceSource(Guid DocumentId, long DocumentVersion, string Sha256,
    string OfficialNumber, Guid Ettn, DateOnly InvoiceDate);

public sealed record PurchaseInvoiceAmounts(decimal Net, decimal Vat, decimal Withholding)
{
    public decimal Payable => checked(Net + Vat - Withholding);

    internal void Validate(int scale)
    {
        if (Net < 0 || Vat < 0 || Withholding < 0 || Withholding > Vat
            || new[] { Net, Vat, Withholding }.Any(value => decimal.Round(value, scale) != value))
            throw new ArgumentException("Resmî fatura tutarları ve para ölçeği uyuşmuyor.");
        _ = Payable;
    }
}

public sealed record PurchaseInvoiceLine(Guid Id, int LineNo, Guid ProductId, Guid UnitId, decimal Quantity,
    string ItemType, string LinePurpose, decimal VatPercent, PurchaseInvoiceAmounts Amounts,
    PurchaseInvoiceAmounts? FunctionalAmounts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ServiceTypeId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ExpenseTypeId = null);

/// <summary>Owner-read receipt facts, including the price/quantity and daily control FX used in the three-way check.
/// These are persisted with the match, not accepted as authoritative client input.</summary>
public sealed record PurchaseInvoiceReceiptMatch(Guid CompanyId, Guid InvoiceLineId, Guid AcceptanceId, Guid OrderId, Guid OrderLineId,
    long OrderVersion, Guid SupplierPartyId, Guid CompanyAccountId, Guid CurrencyId, Guid UnitId,
    decimal Quantity, decimal ExpectedNet, decimal? ControlRate, DateOnly AcceptedOn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ServiceTypeId = null);

/// <summary>The actual Settings version used for comparison. Null policy means unavailable, never zero tolerance.</summary>
public sealed record PurchaseInvoiceVariancePolicy(Guid Id, long Version, decimal AmountTolerance, decimal RateTolerance);

public sealed record PurchaseInvoiceVariance(Guid InvoiceLineId, string Kind, decimal Expected, decimal Actual);
public sealed record PurchaseInvoiceVarianceDecision(string ApprovedBy, string Reason, DateTimeOffset ApprovedAtUtc);
public sealed record PurchaseInvoiceInstallment(Guid Id, Guid OpenItemId, int Sequence, DateOnly DueOn,
    decimal Amount, decimal? FunctionalAmount);

public enum PurchaseIncomingInvoiceStatus { Recorded = 1, Matched = 2, FinancePending = 3, FinanceAccepted = 4 }

/// <summary>ALS-D8/D14/D15/D41/D59/D64. Finalisation freezes one admission and one event; Finance acknowledges in a separate step.</summary>
public sealed class PurchaseIncomingInvoice
{
    // SQL decimal(24,6) storage bound, not a currency rounding policy. Actual scales come from Settings.
    public const int MaximumStoredScale = 6;
    public const decimal MaximumStoredQuantity = 999999999999999999.999999m;
    public const string MatchRequired = "purchasing_invoice_match_required";
    public const string VarianceApprovalRequired = "purchasing_invoice_variance_approval_required";
    public const string SourceMismatch = "purchasing_invoice_source_mismatch";
    public const string StateConflict = "purchasing_invoice_state_conflict";
    public const string PolicyUnavailable = "purchasing_invoice_policy_unavailable";
    public const string MakerChecker = "purchasing_invoice_maker_checker";

    private PurchaseIncomingInvoice(Guid id, Guid companyId, Guid supplierPartyId, Guid companyAccountId, Guid currencyId,
        Guid functionalCurrencyId, int moneyScale, int functionalScale, decimal? officialRate, PurchaseInvoiceSource source,
        PurchaseInvoiceAmounts amounts, PurchaseInvoiceAmounts? functionalAmounts, PurchaseInvoiceLine[] lines,
        PurchaseInvoiceInstallment[] installments, string recordedBy, DateTimeOffset recordedAtUtc)
    {
        Id = id; CompanyId = companyId; SupplierPartyId = supplierPartyId; CompanyAccountId = companyAccountId;
        CurrencyId = currencyId; FunctionalCurrencyId = functionalCurrencyId; MoneyScale = moneyScale; FunctionalScale = functionalScale;
        OfficialRate = officialRate; Source = source; Amounts = amounts; FunctionalAmounts = functionalAmounts;
        Lines = Array.AsReadOnly(lines); Installments = Array.AsReadOnly(installments);
        RecordedBy = recordedBy; RecordedAtUtc = recordedAtUtc; Version = 1;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid SupplierPartyId { get; }
    public Guid CompanyAccountId { get; }
    public Guid CurrencyId { get; }
    public Guid FunctionalCurrencyId { get; }
    public int MoneyScale { get; }
    public int FunctionalScale { get; }
    public decimal? OfficialRate { get; }
    public PurchaseInvoiceSource Source { get; }
    public PurchaseInvoiceAmounts Amounts { get; }
    public PurchaseInvoiceAmounts? FunctionalAmounts { get; }
    public IReadOnlyList<PurchaseInvoiceLine> Lines { get; }
    public IReadOnlyList<PurchaseInvoiceInstallment> Installments { get; }
    public string RecordedBy { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public long Version { get; private set; }
    public PurchaseIncomingInvoiceStatus Status { get; private set; } = PurchaseIncomingInvoiceStatus.Recorded;
    public IReadOnlyList<PurchaseInvoiceReceiptMatch> Matches { get; private set; } = Array.Empty<PurchaseInvoiceReceiptMatch>();
    public IReadOnlyList<PurchaseInvoiceVariance> Variances { get; private set; } = Array.Empty<PurchaseInvoiceVariance>();
    public PurchaseInvoiceVariancePolicy? Policy { get; private set; }
    public PurchaseInvoiceVarianceDecision? VarianceDecision { get; private set; }
    public string? MatchedBy { get; private set; }
    public Guid? AdmissionId { get; private set; }
    public long? FinalizedVersion { get; private set; }
    public string? FinalizedBy { get; private set; }
    public DateTimeOffset? FinalizedAtUtc { get; private set; }
    public DateTimeOffset? FinanceAcceptedAtUtc { get; private set; }

    public static PurchaseIncomingInvoice Record(Guid id, Guid companyId, Guid supplierPartyId, Guid companyAccountId,
        Guid currencyId, Guid functionalCurrencyId, int moneyScale, int functionalScale, decimal? officialRate,
        PurchaseInvoiceSource source, PurchaseInvoiceAmounts amounts, PurchaseInvoiceAmounts? functionalAmounts,
        IEnumerable<PurchaseInvoiceLine> lines, IEnumerable<PurchaseInvoiceInstallment> installments, string recordedBy,
        DateTimeOffset recordedAtUtc)
    {
        foreach (var value in new[] { id, companyId, supplierPartyId, companyAccountId, currencyId, functionalCurrencyId })
            Identity.Require(value, nameof(id));
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(amounts);
        ArgumentNullException.ThrowIfNull(lines); ArgumentNullException.ThrowIfNull(installments);
        if (moneyScale is < 0 or > MaximumStoredScale || functionalScale is < 0 or > MaximumStoredScale) throw new ArgumentOutOfRangeException(nameof(moneyScale));
        if (source.DocumentId == Guid.Empty || source.DocumentVersion < 1 || source.Ettn == Guid.Empty
            || source.InvoiceDate == default || string.IsNullOrWhiteSpace(source.OfficialNumber) || source.OfficialNumber.Length > 128
            || source.OfficialNumber != source.OfficialNumber.Trim() || source.Sha256 is null || source.Sha256.Length != 64
            || source.Sha256.Any(character => !char.IsAsciiHexDigit(character)))
            throw new ArgumentException("Resmî belge kimliği, tarih, sürüm ve dosya özeti zorunludur.", nameof(source));
        var foreign = currencyId != functionalCurrencyId;
        if (foreign != (officialRate is > 0) || foreign != (functionalAmounts is not null) || (!foreign && officialRate is not null))
            throw new ArgumentException("Dövizli faturada resmî kur ve TL tutarlar birlikte gerekir.");
        amounts.Validate(moneyScale); functionalAmounts?.Validate(functionalScale);
        var frozenLines = lines.ToArray(); var frozenInstallments = installments.ToArray();
        if (frozenLines.Length == 0 || frozenLines.Any(line => line is null) || frozenLines.Select(line => line.Id).Distinct().Count() != frozenLines.Length
            || frozenLines.Select(line => line.LineNo).Distinct().Count() != frozenLines.Length)
            throw new ArgumentException("Fatura satır kimlikleri ve sıraları tekil olmalıdır.");
        foreach (var line in frozenLines)
        {
            var expense = line.ItemType == "Expense" && line.LinePurpose == "expense";
            if (line.Id == Guid.Empty || line.LineNo < 1 || line.UnitId == Guid.Empty || line.Quantity <= 0
                || (expense ? line.ProductId != Guid.Empty || line.ServiceTypeId is not null || line.ExpenseTypeId == Guid.Empty
                    : line.ExpenseTypeId is not null || line.ItemType == "Expense" || line.LinePurpose == "expense"
                        || (line.ServiceTypeId is { } service ? service == Guid.Empty || line.ProductId != Guid.Empty
                            || line.ItemType != "Service" || line.LinePurpose != "service" : line.ProductId == Guid.Empty))
                || line.Quantity > MaximumStoredQuantity
                || decimal.Round(line.Quantity, MaximumStoredScale) != line.Quantity
                || line.VatPercent is < 0 or > 100 || string.IsNullOrWhiteSpace(line.ItemType) || line.ItemType.Length > 128
                || line.LinePurpose is not ("production-input" or "resale" or "service" or "expense")
                || foreign != (line.FunctionalAmounts is not null)) throw new ArgumentException("Fatura satır kaynağı geçersiz.");
            ArgumentNullException.ThrowIfNull(line.Amounts);
            line.Amounts.Validate(moneyScale); line.FunctionalAmounts?.Validate(functionalScale);
        }
        if (Sum(frozenLines.Select(line => line.Amounts)) != amounts
            || (foreign && Sum(frozenLines.Select(line => line.FunctionalAmounts!)) != functionalAmounts))
            throw new ArgumentException("Satır toplamları resmî fatura toplamlarına eşit olmalıdır.");
        if (frozenInstallments.Length == 0 || frozenInstallments.Any(item => item is null)
            || frozenInstallments.Select(item => item.Id).Distinct().Count() != frozenInstallments.Length
            || frozenInstallments.Select(item => item.OpenItemId).Distinct().Count() != frozenInstallments.Length
            || frozenInstallments.OrderBy(item => item.Sequence).Where((item, index) => item.Sequence != index + 1).Any())
            throw new ArgumentException("Ödeme planı kimlik ve sıra bilgisi geçersiz.");
        foreach (var item in frozenInstallments)
            if (item.Id == Guid.Empty || item.OpenItemId == Guid.Empty || item.DueOn == default || item.Amount <= 0
                || decimal.Round(item.Amount, moneyScale) != item.Amount || foreign != (item.FunctionalAmount is not null)
                || (item.FunctionalAmount is { } tl && (tl <= 0 || decimal.Round(tl, functionalScale) != tl)))
                throw new ArgumentException("Ödeme planı tutarı geçersiz.");
        if (frozenInstallments.Sum(item => item.Amount) != amounts.Payable
            || (foreign && frozenInstallments.Sum(item => item.FunctionalAmount!.Value) != functionalAmounts!.Payable))
            throw new ArgumentException("Ödeme planı resmî ödenecek toplamı korumalıdır.");
        return new(id, companyId, supplierPartyId, companyAccountId, currencyId, functionalCurrencyId, moneyScale, functionalScale,
            officialRate, source with { Sha256 = source.Sha256.ToLowerInvariant() }, amounts, functionalAmounts,
            frozenLines.OrderBy(line => line.LineNo).ToArray(), frozenInstallments.OrderBy(item => item.Sequence).ToArray(),
            Actor(recordedBy), recordedAtUtc.ToUniversalTime());
    }

    public void Match(long expectedVersion, IEnumerable<PurchaseInvoiceReceiptMatch> matches,
        PurchaseInvoiceVariancePolicy? policy, string matchedBy)
    {
        RequireVersion(expectedVersion); RequireStatus(PurchaseIncomingInvoiceStatus.Recorded);
        if (policy is null || policy.Id == Guid.Empty || policy.Version < 1 || policy.AmountTolerance < 0 || policy.RateTolerance < 0)
            throw new PurchasingRuleException(PolicyUnavailable, "Fatura fark toleransı Ayarlar'dan doğrulanamadı.");
        ArgumentNullException.ThrowIfNull(matches);
        var frozen = matches.ToArray(); var actor = Actor(matchedBy);
        if (frozen.Length == 0 || frozen.Any(item => item is null) || frozen.Select(item => item.AcceptanceId).Distinct().Count() != frozen.Length)
            throw new ArgumentException("Kabul payı yalnız bir kez eşleştirilir.");
        var differences = new List<PurchaseInvoiceVariance>();
        foreach (var item in frozen)
        {
            if (item.Quantity > MaximumStoredQuantity || decimal.Round(item.Quantity, MaximumStoredScale) != item.Quantity)
                throw new ArgumentException("Kabul payı miktarı kayıt ölçeğine tam olarak sığmalıdır; yuvarlanmaz.");
            if (item.CompanyId != CompanyId || item.AcceptanceId == Guid.Empty || item.OrderId == Guid.Empty || item.OrderLineId == Guid.Empty || item.OrderVersion < 1
                || item.SupplierPartyId != SupplierPartyId || item.CompanyAccountId != CompanyAccountId || item.CurrencyId != CurrencyId
                || item.Quantity <= 0 || item.ExpectedNet < 0 || item.AcceptedOn == default
                || (OfficialRate.HasValue ? item.ControlRate is not > 0 : item.ControlRate is not null))
                throw new PurchasingRuleException(SourceMismatch, "Kabul kaynağı faturanın carisi, para birimi veya kuruyla uyuşmuyor.");
            var line = Lines.SingleOrDefault(line => line.Id == item.InvoiceLineId);
            if (line is null || line.UnitId != item.UnitId || line.ServiceTypeId != item.ServiceTypeId)
                throw new PurchasingRuleException(SourceMismatch, "Fatura ve kabul birimleri veya kalem kaynakları uyuşmuyor.");
            if (OfficialRate is { } rate && Math.Abs(rate - item.ControlRate!.Value) > policy.RateTolerance)
                differences.Add(new(line.Id, "ExchangeRate", item.ControlRate.Value, rate));
        }
        foreach (var line in Lines)
        {
            var shares = frozen.Where(item => item.InvoiceLineId == line.Id).ToArray();
            if (shares.Length == 0) throw new PurchasingRuleException(MatchRequired, "Her fatura satırı kabul kaynağına bağlanmalıdır.");
            var quantity = CommercialLinePriceCalculator.ExactSum(shares.Select(item => item.Quantity));
            // ALS-D8: compare the whole invoice line, not independently rounded receipt shares.
            var expectedNet = Money.Round(CommercialLinePriceCalculator.ExactSum(shares.Select(item => item.ExpectedNet)), CurrencyId, MoneyScale).Amount;
            if (quantity != line.Quantity) differences.Add(new(line.Id, "Quantity", quantity, line.Quantity));
            if (Math.Abs(expectedNet - line.Amounts.Net) > policy.AmountTolerance)
                differences.Add(new(line.Id, "Price", expectedNet, line.Amounts.Net));
        }
        Matches = Array.AsReadOnly(frozen); Variances = Array.AsReadOnly(differences.ToArray()); Policy = policy;
        MatchedBy = actor; Status = PurchaseIncomingInvoiceStatus.Matched; Version = checked(Version + 1);
    }

    public void ApproveVariance(long expectedVersion, string approvedBy, string reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion); RequireStatus(PurchaseIncomingInvoiceStatus.Matched);
        var actor = Actor(approvedBy);
        if (actor == RecordedBy || actor == MatchedBy) throw new PurchasingRuleException(MakerChecker, "Hazırlayan kendi fatura farkını onaylayamaz.");
        if (Variances.Count == 0 || VarianceDecision is not null) throw new PurchasingRuleException(StateConflict, "Bekleyen fark onayı yok.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000) throw new ArgumentException("Fark onayı gerekçesi zorunludur.");
        VarianceDecision = new(actor, reason.Trim(), now.ToUniversalTime()); Version = checked(Version + 1);
    }

    public void Finalize(long expectedVersion, Guid admissionId, string finalizedBy, DateTimeOffset now)
    {
        RequireVersion(expectedVersion); RequireStatus(PurchaseIncomingInvoiceStatus.Matched);
        if (Policy is null || Matches.Count == 0) throw new PurchasingRuleException(MatchRequired, "Üçlü kontrol eksik.");
        if (Variances.Count != 0 && VarianceDecision is null)
            throw new PurchasingRuleException(VarianceApprovalRequired, "Fiyat, miktar veya kur farkı onaylanmalıdır.");
        Identity.Require(admissionId, nameof(admissionId)); var actor = Actor(finalizedBy);
        AdmissionId = admissionId; FinalizedBy = actor; FinalizedAtUtc = now.ToUniversalTime();
        Status = PurchaseIncomingInvoiceStatus.FinancePending; Version = checked(Version + 1); FinalizedVersion = Version;
    }

    public void AcknowledgeFinance(long expectedVersion, Guid admissionId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion); RequireStatus(PurchaseIncomingInvoiceStatus.FinancePending);
        if (admissionId != AdmissionId) throw new PurchasingRuleException(SourceMismatch, "Finans kabul kimliği uyuşmuyor.");
        FinanceAcceptedAtUtc = now.ToUniversalTime(); Status = PurchaseIncomingInvoiceStatus.FinanceAccepted; Version = checked(Version + 1);
    }

    private static PurchaseInvoiceAmounts Sum(IEnumerable<PurchaseInvoiceAmounts> values)
    {
        var frozen = values.ToArray(); return new(frozen.Sum(item => item.Net), frozen.Sum(item => item.Vat), frozen.Sum(item => item.Withholding));
    }
    private void RequireVersion(long value)
    {
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
        if (value != Version) throw new PurchasingVersionConflictException(value, Version);
    }
    private void RequireStatus(PurchaseIncomingInvoiceStatus value)
    {
        if (Status != value) throw new PurchasingRuleException(StateConflict, "Fatura bu işleme uygun durumda değil.");
    }
    private static string Actor(string value) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > 128
        ? throw new ArgumentException("İşlemi yapan kişi zorunludur.") : value.Trim();
}
