using System.Text;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseInvoiceFileReaderTests
{
    [Fact]
    public void Explicit_functional_columns_keep_different_line_facts_and_one_unchanged_header_total()
    {
        var extra = new[] { "FunctionalNet", "FunctionalVat", "FunctionalWithholding", "FunctionalLineNet", "FunctionalLineVat", "FunctionalLineWithholding" };
        var columns = Columns.Concat(extra).ToArray();
        var first = Values.Concat(new[] { "30.01", "6", "0", "10", "2", "0" }).ToArray();
        var second = Values.Concat(new[] { "30.01", "6", "0", "20.01", "4", "0" }).ToArray();
        second[Array.IndexOf(columns, "LineId")] = "002";
        var map = columns.ToDictionary(x => x, x => x, StringComparer.Ordinal);
        var source = Encoding.UTF8.GetBytes(string.Join(';', columns) + "\n" + string.Join(';', first) + "\n" + string.Join(';', second));
        var parsed = _reader.Read(source, new(PurchaseInvoiceFileFormatV1.Csv, map));
        Assert.Empty(parsed.Issues);
        Assert.Equal(new PurchaseInvoiceFileFunctionalAmountsV1("30.01", "6", "0"), parsed.Invoice!.FunctionalTotals);
        Assert.Equal(new[] { "10", "20.01" }, parsed.Invoice.Lines.Select(x => x.FunctionalAmounts!.Net));
        second[^1] = "";
        var incomplete = Encoding.UTF8.GetBytes(string.Join(';', columns) + "\n" + string.Join(';', second));
        Assert.Contains(_reader.Read(incomplete, new(PurchaseInvoiceFileFormatV1.Csv, map)).Issues,
            issue => issue.Code == "functional_amounts_incomplete");
    }
}
