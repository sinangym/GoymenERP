using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal static class FinanceOpenItemSourcePolicy
{
    private const int TokenMaximumLength = 128;

    public static IReadOnlyList<ValidationIssue> ValidateReference(FinanceOpenItemSourceReference source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var issues = new List<ValidationIssue>();
        if (source.BatchId == Guid.Empty)
        {
            issues.Add(new ValidationIssue("batchId", "identity_required"));
        }
        if (!IsToken(source.BatchVersion))
        {
            issues.Add(new ValidationIssue("batchVersion", "finance_open_item_batch_version_invalid"));
        }
        if (!IsSha256(source.BatchChecksum))
        {
            issues.Add(new ValidationIssue("batchChecksum", "finance_open_item_batch_checksum_invalid"));
        }
        return issues;
    }

    public static IReadOnlyList<ValidationIssue> ValidateBatch(
        FinanceContext context,
        FinanceOpenItemSourceReference source,
        ApprovedFinanceOpenItemSourceBatch batch)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(batch);
        var issues = new List<ValidationIssue>();
        if (batch.BatchId != source.BatchId || batch.CompanyId != context.CompanyId
            || !string.Equals(batch.BatchVersion, source.BatchVersion, StringComparison.Ordinal)
            || !string.Equals(batch.BatchChecksum, source.BatchChecksum, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue("source", "finance_open_item_source_metadata_mismatch"));
            return issues;
        }
        if (batch.Rows.Count == 0)
        {
            issues.Add(new ValidationIssue("rows", "finance_open_item_source_empty"));
            return issues;
        }

        var itemIds = new HashSet<Guid>();
        var sourceIdentities = new HashSet<(Guid DocumentId, Guid InstallmentId)>();
        for (var index = 0; index < batch.Rows.Count; index++)
        {
            var row = batch.Rows[index];
            var field = $"rows[{index}]";
            if (row.OpenItemId == Guid.Empty || row.SourceDocumentId == Guid.Empty
                || row.SourceInstallmentId == Guid.Empty || row.PartyId == Guid.Empty
                || row.CompanyAccountId == Guid.Empty || row.CurrencyId == Guid.Empty)
            {
                issues.Add(new ValidationIssue(field, "identity_required"));
            }
            if (!itemIds.Add(row.OpenItemId)
                || !sourceIdentities.Add((row.SourceDocumentId, row.SourceInstallmentId)))
            {
                issues.Add(new ValidationIssue(field, "finance_open_item_source_duplicate"));
            }
            if (!IsToken(row.SourceVersion))
            {
                issues.Add(new ValidationIssue(field + ".sourceVersion", "finance_open_item_source_version_invalid"));
            }
            if (!string.Equals(row.DocumentKind,
                    FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                    StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    field + ".documentKind", "finance_open_item_source_document_kind_invalid"));
            }
            if (!FinanceExactDecimalText.TryParsePositive(row.Amount, out _))
            {
                issues.Add(new ValidationIssue(field + ".amount", "finance_open_item_amount_invalid"));
            }
        }
        return issues;
    }

    public static bool MatchesExisting(
        ApprovedFinanceOpenItemSourceRow source,
        FinanceOpenItemRow existing) =>
        existing.Id == source.OpenItemId
        && existing.SourceKind == FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment
        && existing.SourceId == source.SourceDocumentId
        && existing.SourceLineId == source.SourceInstallmentId
        && existing.SourceVersion == source.SourceVersion
        && existing.PartyId == source.PartyId
        && existing.CompanyAccountId == source.CompanyAccountId
        && existing.CurrencyId == source.CurrencyId
        && FinanceExactDecimalText.Equal(existing.OriginalAmountText, source.Amount)
        && existing.DueOn == source.DueOn;

    private static bool IsToken(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= TokenMaximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '.' or '-' or '_' or ':' or '/');

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
}
