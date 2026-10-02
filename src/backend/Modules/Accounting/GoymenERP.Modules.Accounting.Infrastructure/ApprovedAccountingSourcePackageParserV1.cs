using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Modules.Accounting.Infrastructure;

/// <summary>
/// Strict preview parser for the application-owned CSV interchange. Checksums are computed from the
/// exact uploaded bytes. Approval provenance comes only from the separate trusted caller context.
/// </summary>
public sealed class ApprovedAccountingSourcePackageParserV1 : IApprovedAccountingSourcePackageParserV1
{
    private const string AccountsFile = "accounts.csv";
    private const string MappingsFile = "posting-mappings.csv";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] AccountsHeader =
        ApprovedAccountingSourcePackageTemplateV1.AccountsHeader.Split(',');
    private static readonly string[] MappingsHeader =
        ApprovedAccountingSourcePackageTemplateV1.MappingsHeader.Split(',');

    public ApprovedAccountingSourcePackagePreviewV1 Preview(
        TrustedAccountingSourceApprovalContextV1 trustedContext,
        ApprovedAccountingSourcePackagePreviewRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(trustedContext);
        ArgumentNullException.ThrowIfNull(request);

        var accountsSha = Sha256(request.AccountsUtf8Csv);
        var mappingsSha = Sha256(request.PostingMappingsUtf8Csv);
        var issues = new List<ApprovedAccountingSourcePackageIssueV1>();
        if (trustedContext.CompanyId == Guid.Empty || trustedContext.ApprovedByUserId == Guid.Empty
            || trustedContext.ApprovedAtUtc == default
            || trustedContext.ApprovedAtUtc.Offset != TimeSpan.Zero)
        {
            Reject(issues, "context", 0, "trustedContext",
                ApprovedAccountingSourcePackageCodesV1.TrustedContextInvalid);
        }
        if (!Canonical(request.AccountPlanSourceVersion))
        {
            Reject(issues, "request", 0, "accountPlanSourceVersion",
                ApprovedAccountingSourcePackageCodesV1.SourceVersionInvalid);
        }

        var accountRows = ParseAccounts(request.AccountsUtf8Csv, trustedContext.CompanyId, issues);
        var mappingRows = ParseMappings(request.PostingMappingsUtf8Csv, issues);
        if (issues.Count > 0 || accountRows is null || mappingRows is null)
        {
            return Result(accountsSha, mappingsSha, null, issues);
        }

        AccountPlanMirrorV1 mirror;
        try
        {
            mirror = new AccountPlanMirrorV1(
                trustedContext.CompanyId,
                request.AccountPlanSourceVersion,
                accountsSha,
                trustedContext.ApprovedByUserId,
                trustedContext.ApprovedAtUtc,
                accountRows.Select(row => new AccountPlanMirrorRowV1(
                    new AccountRef(trustedContext.CompanyId, row.AccountId), row.Code, row.Name)));
        }
        catch (AccountingDomainException)
        {
            Reject(issues, AccountsFile, 0, "accounts",
                ApprovedAccountingSourcePackageCodesV1.DomainInvalid);
            return Result(accountsSha, mappingsSha, null, issues);
        }

        var accounts = accountRows.ToDictionary(row => row.AccountId);
        var bindings = BuildBindings(trustedContext, mirror, accounts, mappingRows, issues);
        return issues.Count == 0 && bindings is not null
            ? Result(accountsSha, mappingsSha,
                new ApprovedAccountingSourcePackageV1(mirror, Array.AsReadOnly(bindings.ToArray())), issues)
            : Result(accountsSha, mappingsSha, null, issues);
    }

    private static List<AccountCsvRow>? ParseAccounts(
        ReadOnlyMemory<byte> bytes,
        Guid companyId,
        ICollection<ApprovedAccountingSourcePackageIssueV1> issues)
    {
        if (!TryReadLines(AccountsFile, bytes, AccountsHeader, issues, out var lines)) return null;
        var rows = new List<AccountCsvRow>(lines.Length - 1);
        var ids = new HashSet<Guid>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < lines.Length; index++)
        {
            var rowNumber = index + 1;
            if (!TryParseLine(lines[index], out var fields) || fields.Length != AccountsHeader.Length)
            {
                Reject(issues, AccountsFile, rowNumber, "row",
                    ApprovedAccountingSourcePackageCodesV1.RowShapeInvalid);
                continue;
            }

            var accountId = GuidField(AccountsFile, rowNumber, "accountId", fields[0], issues);
            var codeOk = TextField(AccountsFile, rowNumber, "accountCode", fields[1], issues);
            var nameOk = TextField(AccountsFile, rowNumber, "accountName", fields[2], issues);
            if (accountId == Guid.Empty || !codeOk || !nameOk) continue;
            if (!ids.Add(accountId) || !codes.Add(fields[1]))
            {
                Reject(issues, AccountsFile, rowNumber, "accountId",
                    ApprovedAccountingSourcePackageCodesV1.DuplicateAccount);
                continue;
            }
            rows.Add(new AccountCsvRow(accountId, fields[1], fields[2]));
        }
        if (rows.Count == 0 && !issues.Any(issue => issue.File == AccountsFile))
        {
            Reject(issues, AccountsFile, 0, "rows", ApprovedAccountingSourcePackageCodesV1.FileEmpty);
        }
        _ = companyId; // Company is trusted package metadata, never a CSV cell.
        return rows;
    }

    private static List<MappingCsvRow>? ParseMappings(
        ReadOnlyMemory<byte> bytes,
        ICollection<ApprovedAccountingSourcePackageIssueV1> issues)
    {
        if (!TryReadLines(MappingsFile, bytes, MappingsHeader, issues, out var lines)) return null;
        var rows = new List<MappingCsvRow>(lines.Length - 1);
        for (var index = 1; index < lines.Length; index++)
        {
            var rowNumber = index + 1;
            if (!TryParseLine(lines[index], out var fields) || fields.Length != MappingsHeader.Length)
            {
                Reject(issues, MappingsFile, rowNumber, "row",
                    ApprovedAccountingSourcePackageCodesV1.RowShapeInvalid);
                continue;
            }

            var sourceId = GuidField(MappingsFile, rowNumber, "sourceId", fields[0], issues);
            var currencyId = GuidField(MappingsFile, rowNumber, "currencyId", fields[5], issues);
            var accountId = GuidField(MappingsFile, rowNumber, "accountId", fields[10], issues);
            var textFields = new[] { 2, 3, 4, 6, 7, 8 };
            var valid = textFields.All(field =>
                TextField(MappingsFile, rowNumber, MappingsHeader[field], fields[field], issues));
            if (!string.Equals(fields[1], NormalJournalSourceKindsV1.RecordedManualReceipt,
                    StringComparison.Ordinal))
            {
                Reject(issues, MappingsFile, rowNumber, "sourceKind",
                    ApprovedAccountingSourcePackageCodesV1.SourceKindUnknown);
                valid = false;
            }
            var side = fields[9] switch
            {
                "Debit" => PostingSide.Debit,
                "Credit" => PostingSide.Credit,
                _ => (PostingSide?)null,
            };
            if (side is null)
            {
                Reject(issues, MappingsFile, rowNumber, "side",
                    ApprovedAccountingSourcePackageCodesV1.SideUnknown);
                valid = false;
            }
            if (sourceId == Guid.Empty || currencyId == Guid.Empty || accountId == Guid.Empty || side is null || !valid) continue;
            rows.Add(new MappingCsvRow(rowNumber, sourceId, fields[1], fields[2], fields[3], fields[4],
                currencyId, fields[6], fields[7], fields[8], side.Value, accountId));
        }
        if (rows.Count == 0 && !issues.Any(issue => issue.File == MappingsFile))
        {
            Reject(issues, MappingsFile, 0, "rows", ApprovedAccountingSourcePackageCodesV1.FileEmpty);
        }
        return rows;
    }

    private static List<ApprovedNormalJournalSourceBindingV1>? BuildBindings(
        TrustedAccountingSourceApprovalContextV1 trustedContext,
        AccountPlanMirrorV1 mirror,
        IReadOnlyDictionary<Guid, AccountCsvRow> accounts,
        IReadOnlyList<MappingCsvRow> rows,
        ICollection<ApprovedAccountingSourcePackageIssueV1> issues)
    {
        var bindings = new List<ApprovedNormalJournalSourceBindingV1>();
        var groups = rows.GroupBy(row => new BindingKey(row.SourceId, row.SourceKind, row.SourceVersion,
            row.OwnerProfileId, row.OwnerProfileVersion, row.CurrencyId));
        foreach (var group in groups)
        {
            var purposes = group.Select(row => (row.PostingPurpose, row.PostingRuleVersion))
                .Distinct().ToArray();
            if (purposes.Length != 1)
            {
                foreach (var row in group)
                    Reject(issues, MappingsFile, row.RowNumber, "postingRuleVersion",
                        ApprovedAccountingSourcePackageCodesV1.BindingAmbiguous);
                continue;
            }

            var seenMappings = new HashSet<(string Key, PostingSide Side)>();
            var mappings = new List<NormalJournalMappingV1>();
            foreach (var row in group)
            {
                if (!accounts.ContainsKey(row.AccountId))
                {
                    Reject(issues, MappingsFile, row.RowNumber, "accountId",
                        ApprovedAccountingSourcePackageCodesV1.MappedAccountUnknown);
                    continue;
                }
                if (!seenMappings.Add((row.MappingKey, row.Side)))
                {
                    Reject(issues, MappingsFile, row.RowNumber, "mappingKey",
                        ApprovedAccountingSourcePackageCodesV1.MappingDuplicate);
                    continue;
                }
                mappings.Add(new NormalJournalMappingV1(row.MappingKey, row.Side,
                    new AccountRef(trustedContext.CompanyId, row.AccountId)));
            }

            if (mappings.Count < 2 || !mappings.Any(mapping => mapping.Side == PostingSide.Debit)
                || !mappings.Any(mapping => mapping.Side == PostingSide.Credit))
            {
                Reject(issues, MappingsFile, group.Min(row => row.RowNumber), "mappings",
                    ApprovedAccountingSourcePackageCodesV1.MappingIncomplete);
                continue;
            }
            if (issues.Any(issue => issue.File == MappingsFile
                    && group.Any(row => row.RowNumber == issue.RowNumber))) continue;

            try
            {
                var rule = new NormalJournalPostingRuleV1(mirror, purposes[0].PostingPurpose,
                    purposes[0].PostingRuleVersion, trustedContext.ApprovedByUserId,
                    trustedContext.ApprovedAtUtc, mappings);
                bindings.Add(new ApprovedNormalJournalSourceBindingV1(
                    trustedContext.CompanyId, group.Key.SourceId, group.Key.SourceKind,
                    group.Key.SourceVersion, group.Key.OwnerProfileId, group.Key.OwnerProfileVersion,
                    group.Key.CurrencyId, rule));
            }
            catch (AccountingDomainException)
            {
                Reject(issues, MappingsFile, group.Min(row => row.RowNumber), "mappings",
                    ApprovedAccountingSourcePackageCodesV1.DomainInvalid);
            }
        }
        return bindings.Count == 0 ? null : bindings;
    }

    private static bool TryReadLines(
        string file,
        ReadOnlyMemory<byte> bytes,
        IReadOnlyList<string> expectedHeader,
        ICollection<ApprovedAccountingSourcePackageIssueV1> issues,
        out string[] lines)
    {
        lines = [];
        if (bytes.Length == 0)
        {
            Reject(issues, file, 0, "file", ApprovedAccountingSourcePackageCodesV1.FileEmpty);
            return false;
        }
        if (bytes.Length > ApprovedAccountingSourcePackageTemplateV1.MaximumBytesPerFile)
        {
            Reject(issues, file, 0, "file", ApprovedAccountingSourcePackageCodesV1.FileTooLarge);
            return false;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes.Span);
        }
        catch (DecoderFallbackException)
        {
            Reject(issues, file, 0, "file", ApprovedAccountingSourcePackageCodesV1.Utf8Invalid);
            return false;
        }
        if (text.StartsWith('\uFEFF')) text = text[1..];
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Contains('\r'))
        {
            Reject(issues, file, 0, "file", ApprovedAccountingSourcePackageCodesV1.RowShapeInvalid);
            return false;
        }
        lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
        if (lines.Length == 0 || !TryParseLine(lines[0], out var header)
            || !header.SequenceEqual(expectedHeader, StringComparer.Ordinal))
        {
            Reject(issues, file, 1, "header", ApprovedAccountingSourcePackageCodesV1.HeaderInvalid);
            return false;
        }
        if (lines.Length - 1 > ApprovedAccountingSourcePackageTemplateV1.MaximumRowsPerFile)
        {
            Reject(issues, file, 0, "rows", ApprovedAccountingSourcePackageCodesV1.RowLimitExceeded);
            return false;
        }
        return true;
    }

    private static Guid GuidField(string file, int row, string field, string value,
        ICollection<ApprovedAccountingSourcePackageIssueV1> issues)
    {
        if (Guid.TryParseExact(value, "D", out var parsed) && parsed != Guid.Empty) return parsed;
        Reject(issues, file, row, field, ApprovedAccountingSourcePackageCodesV1.IdentityRequired);
        return Guid.Empty;
    }

    private static bool TextField(string file, int row, string field, string value,
        ICollection<ApprovedAccountingSourcePackageIssueV1> issues)
    {
        if (Canonical(value)) return true;
        Reject(issues, file, row, field, ApprovedAccountingSourcePackageCodesV1.TextInvalid);
        return false;
    }

    private static bool Canonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static string Sha256(ReadOnlyMemory<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();

    private static ApprovedAccountingSourcePackagePreviewV1 Result(
        string accountsSha,
        string mappingsSha,
        ApprovedAccountingSourcePackageV1? package,
        IEnumerable<ApprovedAccountingSourcePackageIssueV1> issues) =>
        new(ApprovedAccountingSourcePackageTemplateV1.Version, accountsSha, mappingsSha, package,
            Array.AsReadOnly(issues.ToArray()));

    private static void Reject(ICollection<ApprovedAccountingSourcePackageIssueV1> issues,
        string file, int row, string field, string code) => issues.Add(new(file, row, field, code));

    private static bool TryParseLine(string line, out string[] fields)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        var closedQuote = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        value.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        closedQuote = true;
                    }
                }
                else value.Append(character);
                continue;
            }
            if (closedQuote && character != ',')
            {
                fields = [];
                return false;
            }
            if (character == ',')
            {
                values.Add(value.ToString());
                value.Clear();
                closedQuote = false;
                continue;
            }
            if (character == '"' && value.Length == 0)
            {
                quoted = true;
                continue;
            }
            if (character == '"')
            {
                fields = [];
                return false;
            }
            value.Append(character);
        }
        if (quoted)
        {
            fields = [];
            return false;
        }
        values.Add(value.ToString());
        fields = [.. values];
        return true;
    }

    private sealed record AccountCsvRow(Guid AccountId, string Code, string Name);

    private sealed record MappingCsvRow(
        int RowNumber,
        Guid SourceId,
        string SourceKind,
        string SourceVersion,
        string OwnerProfileId,
        string OwnerProfileVersion,
        Guid CurrencyId,
        string PostingPurpose,
        string PostingRuleVersion,
        string MappingKey,
        PostingSide Side,
        Guid AccountId);

    private sealed record BindingKey(
        Guid SourceId,
        string SourceKind,
        string SourceVersion,
        string OwnerProfileId,
        string OwnerProfileVersion,
        Guid CurrencyId);
}
