namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// Accounting-owned identifier for the only approved first source admission. This is a routing key,
/// not a permission, account mapping, posting purpose or grant.
/// </summary>
public static class NormalJournalSourceKindsV1
{
    public const string RecordedManualReceipt = "finance.manual-receipt.recorded.v1";
}

/// <summary>
/// Immutable, server-owned binding from one exact Finance source/profile version and currency to an
/// already approved account-plan mirror and posting rule. Real rows are supplied by the approved
/// data loader; public requests can neither create nor replace them.
/// </summary>
public sealed record ApprovedNormalJournalSourceBindingV1(
    Guid CompanyId,
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
            RequireIdentity(binding.CompanyId, binding.CurrencyId);
            RequireText(binding.SourceVersion, nameof(binding.SourceVersion));
            RequireText(binding.OwnerProfileId, nameof(binding.OwnerProfileId));
            RequireText(binding.OwnerProfileVersion, nameof(binding.OwnerProfileVersion));
            if (!string.Equals(binding.SourceKind, NormalJournalSourceKindsV1.RecordedManualReceipt,
                    StringComparison.Ordinal)
                || binding.Rule.Mirror.CompanyId != binding.CompanyId)
            {
                throw new ArgumentException("Binding must target the exact approved source kind and company.",
                    nameof(bindings));
            }

            var key = new BindingKey(binding.CompanyId, binding.SourceKind, binding.SourceVersion,
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
        string sourceKind,
        string sourceVersion,
        string ownerProfileId,
        string ownerProfileVersion,
        Guid currencyId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || currencyId == Guid.Empty
            || !string.Equals(sourceKind, NormalJournalSourceKindsV1.RecordedManualReceipt,
                StringComparison.Ordinal)
            || !IsCanonical(sourceVersion) || !IsCanonical(ownerProfileId)
            || !IsCanonical(ownerProfileVersion))
        {
            return Task.FromResult<NormalJournalPostingRuleV1?>(null);
        }

        _rules.TryGetValue(new BindingKey(companyId, sourceKind, sourceVersion,
            ownerProfileId, ownerProfileVersion, currencyId), out var rule);
        return Task.FromResult(rule);
    }

    private static void RequireIdentity(Guid companyId, Guid currencyId)
    {
        if (companyId == Guid.Empty || currencyId == Guid.Empty)
        {
            throw new ArgumentException("Binding company and currency are required.");
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
        string SourceKind,
        string SourceVersion,
        string OwnerProfileId,
        string OwnerProfileVersion,
        Guid CurrencyId);
}
