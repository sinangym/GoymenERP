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
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = CustomizeProblemDetails);
        services.AddSingleton<IAuthorizationMiddlewareResultHandler,
            ErpAuthorizationMiddlewareResultHandler>();

        return services;
    }

    internal static void CustomizeProblemDetails(ProblemDetailsContext context)
    {
        var statusCode = context.ProblemDetails.Status
            ?? context.HttpContext.Response.StatusCode;

        switch (statusCode)
        {
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
