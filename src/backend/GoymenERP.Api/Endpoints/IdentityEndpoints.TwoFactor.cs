using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.Endpoints;

public sealed record LocalTwoFactorCommandViewV1(string CredentialVersion, bool Enabled, IReadOnlyList<string>? RecoveryCodes)
{ public override string ToString() => nameof(LocalTwoFactorCommandViewV1); }

public static partial class IdentityEndpoints
{
    private const string TwoFactorProofInvalid = "identity.two_factor.proof_invalid";
    private const string TwoFactorStateConflict = "identity.two_factor.state_conflict";
    private static void MapLocalTwoFactor(RouteGroupBuilder group)
    {
        group.MapGet("/session/two-factor", async (HttpContext context, IAuthenticationSchemeProvider schemes,
            ILocalTwoFactorServiceV1 service, CancellationToken ct) =>
        {
            NoStore(context);
            if (await schemes.GetSchemeAsync(LocalAccountSession.SchemeName) is null) return NotFound();
            if (!LocalAccountSession.TryReadSession(context.User, out var id, out var stamp)) return Results.Problem(statusCode: 401);
            try
            {
                var state = await service.ReadAsync(id, stamp, ct);
                return state is null ? Results.Problem(statusCode: 401) : Results.Ok(state);
            }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch (Exception error) when (error is DbException or DbUpdateException or InvalidOperationException or CryptographicException or JsonException) { return Results.Problem(statusCode: 503); }
        }).RequireCompanyContext().WithName("GetLocalTwoFactorStateV1").Produces<LocalTwoFactorStateV1>()
            .ProducesErpCodeLessNotFound().ProducesErpBadRequest().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/session/two-factor/enrollment", async (LocalTwoFactorProofV1 request, HttpContext context,
            IAuthenticationSchemeProvider schemes, ILocalTwoFactorServiceV1 service, CancellationToken ct) =>
        {
            NoStore(context);
            if (await schemes.GetSchemeAsync(LocalAccountSession.SchemeName) is null) return NotFound();
            if (!LocalAccountSession.TryReadSession(context.User, out var id, out var stamp)) return Results.Problem(statusCode: 401);
            try
            {
                var result = await service.BeginEnrollmentAsync(id, stamp, request, ct);
                return result is null ? TwoFactorProblem(context, 400, TwoFactorProofInvalid) : Results.Ok(result);
            }
            catch (LocalTwoFactorSessionNotCurrentException) { return Results.Problem(statusCode: 401); }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch (Exception error) when (error is DbException or DbUpdateException or InvalidOperationException or CryptographicException or JsonException) { return Results.Problem(statusCode: 503); }
        }).RequireCompanyContext().WithMetadata(LocalAccountAntiforgery.Required).WithName("PrepareLocalTwoFactorEnrollmentV1")
            .Accepts<LocalTwoFactorProofV1>("application/json").Produces<LocalTwoFactorEnrollmentV1>()
            .ProducesErpBadRequest(TwoFactorProofInvalid).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        foreach (var action in new[] { "enable", "disable", "recovery-codes" })
        {
            group.MapPost($"/session/two-factor/{action}", async (LocalTwoFactorProofV1 request, HttpContext context,
                IAuthenticationSchemeProvider schemes, ILocalTwoFactorServiceV1 service, CancellationToken ct) =>
            {
                NoStore(context);
                if (await schemes.GetSchemeAsync(LocalAccountSession.SchemeName) is null) return NotFound();
                if (!LocalAccountSession.TryReadSession(context.User, out var id, out var stamp)) return Results.Problem(statusCode: 401);
                if (!TryReadIdempotencyKey(context.Request.Headers, out var key))
                    return TwoFactorProblem(context, 400, IdentitySessionProblemCodesV1.IdempotencyKeyInvalid);
                var services = context.RequestServices;
                var company = services.GetRequiredService<ICompanyContext>();
                var current = services.GetRequiredService<ICurrentUser>();
                var correlation = services.GetRequiredService<ICorrelationContext>();
                try
                {
                    var result = await service.ChangeAsync(id, stamp, action, request, new(company.CompanyId, current.UserId ?? string.Empty,
                        correlation.CorrelationId, correlation.CausationId, correlation.SourceChannel, correlation.ClientIpAddress,
                        correlation.UserAgent, key), ct);
                    switch (result.Status)
                    {
                        case LocalTwoFactorCommandStatusV1.Applied when result.SecurityStamp is { } newStamp && result.Version is { } version:
                            var authentication = await context.AuthenticateAsync(LocalAccountSession.SchemeName);
                            var started = LocalAccountSession.TryReadStartedAtUtc(authentication.Properties, out var original)
                                ? original : services.GetRequiredService<TimeProvider>().GetUtcNow();
                            await context.SignInAsync(LocalAccountSession.SchemeName, LocalAccountSession.CreatePrincipal(id, newStamp), LocalAccountSession.CreateProperties(started));
                            return Results.Ok(new LocalTwoFactorCommandViewV1(version, result.Enabled, result.RecoveryCodes));
                        case LocalTwoFactorCommandStatusV1.Replayed when result.Version is { } replayVersion && !string.IsNullOrWhiteSpace(result.OriginalCorrelationId):
                            context.Response.Headers[IdempotencyReplayedHeaderName] = "true";
                            context.Response.Headers[IdempotencyOriginalCorrelationHeaderName] = result.OriginalCorrelationId;
                            return Results.Ok(new LocalTwoFactorCommandViewV1(replayVersion, result.Enabled, null));
                        case LocalTwoFactorCommandStatusV1.SessionNotCurrent: return Results.Problem(statusCode: 401);
                        case LocalTwoFactorCommandStatusV1.VersionConflict: return TwoFactorProblem(context, 409, IdentitySessionProblemCodesV1.VersionConflict);
                        case LocalTwoFactorCommandStatusV1.IdempotencyConflict: return TwoFactorProblem(context, 409, IdentitySessionProblemCodesV1.IdempotencyConflict);
                        case LocalTwoFactorCommandStatusV1.StateConflict: return TwoFactorProblem(context, 409, TwoFactorStateConflict);
                        case LocalTwoFactorCommandStatusV1.InvalidProof: return TwoFactorProblem(context, 400, TwoFactorProofInvalid);
                        default: return TwoFactorProblem(context, 400, IdentitySessionProblemCodesV1.RequestInvalid);
                    }
                }
                catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
                catch (Exception error) when (error is DbException or DbUpdateException or InvalidOperationException or CryptographicException or JsonException) { return Results.Problem(statusCode: 503); }
            }).RequireCompanyContext().WithMetadata(LocalAccountAntiforgery.Required)
                .WithName(action == "enable" ? "EnableLocalTwoFactorV1" : action == "disable" ? "DisableLocalTwoFactorV1" : "RegenerateLocalRecoveryCodesV1")
                .WithIdempotencyContract(200).Accepts<LocalTwoFactorProofV1>("application/json").Produces<LocalTwoFactorCommandViewV1>()
                .ProducesErpBadRequest(TwoFactorProofInvalid, IdentitySessionProblemCodesV1.IdempotencyKeyInvalid, IdentitySessionProblemCodesV1.RequestInvalid)
                .AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
                .ProducesErpConflict(TwoFactorStateConflict, IdentitySessionProblemCodesV1.VersionConflict, IdentitySessionProblemCodesV1.IdempotencyConflict)
                .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        }
    }
    private static IResult TwoFactorProblem(HttpContext context, int status, string code) => Results.Problem(statusCode: status,
        title: "Hesap güvenliği işlemi tamamlanamadı.", detail: "Güncel hesap durumunu okuyup kimlik doğrulama bilgilerinizi kontrol edin.",
        extensions: new Dictionary<string, object?> { ["code"] = code,
            ["correlationId"] = context.RequestServices.GetRequiredService<ICorrelationContext>().CorrelationId });
}
