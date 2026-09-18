using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// The Manufacturing store of the server-resolved company (MFG-5). Every write is one save: the
/// plan or order change, the facts it adds, the new version and its audit event commit together or
/// not at all.
/// <list type="bullet">
/// <item><b>Company:</b> a record of any other company than the resolved context is refused before
/// anything is read (<see cref="ManufacturingStoreCodes.CompanyContextMismatch"/>); every read runs
/// under the tenant filter.</item>
/// <item><b>Rules:</b> the domain decides. A plan or order is rebuilt from its stored facts through
/// the same methods that recorded them, then the requested change is applied to it: the version
/// fence, fact identity, roll binding, return bound, latest WIP reading and the MFG-4 close rule
/// all hold against stored data exactly as in memory.</item>
/// <item><b>Replay (R3):</b> a plan, order or fact id is its idempotency identity. The same id with
/// the same canonical content (masses normalised, times in UTC) is a replay with no second effect
/// and no version change; with another content it is refused.</item>
/// <item><b>Race:</b> every change bumps the plan's or order's version (a concurrency token), so two
/// writers that read the same order cannot both commit; the loser gets
/// <see cref="ManufacturingStoreCodes.ConcurrentChange"/>, nothing of it is stored, and it may retry.</item>
/// <item><b>Audit:</b> ADR-009 envelope from <see cref="ManufacturingCommandMetadata"/>, in the same save.</item>
/// </list>
/// </summary>
public sealed class ManufacturingStore(
    ManufacturingDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : IManufacturingPlanStore, IManufacturingOrderStore
{
    private const string PlanEntityType = "Manufacturing.ProductionPlan";
    private const string OrderEntityType = "Manufacturing.ProductionOrder";
    private const int ActorMaximumLength = 128;
    private const decimal KilogramColumnLimit = 1_000_000_000_000_000m;

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<ManufacturingWriteOutcome> SavePlanAsync(ProductionPlan plan, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequireMetadata(metadata);
        RequireCompany(plan.CompanyId);
        if (plan.Status != PlanStatus.Draft)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanNotDraft,
                "Yalnız taslak plan kaydedilir; onay ayrı bir yazmadır.");
        }

        RequireFits(plan.Inputs.Select(input => input.PlannedMass).Concat(plan.Target is { } t ? [t.Mass] : []));

        var payloadHash = PlanHash(plan);
        var existing = await dbContext.Plans.AsNoTracking().SingleOrDefaultAsync(row => row.Id == plan.Id, cancellationToken);
        if (existing is not null)
        {
            return string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal)
                ? ManufacturingWriteOutcome.Replayed
                : throw Refused(ManufacturingStoreCodes.PlanIdConflict, "Aynı plan kimliği farklı içerikle verildi.");
        }

        var now = timeProvider.GetUtcNow();
        dbContext.Plans.Add(new PlanRow
        {
            Id = plan.Id,
            CompanyId = plan.CompanyId,
            WorkCenterId = plan.WorkCenterId,
            Process = (int)plan.Process,
            Status = (int)plan.Status,
            TargetBasis = plan.Target is null ? null : (int)plan.Target.Basis,
            TargetKg = plan.Target?.Mass.Value,
            Version = plan.Version,
            PayloadHash = payloadHash,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        var sequence = 0;
        foreach (var input in plan.Inputs)
        {
            dbContext.PlanInputs.Add(new PlanInputRow
            {
                CompanyId = plan.CompanyId,
                PlanId = plan.Id,
                ParentRollId = input.ParentRollId,
                Sequence = ++sequence,
                PlannedKg = input.PlannedMass.Value,
            });
        }

        Audit(metadata, plan.CompanyId, PlanEntityType, plan.Id, "manufacturing.plan.created.v1", "Create",
            versionBefore: null, versionAfter: plan.Version, before: "{}", after: PlanAudit(plan), "Üretim planı kaydı");
        await SaveAsync(cancellationToken);
        return ManufacturingWriteOutcome.Recorded;
    }

    public async Task<ProductionPlan> ApprovePlanAsync(Guid planId, long expectedVersion, Guid approvedByUserId,
        DateTimeOffset approvedAt, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var row = await dbContext.Plans.SingleOrDefaultAsync(candidate => candidate.Id == planId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.PlanNotFound, "Bu şirketin böyle bir planı yok.");
        var plan = await RestorePlanAsync(row, cancellationToken);
        var before = PlanAudit(plan);
        var versionBefore = plan.Version;

        // The domain decides: the version fence first, then "only a draft is approved".
        plan.Approve(expectedVersion, approvedByUserId, approvedAt);

        row.Status = (int)plan.Status;
        row.ApprovedByUserId = plan.ApprovedByUserId;
        row.ApprovedAtUtc = plan.ApprovedAtUtc;
        row.Version = plan.Version;
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        Audit(metadata, companyId, PlanEntityType, plan.Id, "manufacturing.plan.approved.v1", "Approve",
            versionBefore, plan.Version, before, PlanAudit(plan), "Üretim planı onayı");
        await SaveAsync(cancellationToken);
        return plan;
    }

    public async Task<ProductionPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Plans.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == planId, cancellationToken);
        return row is null ? null : await RestorePlanAsync(row, cancellationToken);
    }

    public async Task<ManufacturingWriteOutcome> CreateOrderAsync(Guid orderId, Guid planId,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var existing = await dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(row => row.Id == orderId, cancellationToken);
        if (existing is not null)
        {
            return existing.PlanId == planId
                ? ManufacturingWriteOutcome.Replayed
                : throw Refused(ManufacturingStoreCodes.OrderIdConflict, "Aynı emir kimliği başka bir plan için verildi.");
        }

        var planRow = await dbContext.Plans.AsNoTracking().SingleOrDefaultAsync(row => row.Id == planId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.PlanNotFound, "Bu şirketin böyle bir planı yok.");
        var plan = await RestorePlanAsync(planRow, cancellationToken);

        // The domain decides: only an approved plan becomes an order.
        var order = ProductionOrder.FromApprovedPlan(orderId, plan);

        var now = timeProvider.GetUtcNow();
        dbContext.Orders.Add(new OrderRow
        {
            Id = order.Id,
            CompanyId = order.CompanyId,
            PlanId = order.PlanId,
            WorkCenterId = order.WorkCenterId,
            Process = (int)order.Process,
            Status = (int)order.Status,
            TargetBasis = order.Target is null ? null : (int)order.Target.Basis,
            TargetKg = order.Target?.Mass.Value,
            Version = order.Version,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        var sequence = 0;
        foreach (var input in order.PlannedInputs)
        {
            dbContext.OrderInputs.Add(new OrderInputRow
            {
                CompanyId = order.CompanyId,
                OrderId = order.Id,
                ParentRollId = input.ParentRollId,
                Sequence = ++sequence,
                PlannedKg = input.PlannedMass.Value,
            });
        }

        Audit(metadata, companyId, OrderEntityType, order.Id, "manufacturing.order.created.v1", "Create",
            versionBefore: null, versionAfter: order.Version, before: "{}",
            after: JsonSerializer.Serialize(new { order.Id, order.PlanId, order.WorkCenterId, rolls = order.PlannedInputs.Count }, Json),
            "Üretim emri kaydı");
        await SaveAsync(cancellationToken);
        return ManufacturingWriteOutcome.Recorded;
    }

    public Task<ManufacturingWriteOutcome> IssueInputAsync(Guid orderId, long expectedVersion, Guid issueId,
        Guid parentRollId, Kilograms mass, DateTimeOffset issuedAt, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default) =>
        RecordFactAsync(orderId, FactKind.InputIssue, issueId, parentRollId, mass, issuedAt, outputClass: null, metadata,
            order =>
            {
                var fact = order.IssueInput(expectedVersion, issueId, parentRollId, mass, issuedAt);
                return (fact.IssuedAtUtc, (CostStatus?)null);
            },
            "manufacturing.input.issued.v1", cancellationToken);

    public Task<ManufacturingWriteOutcome> ReturnUnusedInputAsync(Guid orderId, long expectedVersion, Guid returnId,
        Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default) =>
        RecordFactAsync(orderId, FactKind.InputReturn, returnId, parentRollId, mass, returnedAt, outputClass: null, metadata,
            order =>
            {
                var fact = order.ReturnUnusedInput(expectedVersion, returnId, parentRollId, mass, returnedAt);
                return (fact.ReturnedAtUtc, (CostStatus?)null);
            },
            "manufacturing.input.returned.v1", cancellationToken);

    public Task<ManufacturingWriteOutcome> DeclareOutputAsync(Guid orderId, long expectedVersion, Guid outputId,
        Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        RecordFactAsync(orderId, FactKind.OutputDeclaration, outputId, parentRollId, mass, declaredAt, outputClass, metadata,
            order =>
            {
                var fact = order.DeclareOutput(expectedVersion, outputId, parentRollId, outputClass, mass, declaredAt);
                return (fact.DeclaredAtUtc, (CostStatus?)fact.Cost.Status);
            },
            "manufacturing.output.declared.v1", cancellationToken);

    public Task<ManufacturingWriteOutcome> RecordWorkInProgressAsync(Guid orderId, long expectedVersion,
        Guid measurementId, Guid parentRollId, Kilograms mass, DateTimeOffset measuredAt,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        RecordFactAsync(orderId, FactKind.WipMeasurement, measurementId, parentRollId, mass, measuredAt, outputClass: null,
            metadata,
            order =>
            {
                var fact = order.RecordWorkInProgress(expectedVersion, measurementId, parentRollId, mass, measuredAt);
                return (fact.MeasuredAtUtc, (CostStatus?)null);
            },
            "manufacturing.wip.measured.v1", cancellationToken);

    public async Task CloseOrderAsync(Guid orderId, long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var row = await dbContext.Orders.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.OrderNotFound, "Bu şirketin böyle bir emri yok.");
        var order = await RestoreOrderAsync(row, cancellationToken);
        var versionBefore = order.Version;

        // The domain decides (MFG-4): each roll on its own; remaining WIP stays; any unexplained
        // difference refuses the close.
        order.Close(expectedVersion, closedByUserId, closedAt);

        row.Status = (int)order.Status;
        row.ClosedByUserId = order.ClosedByUserId;
        row.ClosedAtUtc = order.ClosedAtUtc;
        row.Version = order.Version;
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        Audit(metadata, companyId, OrderEntityType, order.Id, "manufacturing.order.closed.v1", "Close",
            versionBefore, order.Version, before: JsonSerializer.Serialize(new { status = "Open" }, Json),
            after: JsonSerializer.Serialize(new
            {
                status = "Closed",
                balances = order.PlannedInputs.Select(input => order.BalanceOf(input.ParentRollId)).Select(balance => new
                {
                    balance.ParentRollId,
                    issued = balance.Issued.Value,
                    returned = balance.Returned.Value,
                    workInProgress = balance.WorkInProgress.Value,
                    unexplained = balance.Unexplained,
                }),
            }, Json),
            "Üretim emri kapanışı");
        await SaveAsync(cancellationToken);
    }

    public async Task<ProductionOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        return row is null ? null : await RestoreOrderAsync(row, cancellationToken);
    }

    private async Task<ManufacturingWriteOutcome> RecordFactAsync(Guid orderId, FactKind kind, Guid factId,
        Guid parentRollId, Kilograms mass, DateTimeOffset occurredAt, OutputClass? outputClass,
        ManufacturingCommandMetadata metadata, Func<ProductionOrder, (DateTimeOffset OccurredAtUtc, CostStatus? Cost)> apply,
        string eventType, CancellationToken cancellationToken)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        RequireFits([mass]);

        // A replay is recognised before the version fence: the same fact again is answered even
        // after the order has moved on, and never changes the version.
        var kindValue = (int)kind;
        var payloadHash = FactHash(kind, orderId, parentRollId, mass, occurredAt, outputClass);
        var existing = await dbContext.Facts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Kind == kindValue && row.Id == factId, cancellationToken);
        if (existing is not null)
        {
            return string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal)
                ? ManufacturingWriteOutcome.Replayed
                : throw Refused(ManufacturingStoreCodes.FactIdConflict, "Aynı olgu kimliği farklı içerikle verildi.");
        }

        var row = await dbContext.Orders.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.OrderNotFound, "Bu şirketin böyle bir emri yok.");
        var order = await RestoreOrderAsync(row, cancellationToken);
        var versionBefore = order.Version;

        // The domain decides; nothing below runs for a refused fact.
        var (occurredAtUtc, cost) = apply(order);

        var now = timeProvider.GetUtcNow();
        dbContext.Facts.Add(new FactRow
        {
            CompanyId = companyId,
            Kind = kindValue,
            Id = factId,
            OrderId = orderId,
            ParentRollId = parentRollId,
            MassKg = mass.Value,
            OccurredAtUtc = occurredAtUtc,
            OutputClass = outputClass is null ? null : (int)outputClass.Value,
            CostStatus = cost is null ? null : (int)cost.Value,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        });
        row.Version = order.Version;
        row.UpdatedAtUtc = now;
        Audit(metadata, companyId, OrderEntityType, orderId, eventType, "Record", versionBefore, order.Version,
            before: "{}",
            after: JsonSerializer.Serialize(new
            {
                kind = kind.ToString(),
                factId,
                parentRollId,
                massKg = mass.Value,
                occurredAtUtc,
                outputClass = outputClass?.ToString(),
                cost = cost?.ToString(),
                payloadHash,
            }, Json),
            "Üretim gerçekleşme olgusu");
        await SaveAsync(cancellationToken);
        return ManufacturingWriteOutcome.Recorded;
    }

    private async Task<ProductionPlan> RestorePlanAsync(PlanRow row, CancellationToken cancellationToken)
    {
        var inputs = await dbContext.PlanInputs.AsNoTracking()
            .Where(input => input.PlanId == row.Id)
            .OrderBy(input => input.Sequence)
            .ToListAsync(cancellationToken);
        return ProductionPlan.Restore(row.Id, row.CompanyId, row.WorkCenterId, (ManufacturingProcess)row.Process,
            inputs.Select(input => new PlannedInput(input.ParentRollId, new Kilograms(input.PlannedKg))),
            Target(row.TargetBasis, row.TargetKg), row.ApprovedByUserId, row.ApprovedAtUtc, row.Version);
    }

    private async Task<ProductionOrder> RestoreOrderAsync(OrderRow row, CancellationToken cancellationToken)
    {
        var inputs = await dbContext.OrderInputs.AsNoTracking()
            .Where(input => input.OrderId == row.Id)
            .OrderBy(input => input.Sequence)
            .ToListAsync(cancellationToken);
        var facts = await dbContext.Facts.AsNoTracking()
            .Where(fact => fact.OrderId == row.Id)
            .ToListAsync(cancellationToken);
        IEnumerable<FactRow> Of(FactKind kind) => facts.Where(fact => fact.Kind == (int)kind);

        return ProductionOrder.Restore(
            row.Id, row.PlanId, row.CompanyId, row.WorkCenterId, (ManufacturingProcess)row.Process,
            Target(row.TargetBasis, row.TargetKg),
            inputs.Select(input => new PlannedInput(input.ParentRollId, new Kilograms(input.PlannedKg))),
            Of(FactKind.InputIssue).Select(fact => new InputIssue(fact.Id, fact.ParentRollId, new Kilograms(fact.MassKg),
                fact.OccurredAtUtc)),
            Of(FactKind.InputReturn).Select(fact => new InputReturn(fact.Id, fact.ParentRollId, new Kilograms(fact.MassKg),
                fact.OccurredAtUtc)),
            Of(FactKind.OutputDeclaration).Select(fact => new OutputDeclaration(fact.Id, fact.ParentRollId,
                (OutputClass)(fact.OutputClass ?? 0), new Kilograms(fact.MassKg), fact.OccurredAtUtc, Cost(fact.CostStatus))),
            Of(FactKind.WipMeasurement).Select(fact => new WipMeasurement(fact.Id, fact.ParentRollId, new Kilograms(fact.MassKg),
                fact.OccurredAtUtc)),
            row.ClosedByUserId, row.ClosedAtUtc, row.Version);
    }

    private static ProductionTarget? Target(int? basis, decimal? kilograms) =>
        basis is null && kilograms is null
            ? null
            : new ProductionTarget((TargetBasis)(basis ?? 0), new Kilograms(kilograms ?? 0m));

    private static CostResult Cost(int? status) => (CostStatus?)status switch
    {
        CostStatus.NotCalculated => CostResult.NotCalculated,
        CostStatus.WaitingPolicy => CostResult.WaitingPolicy,
        // A known cost cannot be built in this slice; a stored one is not what the domain wrote.
        _ => throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
            "Kayıtlı çıktının maliyet durumu bu dilimde üretilemez."),
    };

    /// <summary>
    /// The canonical content of a plan: company, work center, process, target and inputs in their
    /// order; masses normalised so 5.00 kg and 5 kg are one content.
    /// </summary>
    internal static string PlanHash(ProductionPlan plan)
    {
        var builder = new StringBuilder("manufacturing.plan.v1|");
        builder.Append(plan.CompanyId.ToString("D")).Append('|')
            .Append(plan.WorkCenterId.ToString("D")).Append('|')
            .Append((int)plan.Process).Append('|')
            .Append(plan.Target is null ? "-" : $"{(int)plan.Target.Basis}:{Canonical(plan.Target.Mass.Value)}").Append('|');
        foreach (var input in plan.Inputs)
        {
            builder.Append(input.ParentRollId.ToString("D")).Append(':').Append(Canonical(input.PlannedMass.Value)).Append(';');
        }

        return Hash(builder.ToString());
    }

    /// <summary>
    /// The canonical content of a fact: kind, order, parent roll, normalised mass, the instant in
    /// UTC and, for an output, its class.
    /// </summary>
    internal static string FactHash(FactKind kind, Guid orderId, Guid parentRollId, Kilograms mass, DateTimeOffset occurredAt,
        OutputClass? outputClass) =>
        Hash(string.Join('|',
            "manufacturing.fact.v1",
            ((int)kind).ToString(CultureInfo.InvariantCulture),
            orderId.ToString("D"),
            parentRollId.ToString("D"),
            Canonical(mass.Value),
            occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            outputClass is null ? "-" : ((int)outputClass.Value).ToString(CultureInfo.InvariantCulture)));

    private static string Canonical(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

    private static string PlanAudit(ProductionPlan plan) => JsonSerializer.Serialize(new
    {
        plan.Id,
        plan.WorkCenterId,
        process = plan.Process.ToString(),
        status = plan.Status.ToString(),
        target = plan.Target is null ? null : new { basis = plan.Target.Basis.ToString(), kg = plan.Target.Mass.Value },
        inputs = plan.Inputs.Select(input => new { input.ParentRollId, kg = input.PlannedMass.Value }),
        plan.ApprovedByUserId,
        plan.ApprovedAtUtc,
    }, Json);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw Refused(ManufacturingStoreCodes.ConcurrentChange,
                "Aynı plan ya da emir başka bir yazmayla değişti; hiçbir şey kaydedilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Refused(ManufacturingStoreCodes.ConcurrentChange,
                "Kalıcı tekillik kuralı başka bir yazmanın lehine sonuçlandı; hiçbir şey kaydedilmedi.", exception);
        }
    }

    private void Audit(ManufacturingCommandMetadata metadata, Guid companyId, string entityType, Guid entityId,
        string eventType, string operation, long? versionBefore, long? versionAfter, string before, string after,
        string reason)
    {
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            eventType,
            entityType,
            entityId.ToString("D"),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            before,
            after,
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
    }

    private void RequireCompany(Guid companyId)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            throw Refused(ManufacturingStoreCodes.CompanyContextMismatch, "Kayıt aktif şirket bağlamının şirketine ait değil.");
        }
    }

    private Guid RequireResolvedCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw Refused(ManufacturingStoreCodes.CompanyContextMismatch, "Aktif şirket bağlamı olmadan üretim kaydı okunmaz.");
        }

        return companyContext.CompanyId;
    }

    private static void RequireMetadata(ManufacturingCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.ActorId) || string.IsNullOrWhiteSpace(metadata.PermissionCode)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel) || metadata.RoleCodes is null)
        {
            throw new ArgumentException("Üretim yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.", nameof(metadata));
        }

        if (metadata.ActorId.Trim().Length > ActorMaximumLength || metadata.CorrelationId.Trim().Length > ActorMaximumLength)
        {
            throw Refused(ManufacturingStoreCodes.ValueDoesNotFit, "Aktör ya da correlation değeri kayıt alanına sığmıyor.");
        }
    }

    private static void RequireFits(IEnumerable<Kilograms> masses)
    {
        // Not a production policy: the stored column is decimal(18,3). Kilograms already refuse a
        // fourth decimal (Ö2); a mass with more integer digits than the column holds is refused,
        // never cut by the database.
        if (masses.Any(mass => mass.Value >= KilogramColumnLimit))
        {
            throw Refused(ManufacturingStoreCodes.ValueDoesNotFit, "Kütle kayıt alanına sığmıyor; kesilmez, reddedilir.");
        }
    }

    private static ManufacturingStoreException Refused(string code, string message, Exception? inner = null) =>
        new ManufacturingStoreException(code, message, inner);
}
