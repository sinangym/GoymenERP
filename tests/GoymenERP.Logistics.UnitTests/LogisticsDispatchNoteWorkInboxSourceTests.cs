using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using Microsoft.Extensions.DependencyInjection;
using F = GoymenERP.Logistics.UnitTests.LogisticsWorkInboxTestFixture;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the dispatch-note Work inbox source lists only Draft notes of Issued instructions of the
/// context company, oldest first, the maker's own included, under the approve permission held company-wide. SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsDispatchNoteWorkInboxSourceTests
{
    [Fact]
    public void The_source_declares_its_kind_the_approve_permission_and_the_module_registers_it()
    {
        var fixture = new F();
        var source = new LogisticsDispatchNoteWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        // WP1: the duration class is the Work rule table's (YOL-D18 default: hour scale), never the source's declaration.
        Assert.Equal(("logistics", "logistics.dispatch-note.v1", LogisticsPermissionManifestV1.DispatchNotesApprove,
                "logistics.dispatch-notes.approve", false),
            (source.Module, source.Kind, source.ApproverPermission, LogisticsPermissionManifestV1.DispatchNotesApprove,
                ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.True(NativeApprovalQueueRulesV1.IsValidKind(source.Module, source.Kind));
        var services = new ServiceCollection().AddLogisticsModule(new LogisticsWorkInboxTestSettings());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkInboxSourceV1)
            && descriptor.ImplementationType == typeof(LogisticsDispatchNoteWorkInboxSourceV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task Only_draft_notes_of_issued_instructions_are_listed_oldest_first_with_the_instruction_link()
    {
        var fixture = new F();
        var issued = F.Id(1);
        var draftInstruction = F.Id(2);
        fixture.Instruction(issued);
        fixture.Instruction(draftInstruction, status: 1);
        Note(fixture, issued, F.Id(11), status: 1, minutes: 30);
        Note(fixture, issued, F.Id(12), status: 1, minutes: 10);
        Note(fixture, issued, F.Id(13), status: 2, minutes: 5);
        Note(fixture, issued, F.Id(14), status: 3, minutes: 1);
        Note(fixture, draftInstruction, F.Id(15), status: 1, minutes: 0);
        var source = new LogisticsDispatchNoteWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        F.AssertContractShape(result, "logistics", "logistics.dispatch-note.v1");
        Assert.Equal([F.Id(12), F.Id(11)], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal((F.Id(12).ToString("D"), F.T0.AddMinutes(10).ToUniversalTime(), "/lojistik/yukleme-talimatlari/" + issued.ToString("D")),
            (first.DocumentLabel, first.WaitingSinceUtc, first.Link));
        var rule = Assert.Single(first.Rules);
        Assert.Equal(new WorkApprovalRuleResultV1("logistics.dispatch-note-draft", null, null, null, null), rule);
        var reference = Assert.Single(first.References);
        Assert.Equal(("loading-instruction", issued), (reference.Role, reference.RecordId));
        Assert.Equal((F.CompanyA, false), (result.CompanyId, result.HasMore));
    }

    [Fact]
    public async Task Another_companys_draft_is_never_listed_even_when_its_rows_are_readable()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        fixture.Instruction(F.Id(2), F.CompanyB);
        Note(fixture, F.Id(1), F.Id(11), status: 1, minutes: 5);
        Note(fixture, F.Id(2), F.Id(21), status: 1, minutes: 1, companyId: F.CompanyB);
        var source = new LogisticsDispatchNoteWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal([F.Id(11)], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_the_permission_or_with_a_site_scoped_grant_or_another_user_the_answer_is_denied_without_a_read()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        Note(fixture, F.Id(1), F.Id(11), status: 1, minutes: 5);
        var source = new LogisticsDispatchNoteWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        fixture.Authority.Answer = (_, _) => GoymenERP.BuildingBlocks.Security.PermissionScope.Denied;
        var denied = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Authority.Answer = (_, _) => LogisticsWorkInboxTestAuthority.SiteOnly;
        var siteOnly = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Authority.Answer = (_, _) => LogisticsWorkInboxTestAuthority.CompanyWide;
        var otherUser = await source.ListPendingAsync(fixture.Context(userId: "SENTETIK-other"), 10, CancellationToken.None);
        var otherCompany = await source.ListPendingAsync(fixture.Context(companyId: F.CompanyB), 10, CancellationToken.None);

        Assert.All(new[] { denied, siteOnly, otherUser, otherCompany }, result =>
            Assert.Equal((NativeApprovalQueueOutcomeV1.Denied, (IReadOnlyList<WorkInboxItemV1>?)null), (result.Outcome, result.Items)));
        Assert.Equal(0, fixture.Rows.Reads);
        Assert.All(fixture.Authority.Calls, call => Assert.Equal(LogisticsPermissionManifestV1.DispatchNotesApprove, call.Permission));
    }

    [Fact]
    public async Task The_makers_own_draft_is_returned_for_the_maker_since_work_hides_it_itself()
    {
        var fixture = new F();
        fixture.User.UserId = F.Maker;
        fixture.Instruction(F.Id(1));
        Note(fixture, F.Id(1), F.Id(11), status: 1, minutes: 5);
        var source = new LogisticsDispatchNoteWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(userId: F.Maker), 10, CancellationToken.None);

        Assert.Equal(F.Maker, Assert.Single(result.Items!).PreparedBy);
    }

    [Fact]
    public async Task More_drafts_than_the_maximum_answer_the_oldest_and_has_more()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        for (var n = 0; n < 4; n++)
        {
            Note(fixture, F.Id(1), F.Id(100 + n), status: 1, minutes: 40 - n);
        }

        var source = new LogisticsDispatchNoteWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 3, CancellationToken.None);

        Assert.Equal([F.Id(103), F.Id(102), F.Id(101)], result.Items!.Select(item => item.ItemId));
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task A_failed_read_is_unavailable_and_lost_authority_after_the_read_is_denied()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        Note(fixture, F.Id(1), F.Id(11), status: 1, minutes: 5);
        var source = new LogisticsDispatchNoteWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        fixture.Rows.FailReads = true;
        var failed = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Rows.FailReads = false;
        var calls = 0;
        fixture.Authority.Answer = (_, _) => ++calls == 1 ? LogisticsWorkInboxTestAuthority.CompanyWide
            : GoymenERP.BuildingBlocks.Security.PermissionScope.Denied;
        var revoked = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal((NativeApprovalQueueOutcomeV1.Unavailable, NativeApprovalQueueOutcomeV1.Denied), (failed.Outcome, revoked.Outcome));
        Assert.Null(revoked.Items);
    }

    [Fact]
    public async Task The_query_translates_for_sql_server_with_the_explicit_company_filter()
    {
        var fixture = new F();
        using var rows = new LogisticsSqlTranslatingWorkInboxRows(F.CompanyA);
        var source = new LogisticsDispatchNoteWorkInboxSourceV1(rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        var sql = Assert.Single(rows.Sql);
        Assert.Contains("[DispatchNotes]", sql, StringComparison.Ordinal);
        Assert.Contains("TOP(", sql, StringComparison.Ordinal);
    }

    private static void Note(F fixture, Guid instructionId, Guid noteId, int status, int minutes, Guid? companyId = null) =>
        fixture.Rows.DispatchNoteRows.Add(new DispatchNoteRow
        {
            CompanyId = companyId ?? F.CompanyA, InstructionId = instructionId, DispatchNoteId = noteId, NoteNo = 1, Status = status,
            SharesBefore = 1, CreatedBy = F.Maker, CreatedAtUtc = F.T0.AddMinutes(minutes), CreatedStep = 1,
        });
}
