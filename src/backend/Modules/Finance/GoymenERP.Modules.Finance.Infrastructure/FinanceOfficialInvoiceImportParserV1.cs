using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Strict parser for the application-owned v1 CSV template. It computes provenance from the exact
/// uploaded bytes and emits only the already-published canonical Finance source contract. It does
/// not read a native Mikro layout, approve a file, resolve a party mapping or persist anything.
/// </summary>
public sealed class FinanceOfficialInvoiceImportParserV1 : IFinanceOfficialInvoiceImportParserV1
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] Header = FinanceOfficialInvoiceImportTemplateV1.Header.Split(',');

    public FinanceOfficialInvoiceImportPreviewV1 Preview(
        FinanceContext context,
        FinanceOfficialInvoiceImportPreviewRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var bytes = request.Utf8Csv;
        var checksum = Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();
        var issues = new List<FinanceOfficialInvoiceImportIssueV1>();
        if (context.CompanyId == Guid.Empty || request.BatchId == Guid.Empty)
        {
            issues.Add(new(0, request.BatchId == Guid.Empty ? "batchId" : "companyId", "identity_required"));
        }
        if (bytes.Length == 0)
        {
            issues.Add(new(0, "file", FinanceOfficialInvoiceImportCodesV1.FileEmpty));
            return Result(checksum, issues);
        }
        if (bytes.Length > FinanceOfficialInvoiceImportTemplateV1.MaximumBytes)
        {
            issues.Add(new(0, "file", FinanceOfficialInvoiceImportCodesV1.FileTooLarge));
            return Result(checksum, issues);
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes.Span);
        }
        catch (DecoderFallbackException)
        {
            issues.Add(new(0, "file", FinanceOfficialInvoiceImportCodesV1.Utf8Invalid));
            return Result(checksum, issues);
        }
        if (text.StartsWith('\uFEFF'))
        {
            text = text[1..];
        }
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Contains('\r'))
        {
            issues.Add(new(0, "file", FinanceOfficialInvoiceImportCodesV1.RowShapeInvalid));
            return Result(checksum, issues);
        }

        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }
        if (lines.Length == 0 || !TryParseLine(lines[0], out var actualHeader)
            || !actualHeader.SequenceEqual(Header, StringComparer.Ordinal))
        {
            issues.Add(new(1, "header", FinanceOfficialInvoiceImportCodesV1.HeaderInvalid));
            return Result(checksum, issues);
        }
        if (lines.Length - 1 > FinanceOfficialInvoiceImportTemplateV1.MaximumRows)
        {
            issues.Add(new(0, "rows", FinanceOfficialInvoiceImportCodesV1.RowLimitExceeded));
            return Result(checksum, issues);
        }

        var rows = new List<ApprovedFinanceOpenItemSourceRow>(Math.Max(0, lines.Length - 1));
        for (var index = 1; index < lines.Length; index++)
        {
            var rowNumber = index + 1;
            if (!TryParseLine(lines[index], out var fields) || fields.Length != Header.Length)
            {
                issues.Add(new(rowNumber, "row", FinanceOfficialInvoiceImportCodesV1.RowShapeInvalid));
                continue;
            }

            var rowIssues = new List<FinanceOfficialInvoiceImportIssueV1>();
            var openItemId = GuidField(fields[0], rowNumber, "openItemId", rowIssues);
            var documentId = GuidField(fields[1], rowNumber, "sourceDocumentId", rowIssues);
            var installmentId = GuidField(fields[2], rowNumber, "sourceInstallmentId", rowIssues);
            var partyId = GuidField(fields[4], rowNumber, "partyId", rowIssues);
            var accountId = GuidField(fields[5], rowNumber, "companyAccountId", rowIssues);
            var currencyId = GuidField(fields[6], rowNumber, "currencyId", rowIssues);
            DateOnly? dueOn = null;
            var parsedDueOn = default(DateOnly);
            if (fields[8].Length > 0 && !DateOnly.TryParseExact(fields[8], "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDueOn))
            {
                rowIssues.Add(new(rowNumber, "dueOn", FinanceOfficialInvoiceImportCodesV1.DateInvalid));
            }
            else if (fields[8].Length > 0)
            {
                dueOn = parsedDueOn;
            }

            issues.AddRange(rowIssues);
            if (rowIssues.Count == 0)
            {
                rows.Add(new ApprovedFinanceOpenItemSourceRow(
                    openItemId, documentId, installmentId, fields[3],
                    FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                    partyId, accountId, currencyId, fields[7], dueOn));
            }
        }

        var reference = new FinanceOpenItemSourceReference(request.BatchId, request.BatchVersion, checksum);
        foreach (var issue in FinanceOpenItemSourcePolicy.ValidateReference(reference))
        {
            issues.Add(new(0, issue.Field, issue.Code));
        }
        if (issues.Count > 0)
        {
            return Result(checksum, issues);
        }

        var batch = new ApprovedFinanceOpenItemSourceBatch(
            request.BatchId, context.CompanyId, request.BatchVersion, checksum, rows);
        foreach (var issue in FinanceOpenItemSourcePolicy.ValidateBatch(context, reference, batch))
        {
            var rowNumber = RowNumber(issue.Field);
            var field = rowNumber == 0 ? issue.Field : RowField(issue.Field);
            issues.Add(new(rowNumber, field, issue.Code));
        }
        return issues.Count == 0
            ? new FinanceOfficialInvoiceImportPreviewV1(
                FinanceOfficialInvoiceImportTemplateV1.Version, checksum, batch, [])
            : Result(checksum, issues);
    }

    private static FinanceOfficialInvoiceImportPreviewV1 Result(
        string checksum,
        IReadOnlyList<FinanceOfficialInvoiceImportIssueV1> issues) =>
        new(FinanceOfficialInvoiceImportTemplateV1.Version, checksum, null, issues);

    private static Guid GuidField(
        string value,
        int rowNumber,
        string field,
        ICollection<FinanceOfficialInvoiceImportIssueV1> issues)
    {
        if (Guid.TryParseExact(value, "D", out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }
        issues.Add(new(rowNumber, field, "identity_required"));
        return Guid.Empty;
    }

    private static int RowNumber(string field)
    {
        if (!field.StartsWith("rows[", StringComparison.Ordinal))
        {
            return 0;
        }
        var end = field.IndexOf(']');
        return end > 5 && int.TryParse(field.AsSpan(5, end - 5), NumberStyles.None,
            CultureInfo.InvariantCulture, out var index)
            ? index + 2
            : 0;
    }

    private static string RowField(string field)
    {
        var end = field.IndexOf(']');
        return end >= 0 && end + 1 < field.Length && field[end + 1] == '.'
            ? field[(end + 2)..]
            : field;
    }

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
                else
                {
                    value.Append(character);
                }
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
}
