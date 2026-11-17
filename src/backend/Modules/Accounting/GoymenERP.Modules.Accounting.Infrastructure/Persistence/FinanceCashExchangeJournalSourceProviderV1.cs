using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>Reads Finance's actual approved carrying value again and binds its exact settlement to the existing approved mapping.</summary>
public sealed class FinanceCashExchangeJournalSourceProviderV1(AccountingDbContext db, ICompanyContext company,
    ICurrentUser user, ICorrelationContext correlation, ICashExchangeBookValueServiceV1 bookValues,
    IApprovedNormalJournalSourceBindingCatalogV1 bindings) : IFinanceCashExchangeJournalSourceProviderV1
{
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<NormalJournalDraftServerSourceV1?> ResolveAsync(Guid companyId, Guid sourceId, CancellationToken cancellationToken)
    {
        if (sourceId == Guid.Empty || !Current(companyId, out var actor) || !correlation.IsResolved) return null;
        var row = await db.FinanceJournalSources.AsNoTracking().SingleOrDefaultAsync(x => x.SourceId == sourceId
            && x.SourceKind == NormalJournalSourceKindsV1.ApprovedCashExchangeBookValue, cancellationToken);
        if (!Same(companyId, actor) || row is null || row.CompanyId != companyId
            || row.SourceHash != AccountingInventoryCostSourceValidationV1.Sha256(row.SourceJson)) return null;
        FinanceCashExchangeBookValueApprovedV1? recorded;
        try { recorded = JsonSerializer.Deserialize<FinanceCashExchangeBookValueApprovedV1>(row.SourceJson, Json); }
        catch (JsonException) { return null; }
        if (recorded is not { Source: { }, BookValue: { } } || recorded.EventId != row.Id || recorded.BookValue.Id != sourceId
            || recorded.BookValue.Version != row.SourceVersion || row.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
            || row.AmountText != recorded.BookValue.SourceBookValueTl
            || !FinanceJournalSourceReaderV1.ValidApprovedExchange(recorded.Source, recorded.BookValue, companyId)
            || recorded.Source.Preparation.TargetMoney.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira) return null;
        var context = new FinanceContext(companyId, actor, correlation.CorrelationId, correlation.CausationId);
        var actual = await bookValues.GetAsync(context, recorded.Source.Id, cancellationToken);
        if (!Same(companyId, actor) || actual is not { Outcome: ReadOutcome.Found, Resource: { } resource }
            || resource.CompanyId != companyId || resource.Transfer != recorded.Source
            || resource.Proposals.Count(x => x.Id == sourceId && x == recorded.BookValue) != 1
            || resource.Proposals.Count(x => x.Status == CashExchangeBookValueStatusV1.Approved) != 1) return null;
        var lines = CashExchangeJournalAmountsV1.TryCreate(recorded.BookValue.SourceBookValueTl,
            recorded.Source.Preparation.TargetMoney.Amount);
        if (lines is null) return null;
        var profile = recorded.Source.Preparation.SourceProfile;
        var rule = await bindings.ResolveAsync(companyId, sourceId, NormalJournalSourceKindsV1.ApprovedCashExchangeBookValue,
            recorded.BookValue.Version, profile.ProfileId, profile.Version, SettingsCurrencyIdentitiesV1.TurkishLira, cancellationToken);
        if (!Same(companyId, actor) || rule is null || rule.Mirror.CompanyId != companyId
            || rule.PostingPurpose != CashExchangeJournalAmountsV1.PostingPurpose) return null;
        var total = Math.Max(decimal.Parse(recorded.BookValue.SourceBookValueTl, CultureInfo.InvariantCulture),
            decimal.Parse(recorded.Source.Preparation.TargetMoney.Amount, CultureInfo.InvariantCulture));
        var source = new NormalJournalDraftServerSourceV1(rule, SettingsCurrencyIdentitiesV1.TurkishLira,
            recorded.BookValue.Version, profile.ProfileId, profile.Version, CashExchangeJournalAmountsV1.Text(total), lines);
        return NormalJournalDraftSourcePreparationV1.TryCreate(companyId, sourceId, source) is null ? null : source;
    }

    private bool Current(Guid companyId, out string actor)
    {
        actor = user.UserId ?? string.Empty;
        return companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId && user.IsAuthenticated
            && user.CanAccessCompany(companyId) && Guid.TryParse(actor, out var parsed) && parsed != Guid.Empty;
    }
    private bool Same(Guid companyId, string actor) => Current(companyId, out var current) && actor == current;
}
