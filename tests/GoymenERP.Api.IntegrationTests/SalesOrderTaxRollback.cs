namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MOD-03 → Sales: a rollback test of an OLDER Sales migration must first pass the tax migrations' Downs — AddDraftOrderTax refuses
/// (51135) while any buyer tax or line tax snapshot is stored, and every order now carries one (SAT-D140 "Normal" at create;
/// SAT-D184's backfill). Such a test reproduces the pre-tax state as SQL Server held it before AddDraftOrderTax (no tax columns
/// written), the same way the older tests clear the due terms before AddDraftHeaderRevisions. Both tax migrations then roll back
/// first (BackfillDraftBuyerTax's Down is a no-op), so the older migration's own guard is the one that answers.
/// </summary>
internal static class SalesOrderTaxRollback
{
    /// <summary>The statement (its last statement answers a row count, for the tests' scalar helpers).</summary>
    public const string ClearSql = """
        UPDATE [sales].[DraftLines] SET [TaxJson] = NULL WHERE [TaxJson] IS NOT NULL;
        UPDATE [sales].[DraftRevisionLines] SET [TaxJson] = NULL WHERE [TaxJson] IS NOT NULL;
        UPDATE [sales].[DraftRevisionHeaders] SET [BuyerTaxJson] = NULL WHERE [BuyerTaxJson] IS NOT NULL;
        UPDATE [sales].[Drafts] SET [BuyerTaxJson] = NULL WHERE [BuyerTaxJson] IS NOT NULL;
        SELECT @@ROWCOUNT;
        """;

    /// <summary>How many Sales migrations come after AddDraftCustomerRejections and roll back before an older guard answers.</summary>
    public const int TaxMigrationCount = 2;
}
