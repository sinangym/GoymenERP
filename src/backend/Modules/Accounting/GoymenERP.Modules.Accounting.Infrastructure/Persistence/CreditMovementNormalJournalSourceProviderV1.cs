using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>Q9/TK-30: actual approved movement and owner money profile, bound to the existing approved component package.</summary>
public sealed class CreditMovementNormalJournalSourceProviderV1(AccountingDbContext db, ICompanyContext company,
    ICurrentUser user, ICorrelationContext correlation, IFinanceCreditMovementServiceV1 movements,
    IApprovedNormalJournalSourceBindingCatalogV1 bindings, IFinancePermissionPort permissions) : ICreditMovementNormalJournalSourceProviderV1
{
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();
    public async Task<NormalJournalDraftServerSourceV1?> ResolveAsync(Guid companyId, Guid sourceId, CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty || !Current(companyId, out var actor) || !correlation.IsResolved) return null;
        var row = await db.CreditMovementProposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, cancellationToken);
        if (!Same(companyId, actor) || row is null || row.CompanyId != companyId || row.IsReversal
            || row.SourceHash != AccountingInventoryCostSourceValidationV1.Sha256(row.SourceJson)) return null;
        CreditMovementSourceV1? source;
        try { source = JsonSerializer.Deserialize<CreditMovementSourceV1>(row.SourceJson, Json); }
        catch (JsonException) { return null; }
        if (source is null || source.CompanyId != companyId || source.EventId != row.Id || source.MovementId != row.MovementId
            || source.ReversesMovementId is not null || CreditMovementJournalComponentsV1.Purpose(source.Kind) is null) return null;
        var actual = await movements.ListAsync(new(companyId, actor, correlation.CorrelationId, correlation.CausationId), source.CreditAccountId, cancellationToken);
        if (!Same(companyId, actor) || actual is not { Outcome: ReadOutcome.Found, Resource: { } page }
            || page.CompanyId != companyId || page.Items.Count(x => x.Id == source.MovementId) != 1) return null;
        var movement = page.Items.Single(x => x.Id == source.MovementId); var request = movement.Source;
        if (movement.CompanyId != companyId || movement.Status != CreditMovementStatusV1.Approved
            || movement.CurrencyId != source.CurrencyId || movement.ReversedByMovementId is not null || movement.DecidedAtUtc is null
            || !Canonical(movement.Version) || request.CreditAccountId != source.CreditAccountId || request.Kind.ToString() != source.Kind
            || request.TransactionDate != source.TransactionDate || request.AccountingDate != source.AccountingDate
            || request.Reason != source.Reason || request.SourceDocument.Reference != source.SourceDocument.Reference
            || request.SourceDocument.DocumentDate != source.SourceDocument.DocumentDate || request.SourceDocument.Revision != source.SourceDocument.Revision
            || request.Components.Count != source.Components.Count || request.Components.Where((x, i) =>
                x.Kind != source.Components[i].Kind || x.Amount != source.Components[i].Amount || x.FunctionalAmount != source.Components[i].FunctionalAmount).Any()
            || JsonSerializer.Serialize(request.BankEffect, Json) != JsonSerializer.Serialize(source.BankEffect, Json)
            || movement.MoneyProfile is not { } profile || profile.ProfileId == Guid.Empty || profile.ProfileVersion < 1
            || profile.CurrencyScale is < 0 or > Money.MaximumScale
            || profile.SmallestCurrencyUnit != (profile.CurrencyScale == 0 ? "1" : "0." + new string('0', profile.CurrencyScale - 1) + "1")) return null;
        var profileId = profile.ProfileId.ToString("D"); var version = profile.ProfileVersion.ToString(CultureInfo.InvariantCulture);
        var rule = await bindings.ResolveAsync(companyId, sourceId, NormalJournalSourceKindsV1.RecordedCreditMovement,
            movement.Version, profileId, version, source.CurrencyId, cancellationToken);
        if (!Same(companyId, actor) || rule is null || rule.Mirror.CompanyId != companyId) return null;
        var exact = CreditMovementJournalComponentsV1.TryCreate(source, rule, profile.CurrencyScale);
        if (exact is null) return null;
        var allowed = await permissions.CheckAsync(new(companyId, actor, correlation.CorrelationId, correlation.CausationId),
            FinanceAction.ReadCredits, source.CreditAccountId, cancellationToken);
        if (!Same(companyId, actor) || allowed != PermissionDecision.Allowed) return null;
        var answer = new NormalJournalDraftServerSourceV1(rule, source.CurrencyId, movement.Version, profileId, version,
            ExactSourceLines: exact);
        return NormalJournalDraftSourcePreparationV1.TryCreate(companyId, sourceId, answer) is null ? null : answer;
    }
    private static bool Canonical(string? text) => !string.IsNullOrWhiteSpace(text) && text == text.Trim();
    private bool Current(Guid companyId, out string actor)
    {
        actor = user.UserId ?? string.Empty;
        return companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId && user.IsAuthenticated
            && user.CanAccessCompany(companyId) && Guid.TryParse(actor, out var parsed) && parsed != Guid.Empty;
    }
    private bool Same(Guid companyId, string actor) => Current(companyId, out var current) && actor == current;
}
