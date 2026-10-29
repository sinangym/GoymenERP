using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// SAT-D63 / SAT-D66 (EK11 implementation note) and SAT-D116 (<c>docs/decisions/SINAN_KARARLARI_2026-10-29_SATIS_EK15.md</c>:
/// "bağ yalnız kişinin üye olduğu şirketlere yazılır; üye olmadığı şirketler atlanır ve atlanan şirketler ekranda
/// gösterilir"): the person → limit group binding is stored PER COMPANY (the Sales port reads it per company, and a later
/// company-specific split needs no schema change), and one command writes the same binding to every company the actor can
/// act in WHERE THE PERSON IS AN ACTIVE MEMBER now (the Identity owner's answer through the host's port); the other
/// companies are skipped, untouched, and the read shows them as skipped (<c>isMember = false</c>):
/// <list type="bullet">
/// <item>The request names exactly those member companies with the binding version the editor saw in each ("0" = none);
/// any other set (membership changed meanwhile) is 422 <c>settings_discount_limit_binding_companies_mismatch</c> — the
/// editor re-reads, nothing is guessed. A company-specific binding written later is therefore never overwritten unseen:
/// its version is in the request (409 per company otherwise).</item>
/// <item>The actor must hold the name company-wide in the server company and in EVERY member company (403 otherwise,
/// nothing written), measured first and re-measured fresh before the save. A skipped company needs no authority: nothing
/// there is read or written.</item>
/// <item>A person who is a member of none of the actor's companies cannot be bound: 422
/// <c>settings_discount_limit_binding_person_not_member</c> (nothing to write).</item>
/// <item>A company the actor cannot access is never part of the command (EK15): the person has no limit there until
/// someone who can act there binds them. A later membership (or leaving a company) neither extends nor removes an
/// existing binding by itself — the person is bound again (fail-closed, EK15 "Sınırlar").</item>
/// <item>Groups are matched by CODE in each company (group ids are company-scoped); a code missing in a company is 422
/// <c>…_binding_group_not_found</c>, a passive one 422 <c>…_binding_group_inactive</c>, both naming the company.</item>
/// <item>All companies are written in ONE SQL transaction — one Settings context per company over the same connection, so
/// each company's rows pass its own write fence and its audit event is recorded in that company — and the terminal answer
/// is stored under the Idempotency-Key in the server-resolved company. All or nothing.</item>
/// </list>
/// </summary>
internal sealed partial class SettingsSalesDiscountLimitAdministration
{
    private const string BindingSetCommandName = "settings.sales-discount-limit-binding.set.v1";
    private const string BindingEntityType = "Settings.SalesDiscountLimitBinding";

    /// <summary>A technical bound of one binding request (transport size), not a business policy.</summary>
    private const int MaximumBoundGroups = 50;

    public async Task<SalesDiscountLimitBindingViewV1> GetBindingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var contextCompany = RequireContextCompany();
        await RequireAsync(contextCompany, cancellationToken);
        if (userId == Guid.Empty)
        {
            throw new SettingsPrerequisiteException(SalesDiscountLimitProblemCodesV1.RequestInvalid);
        }

        var companies = ActorCompanies();
        var members = await MemberCompaniesAsync(contextCompany, currentUser.UserId ?? string.Empty, userId, companies,
            cancellationToken);
        foreach (var company in members)
        {
            await RequireAsync(company, cancellationToken);
        }

        RequireRelational();
        var views = new List<SalesDiscountLimitBindingCompanyViewV1>(companies.Count);
        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            foreach (var company in companies)
            {
                if (!members.Contains(company))
                {
                    // Skipped (SAT-D116): the person does not work here now; nothing of this company is read or written.
                    views.Add(new SalesDiscountLimitBindingCompanyViewV1(company, "0", [], IsMember: false));
                    continue;
                }

                await using var store = await OpenCompanyAsync(company, contextCompany, transaction: null, cancellationToken);
                var current = await ReadBindingAsync(store.Database, userId, tracking: false, cancellationToken);
                views.Add(new SalesDiscountLimitBindingCompanyViewV1(company, Text(current.Version),
                    current.Groups.Select(group => new SalesDiscountLimitBoundGroupV1(group.Id, group.Code, group.Name,
                        group.IsActive)).ToArray(), IsMember: true));
            }
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
        }

        await RequireStillAsync(contextCompany, cancellationToken);
        foreach (var company in members)
        {
            await RequireStillAsync(company, cancellationToken);
        }

        return new SalesDiscountLimitBindingViewV1(userId, views);
    }

    public async Task<SettingsCommandResultV1> SetBindingAsync(Guid userId, SetSalesDiscountLimitBindingRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var contextCompany = RequireContextCompany();
        await RequireAsync(contextCompany, cancellationToken);
        if (!TryReadBinding(userId, request, out var codes, out var expected, out var detail))
        {
            return Unstored(Answers.Invalid(detail), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = userId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            UserId = userId,
            GroupCodes = codes,
            Companies = expected.OrderBy(pair => pair.Key).Select(pair => new { CompanyId = pair.Key, Version = pair.Value })
                .ToArray(),
            Reason = reason,
        });
        if (await FindExecutionAsync(BindingSetCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(contextCompany, cancellationToken);
            return Done("binding", metadata, subject, Replay(existing, requestHash, metadata));
        }

        // SAT-D116 (EK15, Sinan 2026-10-29): the binding goes to the actor's companies where the person is an active member now;
        // the others are skipped. Authority is then required in EVERY member company before anything is answered.
        var actorCompanies = ActorCompanies();
        var members = await MemberCompaniesAsync(contextCompany, metadata.ActorId, userId, actorCompanies, cancellationToken);
        if (members.Count == 0)
        {
            return Done("binding", metadata, subject, await StoreRefusalAsync(BindingSetCommandName, Answers.Problem(422,
                    SalesDiscountLimitProblemCodesV1.BindingPersonNotMember, "Kişi hiçbir şirkette üye değil.",
                    "Kişi işlem yapabildiğiniz şirketlerin hiçbirinde etkin üye değil; bağ yazılmadı."),
                keyHash, requestHash, metadata, cancellationToken));
        }

        var companies = actorCompanies.Where(members.Contains).ToArray();
        var scopes = new Dictionary<Guid, PermissionScope>();
        foreach (var company in companies)
        {
            scopes[company] = await RequireAsync(company, cancellationToken);
        }

        if (!companies.ToHashSet().SetEquals(expected.Keys))
        {
            return Done("binding", metadata, subject, await StoreRefusalAsync(BindingSetCommandName, Answers.Problem(422,
                    SalesDiscountLimitProblemCodesV1.BindingCompaniesMismatch, "Şirket listesi güncel değil.",
                    "Bağ, kişinin çalıştığı şirketlerin tamamına birlikte yazılır; liste değişmiş, yenileyip yeniden deneyin."),
                keyHash, requestHash, metadata, cancellationToken));
        }

        RequireRelational();
        return Done("binding", metadata, subject, await WriteBindingAsync(userId, codes, expected, companies, scopes, reason,
            keyHash, requestHash, metadata, cancellationToken));
    }

    private async Task<SettingsCommandResultV1> WriteBindingAsync(Guid userId, IReadOnlyList<string> codes,
        IReadOnlyDictionary<Guid, long> expected, IReadOnlyList<Guid> companies,
        IReadOnlyDictionary<Guid, PermissionScope> scopes, string reason, string keyHash, string requestHash,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        var contextCompany = metadata.CompanyId;
        var stores = new List<CompanyStore>();
        SettingsCommandAnswerV1? refusal = null;
        var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var plans = new List<BindingPlan>();
            foreach (var company in companies)
            {
                var store = await OpenCompanyAsync(company, contextCompany, transaction, cancellationToken);
                stores.Add(store);
                var groups = await store.Database.SalesDiscountLimitGroups
                    .Where(group => codes.Contains(group.Code))
                    .ToDictionaryAsync(group => group.Code, StringComparer.Ordinal, cancellationToken);
                foreach (var code in codes)
                {
                    refusal = !groups.TryGetValue(code, out var group)
                        ? Answers.Problem(422, SalesDiscountLimitProblemCodesV1.BindingGroupNotFound, "Grup bulunamadı.",
                            $"'{code}' kodlu limit grubu bu şirkette yok; bağ hiçbir şirkete yazılmadı.", company, code)
                        : !group.IsActive
                            ? Answers.Problem(422, SalesDiscountLimitProblemCodesV1.BindingGroupInactive, "Grup pasif.",
                                $"'{code}' kodlu limit grubu bu şirkette pasif; bağ hiçbir şirkete yazılmadı.", company, code)
                            : null;
                    if (refusal is not null)
                    {
                        break;
                    }
                }

                if (refusal is not null)
                {
                    break;
                }

                var current = await ReadBindingAsync(store.Database, userId, tracking: true, cancellationToken);
                if (current.Version != expected[company])
                {
                    refusal = Answers.VersionConflict(company);
                    break;
                }

                plans.Add(new BindingPlan(store, company, current, codes.Select(code => groups[code]).ToArray()));
            }

            if (refusal is null)
            {
                var primaryAudit = new List<Action>();
                foreach (var plan in plans)
                {
                    Apply(plan, userId, scopes[plan.Company], reason, codes, companies, metadata, primaryAudit);
                }

                foreach (var company in companies)
                {
                    await RequireStillAsync(company, cancellationToken);
                }

                foreach (var store in stores.Where(store => store.IsOwned))
                {
                    await store.Database.SaveChangesAsync(cancellationToken);
                }

                foreach (var enqueue in primaryAudit)
                {
                    enqueue();
                }

                AddExecution(database, BindingSetCommandName, Answers.NoContent(), keyHash, requestHash, metadata);
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await transaction.DisposeAsync();
                foreach (var store in stores)
                {
                    await store.DisposeAsync();
                }

                return new SettingsCommandResultV1(Answers.NoContent(), false, metadata.CorrelationId);
            }
        }
        catch (DbUpdateException)
        {
            refusal = Answers.VersionConflict();
        }
        catch
        {
            await RollbackAsync(transaction, stores);
            throw;
        }

        await RollbackAsync(transaction, stores);
        if (await FindExecutionAsync(BindingSetCommandName, keyHash, cancellationToken) is { } winner)
        {
            return Replay(winner, requestHash, metadata);
        }

        return await StoreRefusalAsync(BindingSetCommandName, refusal!, keyHash, requestHash, metadata, cancellationToken);
    }

    private async Task RollbackAsync(IDbContextTransaction transaction, List<CompanyStore> stores)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Already completed or rolled back by the server (a deadlock victim): nothing left to undo.
        }

        await transaction.DisposeAsync();
        foreach (var store in stores)
        {
            await store.DisposeAsync();
        }

        database.ChangeTracker.Clear();
    }

    /// <summary>
    /// Stages one company's change: a new binding version with its group rows, the groups' version fence (a concurrent
    /// deactivation fails the save), and the company's audit event. The server-resolved company's audit is deferred until
    /// its own save, so a failed save of another company never leaves an event in the request's collector.
    /// </summary>
    private void Apply(BindingPlan plan, Guid userId, PermissionScope scope, string reason, IReadOnlyList<string> codes,
        IReadOnlyList<Guid> companies, SettingsCommandMetadataV1 metadata, List<Action> primaryAudit)
    {
        var current = plan.Current;
        var targetIds = plan.Groups.Select(group => group.Id).ToHashSet();
        if (targetIds.SetEquals(current.Groups.Select(group => group.Id)))
        {
            return;
        }

        var db = plan.Store.Database;
        var version = checked(current.Version + 1);
        var now = timeProvider.GetUtcNow();
        if (current.Row is null)
        {
            db.SalesDiscountLimitMemberships.Add(new SalesDiscountLimitMembershipRow
            {
                CompanyId = plan.Company,
                UserId = userId,
                Version = version,
                UpdatedAtUtc = now,
                UpdatedBy = metadata.ActorId,
            });
        }
        else
        {
            current.Row.Version = version;
            current.Row.UpdatedAtUtc = now;
            current.Row.UpdatedBy = metadata.ActorId;
        }

        foreach (var group in plan.Groups)
        {
            db.SalesDiscountLimitMembershipGroups.Add(new SalesDiscountLimitMembershipGroupRow
            {
                CompanyId = plan.Company,
                UserId = userId,
                MembershipVersion = version,
                LimitGroupId = group.Id,
            });
            db.Entry(group).Property(row => row.UpdatedAtUtc).IsModified = true;
        }

        var before = new
        {
            CompanyId = plan.Company,
            UserId = userId,
            Version = current.Version,
            Groups = current.Groups.Select(group => new { group.Id, group.Code }).ToArray(),
        };
        var after = new
        {
            CompanyId = plan.Company,
            UserId = userId,
            Version = version,
            Groups = plan.Groups.Select(group => new { group.Id, group.Code }).ToArray(),
            GroupCodes = codes,
            WrittenToCompanies = companies,
            OriginCompanyId = metadata.CompanyId,
        };
        void Enqueue() => Audit(plan.Store.Collector, plan.Company, "settings.sales-discount-limit-binding.changed.v1",
            BindingEntityType, userId.ToString("D"), "SetBinding", current.Version == 0 ? null : current.Version, version,
            scope, before, after, reason, metadata);
        if (plan.Store.IsOwned)
        {
            Enqueue();
        }
        else
        {
            primaryAudit.Add(Enqueue);
        }
    }

    private static async Task<CurrentBinding> ReadBindingAsync(SettingsDbContext db, Guid userId, bool tracking,
        CancellationToken cancellationToken)
    {
        var memberships = tracking ? db.SalesDiscountLimitMemberships : db.SalesDiscountLimitMemberships.AsNoTracking();
        var row = await memberships.SingleOrDefaultAsync(membership => membership.UserId == userId, cancellationToken);
        if (row is null)
        {
            return new CurrentBinding(null, 0, []);
        }

        var version = row.Version;
        var groups = await db.SalesDiscountLimitMembershipGroups.AsNoTracking()
            .Where(bound => bound.UserId == userId && bound.MembershipVersion == version)
            .Join(db.SalesDiscountLimitGroups.AsNoTracking(),
                bound => new { bound.CompanyId, Id = bound.LimitGroupId },
                group => new { group.CompanyId, group.Id },
                (_, group) => new BoundGroup(group.Id, group.Code, group.Name, group.IsActive))
            .ToListAsync(cancellationToken);
        return new CurrentBinding(tracking ? row : null, version, groups.OrderBy(group => group.Code, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>
    /// The context's own Settings context for the server-resolved company; for any other company a context over the same
    /// connection (and transaction) with that company's own company context, audit collector and audit interceptor, so its
    /// rows pass its write fence and its audit event is recorded in that company.
    /// </summary>
    private async Task<CompanyStore> OpenCompanyAsync(Guid company, Guid contextCompany, IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (company == contextCompany)
        {
            return new CompanyStore(database, auditEvents, IsOwned: false);
        }

        var scopedCompany = new CompanyContext();
        scopedCompany.Set(company);
        var collector = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer(database.Database.GetDbConnection())
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, scopedCompany))
            .Options;
        var context = new SettingsDbContext(options, scopedCompany);
        try
        {
            if (transaction is not null)
            {
                await context.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            }

            return new CompanyStore(context, collector, IsOwned: true);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The companies among <paramref name="companies"/> where the person is an active member now (the Identity owner's
    /// answer through the host port). A refusal is 403, an unavailable owner 503 — never "member nowhere".
    /// </summary>
    private async Task<IReadOnlySet<Guid>> MemberCompaniesAsync(Guid contextCompany, string actorId, Guid userId,
        IReadOnlyList<Guid> companies, CancellationToken cancellationToken)
    {
        var directory = personDirectory
            ?? throw new SettingsPrerequisiteException(SalesDiscountLimitProblemCodesV1.ProviderUnavailable);
        var membership = await directory.GetActiveCompaniesAsync(contextCompany, actorId, userId, companies,
            cancellationToken);
        if (membership is not { Outcome: SettingsPersonDirectoryOutcomeV1.Found, ActiveCompanyIds: { } active })
        {
            throw membership?.Outcome == SettingsPersonDirectoryOutcomeV1.Denied
                ? new UnauthorizedAccessException()
                : new SettingsPrerequisiteException(SalesDiscountLimitProblemCodesV1.ProviderUnavailable);
        }

        return companies.Where(active.Contains).ToHashSet();
    }

    private IReadOnlyList<Guid> ActorCompanies() =>
        currentUser.AllowedCompanyIds.Where(company => company != Guid.Empty).OrderBy(company => company).ToArray();

    private void RequireRelational()
    {
        if (!database.Database.IsRelational())
        {
            throw new SettingsPrerequisiteException(SalesDiscountLimitProblemCodesV1.ProviderUnavailable);
        }
    }

    private static bool TryReadBinding(Guid userId, SetSalesDiscountLimitBindingRequestV1 request,
        out IReadOnlyList<string> codes, out IReadOnlyDictionary<Guid, long> expected, out string detail)
    {
        codes = [];
        expected = new Dictionary<Guid, long>();
        detail = string.Empty;
        if (userId == Guid.Empty || request.GroupCodes is null || request.GroupCodes.Count > MaximumBoundGroups)
        {
            detail = $"Kişi ve en çok {MaximumBoundGroups} grup kodu (boş liste: hiçbir grup) zorunludur.";
            return false;
        }

        var normalized = new List<string>(request.GroupCodes.Count);
        foreach (var code in request.GroupCodes)
        {
            if (!TryNormalizeCode(code, out var value) || normalized.Contains(value, StringComparer.Ordinal))
            {
                detail = "Grup kodları geçerli ve birbirinden farklı olmalıdır.";
                return false;
            }

            normalized.Add(value);
        }

        if (request.Companies is not { Count: > 0 } companies)
        {
            detail = "Şirketler ve her birinde görülen bağ sürümü zorunludur.";
            return false;
        }

        var versions = new Dictionary<Guid, long>();
        foreach (var company in companies)
        {
            if (company is null || company.CompanyId == Guid.Empty || versions.ContainsKey(company.CompanyId)
                || !TryParseVersion(company.ExpectedVersion, 0, out var version))
            {
                detail = "Her şirket bir kez ve beklenen bağ sürümüyle (0 veya pozitif tam sayı metni) verilmelidir.";
                return false;
            }

            versions[company.CompanyId] = version;
        }

        codes = normalized.Order(StringComparer.Ordinal).ToArray();
        expected = versions;
        return true;
    }

    private sealed record BoundGroup(Guid Id, string Code, string Name, bool IsActive);

    private sealed record CurrentBinding(SalesDiscountLimitMembershipRow? Row, long Version, IReadOnlyList<BoundGroup> Groups);

    private sealed record BindingPlan(CompanyStore Store, Guid Company, CurrentBinding Current,
        IReadOnlyList<SalesDiscountLimitGroupRow> Groups);

    private sealed record CompanyStore(SettingsDbContext Database, IAuditEventCollector Collector, bool IsOwned)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => IsOwned ? Database.DisposeAsync() : ValueTask.CompletedTask;
    }
}
