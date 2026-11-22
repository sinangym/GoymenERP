using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai.DocumentDrafts;

/// <summary>
/// WP-B3 "belgeden taslak" (DALGA_PLANI_v2 B.3, AI-D22, AI-D34 c). Turns an e-mail body, pasted text or a file into a
/// sales order, supplier confirmation or incoming invoice DRAFT by tiered reading — 1 structured fields (UBL-TR XML
/// through the Purchasing owner's reader, CSV/XLSX by a known header), 2 local PDF text, 3 local OCR port, 4 local
/// model port for still-open header fields (closed until OPEN-014) — and deterministic validation. Every byte of the
/// document is quarantined external DATA: it cannot add a tool, a recipient, an approval or a permission, and a model
/// turn that saw it may return values only. The draft creates no record, sends nothing and calls no write port: the
/// user continues on the owner module's creation screen, whose own command and checks decide.
/// <para>Order: correlation, feature switches, actor, server company, fresh permissions (panel + draft + the owner's
/// own permission for the kind), request shape and size, store the source (object storage), read tiers, quarantine,
/// owner matching ports, fresh permission fence, release.</para>
/// </summary>
public sealed class AiDocumentDraftService(
    IOptions<AiRuntimeOptions> runtimeOptions,
    IOptions<AiDocumentDraftOptions> options,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlationContext,
    ISecurityEventWriterV1 securityEvents,
    IAiDocumentSourceStoreV1 sourceStore,
    IPurchaseInvoiceFileReaderV1 invoiceFileReader,
    IAiDocumentOcrV1 ocr,
    IAiDocumentLocalModelV1 localModel,
    IAiDocumentCounterpartyMatcherV1 counterpartyMatcher,
    IAiDocumentProductMatcherV1 productMatcher,
    TimeProvider timeProvider,
    ILogger<AiDocumentDraftService> logger)
{
    public const string ModelToolCallRefusedFinding = "model_tool_call_refused";

    public async Task<AiDocumentDraftResultV1> DraftAsync(AiDocumentDraftRequestV1? request, CancellationToken cancellationToken = default)
    {
        if (!correlationContext.IsResolved) return AiDocumentDraftResultV1.Of(AiDocumentDraftStatusV1.Unavailable);

        var runtimeEnabled = AiRuntimeOptionsReader.TryRead(runtimeOptions, out var runtime) && runtime!.Enabled;
        var limits = AiDocumentDraftOptionsReader.TryResolve(options, out var draftsEnabled);
        if (!runtimeEnabled || !draftsEnabled) return AiDocumentDraftResultV1.Of(AiDocumentDraftStatusV1.Disabled);
        if (limits is null) return AiDocumentDraftResultV1.Of(AiDocumentDraftStatusV1.Unavailable);

        var actorId = ResolveActorId();
        if (actorId is null)
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.SessionInvalid, null, null, null, cancellationToken);
            return AiDocumentDraftResultV1.Of(AiDocumentDraftStatusV1.Refused);
        }

        if (ResolvedCompanyOrNull() is not { } companyId)
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.CompanyContextMismatch, null, actorId, null, cancellationToken);
            return AiDocumentDraftResultV1.Of(AiDocumentDraftStatusV1.Refused);
        }

        if (request?.DraftKind is not { } kind || !AiDocumentDraftKindsV1.All.Contains(kind, StringComparer.Ordinal))
        {
            return AiDocumentDraftResultV1.InvalidInput(AiDocumentDraftInvalidFieldsV1.DraftKind);
        }

        var permissions = RequiredPermissions(kind);
        if (await FirstMissingPermissionAsync(permissions, companyId, cancellationToken) is { } missing)
        {
            await RefuseAsync(SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventReasonCodesV1.PermissionMissing, companyId, actorId, missing, cancellationToken);
            return AiDocumentDraftResultV1.Of(AiDocumentDraftStatusV1.Refused);
        }

        if (!TryReadInput(request, limits, out var input, out var invalidField))
        {
            return AiDocumentDraftResultV1.InvalidInput(invalidField!);
        }

        if (input!.MediaType is AiDocumentMediaTypesV1.Xml or AiDocumentMediaTypesV1.TextXml && kind != AiDocumentDraftKindsV1.IncomingInvoice)
        {
            // An e-fatura is an invoice; it is never read as an order or a confirmation.
            return AiDocumentDraftResultV1.InvalidInput(AiDocumentDraftInvalidFieldsV1.MediaType);
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(input.Bytes));
        if (!await sourceStore.TryStoreAsync(companyId, input.Bytes, sha256, cancellationToken))
        {
            logger.LogWarning("AI document draft source could not be stored. Kind {DraftKind}.", kind);
            return AiDocumentDraftResultV1.Of(AiDocumentDraftStatusV1.Unavailable);
        }

        Reading reading;
        try
        {
            reading = await ReadAsync(input, limits, cancellationToken);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            logger.LogWarning("AI document draft parsing timed out. Kind {DraftKind}, media {MediaType}.", kind, input.MediaType);
            return new AiDocumentDraftResultV1(AiDocumentDraftStatusV1.Unreadable, null, ["parse_timeout"]);
        }
        catch (AiDocumentUnreadableException exception)
        {
            logger.LogInformation("AI document draft unreadable. Kind {DraftKind}, media {MediaType}, code {Code}.", kind, input.MediaType, exception.Code);
            return new AiDocumentDraftResultV1(AiDocumentDraftStatusV1.Unreadable, null, [exception.Code]);
        }

        var extraFindings = new List<string>();
        if (limits.LocalModelEnabled && localModel.IsAvailable && reading.Text is { } quarantined
            && reading.Document.OpenHeaderFields() is { Count: > 0 } open)
        {
            var response = await localModel.SuggestAsync(new AiLocalModelFieldRequestV1(quarantined, open), cancellationToken);
            if (!AiExternalContentQuarantineV1.AdmitModelOutput(response?.ToolCalls?.Count ?? 0) || response?.Fields is null)
            {
                // A steered model: nothing it returned is used, and the attempt is recorded.
                extraFindings.Add(ModelToolCallRefusedFinding);
                await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiToolNotAllowed, companyId, actorId, null, cancellationToken);
            }
            else
            {
                foreach (var (field, value) in response.Fields)
                {
                    if (open.Contains(field, StringComparer.Ordinal) && !string.IsNullOrWhiteSpace(value) && value.Length <= limits.MaxCellCharacters)
                        reading.Document.TryFillHeader(field, new AiExtractedValueV1(value.Trim(), AiDocumentReadingTiersV1.LocalModel));
                }
            }
        }

        if (reading.Document.IsEmpty) return new AiDocumentDraftResultV1(AiDocumentDraftStatusV1.Unreadable, null, ["no_fields_found"]);

        AiDraftMatchesV1 matches;
        AiDocumentDraftResponseV1 draft;
        try
        {
            matches = await MatchAsync(kind, reading.Document, cancellationToken);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return new AiDocumentDraftResultV1(AiDocumentDraftStatusV1.Unreadable, null, ["parse_timeout"]);
        }

        // Fence: nothing is released if the actor lost a permission while the document was read.
        if (await FirstMissingPermissionAsync(permissions, companyId, cancellationToken) is { } revoked)
        {
            await RefuseAsync(SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventReasonCodesV1.PermissionMissing, companyId, actorId, revoked, cancellationToken);
            return AiDocumentDraftResultV1.Of(AiDocumentDraftStatusV1.Refused);
        }

        try
        {
            draft = AiDocumentDraftValidator.Build(
                kind,
                reading.Tier,
                new AiDraftSourceV1(sha256, input.MediaType, input.Bytes.Length),
                reading.Findings,
                reading.Document,
                matches,
                OpenIn(kind));
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return new AiDocumentDraftResultV1(AiDocumentDraftStatusV1.Unreadable, null, ["parse_timeout"]);
        }
        if (extraFindings.Count != 0) draft = draft with { QuarantineFindings = [.. draft.QuarantineFindings, .. extraFindings] };
        logger.LogInformation("AI document draft prepared. Kind {DraftKind}, tier {Tier}, lines {Lines}, findings {Findings}.",
            kind, draft.ReadingTier, draft.Lines.Count, draft.QuarantineFindings.Count);
        return AiDocumentDraftResultV1.Drafted(draft);
    }

    /// <summary>Panel + draft capability + the owner's own permission for the kind (the draft never exceeds it).</summary>
    public static IReadOnlyList<string> RequiredPermissions(string kind) =>
    [
        ErpPermissions.AiPanelUse,
        ErpPermissions.AiDocumentDraftsPrepare,
        kind switch
        {
            AiDocumentDraftKindsV1.SalesOrder => ErpPermissions.SalesDraftsCreate,
            AiDocumentDraftKindsV1.IncomingInvoice => ErpPermissions.PurchasingInvoicesImport,
            // ASSUMPTION-AI-DRAFT-CONFIRMATION-1: a supplier confirmation is recorded against a purchase order; the
            // draft needs reading that order. No confirmation permission exists yet (NEEDS-SINAN in the report).
            _ => ErpPermissions.PurchasingOrdersRead,
        },
    ];

    public static AiDraftOpenInV1 OpenIn(string kind) => kind switch
    {
        AiDocumentDraftKindsV1.SalesOrder => new("Sales", "sales.drafts.create"),
        AiDocumentDraftKindsV1.IncomingInvoice => new("Purchasing", "purchasing.invoices.import"),
        _ => new("Purchasing", "purchasing.orders.list"),
    };

    private async Task<Reading> ReadAsync(Input input, AiDocumentDraftLimits limits, CancellationToken cancellationToken)
    {
        switch (input.MediaType)
        {
            case AiDocumentMediaTypesV1.Xml or AiDocumentMediaTypesV1.TextXml:
            {
                var document = AiUblInvoiceDraftReader.Read(invoiceFileReader, input.Bytes, limits.MaxLines);
                return Structured(document);
            }

            case AiDocumentMediaTypesV1.Csv:
                return Structured(AiTableDraftReader.ReadCsv(input.Bytes, limits.MaxLines, limits.MaxCellCharacters));

            case AiDocumentMediaTypesV1.Xlsx:
                return Structured(AiTableDraftReader.ReadXlsx(input.Bytes, limits.MaxLines, limits.MaxCellCharacters, limits.MaxExpandedBytes));

            case AiDocumentMediaTypesV1.Pdf:
            {
                var text = AiPdfTextExtractor.Extract(input.Bytes, limits.MaxExpandedBytes);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return FromText(text, AiExternalContentSourceV1.Pdf, AiDocumentReadingTiersV1.PdfText, limits);
                }

                return await OcrAsync(input, limits, cancellationToken); // an image-only (scanned) PDF
            }

            case AiDocumentMediaTypesV1.Png or AiDocumentMediaTypesV1.Jpeg or AiDocumentMediaTypesV1.Tiff:
                return await OcrAsync(input, limits, cancellationToken);

            default:
            {
                var source = input.TextSource == AiDocumentTextSourcesV1.Email ? AiExternalContentSourceV1.Email : AiExternalContentSourceV1.PastedText;
                return FromText(Encoding.UTF8.GetString(input.Bytes), source, AiDocumentReadingTiersV1.PastedText, limits);
            }
        }
    }

    private async Task<Reading> OcrAsync(Input input, AiDocumentDraftLimits limits, CancellationToken cancellationToken)
    {
        if (!ocr.IsAvailable) throw new AiDocumentUnreadableException("ocr_unavailable");
        var result = await ocr.RecognizeAsync(input.Bytes, input.MediaType, cancellationToken);
        if (!result.Recognized || string.IsNullOrWhiteSpace(result.Text) || result.Text.Length > limits.MaxTextLength)
            throw new AiDocumentUnreadableException("ocr_no_text");
        return FromText(result.Text, AiExternalContentSourceV1.ScannedImage, AiDocumentReadingTiersV1.Ocr, limits);
    }

    private static Reading FromText(string text, AiExternalContentSourceV1 source, string tier, AiDocumentDraftLimits limits)
    {
        var quarantined = AiExternalContentQuarantineV1.Admit(text, source);
        return new Reading(tier, AiDocumentTextParser.Parse(quarantined.ReadAsData(), tier, limits.MaxLines), quarantined.Findings, quarantined);
    }

    private static Reading Structured(AiExtractedDocumentV1 document)
    {
        // Every cell is external text too: its findings are reported and keep the cell unverified.
        var values = new[] { document.DocumentNumber, document.DocumentDate, document.Currency, document.CounterpartyTaxId, document.CounterpartyName }
            .Concat(document.Lines.SelectMany(line => new[] { line.ProductCode, line.Description, line.Unit, line.Section, line.WallThickness, line.Length }))
            .Where(value => value is not null)
            .Select(value => value!.Value);
        var quarantined = AiExternalContentQuarantineV1.Admit(string.Join('\n', values), AiExternalContentSourceV1.StructuredFile);
        return new Reading(AiDocumentReadingTiersV1.StructuredFile, document, quarantined.Findings, quarantined);
    }

    private async Task<AiDraftMatchesV1> MatchAsync(string kind, AiExtractedDocumentV1 document, CancellationToken cancellationToken)
    {
        var role = kind == AiDocumentDraftKindsV1.SalesOrder ? AiDraftCounterpartyRoleV1.Customer : AiDraftCounterpartyRoleV1.Supplier;
        AiCounterpartyMatchV1? counterparty = null;
        if (document.CounterpartyTaxId is { Source: not AiDocumentReadingTiersV1.LocalModel } taxId
            && AiDocumentDraftValidator.TaxIdentity.Check(taxId.Value.Trim()) is null
            && AiExternalContentQuarantineV1.Screen(taxId.Value).Count == 0)
        {
            counterparty = await counterpartyMatcher.MatchByTaxIdAsync(taxId.Value.Trim(), role, cancellationToken);
        }

        var effectiveOn = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var products = new Dictionary<int, AiProductMatchV1>();
        for (var index = 0; index < document.Lines.Count; index++)
        {
            var line = document.Lines[index];
            if (line.ProductCode is not { } code || line.ProductCodeIsCounterpartyCode || code.Source == AiDocumentReadingTiersV1.LocalModel
                || AiExternalContentQuarantineV1.Screen(code.Value).Count != 0)
            {
                continue;
            }

            products[index + 1] = await productMatcher.MatchByCodeAsync(code.Value.Trim(), role, effectiveOn, cancellationToken);
        }

        return new AiDraftMatchesV1(counterparty, products);
    }

    private static bool TryReadInput(AiDocumentDraftRequestV1 request, AiDocumentDraftLimits limits, out Input? input, out string? invalidField)
    {
        input = null;
        invalidField = null;
        var hasContent = !string.IsNullOrEmpty(request.ContentBase64);
        var hasText = !string.IsNullOrEmpty(request.Text);
        if (hasContent == hasText)
        {
            invalidField = AiDocumentDraftInvalidFieldsV1.Content;
            return false;
        }

        if (hasText)
        {
            if (request.Text!.Length > limits.MaxTextLength || string.IsNullOrWhiteSpace(request.Text))
            {
                invalidField = AiDocumentDraftInvalidFieldsV1.Text;
                return false;
            }

            if (request.TextSource is not (AiDocumentTextSourcesV1.Email or AiDocumentTextSourcesV1.Pasted) || request.MediaType is not null)
            {
                invalidField = AiDocumentDraftInvalidFieldsV1.TextSource;
                return false;
            }

            input = new Input("text/plain", Encoding.UTF8.GetBytes(request.Text), request.TextSource);
            return true;
        }

        var mediaType = request.MediaType?.Trim().ToLowerInvariant();
        if (mediaType is not (AiDocumentMediaTypesV1.Xml or AiDocumentMediaTypesV1.TextXml or AiDocumentMediaTypesV1.Csv
            or AiDocumentMediaTypesV1.Xlsx or AiDocumentMediaTypesV1.Pdf or AiDocumentMediaTypesV1.Png
            or AiDocumentMediaTypesV1.Jpeg or AiDocumentMediaTypesV1.Tiff) || request.TextSource is not null)
        {
            invalidField = AiDocumentDraftInvalidFieldsV1.MediaType;
            return false;
        }

        // Size is checked on the encoded length first, so an oversized body is never decoded.
        if (request.ContentBase64!.Length > (limits.MaxDocumentBytes + 2L) / 3 * 4 + 4)
        {
            invalidField = AiDocumentDraftInvalidFieldsV1.Content;
            return false;
        }

        var buffer = new byte[request.ContentBase64.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64String(request.ContentBase64, buffer, out var written) || written == 0 || written > limits.MaxDocumentBytes)
        {
            invalidField = AiDocumentDraftInvalidFieldsV1.Content;
            return false;
        }

        input = new Input(mediaType, buffer.AsSpan(0, written).ToArray(), null);
        return true;
    }

    private async Task<string?> FirstMissingPermissionAsync(IReadOnlyList<string> permissions, Guid companyId, CancellationToken cancellationToken)
    {
        foreach (var permission in permissions)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
            if (scope is not { IsGranted: true, IsCompanyWide: true }) return permission;
        }

        return null;
    }

    private string? ResolveActorId() =>
        currentUser.IsAuthenticated && Guid.TryParse(currentUser.UserId, out var userId) && userId != Guid.Empty
            ? userId.ToString("D")
            : null;

    private Guid? ResolvedCompanyOrNull() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId)
            ? companyContext.CompanyId
            : null;

    private async Task RefuseAsync(string eventType, string reasonCode, Guid? companyId, string? actorId, string? permissionCode,
        CancellationToken cancellationToken)
    {
        var policy = AiPolicyReferenceV1.Resolve(AiRuntimeOptionsReader.TryRead(runtimeOptions, out var value) ? value : null);
        // Refusals are fail-open with alarm: the refusal stands whether or not the event was stored.
        await securityEvents.WriteAsync(new SecurityEventV1
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
            ScopeKind = companyId is null ? SecurityEventScopeKindsV1.Platform : SecurityEventScopeKindsV1.Company,
            CompanyId = companyId,
            ActorType = actorId is null ? SecurityEventActorTypesV1.Anonymous : SecurityEventActorTypesV1.User,
            ActorId = actorId,
            EventType = eventType,
            Phase = SecurityEventPhasesV1.Single,
            Outcome = SecurityEventOutcomesV1.Denied,
            ReasonCode = reasonCode,
            Severity = SecurityEventSeveritiesV1.Warning,
            PermissionCode = permissionCode,
            PolicyId = policy.PolicyId,
            PolicyVersion = policy.PolicyVersion,
            CorrelationId = correlationContext.CorrelationId,
            CausationId = correlationContext.CausationId,
            SourceChannel = SourceChannels.AiAssisted,
        }, cancellationToken);
    }

    private sealed record Input(string MediaType, byte[] Bytes, string? TextSource);

    private sealed record Reading(string Tier, AiExtractedDocumentV1 Document, IReadOnlyList<AiQuarantineFindingV1> Findings, AiQuarantinedTextV1? Text);
}
