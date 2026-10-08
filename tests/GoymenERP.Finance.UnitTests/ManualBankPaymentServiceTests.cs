using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>R47 service behavior in memory. SENTETIK / NOT_ENV; not SQL or browser evidence.</summary>
public sealed class ManualBankPaymentServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Bank = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Party = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Account = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Profile = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Document = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly DateOnly OccurredOn = new(2026, 9, 23);

    [Fact]
    public async Task Draft_approve_record_reopen_and_replay_write_one_outgoing_event()
    {
        await using var run = await Run.StartAsync();
        ManualBankPaymentCommandResult draft;
        await using (var maker = run.Scope("maker", new AllowAll()))
        {
            draft = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(), CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, draft.Outcome);
        Assert.Null(draft.Resource!.Preparation.BankReference);

        ManualBankPaymentCommandResult approved;
        await using (var checker = run.Scope("checker", new AllowAll()))
        {
            approved = await checker.Service.ApproveAsync(checker.Context, Key("approve-1"),
                new ApproveManualBankPaymentRequest(draft.Resource.Id, draft.Resource.Version), CancellationToken.None);
        }
        Assert.Equal((CommandOutcome.Succeeded, ManualBankPaymentStatus.Approved),
            (approved.Outcome, approved.Resource!.Status));

        var request = new RecordManualBankPaymentRequest(approved.Resource.Id, approved.Resource.Version,
            BankResultConfirmed: true);
        ManualBankPaymentCommandResult recorded;
        await using (var finalActor = run.Scope("checker", new AllowAll()))
        {
            recorded = await finalActor.Service.RecordAsync(finalActor.Context, Key("record-1"), request,
                CancellationToken.None);
        }
        Assert.Equal((CommandOutcome.Succeeded, ManualBankPaymentStatus.Recorded, true),
            (recorded.Outcome, recorded.Resource!.Status, recorded.Resource.BankResultConfirmed));

        await using (var reopened = run.Scope("reader", new AllowAll()))
        {
            var detail = await reopened.Service.DetailAuthorizedAsync(reopened.Context, recorded.Resource.Id,
                CancellationToken.None);
            var list = await reopened.Service.ListAuthorizedAsync(reopened.Context, null, CancellationToken.None);
            Assert.Equal((ReadOutcome.Found, ManualBankPaymentStatus.Recorded),
                (detail.Outcome, detail.Resource!.Status));
            Assert.Equal(recorded.Resource.Id, Assert.Single(list.Resource!.Items).Id);
        }

        await using (var replayActor = run.Scope("checker", new AllowAll()))
        {
            var replay = await replayActor.Service.RecordAsync(replayActor.Context, Key("record-1"), request,
                CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, recorded.OriginalCorrelationId),
                (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
        }

        await using var verify = run.Context();
        Assert.Equal(1, await verify.OutgoingMoneyEvents.CountAsync());
        Assert.Equal(3, await verify.Commands.CountAsync());
        Assert.Equal(3, await verify.Set<AuditEvent>().CountAsync());
        var bank = await verify.BankAccounts.SingleAsync();
        Assert.Equal("TR330006100519786457841326", bank.Iban);
        Assert.Equal("TR120006100519786457841327", bank.PendingIban);
    }

    [Fact]
    public async Task Maker_cannot_approve_and_unknown_bank_result_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        ManualBankPaymentCommandResult draft;
        await using (var maker = run.Scope("maker", new AllowAll()))
        {
            draft = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(), CancellationToken.None);
            var sameActor = await maker.Service.ApproveAsync(maker.Context, Key("approve-maker"),
                new ApproveManualBankPaymentRequest(draft.Resource!.Id, draft.Resource.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, sameActor.Outcome);
        }

        ManualBankPaymentCommandResult approved;
        await using (var checker = run.Scope("checker", new AllowAll()))
        {
            approved = await checker.Service.ApproveAsync(checker.Context, Key("approve-1"),
                new ApproveManualBankPaymentRequest(draft.Resource!.Id, draft.Resource.Version), CancellationToken.None);
            var unknown = await checker.Service.RecordAsync(checker.Context, Key("record-unknown"),
                new RecordManualBankPaymentRequest(approved.Resource!.Id, approved.Resource.Version,
                    BankResultConfirmed: false), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, unknown.Outcome);
        }

        await using var verify = run.Context();
        Assert.Empty(await verify.OutgoingMoneyEvents.ToListAsync());
        Assert.Equal(2, await verify.Commands.CountAsync());
        Assert.Equal(2, await verify.Set<AuditEvent>().CountAsync());
        Assert.Equal(ManualBankPaymentStatuses.Approved,
            (await verify.ManualBankPayments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Permission_revoked_at_the_final_fence_refuses_record_without_side_effects()
    {
        await using var run = await Run.StartAsync();
        ManualBankPaymentCommandResult draft;
        await using (var maker = run.Scope("maker", new AllowAll()))
        {
            draft = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(), CancellationToken.None);
        }
        ManualBankPaymentCommandResult approved;
        await using (var checker = run.Scope("checker", new AllowAll()))
        {
            approved = await checker.Service.ApproveAsync(checker.Context, Key("approve-1"),
                new ApproveManualBankPaymentRequest(draft.Resource!.Id, draft.Resource.Version), CancellationToken.None);
        }

        var permission = new DenyAtRecordCheck(8);
        await using (var recorder = run.Scope("recorder", permission))
        {
            var refused = await recorder.Service.RecordAsync(recorder.Context, Key("record-1"),
                new RecordManualBankPaymentRequest(approved.Resource!.Id, approved.Resource.Version, true),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
        }
        Assert.Equal(8, permission.RecordChecks);

        await using var verify = run.Context();
        Assert.Empty(await verify.OutgoingMoneyEvents.ToListAsync());
        Assert.DoesNotContain(await verify.Commands.ToListAsync(), row => row.CommandName == ManualBankPaymentService.RecordCommand);
        Assert.Equal(2, await verify.Set<AuditEvent>().CountAsync());
        var payment = await verify.ManualBankPayments.SingleAsync();
        Assert.Equal((ManualBankPaymentStatuses.Approved, 2L, false),
            (payment.Status, payment.Version, payment.BankResultConfirmed));
    }

    [Fact]
    public async Task Unavailable_permission_is_not_reported_as_denied()
    {
        await using var run = await Run.StartAsync();
        await using var unavailable = run.Scope("maker", new FixedPermission(PermissionDecision.Unavailable));
        await using var denied = run.Scope("maker", new FixedPermission(PermissionDecision.Denied));

        var unknown = await unavailable.Service.DraftSaveAsync(unavailable.Context, Key("u"), Draft(), CancellationToken.None);
        var forbidden = await denied.Service.DraftSaveAsync(denied.Context, Key("d"), Draft(), CancellationToken.None);

        Assert.Equal(CommandOutcome.Unavailable, unknown.Outcome);
        Assert.Equal(CommandOutcome.Denied, forbidden.Outcome);
        await using var verify = run.Context();
        Assert.Empty(await verify.ManualBankPayments.ToListAsync());
    }

    [Fact]
    public async Task Linked_document_is_validated_and_canonical_identity_version_are_reopened()
    {
        await using var run = await Run.StartAsync();
        await using var maker = run.Scope("maker", new AllowAll(), new DocumentPort(
            ManualBankPaymentDocumentValidationOutcome.Found, "owner-v7"));

        var saved = await maker.Service.DraftSaveAsync(maker.Context, Key("unlinked"), Draft(),
            CancellationToken.None);
        var linked = Draft(new ManualBankPaymentLinkedDocumentRequest(Document, "owner-v7"));
        var updated = await maker.Service.DraftSaveAsync(maker.Context, Key("linked"),
            new ManualBankPaymentDraftSaveRequest(saved.Resource!.Id, saved.Resource.Version,
                linked.Preparation, linked.LinkedDocument), CancellationToken.None);

        Assert.Equal(CommandOutcome.Succeeded, updated.Outcome);
        Assert.Equal(new ManualBankPaymentLinkedDocument(Document, "owner-v7"), updated.Resource!.LinkedDocument);
        Assert.NotEqual(saved.Resource.Version, updated.Resource.Version);
        var reopened = await maker.Service.DetailAuthorizedAsync(maker.Context, updated.Resource.Id,
            CancellationToken.None);
        Assert.Equal(new ManualBankPaymentLinkedDocument(Document, "owner-v7"), reopened.Resource!.LinkedDocument);
        await using var verify = run.Context();
        var row = await verify.ManualBankPayments.SingleAsync();
        Assert.Equal(Document, row.LinkedDocumentId);
        Assert.Equal("owner-v7", row.LinkedDocumentVersion);
    }

    [Theory]
    [InlineData(ManualBankPaymentDocumentValidationOutcome.Invalid, CommandOutcome.ValidationFailed,
        "finance_manual_bank_payment_document_invalid")]
    [InlineData(ManualBankPaymentDocumentValidationOutcome.Denied, CommandOutcome.Denied, null)]
    [InlineData(ManualBankPaymentDocumentValidationOutcome.NotFound, CommandOutcome.ValidationFailed,
        "finance_manual_bank_payment_document_not_found")]
    [InlineData(ManualBankPaymentDocumentValidationOutcome.VersionConflict, CommandOutcome.VersionConflict,
        "finance_manual_bank_payment_document_version_conflict")]
    [InlineData(ManualBankPaymentDocumentValidationOutcome.Unavailable, CommandOutcome.Unavailable,
        "finance_manual_bank_payment_document_unavailable")]
    public async Task Linked_document_failures_and_retries_write_nothing(
        ManualBankPaymentDocumentValidationOutcome ownerOutcome,
        CommandOutcome expectedOutcome,
        string? expectedCode)
    {
        await using var run = await Run.StartAsync();
        await using var maker = run.Scope("maker", new AllowAll(), new DocumentPort(ownerOutcome));
        var request = Draft(new ManualBankPaymentLinkedDocumentRequest(Document, "expected-v7"));

        foreach (var attempt in new[] { "same-key", "same-key" })
        {
            var result = await maker.Service.DraftSaveAsync(maker.Context, Key(attempt), request,
                CancellationToken.None);
            Assert.Equal(expectedOutcome, result.Outcome);
            if (expectedCode is not null) Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
        }

        await using var verify = run.Context();
        Assert.Empty(await verify.ManualBankPayments.ToListAsync());
        Assert.Empty(await verify.Commands.ToListAsync());
        Assert.Empty(await verify.OutgoingMoneyEvents.ToListAsync());
        Assert.Empty(await verify.Set<AuditEvent>().ToListAsync());
    }

    [Theory]
    [InlineData(false, PermissionDecision.Denied, ReadOutcome.Denied)]
    [InlineData(true, PermissionDecision.Denied, ReadOutcome.Denied)]
    [InlineData(false, PermissionDecision.Unavailable, ReadOutcome.Unavailable)]
    [InlineData(true, PermissionDecision.Unavailable, ReadOutcome.Unavailable)]
    public async Task Read_authority_lost_after_initial_admission_returns_no_resource_or_money_effect(
        bool detail,
        PermissionDecision subsequentDecision,
        ReadOutcome expectedOutcome)
    {
        await using var run = await Run.StartAsync();
        ManualBankPaymentCommandResult saved;
        await using (var maker = run.Scope("maker", new AllowAll()))
        {
            saved = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(),
                CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);

        var permission = new AfterInitialReadPermission(subsequentDecision);
        await using (var reader = run.Scope("reader", permission))
        {
            if (detail)
            {
                var result = await reader.Service.DetailAuthorizedAsync(reader.Context, saved.Resource!.Id,
                    CancellationToken.None);
                Assert.Equal(expectedOutcome, result.Outcome);
                Assert.Null(result.Resource);
            }
            else
            {
                var result = await reader.Service.ListAuthorizedAsync(reader.Context, null, CancellationToken.None);
                Assert.Equal(expectedOutcome, result.Outcome);
                Assert.Null(result.Resource);
            }
        }
        Assert.Equal(2, permission.ReadChecks);

        await using var verify = run.Context();
        Assert.Empty(await verify.OutgoingMoneyEvents.ToListAsync());
        Assert.Equal(1, await verify.Commands.CountAsync());
        Assert.Equal(1, await verify.Set<AuditEvent>().CountAsync());
        var payment = await verify.ManualBankPayments.SingleAsync();
        Assert.Equal(saved.Resource!.Id, payment.Id);
        Assert.Equal((ManualBankPaymentStatuses.Draft, 1L), (payment.Status, payment.Version));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Company_switch_after_initial_read_admission_returns_no_resource_or_money_effect(bool detail)
    {
        await using var run = await Run.StartAsync();
        ManualBankPaymentCommandResult saved;
        await using (var maker = run.Scope("maker", new AllowAll()))
        {
            saved = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(),
                CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);

        var permission = new AfterInitialReadPermission(PermissionDecision.Allowed);
        // Simulate a faulty context provider drifting across an await; the production context remains immutable.
        await using (var reader = run.Scope("reader", permission,
            companyContext: new MutableCompanyContext(Company)))
        {
            var otherCompany = Guid.NewGuid();
            permission.OnSubsequentReadCheck = () => reader.CompanyContext.Set(otherCompany);
            if (detail)
            {
                var result = await reader.Service.DetailAuthorizedAsync(reader.Context, saved.Resource!.Id,
                    CancellationToken.None);
                Assert.Equal(ReadOutcome.Denied, result.Outcome);
                Assert.Null(result.Resource);
            }
            else
            {
                var result = await reader.Service.ListAuthorizedAsync(reader.Context, null, CancellationToken.None);
                Assert.Equal(ReadOutcome.Denied, result.Outcome);
                Assert.Null(result.Resource);
            }
            Assert.Equal(otherCompany, reader.CompanyContext.CompanyId);
        }
        Assert.Equal(2, permission.ReadChecks);

        await using var verify = run.Context();
        Assert.Empty(await verify.OutgoingMoneyEvents.ToListAsync());
        Assert.Equal(1, await verify.Commands.CountAsync());
        Assert.Equal(1, await verify.Set<AuditEvent>().CountAsync());
        var payment = await verify.ManualBankPayments.SingleAsync();
        Assert.Equal(saved.Resource!.Id, payment.Id);
        Assert.Equal((ManualBankPaymentStatuses.Draft, 1L), (payment.Status, payment.Version));
    }

    [Fact]
    public async Task Draft_edit_preserves_identity_clears_optional_fields_replays_and_refuses_stale_version()
    {
        await using var run = await Run.StartAsync();
        var initial = Draft();
        initial = initial with
        {
            Preparation = initial.Preparation with
            {
                ValueOn = OccurredOn.AddDays(1),
                BankReference = "SENTETIK-old-reference",
            },
        };
        ManualBankPaymentCommandResult saved;
        await using (var maker = run.Scope("maker", new AllowAll()))
        {
            saved = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), initial,
                CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
        var original = Assert.IsType<ManualBankPaymentView>(saved.Resource);

        var replacement = new ManualBankPaymentDraftSaveRequest(original.Id, original.Version,
            initial.Preparation with
            {
                Money = new MoneyText("900.00", Currency),
                ValueOn = null,
                BankReference = null,
                Description = null,
            }, null);
        ManualBankPaymentCommandResult edited;
        await using (var editor = run.Scope("maker", new AllowAll()))
        {
            var reopened = await editor.Service.DetailAuthorizedAsync(editor.Context, original.Id,
                CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, reopened.Outcome);
            Assert.Equal(initial.Preparation, reopened.Resource!.Preparation);

            edited = await editor.Service.DraftSaveAsync(editor.Context, Key("edit-1"), replacement,
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, edited.Outcome);
            Assert.Equal((original.Id, ManualBankPaymentStatus.Draft, "2"),
                (edited.Resource!.Id, edited.Resource.Status, edited.Resource.Version));
            Assert.Equal(replacement.Preparation, edited.Resource.Preparation);
        }
        await using (var replayActor = run.Scope("maker", new AllowAll()))
        {
            var replay = await replayActor.Service.DraftSaveAsync(replayActor.Context, Key("edit-1"), replacement,
                CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, edited.OriginalCorrelationId),
                (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
            Assert.Equal(edited.Resource, replay.Resource);
        }
        await using (var staleActor = run.Scope("maker", new AllowAll()))
        {
            var stale = await staleActor.Service.DraftSaveAsync(staleActor.Context, Key("edit-stale"), replacement,
                CancellationToken.None);
            Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
            Assert.Null(stale.Resource);
            Assert.Contains(stale.Issues, issue => issue.Code == "version_conflict");
        }
        await using (var reader = run.Scope("reader", new AllowAll()))
        {
            var reopened = await reader.Service.DetailAuthorizedAsync(reader.Context, original.Id,
                CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, reopened.Outcome);
            Assert.Equal(edited.Resource, reopened.Resource);
        }
        await using var verify = run.Context();
        var payment = Assert.Single(await verify.ManualBankPayments.ToListAsync());
        Assert.Equal((original.Id, ManualBankPaymentStatuses.Draft, 2L),
            (payment.Id, payment.Status, payment.Version));
        Assert.Equal("900.00", payment.AmountText);
        Assert.Null(payment.ValueOn);
        Assert.Null(payment.BankReference);
        Assert.Null(payment.Description);
        Assert.Empty(await verify.OutgoingMoneyEvents.ToListAsync());
        Assert.Equal(2, await verify.Set<AuditEvent>().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approved_or_recorded_payment_cannot_be_replaced_by_draft_edit(bool record)
    {
        await using var run = await Run.StartAsync();
        ManualBankPaymentCommandResult saved;
        await using (var maker = run.Scope("maker", new AllowAll()))
        {
            saved = await maker.Service.DraftSaveAsync(maker.Context, Key("draft-1"), Draft(),
                CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
        ManualBankPaymentCommandResult current;
        await using (var checker = run.Scope("checker", new AllowAll()))
        {
            current = await checker.Service.ApproveAsync(checker.Context, Key("approve-1"),
                new ApproveManualBankPaymentRequest(saved.Resource!.Id, saved.Resource.Version),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, current.Outcome);
            if (record)
            {
                current = await checker.Service.RecordAsync(checker.Context, Key("record-1"),
                    new RecordManualBankPaymentRequest(current.Resource!.Id, current.Resource.Version, true),
                    CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, current.Outcome);
            }
        }
        var before = Assert.IsType<ManualBankPaymentView>(current.Resource);
        await using (var editor = run.Scope("maker", new AllowAll()))
        {
            var refused = await editor.Service.DraftSaveAsync(editor.Context, Key("edit-final"),
                new ManualBankPaymentDraftSaveRequest(before.Id, before.Version,
                    before.Preparation with { Money = new MoneyText("900.00", Currency) }, null),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Null(refused.Resource);
            Assert.Contains(refused.Issues, issue => issue.Code == "payment_not_draft");
        }
        await using (var reader = run.Scope("reader", new AllowAll()))
        {
            var reopened = await reader.Service.DetailAuthorizedAsync(reader.Context, before.Id,
                CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, reopened.Outcome);
            Assert.Equal(before, reopened.Resource);
        }
        await using var verify = run.Context();
        Assert.Equal(record ? 1 : 0, await verify.OutgoingMoneyEvents.CountAsync());
        Assert.Equal(record ? 3 : 2, await verify.Commands.CountAsync());
        Assert.Equal(record ? 3 : 2, await verify.Set<AuditEvent>().CountAsync());
    }

    private static CommandMetadata Key(string value) => new(value);

    private static ManualBankPaymentDraftSaveRequest Draft(
        ManualBankPaymentLinkedDocumentRequest? linkedDocument = null) => new(null, null,
        new ManualBankPaymentPreparation(Party, Account, Bank, new MoneyText("1250.50", Currency),
            new ProfileReference(Profile.ToString("D"), "1"), OccurredOn, null, null, "SENTETIK odeme"),
        linkedDocument);

    private sealed class Run(string databaseName) : IAsyncDisposable
    {
        public static async Task<Run> StartAsync()
        {
            var run = new Run($"finance-r47-{Guid.NewGuid():N}");
            await using var context = run.Context();
            context.BankAccounts.Add(new BankAccountRow
            {
                Id = Bank,
                CompanyId = Company,
                CurrencyId = Currency,
                DisplayName = "SENTETIK banka",
                BankName = "SENTETIK",
                AccountType = "Vadesiz",
                Iban = "TR330006100519786457841326",
                PendingIban = "TR120006100519786457841327",
                PendingIbanProposedBy = "iban-maker",
                PendingIbanProposedAtUtc = DateTimeOffset.UtcNow,
                PendingIbanReason = "SENTETIK",
                MoneyMovementEligible = true,
                Status = 2,
                Version = 2,
                ProposedBy = "bank-maker",
                ProposedAtUtc = DateTimeOffset.UtcNow,
                ApprovedBy = "bank-checker",
                ApprovedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
            return run;
        }

        public FinanceDbContext Context()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, company);
        }

        public Scope Scope(string actor, IFinancePermissionPort permission,
            IManualBankPaymentDocumentValidationPort? documentPort = null,
            ICompanyContext? companyContext = null)
        {
            var company = companyContext ?? new CompanyContext();
            if (!company.IsResolved) company.Set(Company);
            var collector = new AuditEventCollector();
            var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new ManualBankPaymentService(context, company, new User(actor), permission,
                new PartyPort(), documentPort ?? new DocumentPort(ManualBankPaymentDocumentValidationOutcome.Found),
                new SettingsPort(), new OpenPeriod(), collector,
                new ManualBankPaymentBinding(FinanceMoneyOperations.ManualBankPaymentV1),
                new ManualBankPaymentAuditEnvelopes(
                    Envelope(FinancePermissionNames.ManualBankPaymentDraftSave),
                    Envelope(FinancePermissionNames.ManualBankPaymentApprove),
                    Envelope(FinancePermissionNames.ManualBankPaymentRecord)),
                TimeProvider.System);
            return new Scope(context, service, new FinanceContext(Company, actor, $"r47-{Guid.NewGuid():N}", null),
                company);
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Context();
            await context.Database.EnsureDeletedAsync();
        }

        private static FinanceAuditEnvelope Envelope(string permission) =>
            new(permission, ["SENTETIK-FINANCE"], "Web");
    }

    private sealed class Scope(FinanceDbContext db, ManualBankPaymentService service, FinanceContext context,
        ICompanyContext companyContext)
        : IAsyncDisposable
    {
        public IManualBankPaymentService Service => service;
        public FinanceContext Context => context;
        public ICompanyContext CompanyContext => companyContext;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class AfterInitialReadPermission(PermissionDecision subsequentDecision) : IFinancePermissionPort
    {
        private int count;
        public int ReadChecks => Volatile.Read(ref count);
        public Action? OnSubsequentReadCheck { get; set; }

        public async Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action != FinanceAction.ReadManualBankPayments) return PermissionDecision.Allowed;
            var readCheck = Interlocked.Increment(ref count);
            await Task.Yield();
            if (readCheck == 1) return PermissionDecision.Allowed;
            OnSubsequentReadCheck?.Invoke();
            return subsequentDecision;
        }
    }

    private sealed class AllowAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class FixedPermission(PermissionDecision decision) : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(decision);
    }

    private sealed class DenyAtRecordCheck(int denyAt) : IFinancePermissionPort
    {
        private int count;
        public int RecordChecks => Volatile.Read(ref count);

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(action == FinanceAction.RecordManualBankPayment
            && Interlocked.Increment(ref count) >= denyAt ? PermissionDecision.Denied : PermissionDecision.Allowed);
    }

    private sealed class PartyPort : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) => Task.FromResult(
            partyId == Party && companyAccountId == Account
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(Party, Account, Company, true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    private sealed class DocumentPort(
        ManualBankPaymentDocumentValidationOutcome outcome,
        string resolvedVersion = "expected-v7") : IManualBankPaymentDocumentValidationPort
    {
        public Task<ManualBankPaymentDocumentValidationResult> ValidateAsync(FinanceContext context,
            Guid documentId, string expectedVersion, CancellationToken cancellationToken) => Task.FromResult(
            new ManualBankPaymentDocumentValidationResult(outcome,
                outcome == ManualBankPaymentDocumentValidationOutcome.Found
                    ? new ManualBankPaymentLinkedDocument(documentId, resolvedVersion)
                    : null));
    }

    private sealed class SettingsPort : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Task.FromResult(new MoneyProfileV1(
            Company, Currency, "TRY", FinanceMoneyOperations.ManualBankPaymentV1,
            new ProfileVersionV1(Profile, 1), 2, true, new DateOnly(2026, 1, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class OpenPeriod : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    private sealed class User(string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
}
