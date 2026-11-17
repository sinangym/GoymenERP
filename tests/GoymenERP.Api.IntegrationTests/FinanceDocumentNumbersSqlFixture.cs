using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Explicit synthetic company-format preparation for pre-existing actual Finance HTTP journeys.
/// Uses the real Settings administrator. This is test setup, never a production seed/default or a user grant.</summary>
internal static class FinanceDocumentNumbersSqlFixture
{
    internal static async Task PrepareAsync(IServiceProvider host, params Guid[] companies)
    {
        foreach (var company in companies)
        {
            const string actor = "SENTETIK-number-fixture-editor";
            await using var owner = new PurchaseDocumentNumberSqlTestOwner(host, company,
                new PurchaseDocumentNumberSqlTestOwner.FixedActor(company, actor), new PurchaseDocumentNumberSqlTestOwner.DirectStoreAuthority());
            foreach (var (series, prefix) in new[] { (SettingsDocumentSeriesV1.FinanceReceipt, "MAK"), (SettingsDocumentSeriesV1.FinanceVoucher, "DEK") })
            {
                if ((await owner.Administration.GetFormatHistoryAsync(series)).Versions.Count != 0) continue;
                var answer = await owner.Administration.RecordFormatAsync(new(series, "0", prefix, 6, "SENTETIK açık test biçimi"),
                    new(company, actor, "SENTETIK-format-setup", null, "Test", null, null, "SENTETIK-format-" + prefix));
                Assert.Equal(204, answer.Answer.StatusCode);
            }
        }
    }
}
