using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// SAT-D57 (Sinan, 2026-10-27) — the customer (price) group on the Cari card:
/// <list type="bullet">
/// <item><b>The list</b> is the active company's own (<see cref="CustomerGroup"/>): create, rename, deactivate and
/// reactivate under <c>counterparty.customer-groups.manage</c>. A group is never deleted, and no value is seeded.</item>
/// <item><b>The assignment</b> sits on the company's account (<see cref="CompanyPartyAccount.CustomerGroupId"/>) under
/// <c>counterparty.accounts.customer-group.assign</c>: only a group of the same company (422
/// <c>customer_group_not_found</c> otherwise, with the database's composite key behind it), never a passive group
/// (409 <c>customer_group_inactive</c>); an explicit null clears it.</item>
/// </list>
/// Every command is the Counterparty command shape: company-wide permission (plus <c>counterparty.parties.read</c>)
/// measured first and re-measured before the write, one Idempotency-Key (stored terminal answers replay), the record's
/// version as decimal text, one audit event per real change and none for a no-op.
/// </summary>
internal sealed partial class CounterpartyCommandService : ICounterpartyCustomerGroupService
{
    private const string CreateCustomerGroupCommandName = "counterparty.customer-group.create.v1";
    private const string RenameCustomerGroupCommandName = "counterparty.customer-group.rename.v1";
    private const string SetCustomerGroupActiveStateCommandName = "counterparty.customer-group.set-active-state.v1";
    private const string AssignCustomerGroupCommandName = "counterparty.company-account.customer-group.assign.v1";
    private const string CustomerGroupEntityType = "Counterparty.CustomerGroup";

    public async Task<CounterpartyCustomerGroupList> GetCustomerGroupsAsync(CancellationToken cancellationToken)
    {
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Cari okuması kimliği doğrulanmış kullanıcı ister.");
        var groups = await dbContext.CustomerGroups.AsNoTracking()
            .Where(group => group.CompanyId == companyId)
            .OrderBy(group => group.Name)
            .ThenBy(group => group.Id)
            .ToListAsync(cancellationToken);
        var canManage = await HasCompanyWideAsync(CounterpartyCustomerGroupPermissionsV1.Manage, cancellationToken);
        await RequireCustomerGroupReadStillAsync(companyId, actorId, cancellationToken);
        return new CounterpartyCustomerGroupList(
            companyId,
            groups.Select(ToView).ToArray(),
            new CounterpartyCustomerGroupViewer(canManage));
    }

    public async Task<CounterpartyCommandExecution> CreateCustomerGroupAsync(
        CreateCounterpartyCustomerGroup command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyCustomerGroupPermissionsV1.Manage, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => RequireCustomerGroupCommandStillAsync(
            metadata, CounterpartyCustomerGroupPermissionsV1.Manage, token);
        RequireCustomerGroupId(command.Id);
        var name = CustomerGroup.NormalizeName(command.Name);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { command.Id, Name = name });

        var existing = await FindCommandExecutionAsync(CreateCustomerGroupCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ResolveExistingExecution(existing, requestHash, metadata);
        }

        var refusal = await FindCustomerGroupCreateRefusalAsync(metadata.CompanyId, command.Id, name, cancellationToken);
        if (refusal is not null)
        {
            return await CompleteCommandAsync(CreateCustomerGroupCommandName, GroupResponses.Refused(refusal.Value),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        await beforeSave(cancellationToken);
        var group = new CustomerGroup(command.Id, metadata.CompanyId, name);
        dbContext.CustomerGroups.Add(group);
        EnqueueCustomerGroupAudit(metadata, scope, group, "Create", "counterparty.customer-group.created.v1",
            versionBefore: null, beforeJson: "{}", reason: "Müşteri grubu oluşturma");

        CounterpartyCommandExecution result;
        try
        {
            result = await CompleteCommandAsync(CreateCustomerGroupCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A concurrent create took the id or the name between the check and the insert: answer it as that refusal.
            dbContext.ChangeTracker.Clear();
            var lost = await FindCustomerGroupCreateRefusalAsync(metadata.CompanyId, command.Id, name, cancellationToken)
                ?? CounterpartyCommandResult.CustomerGroupExists;
            result = await CompleteCommandAsync(CreateCustomerGroupCommandName, GroupResponses.Refused(lost),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        LogCustomerGroupCommand("create", metadata, command.Id, result);
        return result;
    }

    public async Task<CounterpartyCommandExecution> RenameCustomerGroupAsync(
        Guid customerGroupId,
        RenameCounterpartyCustomerGroup command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyCustomerGroupPermissionsV1.Manage, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => RequireCustomerGroupCommandStillAsync(
            metadata, CounterpartyCustomerGroupPermissionsV1.Manage, token);
        RequireCustomerGroupId(customerGroupId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var name = CustomerGroup.NormalizeName(command.Name);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            CustomerGroupId = customerGroupId, ExpectedVersion = expectedVersion, Name = name,
        });

        var group = await FindCustomerGroupAsync(metadata.CompanyId, customerGroupId, tracking: true, cancellationToken);
        var existing = await FindCommandExecutionAsync(RenameCustomerGroupCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, group is not null);
        }

        if (group is null)
        {
            return await CompleteCommandAsync(RenameCustomerGroupCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (group.Version != expectedVersion)
        {
            return await CompleteCommandAsync(RenameCustomerGroupCommandName,
                Responses.VersionConflict(expectedVersion, group.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (!string.Equals(group.Name, name, StringComparison.Ordinal)
            && await IsCustomerGroupNameTakenAsync(metadata.CompanyId, name, customerGroupId, cancellationToken))
        {
            return await CompleteCommandAsync(RenameCustomerGroupCommandName,
                GroupResponses.Refused(CounterpartyCommandResult.CustomerGroupNameTaken),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var concurrency = new CustomerGroupConcurrencyContext(customerGroupId, expectedVersion);
        var beforeJson = SerializeCustomerGroupSnapshot(group);
        var versionBefore = group.Version;
        if (!group.Rename(name))
        {
            // The no-op is fenced by the same Version + rowversion predicate, without a version move or an audit event.
            dbContext.Entry(group).Property(value => value.Name).IsModified = true;
            return await CompleteCommandAsync(RenameCustomerGroupCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        }

        await beforeSave(cancellationToken);
        EnqueueCustomerGroupAudit(metadata, scope, group, "Rename", "counterparty.customer-group.renamed.v1",
            versionBefore, beforeJson, "Müşteri grubu adı değişikliği");
        CounterpartyCommandExecution result;
        try
        {
            result = await CompleteCommandAsync(RenameCustomerGroupCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            result = await CompleteCommandAsync(RenameCustomerGroupCommandName,
                GroupResponses.Refused(CounterpartyCommandResult.CustomerGroupNameTaken),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        LogCustomerGroupCommand("rename", metadata, customerGroupId, result);
        return result;
    }

    public async Task<CounterpartyCommandExecution> SetCustomerGroupActiveStateAsync(
        Guid customerGroupId,
        SetCounterpartyCustomerGroupActiveState command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyCustomerGroupPermissionsV1.Manage, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => RequireCustomerGroupCommandStillAsync(
            metadata, CounterpartyCustomerGroupPermissionsV1.Manage, token);
        RequireCustomerGroupId(customerGroupId);
        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            CustomerGroupId = customerGroupId, ExpectedVersion = expectedVersion, command.IsActive,
        });

        var group = await FindCustomerGroupAsync(metadata.CompanyId, customerGroupId, tracking: true, cancellationToken);
        var existing = await FindCommandExecutionAsync(SetCustomerGroupActiveStateCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, group is not null);
        }

        if (group is null)
        {
            return await CompleteCommandAsync(SetCustomerGroupActiveStateCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (group.Version != expectedVersion)
        {
            return await CompleteCommandAsync(SetCustomerGroupActiveStateCommandName,
                Responses.VersionConflict(expectedVersion, group.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        var concurrency = new CustomerGroupConcurrencyContext(customerGroupId, expectedVersion);
        var beforeJson = SerializeCustomerGroupSnapshot(group);
        var versionBefore = group.Version;
        if (!group.SetActive(command.IsActive))
        {
            dbContext.Entry(group).Property(value => value.IsActive).IsModified = true;
            return await CompleteCommandAsync(SetCustomerGroupActiveStateCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        }

        await beforeSave(cancellationToken);
        EnqueueCustomerGroupAudit(metadata, scope, group, group.IsActive ? "Activate" : "Deactivate",
            "counterparty.customer-group.active-state.changed.v1", versionBefore, beforeJson,
            group.IsActive ? "Müşteri grubunu yeniden etkinleştirme" : "Müşteri grubunu pasife alma");
        var result = await CompleteCommandAsync(SetCustomerGroupActiveStateCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        LogCustomerGroupCommand(command.IsActive ? "activate" : "deactivate", metadata, customerGroupId, result);
        return result;
    }

    public async Task<CounterpartyAccountCustomerGroupView?> GetAccountCustomerGroupAsync(
        Guid partyId,
        CancellationToken cancellationToken)
    {
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Cari okuması kimliği doğrulanmış kullanıcı ister.");
        var account = await dbContext.CompanyPartyAccounts.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CompanyId == companyId && candidate.PartyId == partyId,
                cancellationToken);
        var group = account?.CustomerGroupId is { } groupId
            ? await FindCustomerGroupAsync(companyId, groupId, tracking: false, cancellationToken)
            : null;
        var canAssign = await HasCompanyWideAsync(CounterpartyCustomerGroupPermissionsV1.Assign, cancellationToken);
        await RequireCustomerGroupReadStillAsync(companyId, actorId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (account.CustomerGroupId is not null && group is null)
        {
            // The composite foreign key makes this impossible; an unreadable group is never shown as "no group".
            throw new InvalidOperationException("Cari hesabın müşteri grubu okunamadı.");
        }

        return new CounterpartyAccountCustomerGroupView(
            account.PartyId,
            account.Id,
            account.CompanyId,
            account.Version.ToString(CultureInfo.InvariantCulture),
            account.IsActive,
            group is null ? null : ToView(group),
            new CounterpartyAccountCustomerGroupViewer(canAssign));
    }

    public async Task<CounterpartyCommandExecution> AssignAccountCustomerGroupAsync(
        Guid partyId,
        AssignCounterpartyCustomerGroup command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var scope = await RequireCompanyWidePermissionAsync(
            CounterpartyCustomerGroupPermissionsV1.Assign, cancellationToken);
        await RequireCompanyWidePermissionAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        Func<CancellationToken, Task> beforeSave = token => RequireCustomerGroupCommandStillAsync(
            metadata, CounterpartyCustomerGroupPermissionsV1.Assign, token);
        RequirePartyId(partyId);
        if (command.CustomerGroupId == Guid.Empty)
        {
            throw new ArgumentException("Müşteri grubu kimliği boş olamaz; grubu kaldırmak için null gönderin.",
                nameof(command));
        }

        var expectedVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, ExpectedVersion = expectedVersion, command.CustomerGroupId,
        });

        var account = await dbContext.CompanyPartyAccounts.SingleOrDefaultAsync(
            candidate => candidate.CompanyId == metadata.CompanyId && candidate.PartyId == partyId,
            cancellationToken);
        var existing = await FindCommandExecutionAsync(AssignCustomerGroupCommandName, keyHash, cancellationToken);
        if (existing is not null)
        {
            await beforeSave(cancellationToken);
            return ReplayFenced(existing, requestHash, metadata, account is not null);
        }

        if (account is null)
        {
            return await CompleteCommandAsync(AssignCustomerGroupCommandName, Responses.NotFound(),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        if (account.Version != expectedVersion)
        {
            return await CompleteCommandAsync(AssignCustomerGroupCommandName,
                Responses.VersionConflict(expectedVersion, account.Version),
                keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
        }

        CustomerGroup? group = null;
        if (command.CustomerGroupId is { } groupId)
        {
            // Only the active company's groups are visible here: another company's group is "not found", never named.
            group = await FindCustomerGroupAsync(metadata.CompanyId, groupId, tracking: false, cancellationToken);
            if (group is null)
            {
                return await CompleteCommandAsync(AssignCustomerGroupCommandName,
                    GroupResponses.Refused(CounterpartyCommandResult.CustomerGroupNotFound),
                    keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
            }

            // SAT-D77: a passive group keeps its existing assignments (and their price) but is never newly assigned.
            // Re-sending the account's current (now passive) group is a no-op below, not a new assignment.
            if (!group.IsActive && account.CustomerGroupId != groupId)
            {
                return await CompleteCommandAsync(AssignCustomerGroupCommandName,
                    GroupResponses.Refused(CounterpartyCommandResult.CustomerGroupInactive),
                    keyHash, requestHash, metadata, null, cancellationToken, beforeSave);
            }
        }

        var concurrency = new AccountConcurrencyContext(partyId, expectedVersion);
        var beforeJson = SerializeAccountCustomerGroupSnapshot(account);
        var versionBefore = account.Version;
        if (!account.AssignCustomerGroup(command.CustomerGroupId))
        {
            dbContext.Entry(account).Property(value => value.CustomerGroupId).IsModified = true;
            return await CompleteCommandAsync(AssignCustomerGroupCommandName, Responses.Succeeded(),
                keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        }

        await beforeSave(cancellationToken);
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.company-account.customer-group.changed.v1",
            "Counterparty.CompanyPartyAccount",
            account.Id.ToString("D"),
            command.CustomerGroupId is null ? "ClearCustomerGroup" : "AssignCustomerGroup",
            versionBefore,
            account.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            CounterpartyCustomerGroupPermissionsV1.Assign,
            scope.RoleCodes,
            beforeJson,
            SerializeAccountCustomerGroupSnapshot(account),
            Reason: "Cari şirket hesabı müşteri grubu değişikliği",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));
        var result = await CompleteCommandAsync(AssignCustomerGroupCommandName, Responses.Succeeded(),
            keyHash, requestHash, metadata, concurrency, cancellationToken, beforeSave);
        logger.LogInformation(
            "Counterparty customer group assignment completed. CompanyId={CompanyId} AccountId={AccountId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            metadata.CompanyId, account.Id, metadata.CorrelationId, result.Response.Result, result.IsReplay);
        return result;
    }

    private Task<CustomerGroup?> FindCustomerGroupAsync(
        Guid companyId, Guid customerGroupId, bool tracking, CancellationToken cancellationToken)
    {
        var groups = tracking ? dbContext.CustomerGroups : dbContext.CustomerGroups.AsNoTracking();
        return groups.SingleOrDefaultAsync(
            group => group.CompanyId == companyId && group.Id == customerGroupId,
            cancellationToken);
    }

    private Task<long?> ReadCustomerGroupVersionAsync(
        Guid companyId, Guid customerGroupId, CancellationToken cancellationToken) =>
        dbContext.CustomerGroups.AsNoTracking()
            .Where(group => group.CompanyId == companyId && group.Id == customerGroupId)
            .Select(group => (long?)group.Version)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<bool> IsCustomerGroupNameTakenAsync(
        Guid companyId, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        dbContext.CustomerGroups.AsNoTracking().AnyAsync(
            group => group.CompanyId == companyId && group.Name == name
                && (exceptId == null || group.Id != exceptId),
            cancellationToken);

    private async Task<CounterpartyCommandResult?> FindCustomerGroupCreateRefusalAsync(
        Guid companyId, Guid customerGroupId, string name, CancellationToken cancellationToken)
    {
        if (await FindCustomerGroupAsync(companyId, customerGroupId, tracking: false, cancellationToken) is not null)
        {
            return CounterpartyCommandResult.CustomerGroupExists;
        }

        return await IsCustomerGroupNameTakenAsync(companyId, name, exceptId: null, cancellationToken)
            ? CounterpartyCommandResult.CustomerGroupNameTaken
            : null;
    }

    private async Task RequireCustomerGroupReadStillAsync(
        Guid companyId, string actorId, CancellationToken cancellationToken)
    {
        RequireCustomerGroupContextStill(companyId, actorId);
        await RequireFreshCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyPartiesRead, companyId, cancellationToken);
        RequireCustomerGroupContextStill(companyId, actorId);
    }

    private async Task RequireCustomerGroupCommandStillAsync(
        CounterpartyCommandMetadata metadata, string permission, CancellationToken cancellationToken)
    {
        ValidateCommandMetadata(metadata);
        await RequireFreshCompanyWidePermissionAsync(permission, metadata.CompanyId, cancellationToken);
        ValidateCommandMetadata(metadata);
        await RequireFreshCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyPartiesRead, metadata.CompanyId, cancellationToken);
        ValidateCommandMetadata(metadata);
    }

    private void RequireCustomerGroupContextStill(Guid companyId, string actorId)
    {
        if (!currentUser.IsAuthenticated
            || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
            || !companyContext.IsResolved
            || companyContext.CompanyId != companyId
            || !currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException("Cari müşteri grubu okuma bağlamı değişti.");
        }
    }

    private static void RequireCustomerGroupId(Guid customerGroupId)
    {
        if (customerGroupId == Guid.Empty)
        {
            throw new ArgumentException("Müşteri grubu kimliği boş olamaz.", nameof(customerGroupId));
        }
    }

    private void EnqueueCustomerGroupAudit(
        CounterpartyCommandMetadata metadata,
        PermissionScope scope,
        CustomerGroup group,
        string operation,
        string eventType,
        long? versionBefore,
        string beforeJson,
        string reason) =>
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            eventType,
            CustomerGroupEntityType,
            group.Id.ToString("D"),
            operation,
            versionBefore,
            group.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            CounterpartyCustomerGroupPermissionsV1.Manage,
            scope.RoleCodes,
            beforeJson,
            SerializeCustomerGroupSnapshot(group),
            reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

    private void LogCustomerGroupCommand(
        string operation, CounterpartyCommandMetadata metadata, Guid customerGroupId, CounterpartyCommandExecution result) =>
        logger.LogInformation(
            "Counterparty customer group command completed. Operation={Operation} CompanyId={CompanyId} CustomerGroupId={CustomerGroupId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            operation, metadata.CompanyId, customerGroupId, metadata.CorrelationId, result.Response.Result, result.IsReplay);

    private static CounterpartyCustomerGroupView ToView(CustomerGroup group) => new(
        group.Id,
        group.Name,
        group.IsActive,
        group.Version.ToString(CultureInfo.InvariantCulture));

    private static string SerializeCustomerGroupSnapshot(CustomerGroup group) => JsonSerializer.Serialize(new
    {
        group.Id,
        group.CompanyId,
        group.Name,
        group.IsActive,
        group.Version,
    });

    private static string SerializeAccountCustomerGroupSnapshot(CompanyPartyAccount account) =>
        JsonSerializer.Serialize(new
        {
            account.Id,
            account.CompanyId,
            account.PartyId,
            account.CustomerGroupId,
            account.Version,
        });

    private static class GroupResponses
    {
        public static CounterpartyCommandResponse Refused(CounterpartyCommandResult result) => result switch
        {
            CounterpartyCommandResult.CustomerGroupExists => Problem(result, 409,
                "Müşteri grubu zaten var.",
                "Bu kimlikle bir müşteri grubu zaten kayıtlı. Listeyi yenileyip tekrar deneyin."),
            CounterpartyCommandResult.CustomerGroupNameTaken => Problem(result, 409,
                "Grup adı kullanılıyor.",
                "Bu şirkette aynı adda bir müşteri grubu (aktif veya pasif) zaten var."),
            CounterpartyCommandResult.CustomerGroupNotFound => Problem(result, 422,
                "Müşteri grubu bulunamadı.",
                "Seçilen müşteri grubu bu şirketin grup listesinde yok."),
            CounterpartyCommandResult.CustomerGroupInactive => Problem(result, 409,
                "Müşteri grubu pasif.",
                "Pasif bir müşteri grubu cari hesaba yeni atanamaz. Grubu yeniden etkinleştirin veya başka bir grup seçin."),
            _ => throw new InvalidOperationException($"SAT-D57 ret sonucu değil: {result}."),
        };

        private static CounterpartyCommandResponse Problem(
            CounterpartyCommandResult result, int statusCode, string title, string detail) =>
            new(result, statusCode, title, detail, Resource: null, Conflict: null);
    }
}
