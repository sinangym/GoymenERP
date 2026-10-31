using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-04 (SATIS_HAZIRLIK_PLANI_v1 §3.1; DEC-SAL-002, DEC-SAL-008, SAT-D3, SAT-D27, SAT-D86, SAT-D101, SAT-D103): the header
/// revision of an order — customer account, currency, due terms ("vade"), sales owner and delivery references — under its own
/// name <c>sales.orders.header.write</c> (granted to no role) — together with the price read <c>sales.orders.price.read</c>,
/// because the request carries the whole header and its due terms are disclosed only with that read.
/// <list type="bullet">
/// <item><b>Re-approval:</b> a change of the due terms, the customer or the currency of an APPROVED order archives it first
/// (approval facts, lines, money profile and — new — the header it was approved with, with the reason) and goes on as the next
/// revision in <c>Preparing</c>; the existing submit/approve commands re-approve it. SAT-D126 (EK18): a change of ONLY the sales
/// owner and/or the delivery references is recorded in place (reason + audit, no Rev+1, no re-approval, gates untouched). SAT-D103: during the re-approval shipping goes on under the LAST APPROVED revision and invoicing
/// waits (the status axis' <c>invoicing.gate</c>, unchanged) — except that a pending CUSTOMER or CURRENCY change stops shipping
/// (the line port answers <c>HeaderRevisionPending</c>; SAT-D103 does not cover a currency change and no decision lets a
/// customer change ship under the old customer).</item>
/// <item><b>Owners, only for what changes:</b> the customer through the Cari account port; the delivery address / contact
/// through the Cari contact information of the (new) account (stale version = the key's terminal 409); the currency's money
/// profile on the order's business date through Settings; the sales owner through Identity's directory — active in the
/// company AND holding the SAT-D27 eligibility name (<c>sales.drafts.create</c>); and, for a customer or currency change of an
/// order that was ever approved, every owner record the cancellation preview reads (allocations, loading, approved dispatch
/// notes): SAT-D128 (EK18) — a started shipping locks the customer and the currency (any record present → 409
/// <c>sales_revision_blocked</c> naming the sources, an unreadable owner → 503); a priced line locks them too (422). A wrong customer
/// or currency means cancelling the order and opening a new one. Neither refusal is recorded (the owners move).</item>
/// <item><b>Ordering</b> (the quantity revision's): fence → version shape → due terms' shape and rules (asks no owner) → the
/// key's replay → the store reads the draft → fence → the domain's own preconditions (version, state, reason, priced lines) →
/// each owner (fence after each) → last fence → ONE save. Terminal 409s recorded and replayed: <c>version_conflict</c>,
/// <c>sales_state_conflict</c>. Nothing is measured after the commit (S03).</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftResponse> ReviseHeaderAsync(Guid draftId, SalesDraftHeaderRevisionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.HeaderPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        // The request is the WHOLE header, due terms included, and the due terms are commercial terms disclosed only with the
        // price read (deny-first, Sinan §4): a caller who cannot see them cannot knowingly keep or clear them, so the revision
        // needs the price read too. Measured before anything is read or recorded (a 403 is never a recorded result).
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            throw new SalesDraftServiceException(403, "forbidden", [new("dueTerms", "forbidden")]);
        }

        // DEC-SAL-008: the due terms' own rules (sequence, range, shares adding up to 100) ask no owner and record nothing.
        SalesDueTerms? dueTerms;
        try
        {
            dueTerms = SalesDraftHeaderApiV1.ToDomain(request.DueTerms);
        }
        catch (SalesDueTermsException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new(DueTermsField(exception), exception.Code)]);
        }

        const string commandName = SalesDraftCommands.ReviseHeaderV1;
        var requestHash = SalesDraftRequestHash.Compute(commandName, draftId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            // The same key, content and actor: the stored result again — the owners are not asked, nothing changes twice.
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        // SAT-D126 (EK18): every header revision — an approved order's in-place owner/delivery change, a Preparing/Rejected
        // order's in-place change and a re-approving one alike — is recorded WITH the user's own (trimmed) reason: it is the
        // audit event's reason (and the "after" picture's `reason`). A blank reason never reaches a save: the domain refuses it
        // (422) inside the store before anything is written; the fixed text only keeps the audit draft well-formed until then.
        var audit = new SalesDraftStateAudit("sales.order-draft.header-revised.v1", "ReviseHeader",
            string.IsNullOrWhiteSpace(request.Reason)
                ? "Satış siparişinin başlığı gerekçeyle revize edildi (DEC-SAL-002, SAT-D126)"
                : request.Reason.Trim());

        SalesDraftStateResult? result;
        try
        {
            result = await store.ReviseDraftHeaderAsync(envelope, requestHash, commandName, draftId,
                (draft, input, now) => draft.ReviseHeader(expectedVersion, input, scope.ActorId, request.Reason, now), audit,
                async (draft, writeCancellationToken) =>
                {
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    // The domain's own preconditions first: a stale, cancelled, pending or withdrawn order asks no owner.
                    draft.RequireHeaderRevisable(expectedVersion, request.Reason);
                    var input = await ReadHeaderOwnersAsync(scope, permission, draft, request, dueTerms,
                        refusal => TerminalAsync(scope, permission, envelope, requestHash, refusal, writeCancellationToken, commandName, draftId),
                        writeCancellationToken);

                    // The last fence that can still prevent the write; none after the commit (ORG #321 P3-1).
                    scope = await RequireStillAsync(scope, permission, writeCancellationToken);
                    return input;
                }, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict,
                [new("expectedVersion", exception.Code)]), cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            throw await TerminalAsync(scope, permission, envelope, requestHash, new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict,
                [new("approvalState", exception.Code)]), cancellationToken, commandName, draftId);
        }
        catch (SalesRuleException exception) when (exception.Code is SalesRuleCodes.RevisionReasonRequired or SalesRuleCodes.RevisionReasonTooLong)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("reason", exception.Code)]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.HeaderRevisionPricedLines)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("header", exception.Code)]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.RevisionNoChange)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("header", exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            // The revised currency's money profile (company, date, operation, validity): the create's rules on currencyId.
            throw new SalesDraftServiceException(422, "validation_failed", [new("currencyId", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won is null)
            {
                throw Store(exception);
            }

            return await ReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (result is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        return Response(scope, result.Draft, result.Outcome == SalesDraftWriteOutcome.Replayed, result.OriginalCorrelationId,
            await DiscloseAsync(scope, cancellationToken));
    }

    /// <summary>
    /// API-04: <c>GET …/{id}/header</c> under the draft read — the order's revisable header as it stands. The due terms are
    /// disclosed only with the price read (deny-first, Sinan §4); the sales owner's display name is Identity's answer, or null
    /// when the directory is not composed or does not answer (the id still shows; no name is invented).
    /// </summary>
    public async Task<SalesDraftHeaderV1> GetDraftHeaderAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesOrderDraft? draft;
        try
        {
            draft = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (draft is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        string? ownerName = null;
        if (draft.Header.SalesOwnerUserId is { } ownerId && userDisplays is not null)
        {
            SalesUserDisplayBatchAnswerV1? answer;
            try
            {
                answer = await GuardedAsync(scope, bindings.ReadPermission,
                    () => userDisplays.GetAuthorizedBatchAsync(new SalesUserDisplayBatchRequestV1(scope.CompanyId, scope.ActorId, [ownerId]),
                        cancellationToken), cancellationToken);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
            {
                answer = null;
            }

            ownerName = answer is { Outcome: SalesUserDisplayBatchOutcomeV1.Success, Items: { } items }
                ? items.SingleOrDefault(item => item?.UserId == ownerId)?.DisplayName
                : null;
        }

        var disclosure = await DiscloseAsync(scope, cancellationToken);
        var header = draft.Header;
        return new SalesDraftHeaderV1(draft.Version.ToString(CultureInfo.InvariantCulture), SalesDraftApiV1.RevisionText(draft.Revision),
            header.PartyId, header.CompanyAccountId, header.CurrencyId, Wire(header.DeliveryAddress), Wire(header.Contact),
            header.SalesOwnerUserId, ownerName, disclosure.ToString(),
            disclosure == SalesPriceDisclosure.Visible ? SalesDraftHeaderApiV1.ToWire(header.DueTerms) : null,
            draft.CustomerOrCurrencyRevisionPending);
    }

    private static SalesDraftOwnerReferenceV1? Wire(OwnerReference? reference) =>
        reference is null ? null : new SalesDraftOwnerReferenceV1(reference.Id, reference.ExpectedVersion);

    /// <summary>
    /// The owners of exactly what the request changes, read afresh (fence after each), and the domain's input. Unchanged values
    /// are not re-read: an unchanged delivery reference keeps its stored version, an unchanged sales owner is not re-measured.
    /// </summary>
    private async Task<SalesHeaderRevisionInput> ReadHeaderOwnersAsync(Scope scope, string? permission, SalesOrderDraft draft,
        SalesDraftHeaderRevisionRequest request, SalesDueTerms? dueTerms,
        Func<SalesDraftTerminalRefusal, Task<SalesDraftServiceException>> terminal, CancellationToken cancellationToken)
    {
        var header = draft.Header;
        var customerChanged = request.PartyId != header.PartyId || request.CompanyAccountId != header.CompanyAccountId;
        var currencyChanged = request.CurrencyId != header.CurrencyId;
        if ((customerChanged || currencyChanged) && draft.Lines.Any(line => line.Commercial.UnitPrice is not null))
        {
            // The domain's own rule, asked before any owner (it runs again in ReviseHeader), naming the members that changed.
            var fields = new List<SalesDraftFieldError>();
            if (customerChanged) fields.Add(new("companyAccountId", SalesRuleCodes.HeaderRevisionPricedLines));
            if (currencyChanged) fields.Add(new("currencyId", SalesRuleCodes.HeaderRevisionPricedLines));
            throw new SalesDraftServiceException(422, "validation_failed", fields);
        }

        if (customerChanged)
        {
            await RequireAccountAsync(scope, permission, request.PartyId, request.CompanyAccountId, cancellationToken);
        }

        var deliveryAddress = header.DeliveryAddress;
        var contact = header.Contact;
        var addressChanged = !SameReference(request.DeliveryAddress, header.DeliveryAddress);
        var contactChanged = !SameReference(request.Contact, header.Contact);
        var readAddress = request.DeliveryAddress is not null && (customerChanged || addressChanged);
        var readContact = request.Contact is not null && (customerChanged || contactChanged);
        if (readAddress || readContact)
        {
            var resolved = await ReadContactReferencesAsync(scope, permission, request.PartyId, request.CompanyAccountId,
                readAddress ? request.DeliveryAddress : null, readContact ? request.Contact : null, terminal, cancellationToken);
            if (readAddress) deliveryAddress = resolved.DeliveryAddress;
            if (readContact) contact = resolved.Contact;
        }

        if (request.DeliveryAddress is null) deliveryAddress = null;
        if (request.Contact is null) contact = null;

        MoneyProfileSnapshot? money = null;
        if (currencyChanged)
        {
            money = await ReadMoneyProfileAsync(scope, permission, request.CurrencyId, header.BusinessDate, cancellationToken);
        }

        if (request.SalesOwnerUserId is { } ownerId && ownerId != header.SalesOwnerUserId)
        {
            await RequireSalesOwnerAsync(scope, permission, ownerId, cancellationToken);
        }

        if ((customerChanged || currencyChanged) && (draft.ApprovalState == InternalApprovalState.Approved || draft.ArchivedRevisions.Count > 0))
        {
            // An order that was ever approved may carry owner records (a never-approved one cannot: the line port refused them).
            var impact = await ReadImpactAsync(scope, permission, draft, cancellationToken);
            if (impact.Unreadable)
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
            }

            if (impact.Blockers.Count > 0)
            {
                throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.RevisionBlocked,
                    [.. impact.Blockers.Select(source => new SalesDraftFieldError(source, SalesDraftProblemCodesV1.RevisionBlocked))]);
            }
        }

        return new SalesHeaderRevisionInput(request.PartyId, request.CompanyAccountId, request.CurrencyId, money, deliveryAddress, contact,
            request.SalesOwnerUserId, dueTerms);
    }

    private static bool SameReference(SalesDraftOwnerReferenceV1? requested, OwnerReference? stored) =>
        requested is null
            ? stored is null
            : stored is not null && requested.Id == stored.Id
                && string.Equals(requested.ExpectedVersion.Trim(), stored.ExpectedVersion, StringComparison.Ordinal);

    /// <summary>
    /// SAT-D27 (1): the sales owner is a person active in the company who holds the eligibility name the Sales owner was given
    /// (<c>sales.drafts.create</c>). Identity is asked through the two existing Sales ports — the display of the id (active in
    /// the company?) and the bounded eligibility search narrowed by that display name (does the id hold the name?). Not
    /// answered, inactive or not eligible → 422 <c>owner_reference_invalid</c> on <c>salesOwnerUserId</c>; a directory that is not
    /// composed, fails, or cannot confirm the person within one page → 503; denied → 403. Nothing is guessed.
    /// </summary>
    private async Task RequireSalesOwnerAsync(Scope scope, string? permission, Guid ownerId, CancellationToken cancellationToken)
    {
        var eligibility = bindings.SalesOwnerEligibilityPermission;
        if (userDisplays is null || salesOwners is null || string.IsNullOrWhiteSpace(eligibility))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SalesOwnerUnavailable);
        }

        SalesUserDisplayBatchAnswerV1? display;
        try
        {
            display = await GuardedAsync(scope, permission,
                () => userDisplays.GetAuthorizedBatchAsync(new SalesUserDisplayBatchRequestV1(scope.CompanyId, scope.ActorId, [ownerId]),
                    cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SalesOwnerUnavailable);
        }

        var person = display switch
        {
            { Outcome: SalesUserDisplayBatchOutcomeV1.Success, Items: { } items } => items.Where(item => item?.UserId == ownerId).ToArray(),
            { Outcome: SalesUserDisplayBatchOutcomeV1.Denied } => throw new SalesDraftServiceException(403, "forbidden"),
            _ => throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SalesOwnerUnavailable),
        };
        if (person.Length > 1)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SalesOwnerUnavailable);
        }

        if (person.Length == 0 || !person[0].IsActiveInCompany)
        {
            throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.OwnerReferenceInvalid,
                [new("salesOwnerUserId", SalesDraftProblemCodesV1.OwnerReferenceInvalid)]);
        }

        var name = person[0].DisplayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > SalesOwnerPickerLimitsV1.MaximumQueryLength)
        {
            // The bounded search cannot be narrowed to this person: it cannot be confirmed, so it is not accepted.
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SalesOwnerUnavailable);
        }

        SalesOwnerCandidateAnswerV1? eligible;
        try
        {
            eligible = await GuardedAsync(scope, permission,
                () => salesOwners.SearchAsync(new SalesOwnerCandidateRequestV1(scope.CompanyId, scope.ActorId, name,
                    SalesOwnerPickerLimitsV1.MaximumLimit, eligibility), cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SalesOwnerUnavailable);
        }

        switch (eligible)
        {
            case { Outcome: SalesOwnerCandidateOutcomeV1.Success, Items: { } candidates }
                when candidates.Count <= SalesOwnerPickerLimitsV1.MaximumLimit:
                if (candidates.Any(candidate => candidate?.UserId == ownerId))
                {
                    return;
                }

                if (eligible.HasMore)
                {
                    throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SalesOwnerUnavailable);
                }

                throw new SalesDraftServiceException(422, SalesDraftProblemCodesV1.OwnerReferenceInvalid,
                    [new("salesOwnerUserId", SalesDraftProblemCodesV1.OwnerReferenceInvalid)]);
            case { Outcome: SalesOwnerCandidateOutcomeV1.Denied }:
                throw new SalesDraftServiceException(403, "forbidden");
            default:
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SalesOwnerUnavailable);
        }
    }

    /// <summary>
    /// API-04 (fail-closed): the approval of an order whose pending revision changes the customer or the currency reads the owner
    /// records again (the same categories as the revision) — a record committed between the revision's read and its save, under
    /// the last approved revision, is caught here; present → 409 <c>sales_revision_blocked</c>, unreadable → 503. Not recorded.
    /// </summary>
    private async Task RequireHeaderReapprovalClearAsync(Scope scope, string? permission, SalesOrderDraft current, long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!current.CustomerOrCurrencyRevisionPending || current.Version != expectedVersion
            || current.ApprovalState != InternalApprovalState.PendingApproval)
        {
            return;
        }

        var impact = await ReadImpactAsync(scope, permission, current, cancellationToken);
        if (impact.Unreadable)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
        }

        if (impact.Blockers.Count > 0)
        {
            throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.RevisionBlocked,
                [.. impact.Blockers.Select(source => new SalesDraftFieldError(source, SalesDraftProblemCodesV1.RevisionBlocked))]);
        }
    }

    /// <summary>The field a due-terms rule is answered on: <c>dueTerms.instalments[i].&lt;member&gt;</c> for an instalment's rule, else <c>dueTerms</c>.</summary>
    private static string DueTermsField(SalesDueTermsException exception)
    {
        if (exception.InstalmentIndex is not { } index)
        {
            return exception.Code == SalesDueTermsCodes.SharesNotWhole ? "dueTerms.instalments" : "dueTerms";
        }

        var member = exception.Code switch
        {
            SalesDueTermsCodes.SequenceInvalid => "sequenceNo",
            SalesDueTermsCodes.BasisInvalid => "basis",
            SalesDueTermsCodes.DaysOutOfRange => "daysAfter",
            SalesDueTermsCodes.ShareInvalid => "sharePercent",
            _ => null,
        };
        return member is null ? $"dueTerms.instalments[{index}]" : $"dueTerms.instalments[{index}].{member}";
    }
}
