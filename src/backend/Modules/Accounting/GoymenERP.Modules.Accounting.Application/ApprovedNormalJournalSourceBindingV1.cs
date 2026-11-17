namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// Accounting-owned identifiers for supported source admissions. These are routing keys,
/// not a permission, account mapping, posting purpose or grant.
/// </summary>
public static class NormalJournalSourceKindsV1
{
    public const string RecordedManualReceipt = "finance.manual-receipt.recorded.v1";
    public const string RecordedExpensePayment = "finance.expense-payment.recorded.v1";
    public const string RecordedCashTransfer = "finance.cash-transfer.recorded.v1";

    public static bool IsSupported(string? value) => value is RecordedManualReceipt or RecordedExpensePayment or RecordedCashTransfer;
}

/// <summary>Routes a source identity only from its actual approved Accounting package; ambiguity is unavailable.</summary>
public interface IApprovedNormalJournalSourceKindCatalogV1
{
    Task<string?> ResolveKindAsync(Guid companyId, Guid sourceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable, server-owned binding from one exact Finance source/profile version and currency to an
/// already approved account-plan mirror and posting rule. Real rows are supplied by the approved
/// data loader; public requests can neither create nor replace them.
/// </summary>
public sealed record ApprovedNormalJournalSourceBindingV1(
    Guid CompanyId,
    Guid SourceId,
    string SourceKind,
    string SourceVersion,
    string OwnerProfileId,
    string OwnerProfileVersion,
    Guid CurrencyId,
    NormalJournalPostingRuleV1 Rule);

public interface IApprovedNormalJournalSourceBindingCatalogV1
{
    Task<NormalJournalPostingRuleV1?> ResolveAsync(
        Guid companyId,
        Guid sourceId,
        string sourceKind,
        string sourceVersion,
        string ownerProfileId,
        string ownerProfileVersion,
        Guid currencyId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only exact-match catalog. The parameterless instance is intentionally empty, so deployment
/// without the real approved Mikro mirror and posting mappings remains unavailable rather than
/// manufacturing accounts or debit/credit semantics.
/// </summary>
public sealed class ApprovedNormalJournalSourceBindingCatalogV1
    : IApprovedNormalJournalSourceBindingCatalogV1
{
    private readonly IReadOnlyDictionary<BindingKey, NormalJournalPostingRuleV1> _rules;

    public ApprovedNormalJournalSourceBindingCatalogV1()
        : this([])
    {
    }

    public ApprovedNormalJournalSourceBindingCatalogV1(
        IEnumerable<ApprovedNormalJournalSourceBindingV1> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var rules = new Dictionary<BindingKey, NormalJournalPostingRuleV1>();
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(binding.Rule);
            RequireIdentity(binding.CompanyId, binding.SourceId, binding.CurrencyId);
            RequireText(binding.SourceVersion, nameof(binding.SourceVersion));
            RequireText(binding.OwnerProfileId, nameof(binding.OwnerProfileId));
            RequireText(binding.OwnerProfileVersion, nameof(binding.OwnerProfileVersion));
            if (!NormalJournalSourceKindsV1.IsSupported(binding.SourceKind)
                || binding.Rule.Mirror.CompanyId != binding.CompanyId)
            {
                throw new ArgumentException("Binding must target the exact approved source kind and company.",
                    nameof(bindings));
            }

            var key = new BindingKey(binding.CompanyId, binding.SourceId, binding.SourceKind, binding.SourceVersion,
                binding.OwnerProfileId, binding.OwnerProfileVersion, binding.CurrencyId);
            if (!rules.TryAdd(key, binding.Rule))
            {
                throw new ArgumentException("An approved source binding key cannot be ambiguous.", nameof(bindings));
            }
        }

        _rules = rules;
    }

    public Task<NormalJournalPostingRuleV1?> ResolveAsync(
        Guid companyId,
        Guid sourceId,
        string sourceKind,
        string sourceVersion,
        string ownerProfileId,
        string ownerProfileVersion,
        Guid currencyId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || sourceId == Guid.Empty || currencyId == Guid.Empty
            || !NormalJournalSourceKindsV1.IsSupported(sourceKind)
            || !IsCanonical(sourceVersion) || !IsCanonical(ownerProfileId)
            || !IsCanonical(ownerProfileVersion))
        {
            return Task.FromResult<NormalJournalPostingRuleV1?>(null);
        }

        _rules.TryGetValue(new BindingKey(companyId, sourceId, sourceKind, sourceVersion,
            ownerProfileId, ownerProfileVersion, currencyId), out var rule);
        return Task.FromResult(rule);
    }

    private static void RequireIdentity(Guid companyId, Guid sourceId, Guid currencyId)
    {
        if (companyId == Guid.Empty || sourceId == Guid.Empty || currencyId == Guid.Empty)
        {
            throw new ArgumentException("Binding company, source and currency are required.");
        }
    }

    private static void RequireText(string? value, string parameterName)
    {
        if (!IsCanonical(value))
        {
            throw new ArgumentException("Binding text must be canonical and non-empty.", parameterName);
        }
    }

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private sealed record BindingKey(
        Guid CompanyId,
        Guid SourceId,
        string SourceKind,
        string SourceVersion,
        string OwnerProfileId,
        string OwnerProfileVersion,
        Guid CurrencyId);
}
