using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Views;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyCompanyAccountLinkService(
    CounterpartyDbContext dbContext,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService,
    IAuditEventCollector auditEventCollector,
    ILogger<CounterpartyCompanyAccountLinkService> logger)
    : ICounterpartyCompanyAccountLinkService
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string CommandName = "counterparty.company-account.create.v1";

    public async Task<CounterpartyCommandExecution> CreateCompanyAccountAsync(
        Guid partyId,
        CreateCompanyPartyAccount command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        if (partyId == Guid.Empty)
        {
            throw new ArgumentException("Cari kimliği boş olamaz.", nameof(partyId));
        }

        var roles = ValidateRoles(command.Roles);
        var createScope = await RequireCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyAccountsCreate,
            cancellationToken);
        await RequireCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyPartiesRead,
            cancellationToken);

        var idempotencyKeyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new CreateCompanyAccountFingerprint(
            partyId,
            roles));
        var existingExecution = await FindCommandExecutionAsync(
            idempotencyKeyHash,
            cancellationToken);
        if (existingExecution is not null)
        {
            return ResolveExistingExecution(existingExecution, requestHash, metadata);
        }

        if (!await IsVisibleThroughAuthorizedExistingAccountAsync(
                partyId,
                cancellationToken))
        {
            return await CompleteCommandAsync(
                Responses.NotFound(),
                idempotencyKeyHash,
                requestHash,
                metadata,
                linkContext: null,
                cancellationToken);
        }

        var party = await dbContext.Parties
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == partyId && x.IsActive,
                cancellationToken);
        if (party is null)
        {
            return await CompleteCommandAsync(
                Responses.NotFound(),
                idempotencyKeyHash,
                requestHash,
                metadata,
                linkContext: null,
                cancellationToken);
        }

        var targetAlreadyLinked = await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .AnyAsync(
                x => x.CompanyId == metadata.CompanyId && x.PartyId == partyId,
                cancellationToken);
        if (targetAlreadyLinked)
        {
            return await CompleteCommandAsync(
                Responses.AlreadyLinked(),
                idempotencyKeyHash,
                requestHash,
                metadata,
                linkContext: null,
                cancellationToken);
        }

        var account = new CompanyPartyAccount(
            Guid.NewGuid(),
            metadata.CompanyId,
            partyId,
            roles);
        dbContext.CompanyPartyAccounts.Add(account);

        var resource = PartyCoreViewComposer.ComposeAuthorized(
            party,
            [account],
            new HashSet<Guid> { metadata.CompanyId })
            ?? throw new InvalidOperationException("Yeni şirket-cari ilişkisi görünümü oluşturulamadı.");

        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.company-account.created.v1",
            "Counterparty.CompanyPartyAccount",
            account.Id.ToString("D"),
            "Create",
            EntityVersionBefore: null,
            EntityVersionAfter: account.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            ErpPermissions.CounterpartyAccountsCreate,
            createScope.RoleCodes,
            BeforeJson: "{}",
            AfterJson: SerializeAccountAuditSnapshot(account),
            Reason: "Cari şirket hesabı oluşturma",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

        var result = await CompleteCommandAsync(
            Responses.Created(resource),
            idempotencyKeyHash,
            requestHash,
            metadata,
            new CompanyAccountLinkContext(partyId),
            cancellationToken);
        logger.LogInformation(
            "Counterparty company-account create command completed. CompanyId={CompanyId} PartyId={PartyId} ActorId={ActorId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            metadata.CompanyId,
            partyId,
            metadata.ActorId,
            metadata.CorrelationId,
            result.Response.Result,
            result.IsReplay);
        return result;
    }

    private async Task<CounterpartyCommandExecution> CompleteCommandAsync(
        CounterpartyCommandResponse response,
        string idempotencyKeyHash,
        string requestHash,
        CounterpartyCommandMetadata metadata,
        CompanyAccountLinkContext? linkContext,
        CancellationToken cancellationToken)
    {
        dbContext.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            metadata.CompanyId,
            CommandName,
            idempotencyKeyHash,
            requestHash,
            metadata.ActorId,
            response.Result.ToString(),
            response.StatusCode,
            JsonSerializer.Serialize(response),
            metadata.CorrelationId,
            DateTimeOffset.UtcNow,
            CommandExecutionSchemaVersion)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CounterpartyCommandExecution(
                response,
                false,
                metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindCommandExecutionAsync(
                idempotencyKeyHash,
                cancellationToken);
            if (winner is not null)
            {
                return ResolveExistingExecution(winner, requestHash, metadata);
            }

            if (linkContext is not null
                && await dbContext.CompanyPartyAccounts
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.CompanyId == metadata.CompanyId
                             && x.PartyId == linkContext.PartyId,
                        cancellationToken))
            {
                return await CompleteCommandAsync(
                    Responses.AlreadyLinked(),
                    idempotencyKeyHash,
                    requestHash,
                    metadata,
                    linkContext: null,
                    cancellationToken);
            }

            throw;
        }
    }

    private Task<CommandExecution?> FindCommandExecutionAsync(
        string idempotencyKeyHash,
        CancellationToken cancellationToken)
    {
        return dbContext.CommandExecutions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CommandName == CommandName
                     && x.IdempotencyKeyHash == idempotencyKeyHash,
                cancellationToken);
    }

    private static CounterpartyCommandExecution ResolveExistingExecution(
        CommandExecution execution,
        string requestHash,
        CounterpartyCommandMetadata metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new CounterpartyCommandExecution(
                Responses.IdempotencyConflict(),
                false,
                metadata.CorrelationId);
        }

        if (execution.SchemaVersion != CommandExecutionSchemaVersion)
        {
            throw new InvalidOperationException(
                "Kalıcı cari şirket-bağlama sonucu bilinmeyen şema sürümü taşıyor.");
        }

        var response = JsonSerializer.Deserialize<CounterpartyCommandResponse>(
            execution.ResponseJson);
        if (response is null
            || !string.Equals(
                response.Result.ToString(),
                execution.Outcome,
                StringComparison.Ordinal)
            || response.StatusCode != execution.HttpStatusCode
            || response.Result == CounterpartyCommandResult.IdempotencyConflict)
        {
            throw new InvalidOperationException(
                "Kalıcı cari şirket-bağlama idempotency sonucu command sözleşmesiyle uyumsuz.");
        }

        return new CounterpartyCommandExecution(
            response,
            true,
            execution.OriginalCorrelationId);
    }

    private async Task<bool> IsVisibleThroughAuthorizedExistingAccountAsync(
        Guid partyId,
        CancellationToken cancellationToken)
    {
        var readableCompanyIds = new HashSet<Guid>();
        foreach (var allowedCompanyId in currentUser.AllowedCompanyIds.Distinct())
        {
            if (allowedCompanyId == Guid.Empty)
            {
                continue;
            }

            if (await authorizationService.HasPermissionAsync(
                    ErpPermissions.CounterpartyPartiesRead,
                    allowedCompanyId,
                    cancellationToken))
            {
                readableCompanyIds.Add(allowedCompanyId);
            }
        }

        if (readableCompanyIds.Count == 0)
        {
            return false;
        }

        var companyIdArray = readableCompanyIds.ToArray();
        return await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .AnyAsync(
                x => x.PartyId == partyId
                     && companyIdArray.Contains(x.CompanyId),
                cancellationToken);
    }

    private async Task<PermissionScope> RequireCompanyWidePermissionAsync(
        string permission,
        CancellationToken cancellationToken)
    {
        var scope = await authorizationService.GetPermissionScopeAsync(
            permission,
            companyContext.CompanyId,
            cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide)
        {
            throw new UnauthorizedAccessException(
                "Cari şirket-bağlama command'i için şirket kapsamlı yetki zorunludur.");
        }

        return scope;
    }

    private void ValidateCommandMetadata(CounterpartyCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved
            || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Cari şirket-bağlama command yürütme bağlamı geçersiz.");
        }
    }

    private static PartyRole ValidateRoles(PartyRole roles)
    {
        const PartyRole knownRoles =
            PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service;
        if (roles == PartyRole.None || (roles & ~knownRoles) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roles),
                roles,
                "Cari şirket hesabı en az bir bilinen ticari rol taşımalıdır.");
        }

        return roles;
    }

    private static string SerializeAccountAuditSnapshot(CompanyPartyAccount account)
    {
        return JsonSerializer.Serialize(new
        {
            account.Id,
            account.CompanyId,
            account.PartyId,
            account.Roles,
            account.ExternalIntegrationCode,
            account.IsActive,
            account.Version,
        });
    }

    private sealed record CreateCompanyAccountFingerprint(
        Guid PartyId,
        PartyRole Roles);

    private sealed record CompanyAccountLinkContext(Guid PartyId);

    private static class Responses
    {
        public static CounterpartyCommandResponse Created(PartyCoreView resource) => new(
            CounterpartyCommandResult.Created,
            201,
            Title: null,
            Detail: null,
            resource,
            Conflict: null);

        public static CounterpartyCommandResponse NotFound() => new(
            CounterpartyCommandResult.NotFound,
            404,
            "Kayıt bulunamadı.",
            "İstenen cari kayıt bulunamadı veya yetkili şirket kapsamınızda kullanılamıyor.",
            Resource: null,
            Conflict: null);

        public static CounterpartyCommandResponse AlreadyLinked() => new(
            CounterpartyCommandResult.AlreadyLinked,
            409,
            "Cari şirket ilişkisi zaten mevcut.",
            "Bu cari seçili Göymen şirketine zaten bağlı.",
            Resource: null,
            Conflict: null);

        public static CounterpartyCommandResponse IdempotencyConflict() => new(
            CounterpartyCommandResult.IdempotencyConflict,
            409,
            "Idempotency çakışması.",
            "Aynı işlem anahtarı farklı bir istek için kullanılamaz.",
            Resource: null,
            Conflict: null);
    }
}
