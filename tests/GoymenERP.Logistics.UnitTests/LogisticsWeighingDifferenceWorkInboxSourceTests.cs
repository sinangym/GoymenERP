using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using Microsoft.Extensions.DependencyInjection;
using F = GoymenERP.Logistics.UnitTests.LogisticsWorkInboxTestFixture;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the weighing-difference Work inbox source lists only PendingApproval records of the
/// context company, oldest recording first, the recorder's own included; its rule numbers are the record's stored evaluation
/// (rate, note/scale kg, uncovered excess). SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsWeighingDifferenceWorkInboxSourceTests
{
    [Fact]
    public void The_source_declares_its_kind_the_approve_permission_and_the_module_registers_it()
    {
        var fixture = new F();
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        // WP1: the duration class is the Work rule table's (YOL-D18 default: hour scale), never the source's declaration.
        Assert.Equal(("logistics", "logistics.weighing-difference.v1", "logistics.weighing-differences.approve", false),
            (source.Module, source.Kind, source.ApproverPermission, ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.Equal(LogisticsPermissionManifestV1.WeighingDifferencesApprove, source.ApproverPermission);
        var services = new ServiceCollection().AddLogisticsModule(new LogisticsWorkInboxTestSettings());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkInboxSourceV1)
            && descriptor.ImplementationType == typeof(LogisticsWeighingDifferenceWorkInboxSourceV1));
    }

    [Fact]
    public async Task Only_pending_approval_records_are_listed_oldest_first_with_the_note_label_and_instruction_link()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        Note(fixture, F.Id(1), F.Id(11));
        Note(fixture, F.Id(1), F.Id(12), series: "IRS", sequence: 42);
        Note(fixture, F.Id(1), F.Id(13));
        Note(fixture, F.Id(1), F.Id(14));
        Record(fixture, F.Id(1), F.Id(11), F.Id(21), status: 2, minutes: 30);
        Record(fixture, F.Id(1), F.Id(12), F.Id(22), status: 2, minutes: 10);
        Record(fixture, F.Id(1), F.Id(13), F.Id(23), status: 1, minutes: 1);
        Record(fixture, F.Id(1), F.Id(14), F.Id(24), status: 3, minutes: 2);
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        F.AssertContractShape(result, "logistics", "logistics.weighing-difference.v1");
        Assert.Equal([F.Id(22), F.Id(21)], result.Items!.Select(item => item.ItemId));
        var link = "/lojistik/yukleme-talimatlari/" + F.Id(1).ToString("D");
        Assert.Equal(("IRS-000042", F.Id(11).ToString("D"), link, F.T0.AddMinutes(10).ToUniversalTime()),
            (result.Items![0].DocumentLabel, result.Items![1].DocumentLabel, result.Items![0].Link, result.Items![0].WaitingSinceUtc));
        Assert.Equal(["dispatch-note", "loading-instruction"], result.Items![0].References.Select(reference => reference.Role));
        Assert.Equal(F.Id(12), result.Items![0].References[0].RecordId);
    }

    [Fact]
    public async Task The_rule_numbers_are_the_stored_rate_note_and_scale_kg_difference_and_uncovered_excess()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        Note(fixture, F.Id(1), F.Id(11));
        Note(fixture, F.Id(1), F.Id(12));
        Record(fixture, F.Id(1), F.Id(11), F.Id(21), status: 2, minutes: 1, theoretical: 1000m, scale: 1030.5m, difference: 30.5m,
            excess: 25m, rate: 2m);
        Record(fixture, F.Id(1), F.Id(12), F.Id(22), status: 2, minutes: 2, theoretical: 800m, scale: 790m, difference: -10m,
            excess: 4m, rate: null, enteredByComplete: false);
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal(
            [
                new WorkApprovalRuleResultV1("logistics.weighing-excess-rate", "2", "2.5", null, "%"),
                new WorkApprovalRuleResultV1("logistics.weighing-scale-difference", "1000", "1030.5", "30.5", "kg"),
                new WorkApprovalRuleResultV1("logistics.weighing-uncovered-excess", null, "25", null, "kg"),
            ],
            result.Items![0].Rules);
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1("logistics.weighing-excess-rate-unreadable", null, null, null, null),
                new WorkApprovalRuleResultV1("logistics.weighing-scale-difference", "800", "790", "-10", "kg"),
                new WorkApprovalRuleResultV1("logistics.weighing-uncovered-excess", null, "4", null, "kg"),
                new WorkApprovalRuleResultV1("logistics.weighing-entered-by-unknown", null, null, null, null),
            ],
            result.Items![1].Rules);
    }

    [Fact]
    public async Task Another_companys_record_is_never_listed()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        fixture.Instruction(F.Id(2), F.CompanyB);
        Note(fixture, F.Id(1), F.Id(11));
        Note(fixture, F.Id(2), F.Id(12), companyId: F.CompanyB);
        Record(fixture, F.Id(1), F.Id(11), F.Id(21), status: 2, minutes: 5);
        Record(fixture, F.Id(2), F.Id(12), F.Id(22), status: 2, minutes: 1, companyId: F.CompanyB);
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal([F.Id(21)], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_the_company_wide_approve_permission_or_with_an_unresolved_company_the_answer_is_denied()
    {
        var fixture = new F();
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows, fixture.Fence);
        fixture.Authority.Answer = (_, _) => PermissionScope.Denied;
        var denied = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        var unresolved = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows,
            new LogisticsWorkInboxFence(new GoymenERP.BuildingBlocks.Multitenancy.CompanyContext(), fixture.User, new LogisticsWorkInboxTestAuthority()));
        var noCompany = await unresolved.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.User.IsAuthenticated = false;
        fixture.Authority.Answer = (_, _) => LogisticsWorkInboxTestAuthority.CompanyWide;
        var anonymous = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.All(new[] { denied, noCompany, anonymous }, result => Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome));
        Assert.Equal(0, fixture.Rows.Reads);
    }

    [Fact]
    public async Task The_recorders_own_record_is_returned_for_the_recorder_since_work_hides_it_itself()
    {
        var fixture = new F();
        fixture.User.UserId = F.Maker;
        fixture.Instruction(F.Id(1));
        Note(fixture, F.Id(1), F.Id(11));
        Record(fixture, F.Id(1), F.Id(11), F.Id(21), status: 2, minutes: 5);
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(userId: F.Maker), 10, CancellationToken.None);

        Assert.Equal(F.Maker, Assert.Single(result.Items!).PreparedBy);
    }

    [Fact]
    public async Task Who_entered_the_weighing_or_a_shipment_is_the_owners_may_not_approve_set_beside_the_recorder()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        Note(fixture, F.Id(1), F.Id(11));
        Note(fixture, F.Id(1), F.Id(12));
        Record(fixture, F.Id(1), F.Id(11), F.Id(21), status: 2, minutes: 1);
        Record(fixture, F.Id(1), F.Id(12), F.Id(22), status: 2, minutes: 2);
        EnteredBy(fixture, F.Id(21), "SENTETIK-kantar", "SENTETIK-sevk", F.Maker, "SENTETIK-kantar");
        EnteredBy(fixture, F.Id(22), F.Maker);
        EnteredBy(fixture, F.Id(23), "SENTETIK-baska-kayit");
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        // WP6 D4: WeighingDifferenceRecord.Approve refuses everyone who entered the weighing or a shipment (ROL-D14 keeps the
        // recorder out too); each actor once, the recorder not repeated, another record's actors never mixed in.
        Assert.Equal(["SENTETIK-kantar", "SENTETIK-sevk"], result.Items![0].ExcludedDeciders);
        Assert.Null(result.Items![1].ExcludedDeciders);
        Assert.True(result.Items![0].Excludes("SENTETIK-sevk"));
        Assert.True(result.Items![0].Excludes(F.Maker));
        Assert.False(result.Items![0].Excludes(F.Approver));
    }

    [Fact]
    public async Task A_record_with_more_entering_actors_than_the_contract_carries_is_unavailable_never_truncated()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        Note(fixture, F.Id(1), F.Id(11));
        Record(fixture, F.Id(1), F.Id(11), F.Id(21), status: 2, minutes: 1);
        EnteredBy(fixture, F.Id(21), [.. Enumerable.Range(0, WorkInboxRulesV1.MaxExcludedDecidersPerItem + 1)
            .Select(n => $"SENTETIK-actor-{n}")]);
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task More_records_than_the_maximum_answer_the_oldest_and_has_more()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        for (var n = 0; n < 3; n++)
        {
            Note(fixture, F.Id(1), F.Id(60 + n));
            Record(fixture, F.Id(1), F.Id(60 + n), F.Id(70 + n), status: 2, minutes: 10 - n);
        }

        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 2, CancellationToken.None);

        Assert.Equal([F.Id(72), F.Id(71)], result.Items!.Select(item => item.ItemId));
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task The_query_translates_for_sql_server_with_the_explicit_company_filter()
    {
        var fixture = new F();
        using var rows = new LogisticsSqlTranslatingWorkInboxRows(F.CompanyA);
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Contains("[WeighingDifferences]", Assert.Single(rows.Sql), StringComparison.Ordinal);
    }

    private static void EnteredBy(F fixture, Guid recordId, params string[] actors) =>
        fixture.Rows.WeighingDifferenceEnteredByRows.AddRange(actors.Select(actor => new WeighingDifferenceEnteredByRow
        {
            CompanyId = F.CompanyA, RecordId = recordId, ActorId = actor,
        }));

    private static void Note(F fixture, Guid instructionId, Guid noteId, string? series = null, long? sequence = null,
        Guid? companyId = null) =>
        fixture.Rows.DispatchNoteRows.Add(new DispatchNoteRow
        {
            CompanyId = companyId ?? F.CompanyA, InstructionId = instructionId, DispatchNoteId = noteId, NoteNo = 1,
            Status = series is null ? 1 : 2, SharesBefore = 1, CreatedBy = F.Maker, CreatedAtUtc = F.T0, CreatedStep = 1,
            SeriesCode = series, SequenceNo = sequence,
        });

    private static void Record(F fixture, Guid instructionId, Guid noteId, Guid recordId, int status, int minutes,
        decimal theoretical = 100m, decimal scale = 110m, decimal difference = 10m, decimal excess = 10m, decimal? rate = 5m,
        bool enteredByComplete = true, Guid? companyId = null) =>
        fixture.Rows.WeighingDifferenceRows.Add(new WeighingDifferenceRecordRow
        {
            CompanyId = companyId ?? F.CompanyA, RecordId = recordId, InstructionId = instructionId, DispatchNoteId = noteId,
            TheoreticalKg = theoretical, ScaleKg = scale, DifferenceKg = difference, BandLevel = 0, TotalExcessKg = excess,
            ApprovalRatePercent = rate, Decision = status == 1 ? 2 : 3, EnteredByComplete = enteredByComplete, Status = status,
            Version = status == 3 ? 2 : 1, RecordedBy = F.Maker, RecordedAtUtc = F.T0.AddMinutes(minutes),
        });
}
