using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai.Feedback;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiFeedbackRequestV1(string? ExpectedVersion, string? Kind, string? ReasonCode);
public sealed record AiFeedbackViewV1(Guid AnswerId, string Version, string? Kind, string? ReasonCode);
public sealed record AiFeedbackReceiptViewV1(Guid AnswerId, string Version, string Kind, string? ReasonCode, string OriginalCorrelationId, bool Replayed);
public enum AiFeedbackApiStatusV1 { Succeeded, Disabled, Invalid, Refused, NotFound, VersionConflict, IdempotencyConflict, Unavailable }
public sealed record AiFeedbackApiResultV1(AiFeedbackApiStatusV1 Status, AiFeedbackViewV1? Current = null, AiFeedbackReceiptViewV1? Receipt = null);

/// <summary>Records the user's observation only; no prompt, correction text, ERP command or rule publication.</summary>
public sealed class AiFeedbackServiceV1(IOptions<AiRuntimeOptions> options, IAiFeedbackPersistenceV1? persistence,
    ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority,
    IActorSessionBindingResolver sessions, ICorrelationContext correlation)
{
    public async Task<AiFeedbackApiResultV1> RecordAsync(Guid answerId, AiFeedbackRequestV1? request, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return new(AiFeedbackApiStatusV1.Disabled);
        if (!Capture(out var companyId, out var actorId)) return new(AiFeedbackApiStatusV1.Refused);
        if (answerId == Guid.Empty || request is null || !long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version is < 0 or long.MaxValue || version.ToString(CultureInfo.InvariantCulture) != request.ExpectedVersion
            || !Enum.TryParse<AiFeedbackKindV1>(request.Kind, out var kind) || kind.ToString() != request.Kind)
            return new(AiFeedbackApiStatusV1.Invalid);
        var command = new AiFeedbackAppendV1(companyId, answerId, actorId, version, kind, request.ReasonCode, idempotencyKey, correlation.CorrelationId);
        if (!AiFeedbackRulesV1.Valid(command)) return new(AiFeedbackApiStatusV1.Invalid);
        var session = await sessions.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(session)) return new(AiFeedbackApiStatusV1.Refused);
        try
        {
            await Fence(companyId, actorId, session, cancellationToken);
            if (persistence is null) return new(AiFeedbackApiStatusV1.Unavailable);
            var result = await persistence.AppendAsync(command, ct => Fence(companyId, actorId, session, ct), cancellationToken);
            await Fence(companyId, actorId, session, cancellationToken); // A committed observation does not prove response delivery.
            if (result.Status is AiFeedbackWriteStatusV1.Recorded or AiFeedbackWriteStatusV1.Replayed)
            {
                if (result.Event is not { } value || !AiFeedbackRulesV1.Valid(value) || value.CompanyId != companyId || value.AnswerId != answerId
                    || value.Version != version + 1 || value.Kind != kind || value.ReasonCode != request.ReasonCode
                    || !AiAnswerReceiptRulesV1.Token(result.OriginalCorrelationId)) return new(AiFeedbackApiStatusV1.Unavailable);
                return new(AiFeedbackApiStatusV1.Succeeded, Receipt: new(answerId, value.Version.ToString(CultureInfo.InvariantCulture),
                    value.Kind.ToString(), value.ReasonCode, result.OriginalCorrelationId!, result.Status == AiFeedbackWriteStatusV1.Replayed));
            }
            return new(result.Status switch {
                AiFeedbackWriteStatusV1.Invalid => AiFeedbackApiStatusV1.Invalid, AiFeedbackWriteStatusV1.Refused => AiFeedbackApiStatusV1.Refused,
                AiFeedbackWriteStatusV1.NotFound => AiFeedbackApiStatusV1.NotFound, AiFeedbackWriteStatusV1.VersionConflict => AiFeedbackApiStatusV1.VersionConflict,
                AiFeedbackWriteStatusV1.IdempotencyConflict => AiFeedbackApiStatusV1.IdempotencyConflict, _ => AiFeedbackApiStatusV1.Unavailable });
        }
        catch (AiFeedbackAuthorityLostException) { return new(AiFeedbackApiStatusV1.Refused); }
        catch (Exception error) when (error is not OperationCanceledException) { return new(AiFeedbackApiStatusV1.Unavailable); }
    }
    public async Task<AiFeedbackApiResultV1> GetAsync(Guid answerId, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return new(AiFeedbackApiStatusV1.Disabled);
        if (!Capture(out var companyId, out var actorId)) return new(AiFeedbackApiStatusV1.Refused);
        if (answerId == Guid.Empty) return new(AiFeedbackApiStatusV1.Invalid);
        var session = await sessions.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(session)) return new(AiFeedbackApiStatusV1.Refused);
        try
        {
            await Fence(companyId, actorId, session, cancellationToken);
            if (persistence is null) return new(AiFeedbackApiStatusV1.Unavailable);
            var value = await persistence.GetOwnCurrentAsync(companyId, answerId, actorId, ct => Fence(companyId, actorId, session, ct), cancellationToken);
            await Fence(companyId, actorId, session, cancellationToken);
            if (value is null) return new(AiFeedbackApiStatusV1.NotFound);
            if (value.AnswerId != answerId || value.Version < 0 || (value.Version == 0 ? value.Kind is not null || value.ReasonCode is not null
                : value.Kind is null || !AiFeedbackReasonsV1.Valid(value.Kind.Value, value.ReasonCode))) return new(AiFeedbackApiStatusV1.Unavailable);
            return new(AiFeedbackApiStatusV1.Succeeded, new(answerId, value.Version.ToString(CultureInfo.InvariantCulture), value.Kind?.ToString(), value.ReasonCode));
        }
        catch (AiFeedbackAuthorityLostException) { return new(AiFeedbackApiStatusV1.Refused); }
        catch (Exception error) when (error is not OperationCanceledException) { return new(AiFeedbackApiStatusV1.Unavailable); }
    }
    private bool Capture(out Guid companyId, out Guid actorId)
    {
        companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        return Guid.TryParse(user.UserId, out actorId) && Current(companyId, actorId);
    }
    private bool Current(Guid companyId, Guid actorId) => companyId != Guid.Empty && actorId != Guid.Empty && company.IsResolved
        && company.CompanyId == companyId && user.IsAuthenticated && Guid.TryParse(user.UserId, out var actor) && actor == actorId && user.CanAccessCompany(companyId);
    private async Task Fence(Guid companyId, Guid actorId, string session, CancellationToken token)
    {
        if (!options.Value.Enabled || !Current(companyId, actorId)) throw new AiFeedbackAuthorityLostException();
        var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.AiPanelUse, companyId, token);
        if (scope is not { IsGranted: true, IsCompanyWide: true } || !Current(companyId, actorId)) throw new AiFeedbackAuthorityLostException();
        if (await sessions.ResolveAsync(token) != session || !Current(companyId, actorId)) throw new AiFeedbackAuthorityLostException();
    }
}
