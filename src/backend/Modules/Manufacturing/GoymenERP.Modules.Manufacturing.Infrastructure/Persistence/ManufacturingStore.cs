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
    TimeProvider timeProvider)
    : IManufacturingPlanStore, IManufacturingPlanListStore, IManufacturingPlanRevisionStore, IManufacturingOrderStore,
        IManufacturingCommandLog
{
    private const string PlanEntityType = "Manufacturing.ProductionPlan";
    private const string OrderEntityType = "Manufacturing.ProductionOrder";
    private const int ActorMaximumLength = 128;
    // The list page bound, as Sales' draft list (#257): a default the caller may keep and a maximum it cannot pass.
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    // The exclusive bound of a decimal(18,3) column (15 integer digits): every such column — the masses and the
    // plan's band widths alike — refuses a value at or above it before the save (FİNANS #342 INFO-3: a neutral name).
    private const decimal Decimal18Scale3Limit = 1_000_000_000_000_000m;

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

        RequireFits(plan.Inputs.Select(input => input.PlannedMass).Concat(plan.Target is { } t ? [t.Mass] : [])
            .Concat(plan.Spec?.WindingTargetKg is { } winding ? [winding] : []));
        RequireTechnicalSnapshotFits(plan.Inputs);
        // A band width is a decimal(18,3) millimetre: beyond the column it is refused, never cut.
        if (plan.Spec is { } widths && widths.Lines.Any(line => line.BandWidthMm >= Decimal18Scale3Limit))
        {
            throw Refused(ManufacturingStoreCodes.ValueDoesNotFit, "Bant eni kayıt sütununa sığmıyor.");
        }
        if (plan.CutToLengthSpec is { } cutSpec
            && cutSpec.Lines.Any(line => line.LengthMm >= Decimal18Scale3Limit))
        {
            throw Refused(ManufacturingStoreCodes.ValueDoesNotFit, "Kesim boyu kayıt sütununa sığmıyor.");
        }

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
            WindingTargetKg = plan.Spec?.WindingTargetKg?.Value,
            Version = plan.Version,
            PayloadHash = payloadHash,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        var sequence = 0;
        foreach (var input in plan.Inputs)
        {
            var row = new PlanInputRow
            {
                CompanyId = plan.CompanyId,
                PlanId = plan.Id,
                ParentRollId = input.ParentRollId,
                Sequence = ++sequence,
                PlannedKg = input.PlannedMass.Value,
                ExpectedLabelVersion = input.ExpectedLabelVersion,
            };
            CaptureTechnicalSnapshot(row, input.TechnicalSnapshot);
            CaptureCommercialContext(row, input.CommercialContext);
            dbContext.PlanInputs.Add(row);
        }

        // MFG-6: the slitting spec is plan content, stored in the same save.
        var lineSequence = 0;
        foreach (var line in plan.Spec?.Lines ?? Array.Empty<SlitLine>())
        {
            dbContext.PlanSlitLines.Add(new PlanSlitLineRow
            {
                CompanyId = plan.CompanyId,
                PlanId = plan.Id,
                Sequence = ++lineSequence,
                BandWidthMm = line.BandWidthMm,
                BandCount = line.Count,
            });
        }

        var cutOrdinal = 0;
        foreach (var line in plan.CutToLengthSpec?.Lines ?? Array.Empty<CutToLengthLine>())
        {
            dbContext.PlanCutLines.Add(new PlanCutLineRow
            {
                CompanyId = plan.CompanyId,
                PlanId = plan.Id,
                LineId = line.LineId,
                Ordinal = ++cutOrdinal,
                Sequence = line.Sequence,
                LengthMm = line.LengthMm,
                Count = line.Count,
            });
        }

        Audit(metadata, plan.CompanyId, PlanEntityType, plan.Id, "manufacturing.plan.created.v1", "Create",
            versionBefore: null, versionAfter: plan.Version, before: "{}", after: PlanAudit(plan), "Üretim planı kaydı");
        Complete(metadata, companyId: plan.CompanyId, plan);
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
        Complete(metadata, companyId, plan);
        await SaveAsync(cancellationToken);
        return plan;
    }

    public async Task<ProductionPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Plans.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == planId, cancellationToken);
        return row is null ? null : await RestorePlanAsync(row, cancellationToken);
    }

    /// <summary>
    /// One page of this company's plans from the plan rows and their planned inputs (MFG-PLAN-LIST), the order
    /// list's pattern: no plan is rebuilt and nothing is computed. The ordering is the stored createdAtUtc and
    /// id, so a page is stable while plans are added; an approval changes neither, so a plan never moves pages.
    /// </summary>
    public async Task<ManufacturingPlanPage> ListPlansAsync(int pageSize, string? cursor, PlanStatus? status,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize,
                $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır (varsayılan {DefaultPageSize}).");
        }

        var query = dbContext.Plans.AsNoTracking();
        if (status is { } requested)
        {
            if (!Enum.IsDefined(requested))
            {
                throw new ArgumentOutOfRangeException(nameof(status), requested, "Tanımsız plan durumu.");
            }

            var statusValue = (int)requested;
            query = query.Where(row => row.Status == statusValue);
        }

        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId, ListCursor.PlanOrdering);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc
                || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        if (page.Count == 0)
        {
            return new ManufacturingPlanPage([], null);
        }

        var ids = page.Select(row => row.Id).ToArray();
        var inputs = await dbContext.PlanInputs.AsNoTracking()
            .Where(input => ids.Contains(input.PlanId))
            .GroupBy(input => input.PlanId)
            .Select(group => new { PlanId = group.Key, Count = group.Count(), PlannedKg = group.Sum(input => input.PlannedKg) })
            .ToListAsync(cancellationToken);

        // MFG-PLAN-EDIT: a revised plan lists its CURRENT content — the revision its row's version counts — not the
        // content it was created with. A revision committed after the row was read is not this page's; the next read
        // shows it with its own version.
        var revised = page.Where(row => RevisionCountOf(row) > 0).Select(row => row.Id).ToArray();
        var revisionHeaders = revised.Length == 0
            ? []
            : await dbContext.PlanRevisions.AsNoTracking()
                .Where(revision => revised.Contains(revision.PlanId))
                .ToListAsync(cancellationToken);
        // The same grouped sum as the creation inputs above (an empty id set reads nothing).
        var revisionInputs = await dbContext.PlanRevisionInputs.AsNoTracking()
            .Where(input => revised.Contains(input.PlanId))
            .GroupBy(input => new { input.PlanId, input.RevisionNo })
            .Select(group => new
            {
                group.Key.PlanId,
                group.Key.RevisionNo,
                Count = group.Count(),
                PlannedKg = group.Sum(input => input.PlannedKg),
            })
            .ToListAsync(cancellationToken);

        var summaries = new List<ManufacturingPlanSummary>(page.Count);
        foreach (var row in page)
        {
            var revisionNo = RevisionCountOf(row);
            var (workCenterId, targetBasis, targetKg) = (row.WorkCenterId, row.TargetBasis, row.TargetKg);
            int count;
            decimal plannedKg;
            if (revisionNo == 0)
            {
                // A plan always has at least one planned input; without one the row is not summarised.
                var planned = inputs.SingleOrDefault(input => input.PlanId == row.Id)
                    ?? throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                        "Kayıtlı planın planlanan girdisi yok.");
                (count, plannedKg) = (planned.Count, planned.PlannedKg);
            }
            else
            {
                var header = revisionHeaders.SingleOrDefault(revision =>
                        revision.PlanId == row.Id && revision.RevisionNo == revisionNo)
                    ?? throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                        "Kayıtlı planın sürümünün saydığı revizyon yok.");
                var planned = revisionInputs.SingleOrDefault(input =>
                        input.PlanId == row.Id && input.RevisionNo == revisionNo)
                    ?? throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                        "Kayıtlı plan revizyonunun planlanan girdisi yok.");
                (workCenterId, targetBasis, targetKg) = (header.WorkCenterId, header.TargetBasis, header.TargetKg);
                (count, plannedKg) = (planned.Count, planned.PlannedKg);
            }

            summaries.Add(new ManufacturingPlanSummary(row.Id, row.CompanyId, workCenterId,
                (ManufacturingProcess)row.Process, (PlanStatus)row.Status, row.Version, count,
                new Kilograms(plannedKg), (TargetBasis?)targetBasis,
                targetKg is { } target ? new Kilograms(target) : null, row.CreatedAtUtc, row.ApprovedByUserId,
                row.ApprovedAtUtc));
        }

        var last = page.LastOrDefault();
        return new ManufacturingPlanPage(summaries,
            hasMore && last is not null
                ? ListCursor.Encode(companyId, last.CreatedAtUtc, last.Id, ListCursor.PlanOrdering)
                : null);
    }

    /// <summary>
    /// MFG-PLAN-EDIT: one Draft revision in one save — the revision and its complete inputs (append-only), the plan
    /// row's new version and update time, the audit event (<c>manufacturing.plan.revised.v1</c>, before and after, the
    /// user's reason) and the command record — or nothing. The domain decides on the plan rebuilt from storage (the
    /// version fence, only a Draft, no slitting spec, the process fixed, the input rules, a real change). Fail-closed
    /// beyond the domain: a plan that has opened an order has a downstream effect and is refused as not a draft, even
    /// against a stored record that says Draft. Two writers that read the same version cannot both commit: the plan
    /// row's concurrency token and the revision key refuse the loser (<see cref="ManufacturingStoreCodes.ConcurrentChange"/>).
    /// </summary>
    public async Task<ProductionPlan> RevisePlanAsync(Guid planId, long expectedVersion, Guid workCenterId,
        ManufacturingProcess workCenterProcess, IReadOnlyList<PlannedInput> inputs, ProductionTarget? target,
        string reason, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        await RevisePlanWithCutLinesAsync(planId, expectedVersion, workCenterId, workCenterProcess, inputs, target,
            cutToLengthSpec: null, reason, metadata, cancellationToken);

    public async Task<ProductionPlan> RevisePlanWithCutLinesAsync(Guid planId, long expectedVersion, Guid workCenterId,
        ManufacturingProcess workCenterProcess, IReadOnlyList<PlannedInput> inputs, ProductionTarget? target,
        CutToLengthSpec? cutToLengthSpec, string reason, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        ArgumentNullException.ThrowIfNull(inputs);
        var companyId = RequireResolvedCompany();
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason))
        {
            throw new ArgumentException("Plan düzenlemesi bir gerekçe ister.", nameof(reason));
        }

        if (trimmedReason.Length > ManufacturingStoreCodes.PlanRevisionReasonMaximumLength)
        {
            throw Refused(ManufacturingStoreCodes.ValueDoesNotFit, "Düzenleme gerekçesi kayıt alanına sığmıyor.");
        }

        if (inputs.Any(input => input?.ExpectedLabelVersion is null))
        {
            throw new ArgumentException("Düzenlenen planın her ana rulosu doğrulanmış etiket sürümünü taşır.", nameof(inputs));
        }

        RequireFits(inputs.Select(input => input.PlannedMass).Concat(target is { } declared ? [declared.Mass] : []));
        RequireTechnicalSnapshotFits(inputs);
        if (cutToLengthSpec is { } cutSpec && cutSpec.Lines.Any(line => line.LengthMm >= Decimal18Scale3Limit))
        {
            throw Refused(ManufacturingStoreCodes.ValueDoesNotFit, "Kesim boyu kayıt sütununa sığmıyor.");
        }

        var row = await dbContext.Plans.SingleOrDefaultAsync(candidate => candidate.Id == planId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.PlanNotFound, "Bu şirketin böyle bir planı yok.");
        var plan = await RestorePlanAsync(row, cancellationToken);
        var before = PlanAudit(plan);
        var versionBefore = plan.Version;

        // The domain decides: the version fence first, then "only a draft", the spec, the process, the inputs.
        plan.Revise(expectedVersion, workCenterId, workCenterProcess, inputs, target, cutToLengthSpec);

        if (await dbContext.Orders.AsNoTracking().AnyAsync(order => order.PlanId == planId, cancellationToken))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanNotDraft,
                "Emir açmış plan düzenlenmez; aşağı akış etkisi vardır.");
        }

        var now = timeProvider.GetUtcNow();
        var revisionNo = plan.RevisionCount;
        dbContext.PlanRevisions.Add(new PlanRevisionRow
        {
            CompanyId = companyId,
            PlanId = plan.Id,
            RevisionNo = revisionNo,
            WorkCenterId = plan.WorkCenterId,
            TargetBasis = plan.Target is null ? null : (int)plan.Target.Basis,
            TargetKg = plan.Target?.Mass.Value,
            Reason = trimmedReason,
            PlanVersion = plan.Version,
            RevisedBy = metadata.ActorId.Trim(),
            RevisedAtUtc = now,
            CorrelationId = metadata.CorrelationId.Trim(),
        });
        var sequence = 0;
        foreach (var input in plan.Inputs)
        {
            var inputRow = new PlanRevisionInputRow
            {
                CompanyId = companyId,
                PlanId = plan.Id,
                RevisionNo = revisionNo,
                ParentRollId = input.ParentRollId,
                Sequence = ++sequence,
                PlannedKg = input.PlannedMass.Value,
                ExpectedLabelVersion = input.ExpectedLabelVersion!.Value,
            };
            CaptureTechnicalSnapshot(inputRow, input.TechnicalSnapshot);
            CaptureCommercialContext(inputRow, input.CommercialContext);
            dbContext.PlanRevisionInputs.Add(inputRow);
        }

        var cutOrdinal = 0;
        foreach (var line in plan.CutToLengthSpec?.Lines ?? Array.Empty<CutToLengthLine>())
        {
            dbContext.PlanRevisionCutLines.Add(new PlanRevisionCutLineRow
            {
                CompanyId = companyId,
                PlanId = plan.Id,
                RevisionNo = revisionNo,
                LineId = line.LineId,
                Ordinal = ++cutOrdinal,
                Sequence = line.Sequence,
                LengthMm = line.LengthMm,
                Count = line.Count,
            });
        }

        row.Version = plan.Version;
        row.UpdatedAtUtc = now;
        Audit(metadata, companyId, PlanEntityType, plan.Id, "manufacturing.plan.revised.v1", "Update",
            versionBefore, plan.Version, before, PlanAudit(plan), trimmedReason);
        Complete(metadata, companyId, plan);
        await SaveAsync(cancellationToken);
        return plan;
    }

    /// <summary>
    /// MFG-PLAN-EDIT: the plan's history — revision 0 from the plan's own rows as created, then every revision its row's
    /// version counts, each with its complete inputs. Null when the company has no such plan.
    /// </summary>
    public async Task<IReadOnlyList<ManufacturingPlanRevision>?> ListPlanRevisionsAsync(Guid planId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Plans.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == planId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var created = await dbContext.PlanInputs.AsNoTracking()
            .Where(input => input.PlanId == row.Id)
            .OrderBy(input => input.Sequence)
            .ToListAsync(cancellationToken);
        var createdCutLines = await dbContext.PlanCutLines.AsNoTracking()
            .Where(line => line.PlanId == row.Id)
            .OrderBy(line => line.Ordinal)
            .ToListAsync(cancellationToken);
        var (headers, revisionInputs, revisionCutLines) = await ReadRevisionsAsync(row, cancellationToken);
        var history = new List<ManufacturingPlanRevision>(headers.Count + 1)
        {
            new ManufacturingPlanRevision(0, 1, row.WorkCenterId,
                [.. created.Select(input => new PlannedInput(input.ParentRollId, new Kilograms(input.PlannedKg),
                    input.ExpectedLabelVersion, RestoreTechnicalSnapshot(input), RestoreCommercialContext(input)))],
                Target(row.TargetBasis, row.TargetKg), Reason: null, RevisedBy: null, row.CreatedAtUtc,
                CutSpec(createdCutLines)),
        };
        history.AddRange(headers.Select(header => new ManufacturingPlanRevision(header.RevisionNo, header.PlanVersion,
            header.WorkCenterId,
            [.. revisionInputs.Where(input => input.RevisionNo == header.RevisionNo)
                .Select(input => new PlannedInput(input.ParentRollId, new Kilograms(input.PlannedKg),
                    input.ExpectedLabelVersion, RestoreTechnicalSnapshot(input), RestoreCommercialContext(input)))],
            Target(header.TargetBasis, header.TargetKg), header.Reason, header.RevisedBy, header.RevisedAtUtc,
            CutRevisionSpec(revisionCutLines.Where(line => line.RevisionNo == header.RevisionNo)))));
        return history;
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
            var inputRow = new OrderInputRow
            {
                CompanyId = order.CompanyId,
                OrderId = order.Id,
                ParentRollId = input.ParentRollId,
                Sequence = ++sequence,
                PlannedKg = input.PlannedMass.Value,
                ExpectedLabelVersion = input.ExpectedLabelVersion,
            };
            CaptureCommercialContext(inputRow, input.CommercialContext);
            dbContext.OrderInputs.Add(inputRow);
        }

        Audit(metadata, companyId, OrderEntityType, order.Id, "manufacturing.order.created.v1", "Create",
            versionBefore: null, versionAfter: order.Version, before: "{}",
            after: JsonSerializer.Serialize(new
            {
                order.Id,
                order.PlanId,
                order.WorkCenterId,
                rolls = order.PlannedInputs.Count,
                inputs = order.PlannedInputs.Select(input => new
                {
                    input.ParentRollId,
                    kg = input.PlannedMass.Value,
                    input.ExpectedLabelVersion,
                    commercial = input.CommercialContext,
                }),
            }, Json),
            "Üretim emri kaydı");
        Complete(metadata, companyId, order);
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
        // ADR-011 §6: the close's command record commits with the close itself and its audit event.
        Complete(metadata, companyId, order);
        await SaveAsync(cancellationToken);
    }

    public async Task CloseOrderWithPolicyAsync(Guid orderId, long expectedVersion, Guid closedByUserId,
        DateTimeOffset closedAt, ClosureTolerancePolicySnapshotV1 policy,
        ClosureExceptionApprovalV1? exceptionApproval,
        ManufacturingPersistenceAuthorityFenceV1 persistenceAuthority,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(persistenceAuthority);
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var row = await dbContext.Orders.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.OrderNotFound, "Bu şirketin böyle bir emri yok.");
        var order = await RestoreOrderAsync(row, cancellationToken);
        var versionBefore = order.Version;

        // The application-level checks protect the reads. This final fresh fence runs after all stored facts have
        // been rebuilt and before the domain object or EF change tracker is changed, leaving no order, decision,
        // audit or command effect when either the captured context or a required permission has gone away.
        if (!await persistenceAuthority.RevalidateAsync(companyId, closedByUserId, cancellationToken))
        {
            throw Refused(ManufacturingStoreCodes.AuthorityDenied,
                "Üretim emri kapanış yetkisi kalıcılık sınırında doğrulanamadı; hiçbir şey kaydedilmedi.");
        }

        order.CloseWithPolicy(expectedVersion, closedByUserId, closedAt, policy, exceptionApproval);

        dbContext.OrderClosureDecisions.Add(new OrderClosureDecisionRow
        {
            CompanyId = companyId,
            OrderId = order.Id,
            OrderVersion = versionBefore,
            PolicyId = policy.PolicyId,
            PolicyVersion = policy.PolicyVersion,
            Process = (int)policy.Process,
            MaximumFirePercent = Canonical(policy.MaximumFirePercent),
            IsActive = policy.IsActive,
            EffectiveFrom = policy.EffectiveFrom,
            EffectiveThrough = policy.EffectiveThrough,
            SelectedForDate = policy.SelectedForDate,
            ApprovedByUserId = exceptionApproval?.ApprovedByUserId,
            ApprovalReason = exceptionApproval?.Reason,
            ApprovalOrderVersion = exceptionApproval?.OrderVersion,
            ApprovalPolicyId = exceptionApproval?.PolicyId,
            ApprovalPolicyVersion = exceptionApproval?.PolicyVersion,
            ApprovedAtUtc = exceptionApproval?.ApprovedAtUtc,
        });

        row.Status = (int)order.Status;
        row.ClosedByUserId = order.ClosedByUserId;
        row.ClosedAtUtc = order.ClosedAtUtc;
        row.Version = order.Version;
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        Audit(metadata, companyId, OrderEntityType, order.Id, "manufacturing.order.closed.v2", "CloseWithPolicy",
            versionBefore, order.Version, before: JsonSerializer.Serialize(new { status = "Open" }, Json),
            after: JsonSerializer.Serialize(new
            {
                status = "Closed",
                tolerance = new
                {
                    policy.PolicyId,
                    policy.PolicyVersion,
                    process = policy.Process.ToString(),
                    maximumFirePercent = Canonical(policy.MaximumFirePercent),
                    policy.IsActive,
                    policy.EffectiveFrom,
                    policy.EffectiveThrough,
                    policy.SelectedForDate,
                },
                exceptionApproval = exceptionApproval is null ? null : new
                {
                    exceptionApproval.ApprovedByUserId,
                    exceptionApproval.Reason,
                    exceptionApproval.OrderVersion,
                    exceptionApproval.PolicyId,
                    exceptionApproval.PolicyVersion,
                    exceptionApproval.ApprovedAtUtc,
                },
                balances = order.PlannedInputs.Select(input => order.BalanceOf(input.ParentRollId)).Select(balance => new
                {
                    balance.ParentRollId,
                    issued = balance.Issued.Value,
                    returned = balance.Returned.Value,
                    fire = balance.OutputOf(OutputClass.Scrap).Value
                        + balance.OutputOf(OutputClass.ProcessLoss).Value,
                    netIssued = balance.NetIssued.Value,
                    workInProgress = balance.WorkInProgress.Value,
                    unexplained = balance.Unexplained,
                }),
            }, Json),
            exceptionApproval?.Reason ?? "Üretim emri tolerans profiliyle kapanışı");
        Complete(metadata, companyId, order);
        await SaveAsync(cancellationToken);
    }

    /// <summary>
    /// One page of this company's orders from the order rows and their input snapshot: no fact is
    /// read and no balance is computed (a balance is the order read's answer, roll by roll). The
    /// ordering is the stored createdAtUtc and id, so a page is stable while orders are added.
    /// </summary>
    public async Task<ManufacturingOrderPage> ListOrdersAsync(int pageSize, string? cursor, OrderStatus? status,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize,
                $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır (varsayılan {DefaultPageSize}).");
        }

        var query = dbContext.Orders.AsNoTracking();
        if (status is { } requested)
        {
            if (!Enum.IsDefined(requested))
            {
                throw new ArgumentOutOfRangeException(nameof(status), requested, "Tanımsız emir durumu.");
            }

            var statusValue = (int)requested;
            query = query.Where(row => row.Status == statusValue);
        }

        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc
                || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        if (page.Count == 0)
        {
            return new ManufacturingOrderPage([], null);
        }

        var ids = page.Select(row => row.Id).ToArray();
        var inputs = await dbContext.OrderInputs.AsNoTracking()
            .Where(input => ids.Contains(input.OrderId))
            .GroupBy(input => input.OrderId)
            .Select(group => new { OrderId = group.Key, Count = group.Count(), PlannedKg = group.Sum(input => input.PlannedKg) })
            .ToListAsync(cancellationToken);

        var summaries = new List<ManufacturingOrderSummary>(page.Count);
        foreach (var row in page)
        {
            var snapshot = inputs.SingleOrDefault(input => input.OrderId == row.Id);
            if (snapshot is null)
            {
                // An order always has its own input snapshot; without one the row is not summarised.
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı emrin planlanan girdi anlık görüntüsü yok.");
            }

            summaries.Add(new ManufacturingOrderSummary(row.Id, row.CompanyId, row.PlanId, row.WorkCenterId,
                (ManufacturingProcess)row.Process, (OrderStatus)row.Status, row.Version, snapshot.Count,
                new Kilograms(snapshot.PlannedKg), (TargetBasis?)row.TargetBasis,
                row.TargetKg is { } targetKg ? new Kilograms(targetKg) : null, row.CreatedAtUtc, row.ClosedByUserId,
                row.ClosedAtUtc));
        }

        var last = page.LastOrDefault();
        return new ManufacturingOrderPage(summaries,
            hasMore && last is not null ? ListCursor.Encode(companyId, last.CreatedAtUtc, last.Id) : null);
    }

    public async Task<ProductionOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        // A read that meets a write committing between the order row and its facts reads again.
        for (var attempt = 1; ; attempt++)
        {
            var row = await dbContext.Orders.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
            if (row is null)
            {
                return null;
            }

            try
            {
                return await RestoreOrderAsync(row, cancellationToken);
            }
            catch (ManufacturingStoreException exception)
                when (exception.Code == ManufacturingStoreCodes.ConcurrentChange && attempt < 3)
            {
            }
        }
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
        Complete(metadata, companyId, order);
        await SaveAsync(cancellationToken);
        return ManufacturingWriteOutcome.Recorded;
    }

    public async Task<ManufacturingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Commands.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.CommandName == commandName && candidate.IdempotencyKeyHash == idempotencyKeyHash,
            cancellationToken);
        return row is null
            ? null
            : new ManufacturingCommandRecord(row.CommandName, row.IdempotencyKeyHash, row.ActorId, row.RequestHash,
                row.ResultJson, row.OriginalCorrelationId);
    }

    public async Task RecordCommandAsync(ManufacturingCommandRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        AddCommand(RequireResolvedCompany(), record);
        await SaveAsync(cancellationToken);
    }

    /// <summary>
    /// ADR-011 §6: the completed command's record joins the business change and its audit event in
    /// the same save. Without a completion (a caller outside the HTTP surface) nothing is added.
    /// </summary>
    private void Complete(ManufacturingCommandMetadata metadata, Guid companyId, object result)
    {
        if (metadata.Completion is { } completion)
        {
            AddCommand(companyId, completion(result));
        }
    }

    private void AddCommand(Guid companyId, ManufacturingCommandRecord record) =>
        dbContext.Commands.Add(new CommandRow
        {
            CompanyId = companyId,
            CommandName = record.CommandName,
            IdempotencyKeyHash = record.IdempotencyKeyHash,
            ActorId = record.ActorId,
            RequestHash = record.RequestHash,
            ResultJson = record.ResultJson,
            OriginalCorrelationId = record.OriginalCorrelationId,
            RecordedAtUtc = timeProvider.GetUtcNow(),
        });

    private async Task<ProductionPlan> RestorePlanAsync(PlanRow row, CancellationToken cancellationToken)
    {
        var inputs = await dbContext.PlanInputs.AsNoTracking()
            .Where(input => input.PlanId == row.Id)
            .OrderBy(input => input.Sequence)
            .ToListAsync(cancellationToken);
        var lines = await dbContext.PlanSlitLines.AsNoTracking()
            .Where(line => line.PlanId == row.Id)
            .OrderBy(line => line.Sequence)
            .ToListAsync(cancellationToken);
        var cutLines = await dbContext.PlanCutLines.AsNoTracking()
            .Where(line => line.PlanId == row.Id)
            .OrderBy(line => line.Ordinal)
            .ToListAsync(cancellationToken);
        var (headers, revisionInputs, revisionCutLines) = await ReadRevisionsAsync(row, cancellationToken);
        // MFG-PLAN-EDIT: the revisions are replayed through the domain's own rule, in their order, before the approval.
        return ProductionPlan.Restore(row.Id, row.CompanyId, row.WorkCenterId, (ManufacturingProcess)row.Process,
            inputs.Select(input => new PlannedInput(input.ParentRollId, new Kilograms(input.PlannedKg),
                input.ExpectedLabelVersion, RestoreTechnicalSnapshot(input), RestoreCommercialContext(input))),
            Target(row.TargetBasis, row.TargetKg), row.ApprovedByUserId, row.ApprovedAtUtc, row.Version,
            Spec(lines, row.WindingTargetKg),
            headers.Select(header => new PlanRevisionContent(header.WorkCenterId,
                [.. revisionInputs.Where(input => input.RevisionNo == header.RevisionNo)
                    .Select(input => new PlannedInput(input.ParentRollId, new Kilograms(input.PlannedKg),
                        input.ExpectedLabelVersion, RestoreTechnicalSnapshot(input), RestoreCommercialContext(input)))],
                Target(header.TargetBasis, header.TargetKg),
                CutRevisionSpec(revisionCutLines.Where(line => line.RevisionNo == header.RevisionNo)))),
            CutSpec(cutLines));
    }

    /// <summary>
    /// MFG-PLAN-EDIT: how many revisions the plan row's version counts — one step for the creation, one per revision
    /// and one for the approval. Read under RCSI, the row fixes the snapshot: a revision committed after it (with its
    /// own version step) is not this read's, so a plan never pairs one version with another version's content.
    /// </summary>
    private static int RevisionCountOf(PlanRow row) =>
        (int)Math.Max(0L, row.Version - 1 - (row.Status == (int)PlanStatus.Approved ? 1 : 0));

    /// <summary>
    /// The revisions the row's version counts, with their complete inputs, oldest first. Numbered 1..n without a gap,
    /// or the stored plan is refused, never repaired.
    /// </summary>
    private async Task<(IReadOnlyList<PlanRevisionRow> Headers, IReadOnlyList<PlanRevisionInputRow> Inputs,
        IReadOnlyList<PlanRevisionCutLineRow> CutLines)> ReadRevisionsAsync(
        PlanRow row, CancellationToken cancellationToken)
    {
        var count = RevisionCountOf(row);
        if (count == 0)
        {
            return ([], [], []);
        }

        var headers = await dbContext.PlanRevisions.AsNoTracking()
            .Where(revision => revision.PlanId == row.Id && revision.RevisionNo <= count)
            .OrderBy(revision => revision.RevisionNo)
            .ToListAsync(cancellationToken);
        if (!headers.Select(header => header.RevisionNo).SequenceEqual(Enumerable.Range(1, count)))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı planın sürümünün saydığı revizyonlar eksiksiz değil.");
        }

        var inputs = await dbContext.PlanRevisionInputs.AsNoTracking()
            .Where(input => input.PlanId == row.Id && input.RevisionNo <= count)
            .OrderBy(input => input.RevisionNo).ThenBy(input => input.Sequence)
            .ToListAsync(cancellationToken);
        var cutLines = await dbContext.PlanRevisionCutLines.AsNoTracking()
            .Where(line => line.PlanId == row.Id && line.RevisionNo <= count)
            .OrderBy(line => line.RevisionNo).ThenBy(line => line.Ordinal)
            .ToListAsync(cancellationToken);
        return (headers, inputs, cutLines);
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
        var closureDecision = await dbContext.OrderClosureDecisions.AsNoTracking()
            .SingleOrDefaultAsync(decision => decision.OrderId == row.Id, cancellationToken);
        IEnumerable<FactRow> Of(FactKind kind) => facts.Where(fact => fact.Kind == (int)kind);

        try
        {
            return RestoreOrder(row, inputs, Of, closureDecision);
        }
        catch (ManufacturingRuleException exception) when (exception.Code == ManufacturingRuleCodes.StoredRecordInvalid)
        {
            // Under read-committed snapshot each statement reads its own committed state: a writer
            // that commits between the order row and its facts leaves facts newer than the row's
            // version. That is a concurrent change, not a broken record; only an unchanged order
            // that still fails to rebuild is invalid.
            var current = await dbContext.Orders.AsNoTracking()
                .Where(candidate => candidate.Id == row.Id)
                .Select(candidate => (long?)candidate.Version)
                .SingleOrDefaultAsync(cancellationToken);
            if (current != row.Version)
            {
                throw Refused(ManufacturingStoreCodes.ConcurrentChange,
                    "Emir okunurken başka bir yazma kaydedildi; hiçbir şey kaydedilmedi.", exception);
            }

            throw;
        }
    }

    private static ProductionOrder RestoreOrder(OrderRow row, IReadOnlyList<OrderInputRow> inputs,
        Func<FactKind, IEnumerable<FactRow>> Of, OrderClosureDecisionRow? closureDecision)
    {
        var (policy, approval) = RestoreClosureDecision(closureDecision);
        return
        ProductionOrder.Restore(
            row.Id, row.PlanId, row.CompanyId, row.WorkCenterId, (ManufacturingProcess)row.Process,
            Target(row.TargetBasis, row.TargetKg),
            inputs.Select(input => new PlannedInput(input.ParentRollId, new Kilograms(input.PlannedKg),
                input.ExpectedLabelVersion, technicalSnapshot: null,
                commercialContext: RestoreCommercialContext(input))),
            Of(FactKind.InputIssue).Select(fact => new InputIssue(fact.Id, fact.ParentRollId, new Kilograms(fact.MassKg),
                fact.OccurredAtUtc)),
            Of(FactKind.InputReturn).Select(fact => new InputReturn(fact.Id, fact.ParentRollId, new Kilograms(fact.MassKg),
                fact.OccurredAtUtc)),
            Of(FactKind.OutputDeclaration).Select(fact => new OutputDeclaration(fact.Id, fact.ParentRollId,
                (OutputClass)(fact.OutputClass ?? 0), new Kilograms(fact.MassKg), fact.OccurredAtUtc, Cost(fact.CostStatus))),
            Of(FactKind.WipMeasurement).Select(fact => new WipMeasurement(fact.Id, fact.ParentRollId, new Kilograms(fact.MassKg),
                fact.OccurredAtUtc)),
            row.ClosedByUserId, row.ClosedAtUtc, row.Version, policy, approval);
    }

    private static (ClosureTolerancePolicySnapshotV1? Policy, ClosureExceptionApprovalV1? Approval)
        RestoreClosureDecision(OrderClosureDecisionRow? row)
    {
        if (row is null)
        {
            return (null, null);
        }

        try
        {
            if (!decimal.TryParse(row.MaximumFirePercent, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var maximumFirePercent))
            {
                throw new ArgumentException("Kayıtlı tolerans yüzdesi decimal değildir.");
            }

            var policy = new ClosureTolerancePolicySnapshotV1(
                row.CompanyId, row.PolicyId, row.PolicyVersion, (ManufacturingProcess)row.Process,
                maximumFirePercent, row.IsActive, row.EffectiveFrom, row.EffectiveThrough, row.SelectedForDate);
            var approval = row.ApprovedByUserId is not Guid approvedBy
                ? null
                : ClosureExceptionApprovalV1.Create(
                    row.CompanyId, row.OrderId, approvedBy, row.ApprovalReason!,
                    row.ApprovalOrderVersion ?? 0, row.ApprovalPolicyId ?? Guid.Empty,
                    row.ApprovalPolicyVersion ?? 0, row.ApprovedAtUtc ?? default);
            return (policy, approval);
        }
        catch (ArgumentException)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı kapanış kararı geçersiz.");
        }
    }

    /// <summary>
    /// A plan has a slitting spec exactly when it has slit lines; a winding target without lines is a
    /// stored record the domain cannot have produced, and is refused, not repaired.
    /// </summary>
    private static SlittingSpec? Spec(IReadOnlyList<PlanSlitLineRow> lines, decimal? windingTargetKg)
    {
        if (lines.Count == 0)
        {
            return windingTargetKg is null
                ? null
                : throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı sarım hedefi dilme satırı olmadan var olamaz.");
        }

        return new SlittingSpec(lines.Select(line => new SlitLine(line.BandWidthMm, line.BandCount)),
            windingTargetKg is { } target ? new Kilograms(target) : null);
    }

    private static CutToLengthSpec? CutSpec(IReadOnlyList<PlanCutLineRow> lines) => lines.Count == 0
        ? null
        : new CutToLengthSpec(lines.Select(line =>
            new CutToLengthLine(line.LineId, line.Sequence, line.LengthMm, line.Count)));

    private static CutToLengthSpec? CutRevisionSpec(IEnumerable<PlanRevisionCutLineRow> lines)
    {
        var materialized = lines.ToArray();
        return materialized.Length == 0
            ? null
            : new CutToLengthSpec(materialized.Select(line =>
                new CutToLengthLine(line.LineId, line.Sequence, line.LengthMm, line.Count)));
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
            builder.Append(input.ParentRollId.ToString("D")).Append(':').Append(Canonical(input.PlannedMass.Value));
            if (input.ExpectedLabelVersion is { } expectedLabelVersion)
            {
                builder.Append('@').Append(expectedLabelVersion.ToString(CultureInfo.InvariantCulture));
            }

            if (input.TechnicalSnapshot is { } technical)
            {
                // A versioned extension: legacy inputs append nothing, preserving their exact historical hash bytes.
                builder.Append("#technical.v1|");
                AppendToken(builder, technical.ProductId.ToString("D"));
                AppendToken(builder, technical.ProductVersion);
                AppendToken(builder, technical.FamilyId.ToString("D"));
                AppendToken(builder, technical.FamilyCode);
                AppendToken(builder, technical.FamilySourceRevision);
                AppendToken(builder, technical.TemplateId.ToString("D"));
                AppendToken(builder, technical.TemplateVersion);
                AppendToken(builder, technical.TemplateCode);
                AppendToken(builder, technical.TemplateSourceRevision);
                AppendToken(builder, technical.KilogramUnitCompanyId.ToString("D"));
                AppendToken(builder, technical.KilogramUnitId.ToString("D"));
                AppendToken(builder, technical.KilogramUnitCode);
                AppendToken(builder, technical.KilogramProfileId.ToString("D"));
                AppendToken(builder, technical.KilogramProfileVersion);
                AppendToken(builder, technical.EffectiveOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                AppendToken(builder, Canonical(technical.ThicknessMm));
                AppendToken(builder, Canonical(technical.WidthMm));
            }

            if (input.CommercialContext is { } commercial)
            {
                // A versioned extension: legacy/company-owned inputs append nothing, preserving their exact hashes.
                builder.Append("#commercial.v1|");
                AppendToken(builder, commercial.PartyId.ToString("D"));
                AppendToken(builder, commercial.CompanyPartyAccountId.ToString("D"));
                AppendToken(builder, commercial.PartyVersion);
                AppendToken(builder, commercial.AccountVersion);
            }

            builder.Append(';');
        }

        // MFG-6: only a plan WITH a slitting spec extends the canonical content, so every plan stored before
        // it keeps its hash (and its replay identity) unchanged.
        if (plan.Spec is { } spec)
        {
            builder.Append("|slitting.v1|")
                .Append(spec.WindingTargetKg is { } winding ? Canonical(winding.Value) : "-").Append('|');
            foreach (var line in spec.Lines)
            {
                builder.Append(Canonical(line.BandWidthMm)).Append('x')
                    .Append(line.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
            }
        }


        // Only plans carrying cut lines extend the historical hash bytes. Explicit sequence and list order are content.
        if (plan.CutToLengthSpec is { } cutSpec)
        {
            builder.Append("|cut-to-length.v1|");
            foreach (var line in cutSpec.Lines)
            {
                builder.Append(line.LineId.ToString("D")).Append(':')
                    .Append(line.Sequence.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(Canonical(line.LengthMm)).Append('x')
                    .Append(line.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
            }
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

    private static void AppendToken(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');

    private static void CaptureTechnicalSnapshot(IPlanTechnicalSnapshotRow row,
        PlanTechnicalParameterSnapshotV1? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        row.TechnicalProductId = snapshot.ProductId;
        row.TechnicalProductVersion = snapshot.ProductVersion;
        row.TechnicalFamilyId = snapshot.FamilyId;
        row.TechnicalFamilyCode = snapshot.FamilyCode;
        row.TechnicalFamilySourceRevision = snapshot.FamilySourceRevision;
        row.TechnicalTemplateId = snapshot.TemplateId;
        row.TechnicalTemplateVersion = snapshot.TemplateVersion;
        row.TechnicalTemplateCode = snapshot.TemplateCode;
        row.TechnicalTemplateSourceRevision = snapshot.TemplateSourceRevision;
        row.TechnicalKilogramUnitCompanyId = snapshot.KilogramUnitCompanyId;
        row.TechnicalKilogramUnitId = snapshot.KilogramUnitId;
        row.TechnicalKilogramUnitCode = snapshot.KilogramUnitCode;
        row.TechnicalKilogramProfileId = snapshot.KilogramProfileId;
        row.TechnicalKilogramProfileVersion = snapshot.KilogramProfileVersion;
        row.TechnicalEffectiveOn = snapshot.EffectiveOn;
        row.TechnicalThicknessMm = snapshot.ThicknessMm;
        row.TechnicalWidthMm = snapshot.WidthMm;
        row.TechnicalLengthMm = snapshot.LengthMm;
    }

    private static PlanTechnicalParameterSnapshotV1? RestoreTechnicalSnapshot(IPlanTechnicalSnapshotRow row)
    {
        var values = new object?[]
        {
            row.TechnicalProductId, row.TechnicalProductVersion, row.TechnicalFamilyId, row.TechnicalFamilyCode,
            row.TechnicalFamilySourceRevision, row.TechnicalTemplateId, row.TechnicalTemplateVersion,
            row.TechnicalTemplateCode, row.TechnicalTemplateSourceRevision, row.TechnicalKilogramUnitCompanyId,
            row.TechnicalKilogramUnitId, row.TechnicalKilogramUnitCode, row.TechnicalKilogramProfileId,
            row.TechnicalKilogramProfileVersion, row.TechnicalEffectiveOn, row.TechnicalThicknessMm,
            row.TechnicalWidthMm,
        };
        if (values.All(value => value is null) && row.TechnicalLengthMm is null)
        {
            return null;
        }

        if (values.Any(value => value is null))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı plan girdisinin teknik snapshot'ı eksik.");
        }

        try
        {
            return new PlanTechnicalParameterSnapshotV1(
                row.TechnicalProductId!.Value,
                row.TechnicalProductVersion!,
                row.TechnicalFamilyId!.Value,
                row.TechnicalFamilyCode!,
                row.TechnicalFamilySourceRevision!,
                row.TechnicalTemplateId!.Value,
                row.TechnicalTemplateVersion!,
                row.TechnicalTemplateCode!,
                row.TechnicalTemplateSourceRevision!,
                row.TechnicalKilogramUnitCompanyId!.Value,
                row.TechnicalKilogramUnitId!.Value,
                row.TechnicalKilogramUnitCode!,
                row.TechnicalKilogramProfileId!.Value,
                row.TechnicalKilogramProfileVersion!,
                row.TechnicalEffectiveOn!.Value,
                row.TechnicalThicknessMm!.Value,
                row.TechnicalWidthMm!.Value,
                row.TechnicalLengthMm);
        }
        catch (ArgumentException exception)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                $"Kayıtlı plan girdisinin teknik snapshot'ı geçersiz ({exception.ParamName}).");
        }
    }

    private static void CaptureCommercialContext(IPlanCommercialContextRow row,
        PlanCommercialContextSnapshotV1? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        row.CommercialPartyId = snapshot.PartyId;
        row.CommercialCompanyPartyAccountId = snapshot.CompanyPartyAccountId;
        row.CommercialPartyVersion = snapshot.PartyVersion;
        row.CommercialAccountVersion = snapshot.AccountVersion;
    }

    private static PlanCommercialContextSnapshotV1? RestoreCommercialContext(IPlanCommercialContextRow row)
    {
        var values = new object?[]
        {
            row.CommercialPartyId,
            row.CommercialCompanyPartyAccountId,
            row.CommercialPartyVersion,
            row.CommercialAccountVersion,
        };
        if (values.All(value => value is null))
        {
            return null;
        }

        if (values.Any(value => value is null))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı üretim girdisinin ticari bağlamı eksik.");
        }

        try
        {
            return new PlanCommercialContextSnapshotV1(
                row.CommercialPartyId!.Value,
                row.CommercialCompanyPartyAccountId!.Value,
                row.CommercialPartyVersion!,
                row.CommercialAccountVersion!);
        }
        catch (ArgumentException exception)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                $"Kayıtlı üretim girdisinin ticari bağlamı geçersiz ({exception.ParamName}).");
        }
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

    private static string PlanAudit(ProductionPlan plan) => plan.Spec is { } spec
        // MFG-6: a plan with a slitting spec carries it in its audit; a plan without one keeps the MFG-5 shape.
        ? JsonSerializer.Serialize(new
        {
            plan.Id,
            plan.WorkCenterId,
            process = plan.Process.ToString(),
            status = plan.Status.ToString(),
            target = plan.Target is null ? null : new { basis = plan.Target.Basis.ToString(), kg = plan.Target.Mass.Value },
            inputs = plan.Inputs.Select(input => new
            {
                input.ParentRollId,
                kg = input.PlannedMass.Value,
                input.ExpectedLabelVersion,
                technical = input.TechnicalSnapshot,
                commercial = input.CommercialContext,
            }),
            slitting = new
            {
                lines = spec.Lines.Select(line => new { bandWidthMm = line.BandWidthMm, count = line.Count }),
                windingTargetKg = spec.WindingTargetKg?.Value,
            },
            plan.ApprovedByUserId,
            plan.ApprovedAtUtc,
        }, Json)
        : plan.CutToLengthSpec is { } cutSpec
        ? JsonSerializer.Serialize(new
        {
            plan.Id,
            plan.WorkCenterId,
            process = plan.Process.ToString(),
            status = plan.Status.ToString(),
            target = plan.Target is null ? null : new { basis = plan.Target.Basis.ToString(), kg = plan.Target.Mass.Value },
            inputs = plan.Inputs.Select(input => new
            {
                input.ParentRollId,
                kg = input.PlannedMass.Value,
                input.ExpectedLabelVersion,
                technical = input.TechnicalSnapshot,
                commercial = input.CommercialContext,
            }),
            cutToLength = new
            {
                lines = cutSpec.Lines.Select(line => new
                    { line.LineId, line.Sequence, line.LengthMm, line.Count }),
            },
            plan.ApprovedByUserId,
            plan.ApprovedAtUtc,
        }, Json)
        : JsonSerializer.Serialize(new
    {
        plan.Id,
        plan.WorkCenterId,
        process = plan.Process.ToString(),
        status = plan.Status.ToString(),
        target = plan.Target is null ? null : new { basis = plan.Target.Basis.ToString(), kg = plan.Target.Mass.Value },
        inputs = plan.Inputs.Select(input => new
        {
            input.ParentRollId,
            kg = input.PlannedMass.Value,
            input.ExpectedLabelVersion,
            technical = input.TechnicalSnapshot,
            commercial = input.CommercialContext,
        }),
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
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql)
        {
            // A unique key lost to another writer, or a plan naming a work center that is not this
            // company's (the composite foreign key, 547). Anything else stays the database's error.
            var code = sql.Number switch
            {
                2601 or 2627 => ManufacturingStoreCodes.ConcurrentChange,
                547 when sql.Message.Contains("FK_Plans_WorkCenters_CompanyId_WorkCenterId", StringComparison.Ordinal)
                    || sql.Message.Contains("FK_PlanRevisions_WorkCenters_CompanyId_WorkCenterId", StringComparison.Ordinal)
                    => ManufacturingStoreCodes.WorkCenterNotFound,
                _ => null,
            };
            if (code is null)
            {
                throw;
            }

            throw Refused(code, code == ManufacturingStoreCodes.WorkCenterNotFound
                ? "Plan bu şirketin bir iş merkezine bağlı değil; hiçbir şey kaydedilmedi."
                : "Kalıcı tekillik kuralı başka bir yazmanın lehine sonuçlandı; hiçbir şey kaydedilmedi.", exception);
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
        if (masses.Any(mass => mass.Value >= Decimal18Scale3Limit))
        {
            throw Refused(ManufacturingStoreCodes.ValueDoesNotFit, "Kütle kayıt alanına sığmıyor; kesilmez, reddedilir.");
        }
    }

    private static void RequireTechnicalSnapshotFits(IEnumerable<PlannedInput> inputs)
    {
        // Persistence capacity, not a production tolerance: both immutable dimensions use
        // decimal(18,3). Refuse before any read or tracked mutation, and never truncate.
        if (inputs.Select(input => input.TechnicalSnapshot)
            .Where(snapshot => snapshot is not null)
            .Any(snapshot => snapshot!.ThicknessMm >= Decimal18Scale3Limit
                || snapshot.WidthMm >= Decimal18Scale3Limit))
        {
            throw Refused(ManufacturingStoreCodes.ValueDoesNotFit,
                "Teknik ölçü kayıt alanına sığmıyor; kesilmez, reddedilir.");
        }
    }

    private static ManufacturingStoreException Refused(string code, string message, Exception? inner = null) =>
        new ManufacturingStoreException(code, message, inner);

    /// <summary>
    /// The opaque order-list cursor, as Sales' draft cursor (#257): the last row's ordering key,
    /// bound to the company that read it and to this ordering. A malformed cursor, another company's
    /// or another ordering's is <see cref="ManufacturingStoreCodes.ListCursorInvalid"/> — never a
    /// silent restart at the first page.
    /// </summary>
    internal static class ListCursor
    {
        /// <summary>The order list's ordering, unchanged since MFG-ORD: every order cursor already issued stays valid.</summary>
        internal const string Ordering = "createdAtUtc desc, id desc";

        /// <summary>
        /// The plan list's ordering (MFG-PLAN-LIST). The same keys over another table, so it is named apart: a plan
        /// cursor is refused by the order list and an order cursor by the plan list, never read as the other's page.
        /// </summary>
        internal const string PlanOrdering = "plans: createdAtUtc desc, id desc";

        public static string Encode(Guid companyId, DateTimeOffset createdAtUtc, Guid id) =>
            Encode(companyId, createdAtUtc, id, Ordering);

        public static string Encode(Guid companyId, DateTimeOffset createdAtUtc, Guid id, string ordering) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new Payload(companyId, ordering, createdAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), id), Json)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static (DateTimeOffset CreatedAtUtc, Guid Id) Decode(string cursor, Guid companyId) =>
            Decode(cursor, companyId, Ordering);

        public static (DateTimeOffset CreatedAtUtc, Guid Id) Decode(string cursor, Guid companyId, string ordering)
        {
            Payload? payload;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
                payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)), Json);
            }
            catch (Exception exception) when (exception is FormatException or JsonException or DecoderFallbackException)
            {
                throw Refused(ManufacturingStoreCodes.ListCursorInvalid, "Liste imleci çözülemedi.", exception);
            }

            if (payload is null || payload.Company != companyId
                || !string.Equals(payload.Ordering, ordering, StringComparison.Ordinal) || payload.Id == Guid.Empty
                || !DateTimeOffset.TryParseExact(payload.CreatedAtUtc, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAtUtc))
            {
                throw Refused(ManufacturingStoreCodes.ListCursorInvalid,
                    "Liste imleci bu şirketin ve bu sıralamanın imleci değil.");
            }

            return (createdAtUtc, payload.Id);
        }

        private sealed record Payload(Guid Company, string Ordering, string CreatedAtUtc, Guid Id);
    }
}
