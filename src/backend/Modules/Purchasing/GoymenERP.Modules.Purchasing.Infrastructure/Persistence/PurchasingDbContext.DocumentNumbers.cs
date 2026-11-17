namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingDbContext
{
    internal const string OrderDocumentNumberCheck =
        "([DocumentNumber] IS NULL AND [DocumentNumberFormatVersion] IS NULL AND [DocumentNumberSequence] IS NULL AND [DocumentNumberCancelledAtUtc] IS NULL) OR "
        + "([DocumentNumber] IS NOT NULL AND LEN([DocumentNumber]) > 0 AND [DocumentNumberFormatVersion] IS NOT NULL AND [DocumentNumberFormatVersion] > 0 "
        + "AND [DocumentNumberSequence] IS NOT NULL AND [DocumentNumberSequence] > 0)";
}
