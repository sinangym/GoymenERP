using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>One company's active-state answer; <c>StateChanged</c> is null on a replay or a refusal.</summary>
internal sealed record CounterpartyAccountActiveStateOutcome(
    CounterpartyCommandExecution Execution,
    bool? StateChanged);

/// <summary>
/// ALS-D100: group-wide deactivation. The origin company (the request's company context) must grant
/// <see cref="ErpPermissions.CounterpartyPartiesGroupDeactivate"/> company-wide; every target company then runs its own
/// command in its own DI scope whose company context is that company, so the permission, expected version, idempotency
/// (a key derived per company), ALS-D99 warning, audit, outbox event and transaction are that company's. A refused or
/// failing company is reported and never rolls back the others (each company is its own consistency boundary).
/// </summary>
internal sealed class CounterpartyGroupDeactivationService(
    IServiceScopeFactory scopeFactory,
    CounterpartyDbContext dbContext,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService,
    IOrganizationService organizationService,
    ICounterpartyDeactivationExposureReader exposureReader,
    ILogger<CounterpartyGroupDeactivationService> logger)
    : ICounterpartyGroupDeactivationService
{
    private const int ReasonMaxLength = 1000;

    public async Task<CounterpartyGroupDeactivationPreviewView?> PreviewAsync(
        Guid partyId,
        CancellationToken cancellationToken)
    {
        if (partyId == Guid.Empty)
        {
            throw new ArgumentException("Cari kimliği boş olamaz.", nameof(partyId));
        }

        var reachable = await RequireOriginAndResolveReachableCompaniesAsync(cancellationToken);
        var accountCompanyIds = await ReadAccountCompanyIdsAsync(partyId, reachable, activeOnly: false, cancellationToken);
        if (accountCompanyIds.Length == 0)
        {
            return null;
        }

        var actorId = RequireActorId();
        var companies = new List<CounterpartyGroupDeactivationPreviewCompanyView>(accountCompanyIds.Length);
        foreach (var companyId in accountCompanyIds)
        {
            var exposure = await exposureReader.ReadAsync(companyId, actorId, partyId, cancellationToken);
            if (exposure is null)
            {
                continue;
            }

            var permission = await authorizationService.GetPermissionScopeAsync(
                ErpPermissions.CounterpartyPartiesGroupDeactivate,
                companyId,
                cancellationToken);
            companies.Add(new CounterpartyGroupDeactivationPreviewCompanyView(
                companyId,
                permission.IsGranted && permission.IsCompanyWide,
                exposure));
        }

        return companies.Count == 0
            ? null
            : new CounterpartyGroupDeactivationPreviewView(partyId, companies);
    }

    public async Task<CounterpartyGroupDeactivationResultView> DeactivateAsync(
        Guid partyId,
        GroupDeactivateCounterparty command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateCommand(partyId, command);
        if (!companyContext.IsResolved
            || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Cari grup pasife alma yürütme bağlamı geçersiz.");
        }

        var reachable = await RequireOriginAndResolveReachableCompaniesAsync(cancellationToken);
        var outcomes = new List<CounterpartyGroupDeactivationCompanyOutcomeView>(command.Targets.Count);
        foreach (var target in command.Targets.OrderBy(target => target.CompanyId))
        {
            if (!reachable.Contains(target.CompanyId))
            {
                outcomes.Add(Outcome(target.CompanyId, CounterpartyGroupDeactivationOutcomesV1.Forbidden));
                continue;
            }

            outcomes.Add(await DeactivateInCompanyAsync(partyId, target, command, metadata, cancellationToken));
        }

        var requested = command.Targets.Select(target => target.CompanyId).ToHashSet();
        var stillActiveNotRequested = await ReadAccountCompanyIdsAsync(
            partyId,
            reachable.Where(companyId => !requested.Contains(companyId)).ToHashSet(),
            activeOnly: true,
            cancellationToken);
        outcomes.AddRange(stillActiveNotRequested.Select(companyId =>
            Outcome(companyId, CounterpartyGroupDeactivationOutcomesV1.NotRequested)));

        logger.LogInformation(
            "Counterparty group deactivation completed. OriginCompanyId={CompanyId} PartyId={PartyId} ActorId={ActorId} CorrelationId={CorrelationId} Requested={Requested} Deactivated={Deactivated}",
            metadata.CompanyId,
            partyId,
            metadata.ActorId,
            metadata.CorrelationId,
            command.Targets.Count,
            outcomes.Count(outcome => outcome.Outcome == CounterpartyGroupDeactivationOutcomesV1.Deactivated));
        return new CounterpartyGroupDeactivationResultView(partyId, outcomes);
    }

    /// <summary>
    /// The per-company idempotency key is derived from the request key and the company, so a retried group request
    /// replays each company's stored answer and two companies never share a key.
    /// </summary>
    internal static string DeriveCompanyIdempotencyKey(string idempotencyKey, Guid companyId) =>
        $"{idempotencyKey}:{companyId:N}";

    private async Task<CounterpartyGroupDeactivationCompanyOutcomeView> DeactivateInCompanyAsync(
        Guid partyId,
        CounterpartyGroupDeactivationTarget target,
        GroupDeactivateCounterparty command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(target.CompanyId);
            var companyService = scope.ServiceProvider.GetRequiredService<CounterpartyCommandService>();
            var outcome = await companyService.DeactivateForGroupAsync(
                partyId,
                target.ExpectedVersion,
                command.Reason,
                command.ExposureWarningAcknowledged,
                metadata with
                {
                    CompanyId = target.CompanyId,
                    IdempotencyKey = DeriveCompanyIdempotencyKey(metadata.IdempotencyKey, target.CompanyId),
                },
                cancellationToken);
            return Map(target.CompanyId, outcome);
        }
        catch (UnauthorizedAccessException)
        {
            return Outcome(target.CompanyId, CounterpartyGroupDeactivationOutcomesV1.Forbidden);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Counterparty group deactivation failed in one company; the other companies are unaffected. CompanyId={CompanyId} PartyId={PartyId} CorrelationId={CorrelationId}",
                target.CompanyId,
                partyId,
                metadata.CorrelationId);
            return Outcome(target.CompanyId, CounterpartyGroupDeactivationOutcomesV1.Failed);
        }
    }

    private static CounterpartyGroupDeactivationCompanyOutcomeView Map(
        Guid companyId,
        CounterpartyAccountActiveStateOutcome outcome)
    {
        var response = outcome.Execution.Response;
        var replayed = outcome.Execution.IsReplay;
        return response.Result switch
        {
            CounterpartyCommandResult.Succeeded => new CounterpartyGroupDeactivationCompanyOutcomeView(
                companyId,
                outcome.StateChanged == false
                    ? CounterpartyGroupDeactivationOutcomesV1.AlreadyPassive
                    : CounterpartyGroupDeactivationOutcomesV1.Deactivated,
                replayed,
                CurrentVersion: null),
            CounterpartyCommandResult.VersionConflict => new CounterpartyGroupDeactivationCompanyOutcomeView(
                companyId,
                CounterpartyGroupDeactivationOutcomesV1.VersionConflict,
                replayed,
                response.Conflict?.CurrentVersion.ToString(CultureInfo.InvariantCulture)),
            CounterpartyCommandResult.NotFound => new CounterpartyGroupDeactivationCompanyOutcomeView(
                companyId, CounterpartyGroupDeactivationOutcomesV1.NotFound, replayed, CurrentVersion: null),
            CounterpartyCommandResult.IdempotencyConflict => new CounterpartyGroupDeactivationCompanyOutcomeView(
                companyId, CounterpartyGroupDeactivationOutcomesV1.IdempotencyConflict, replayed, CurrentVersion: null),
            CounterpartyCommandResult.DeactivationWarningAcknowledgementRequired =>
                new CounterpartyGroupDeactivationCompanyOutcomeView(
                    companyId,
                    CounterpartyGroupDeactivationOutcomesV1.WarningAcknowledgementRequired,
                    replayed,
                    CurrentVersion: null),
            _ => throw new InvalidOperationException(
                $"Cari grup pasife alma beklenmeyen şirket sonucu: {response.Result}."),
        };
    }

    private static CounterpartyGroupDeactivationCompanyOutcomeView Outcome(Guid companyId, string outcome) =>
        new(companyId, outcome, Replayed: false, CurrentVersion: null);

    private static void ValidateCommand(Guid partyId, GroupDeactivateCounterparty command)
    {
        if (partyId == Guid.Empty
            || command.Targets is null
            || command.Targets.Count == 0
            || command.Targets.Any(target => target is null
                || target.CompanyId == Guid.Empty
                || target.ExpectedVersion <= 0)
            || command.Targets.Select(target => target.CompanyId).Distinct().Count() != command.Targets.Count
            || string.IsNullOrWhiteSpace(command.Reason)
            || command.Reason.Trim().Length > ReasonMaxLength)
        {
            throw new ArgumentException(
                "Grup pasife alma için en az bir şirket (tekrarsız, sıfırdan büyük beklenen sürüm) ve 1-1000 karakter gerekçe zorunludur.");
        }
    }

    private async Task<HashSet<Guid>> RequireOriginAndResolveReachableCompaniesAsync(
        CancellationToken cancellationToken)
    {
        if (!companyContext.IsResolved)
        {
            throw new UnauthorizedAccessException("Cari grup pasife alma için şirket bağlamı zorunludur.");
        }

        var origin = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.CounterpartyPartiesGroupDeactivate,
            companyContext.CompanyId,
            cancellationToken);
        if (!origin.IsGranted || !origin.IsCompanyWide)
        {
            throw new UnauthorizedAccessException(
                "Cari grup pasife alma için şirket kapsamlı ayrı yetki zorunludur.");
        }

        var authorized = await organizationService.GetAuthorizedCompaniesAsync(cancellationToken);
        var reachable = authorized
            .Select(company => company.Id)
            .Where(companyId => companyId != Guid.Empty)
            .Where(currentUser.CanAccessCompany)
            .ToHashSet();
        if (!reachable.Contains(companyContext.CompanyId))
        {
            throw new UnauthorizedAccessException("Seçili şirket bu kullanıcı için etkin değil.");
        }

        return reachable;
    }

    private async Task<Guid[]> ReadAccountCompanyIdsAsync(
        Guid partyId,
        IReadOnlySet<Guid> companyIds,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        if (companyIds.Count == 0)
        {
            return [];
        }

        var allowed = companyIds.ToArray();
        return await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .Where(account => account.PartyId == partyId
                && allowed.Contains(account.CompanyId)
                && (!activeOnly || account.IsActive))
            .Select(account => account.CompanyId)
            .OrderBy(companyId => companyId)
            .ToArrayAsync(cancellationToken);
    }

    private string RequireActorId()
    {
        var actorId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new UnauthorizedAccessException("Kimliği doğrulanmış kullanıcı zorunludur.");
        }

        return actorId;
    }
}
