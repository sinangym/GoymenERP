using GoymenERP.BuildingBlocks.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Errors;

public static class ErpProblemDetailsExtensions
{
    public static IServiceCollection AddErpProblemDetails(
        this IServiceCollection services)
    {
        services.AddExceptionHandler<ErpUnhandledExceptionHandler>();
        services.AddSingleton<IProblemDetailsWriter, ErpPlatformProblemDetailsWriter>();
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = CustomizeProblemDetails);
        services.AddSingleton<IAuthorizationMiddlewareResultHandler,
            ErpAuthorizationMiddlewareResultHandler>();

        return services;
    }

    internal static bool IsPlatformStatus(int statusCode)
    {
        return statusCode is StatusCodes.Status401Unauthorized
            or StatusCodes.Status403Forbidden
            or StatusCodes.Status500InternalServerError
            or StatusCodes.Status503ServiceUnavailable;
    }

    internal static bool IsPlatformProblem(ProblemDetailsContext context)
    {
        var statusCode = context.ProblemDetails.Status
            ?? context.HttpContext.Response.StatusCode;
        return IsPlatformStatus(statusCode)
            || statusCode == StatusCodes.Status400BadRequest
            && TryGetRequestMetadataCode(context, out _);
    }

    internal static void CustomizeProblemDetails(ProblemDetailsContext context)
    {
        var statusCode = context.ProblemDetails.Status
            ?? context.HttpContext.Response.StatusCode;

        if (statusCode == StatusCodes.Status400BadRequest
            && TryGetReservedRequestMetadataCode(
                context.ProblemDetails,
                out _)
            && !TryGetRequestMetadataCode(context, out _))
        {
            context.ProblemDetails.Extensions.Remove("code");
            return;
        }

        switch (statusCode)
        {
            case StatusCodes.Status400BadRequest
                when TryGetRequestMetadataCode(
                    context,
                    out var requestMetadataCode):
                SetSafeRequestMetadataProblem(
                    context.ProblemDetails,
                    requestMetadataCode);
                break;
            case StatusCodes.Status401Unauthorized:
                SetSafePlatformProblem(
                    context.ProblemDetails,
                    statusCode,
                    ErpProblemCodes.AuthenticationRequired,
                    "Oturum gerekli.",
                    "İşleme devam etmek için güvenli şekilde oturum açın.",
                    "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.2");
                break;
            case StatusCodes.Status403Forbidden:
                SetSafePlatformProblem(
                    context.ProblemDetails,
                    statusCode,
                    ErpProblemCodes.Forbidden,
                    "Erişim reddedildi.",
                    "Bu işlem için gerekli yetkiye sahip değilsiniz.",
                    "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.4");
                break;
            case StatusCodes.Status500InternalServerError:
                SetSafePlatformProblem(
                    context.ProblemDetails,
                    statusCode,
                    ErpProblemCodes.InternalError,
                    "Beklenmeyen bir hata oluştu.",
                    "İşlem tamamlanamadı. Destek için korelasyon kodunu paylaşın.",
                    "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.1");
                break;
            case StatusCodes.Status503ServiceUnavailable:
                SetSafePlatformProblem(
                    context.ProblemDetails,
                    statusCode,
                    ErpProblemCodes.ServiceUnavailable,
                    "Hizmet geçici olarak kullanılamıyor.",
                    "Lütfen daha sonra yeniden deneyin.",
                    "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4");
                break;
            default:
                return;
        }

        var correlationContext = context.HttpContext.RequestServices
            .GetService<ICorrelationContext>();
        if (correlationContext?.IsResolved == true)
        {
            context.ProblemDetails.Extensions["correlationId"] =
                correlationContext.CorrelationId;
        }
    }

    private static bool TryGetRequestMetadataCode(
        ProblemDetailsContext context,
        out string code)
    {
        if (ErpRequestMetadataProblem.TryGetOwnedCode(
                context.HttpContext,
                context.ProblemDetails,
                out var candidate)
            && IsReservedRequestMetadataCode(candidate))
        {
            code = candidate;
            return true;
        }

        code = string.Empty;
        return false;
    }

    private static bool TryGetReservedRequestMetadataCode(
        ProblemDetails problemDetails,
        out string code)
    {
        if (problemDetails.Extensions.TryGetValue("code", out var value)
            && value is string candidate
            && IsReservedRequestMetadataCode(candidate))
        {
            code = candidate;
            return true;
        }

        code = string.Empty;
        return false;
    }

    private static bool IsReservedRequestMetadataCode(string code)
    {
        return code is ErpProblemCodes.CompanyContextInvalid
            or ErpProblemCodes.CorrelationMetadataInvalid;
    }

    private static void SetSafeRequestMetadataProblem(
        ProblemDetails problemDetails,
        string code)
    {
        var (title, detail) = code switch
        {
            ErpProblemCodes.CompanyContextInvalid => (
                "Şirket seçimi gerekli.",
                "İşleme devam etmek için geçerli bir şirket seçin."),
            ErpProblemCodes.CorrelationMetadataInvalid => (
                "Korelasyon bilgisi geçersiz.",
                "Korelasyon ve neden kimlikleri en fazla 128 güvenli karakter içermelidir."),
            _ => throw new InvalidOperationException(
                "Bilinmeyen platform istek metadata hata kodu."),
        };

        SetSafePlatformProblem(
            problemDetails,
            StatusCodes.Status400BadRequest,
            code,
            title,
            detail,
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1");
    }

    private static void SetSafePlatformProblem(
        ProblemDetails problemDetails,
        int statusCode,
        string code,
        string title,
        string detail,
        string type)
    {
        problemDetails.Extensions.Clear();
        problemDetails.Status = statusCode;
        problemDetails.Type = type;
        problemDetails.Title = title;
        problemDetails.Detail = detail;
        problemDetails.Instance = null;
        problemDetails.Extensions["code"] = code;
    }
}
