using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSourceDocumentAdmissionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ReversalChecker = Guid.Parse("e0420000-0000-4000-8000-000000000007");

    [SqlServerFact]
    public async Task Official_source_cancellation_proposes_in_the_selected_open_period_and_another_actor_reverses_the_exact_posted_lines()
    {
        await using var run = await Run.StartAsync(true); await OpenVoucherPeriod(run);
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        var posted = await ApproveVoucher(run, admitted.VoucherDraftId!.Value, _ => Task.CompletedTask);
        var cancellation = Cancellation(); await CancelVoucherSource(run, cancellation);
        await using (var closing = run.Factory.Services.CreateAsyncScope())
        {
            closing.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var version = await run.ScalarAsync<long>("SELECT Version FROM accounting.Periods WHERE CompanyId=@c AND Id=@p",
                ("@c", CompanyA), ("@p", posted.PeriodId!.Value));
            await closing.ServiceProvider.GetRequiredService<IAccountingPeriodStore>()
                .ClosePeriodAsync(posted.PeriodId.Value, version, ReversalChecker, VoucherMetadata(ReversalChecker));
        }
        var target = await OpenReversalTarget(run);
        var request = ReversalRequest(target, posted.Version);
        var proposed = await ProposeSourceReversal(run, posted.Id, request, _ => Task.CompletedTask);
        Assert.Equal("CancellationReversalPending", proposed.Status); Assert.Equal(cancellation.CancellationId, proposed.CancellationId);
        Assert.Equal("Pending", proposed.ReversalProposal!.Status); Assert.Equal(target, proposed.ReversalProposal.PeriodId);
        Assert.Equal(new DateOnly(2026, 12, 3), proposed.ReversalProposal.AccountingDate);
        Assert.Equal(posted.Id, proposed.ReversalProposal.SourceJournalId); Assert.Equal(posted.Version + 1, proposed.Version);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalReversalProposals WHERE CompanyId=@c", ("@c", CompanyA)));
        var own = await Assert.ThrowsAsync<AccountingJournalReversalPolicyExceptionV1>(() => ApproveSourceReversal(run, request.ProposalId, VoucherChecker));
        Assert.Equal(AccountingJournalReversalPolicyCodesV1.MakerCheckerRequired, own.Code);
        var originalMaker = await Assert.ThrowsAsync<AccountingJournalReversalPolicyExceptionV1>(() => ApproveSourceReversal(run, request.ProposalId, Request(CompanyA).SourceActorId));
        Assert.Equal(AccountingJournalReversalPolicyCodesV1.MakerCheckerRequired, originalMaker.Code);
        var reversed = await ApproveSourceReversal(run, request.ProposalId, ReversalChecker);
        Assert.Equal(AccountingJournalReversalProposalStatusV1.Approved, reversed.Status);
        Assert.Equal(ReversalChecker, reversed.DecidedBy);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalLines a JOIN accounting.JournalLines b ON a.CompanyId=b.CompanyId AND a.LineNumber=b.LineNumber WHERE a.CompanyId=@c AND a.JournalId=@s AND b.JournalId=@r AND (a.AccountId<>b.AccountId OR a.Debit<>b.Credit OR a.Credit<>b.Debit)",
            ("@c", CompanyA), ("@s", posted.Id), ("@r", reversed.ReversalJournalId!.Value)));
        await using var read = run.Factory.Services.CreateAsyncScope(); read.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var view = await read.ServiceProvider.GetRequiredService<IAccountingSourceVoucherStoreV1>().FindAsync(posted.Id);
        Assert.Equal("CancelledByReversal", view!.Status); Assert.Equal("Approved", view.ReversalProposal!.Status);
        Assert.Equal(posted.DocumentDate, view.DocumentDate); Assert.Equal(posted.FunctionalPayable, view.FunctionalPayable);
        Assert.Equal(posted.JournalId, view.JournalId); Assert.Equal(posted.PeriodId, view.PeriodId);
    }

    [SqlServerFact]
    public async Task Source_reversal_requires_the_real_cancellation_and_final_authority_loss_rolls_back_proposal_audit_and_voucher_version()
    {
        await using var run = await Run.StartAsync(true); await OpenVoucherPeriod(run);
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        var posted = await ApproveVoucher(run, admitted.VoucherDraftId!.Value, _ => Task.CompletedTask);
        var request = ReversalRequest(await OpenReversalTarget(run), posted.Version);
        var refused = await Assert.ThrowsAsync<AccountingStoreException>(() => ProposeSourceReversal(run, posted.Id, request, _ => Task.CompletedTask));
        Assert.Equal("source_voucher_reversal_not_proposable", refused.Code);
        await CancelVoucherSource(run, Cancellation());
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProposeSourceReversal(run, posted.Id, request, _ =>
        { if (++calls == 4) throw new InvalidOperationException("SENTETIK final authority lost"); return Task.CompletedTask; }));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalReversalProposals WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(posted.Version, await run.ScalarAsync<long>("SELECT Version FROM accounting.SourceDocumentVoucherDrafts WHERE CompanyId=@c AND Id=@i", ("@c", CompanyA), ("@i", posted.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@c AND EventType=N'accounting.journal-reversal.proposed.v1'", ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CompanyId=@c AND CommandName=N'SENTETIK-source-reversal'", ("@c", CompanyA)));
        await using var foreign = run.Factory.Services.CreateAsyncScope(); foreign.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyB);
        Assert.Null(await foreign.ServiceProvider.GetRequiredService<IAccountingSourceVoucherStoreV1>().FindAsync(posted.Id));
    }

    [SqlServerFact]
    public async Task Source_reversal_closed_target_remains_pending_without_reopening_or_posting()
    {
        await using var run = await Run.StartAsync(true); await OpenVoucherPeriod(run);
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        var posted = await ApproveVoucher(run, admitted.VoucherDraftId!.Value, _ => Task.CompletedTask);
        await CancelVoucherSource(run, Cancellation());
        var target = await OpenReversalTarget(run); var request = ReversalRequest(target, posted.Version);
        var outside = await Assert.ThrowsAsync<AccountingStoreException>(() => ProposeSourceReversal(run, posted.Id,
            request with { AccountingDate = new(2027, 1, 1) }, _ => Task.CompletedTask));
        Assert.Equal("source_voucher_reversal_period_not_open", outside.Code);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalReversalProposals WHERE CompanyId=@c", ("@c", CompanyA)));
        await ProposeSourceReversal(run, posted.Id, request, _ => Task.CompletedTask);
        await using (var closing = run.Factory.Services.CreateAsyncScope())
        {
            closing.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await closing.ServiceProvider.GetRequiredService<IAccountingPeriodStore>()
                .ClosePeriodAsync(target, 2, ReversalChecker, VoucherMetadata(ReversalChecker));
        }
        await Assert.ThrowsAsync<AccountingDomainException>(() => ApproveSourceReversal(run, request.ProposalId, ReversalChecker));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM accounting.JournalReversalProposals WHERE CompanyId=@c AND Id=@p", ("@c", CompanyA), ("@p", request.ProposalId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM accounting.Periods WHERE CompanyId=@c AND Id=@p", ("@c", CompanyA), ("@p", target)));
    }

    [SqlServerFact]
    public async Task Racing_source_reversal_proposals_cannot_create_two_pending_proposals_or_a_journal()
    {
        await using var run = await Run.StartAsync(true); await OpenVoucherPeriod(run);
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        var posted = await ApproveVoucher(run, admitted.VoucherDraftId!.Value, _ => Task.CompletedTask);
        await CancelVoucherSource(run, Cancellation()); var target = await OpenReversalTarget(run);
        async Task<Exception?> Propose() => await Record.ExceptionAsync(() => ProposeSourceReversal(run, posted.Id,
            ReversalRequest(target, posted.Version), _ => Task.CompletedTask));
        var answers = await Task.WhenAll(Propose(), Propose());
        Assert.Single(answers, x => x is null);
        Assert.All(answers.Where(x => x is not null), error => Assert.True(error is AccountingVersionConflictException
            || error is AccountingStoreException { Code: AccountingStoreCodes.ConcurrentChange or "source_voucher_reversal_not_proposable" }, error!.ToString()));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalReversalProposals WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
    }

    private static ProposeAccountingSourceVoucherReversalV1 ReversalRequest(Guid period, long version) =>
        new(version, Guid.NewGuid(), period, new(2026, 12, 2), new(2026, 12, 3), null, "SENTETIK dış resmî iptal");
    private static async Task<Guid> OpenReversalTarget(Run run)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var store = scope.ServiceProvider.GetRequiredService<IAccountingPeriodStore>(); var id = Guid.NewGuid();
        var (period, _) = await store.ProposePeriodAsync(id, new(2026, 12, 1), new(2026, 12, 31), VoucherChecker, VoucherMetadata(VoucherChecker));
        await store.ApprovePeriodAsync(id, period.Version, ReversalChecker, VoucherMetadata(ReversalChecker)); return id;
    }
    private static AccountingStore ReversalPosting(IServiceProvider provider)
    {
        var company = provider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        return new(provider.GetRequiredService<AccountingDbContext>(), company, provider.GetRequiredService<IAuditEventCollector>(), TimeProvider.System,
            new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(CompanyA)));
    }
    private static async Task<AccountingSourceVoucherViewV1> ProposeSourceReversal(Run run, Guid id,
        ProposeAccountingSourceVoucherReversalV1 request, Func<CancellationToken, Task> fence)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope(); var provider = scope.ServiceProvider; var posting = ReversalPosting(provider);
        var store = new AccountingSourceVoucherStoreV1(provider.GetRequiredService<AccountingDbContext>(), posting,
            provider.GetRequiredService<ICompanyContext>(), provider.GetRequiredService<IActiveAccountPlanReaderV1>(),
            new VoucherCurrencies(), provider.GetRequiredService<IAuditEventCollector>(), TimeProvider.System);
        var metadata = VoucherMetadata(VoucherChecker) with { PermissionCode = AccountingJournalReversalPermissionNamesV1.Propose,
            Completion = value => new("SENTETIK-source-reversal", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.ProposalId.ToByteArray())),
                VoucherChecker.ToString("D"), new string('A', 64), System.Text.Json.JsonSerializer.Serialize(value), "SENTETIK-correlation") };
        return await store.ProposeCancellationReversalAsync(id, request, VoucherChecker, fence, metadata);
    }
    private static async Task<AccountingJournalReversalProposalV1> ApproveSourceReversal(Run run, Guid id, Guid actor)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope(); var posting = ReversalPosting(scope.ServiceProvider);
        return await posting.ApproveAndReverseAsync(id, 1, actor, _ => Task.CompletedTask,
            VoucherMetadata(actor) with { PermissionCode = AccountingJournalReversalPermissionNamesV1.Approve });
    }
}
