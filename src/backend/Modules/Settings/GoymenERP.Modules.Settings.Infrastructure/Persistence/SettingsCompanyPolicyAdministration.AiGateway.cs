using System.Globalization;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// WP-A1 (AI-D24, AI-D30, AI-D34 d): the versioned AI gateway policy on <see cref="SettingsAiGatewayPermissionsV1.Manage"/>.
/// The same K3 path as the holiday calendar: company-wide permission measured first and fresh before every save, mandatory
/// reason, one Idempotency-Key per command (stored terminal answer, another body is 409), expected version, one audit event
/// per real change, insert-only full snapshots. A cloud route above Internal data is refused (AI-D3/AI-D4). An empty
/// ceiling or reservation is a valid "budget closed" record; the code never proposes a budget value (S5, human gate).
/// </summary>
internal sealed partial class SettingsCompanyPolicyAdministration : ISettingsAiGatewayAdministrationV1
{
    private const string AiGatewaySpecKey = "ai.gateway-policy";

    private static readonly KeySpec AiGatewayPolicy = new(AiGatewaySpecKey, SettingsAiGatewayPermissionsV1.Manage,
        "settings.ai-gateway-policy.record.v1", "settings.ai-gateway-policy.recorded.v1", "Settings.AiGatewayPolicy",
        "Yapay zekâ ağ geçidi politikası sürümü kaydedildi");

    public async Task<AiGatewayPolicyViewV1> GetAiGatewayPolicyAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        var actor = currentUser.UserId;
        await RequireAsync(AiGatewayPolicy.Permission, companyId, cancellationToken);
        var policy = await ReadAsync(() => AiGatewayPolicyReader.ReadLatestAsync(database, companyId, cancellationToken));
        var versions = await ReadAsync(() => database.AiGatewayPolicyVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId)
            .OrderByDescending(row => row.Version).ToArrayAsync(cancellationToken));
        var month = SettingsAiBudgetMonthV1.Of(timeProvider.GetUtcNow());
        var usage = await ReadAsync(() => AiGatewayPolicyReader.ReadMonthAsync(database, companyId, month, cancellationToken));
        await RequireStillAsync(AiGatewayPolicy.Permission, companyId, cancellationToken);
        if (RequireContextCompany() != companyId || currentUser.UserId != actor) throw new UnauthorizedAccessException();
        return new(companyId, Text(policy.Version), policy.MonthlyUsageCeiling, policy.MaxUnitsPerRequest,
            policy.EnabledFeatures.Order().Select(feature => feature.ToString()).ToArray(),
            policy.Routes.Select(route => new AiModelRouteViewV1(route.TaskKind.ToString(), route.DataClass.ToString(),
                route.Placement.ToString(), route.ModelDeployment)).ToArray(),
            new AiBudgetMonthViewV1(usage.BudgetMonth, usage.CommittedUnits, usage.EntryCount),
            versions.Select(row => new AiGatewayPolicyVersionViewV1(Text(row.Version), row.MonthlyUsageCeiling,
                row.MaxUnitsPerRequest, row.FeatureCount, row.RouteCount, row.RecordedBy, row.RecordedAtUtc, row.Reason)).ToArray());
    }

    public async Task<CompanyPolicyCommandResultV1> RecordAiGatewayPolicyAsync(RecordAiGatewayPolicyRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (auditWriter is null) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        ValidateMetadata(metadata);
        var spec = AiGatewayPolicy;
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(spec.Permission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        if (request.MonthlyUsageCeiling is < 0 or > SettingsAiGatewayLimitsV1.MaximumMonthlyUsageCeiling)
            return Unstored(Answers.Invalid(string.Create(CultureInfo.InvariantCulture,
                $"Aylık kullanım tavanı boş (kapalı) ya da 0 ile {SettingsAiGatewayLimitsV1.MaximumMonthlyUsageCeiling} arasında olmalıdır.")), metadata);
        if (request.MaxUnitsPerRequest is < 1 or > SettingsAiGatewayLimitsV1.MaximumUnitsPerRequest)
            return Unstored(Answers.Invalid(string.Create(CultureInfo.InvariantCulture,
                $"İstek başına ayrılan birim boş (kapalı) ya da 1 ile {SettingsAiGatewayLimitsV1.MaximumUnitsPerRequest} arasında olmalıdır.")), metadata);
        if (!TryReadFeatures(request.EnabledFeatures, out var features, out var detail)
            || !TryReadRoutes(request.Routes, out var routes, out detail))
            return Unstored(Answers.Invalid(detail), metadata);
        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
            return Unstored(reasonRefusal, metadata);

        var subject = string.Create(CultureInfo.InvariantCulture, $"{companyId:D}:{spec.Key}");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var featurePayload = features.Select(feature => feature.ToString()).ToArray();
        var routePayload = routes.Select(route => new
        {
            TaskKind = route.TaskKind.ToString(),
            DataClass = route.DataClass.ToString(),
            Placement = route.Placement.ToString(),
            route.ModelDeployment,
        }).ToArray();
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            spec.Key, ExpectedVersion = expectedVersion, request.MonthlyUsageCeiling, request.MaxUnitsPerRequest,
            Features = featurePayload, Routes = routePayload, Reason = reason,
        });
        if (await FindExecutionAsync(spec, keyHash, cancellationToken) is { } existing)
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject, Replay(existing, requestHash, metadata));
        }

        var current = await ReadAsync(() => AiGatewayPolicyReader.ReadLatestAsync(database, companyId, cancellationToken));
        if (current.Version != expectedVersion)
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject,
                await StoreRefusalAsync(spec, Answers.VersionConflict(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (current.Version > 0 && current.MonthlyUsageCeiling == request.MonthlyUsageCeiling
            && current.MaxUnitsPerRequest == request.MaxUnitsPerRequest && current.EnabledFeatures.SetEquals(features)
            && current.Routes.SequenceEqual(routes))
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject,
                await StoreRefusalAsync(spec, Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var version = checked(current.Version + 1);
        database.AiGatewayPolicyVersions.Add(new AiGatewayPolicyVersionRow
        {
            CompanyId = companyId,
            Version = version,
            MonthlyUsageCeiling = request.MonthlyUsageCeiling,
            MaxUnitsPerRequest = request.MaxUnitsPerRequest,
            FeatureCount = features.Count,
            RouteCount = routes.Count,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        });
        foreach (var feature in features)
            database.AiGatewayFeatures.Add(new AiGatewayFeatureRow { CompanyId = companyId, Version = version, Feature = (int)feature });
        foreach (var route in routes)
            database.AiGatewayModelRoutes.Add(new AiGatewayModelRouteRow
            {
                CompanyId = companyId,
                Version = version,
                TaskKind = (int)route.TaskKind,
                DataClass = (int)route.DataClass,
                Placement = (int)route.Placement,
                ModelDeployment = route.ModelDeployment,
            });
        Audit(spec, companyId, subject, current.Version == 0 ? null : current.Version, version, scope,
            new
            {
                CompanyId = companyId, Version = version, request.MonthlyUsageCeiling, request.MaxUnitsPerRequest,
                Features = featurePayload, Routes = routePayload,
                PreviousMonthlyUsageCeiling = current.MonthlyUsageCeiling, PreviousMaxUnitsPerRequest = current.MaxUnitsPerRequest,
            }, reason, metadata);
        await RequireReplayStillAsync(spec, metadata, cancellationToken);
        return Done(spec, metadata, subject, await SaveChangeAsync(spec, Answers.NoContent(), keyHash, requestHash, metadata,
            Answers.VersionConflict(), cancellationToken));
    }

    private static bool TryReadFeatures(IReadOnlyList<string>? input, out IReadOnlyList<AiFeatureV1> features, out string detail)
    {
        features = [];
        detail = string.Empty;
        if (input is null)
        {
            detail = "Açık özellikler listesi zorunludur (boş liste \"hepsi kapalı\" demektir).";
            return false;
        }

        var read = new SortedSet<AiFeatureV1>();
        foreach (var name in input)
        {
            if (!TryParseName(name, out AiFeatureV1 feature) || !read.Add(feature))
            {
                detail = "Her özellik bir kez ve tanımlı adla (Panel, Summary, Document, Query, Write, Chat, Scheduled, Memory, Email) girilmelidir.";
                return false;
            }
        }

        features = read.ToArray();
        return true;
    }

    private static bool TryReadRoutes(IReadOnlyList<RecordAiModelRouteV1>? input, out IReadOnlyList<SettingsAiModelRouteV1> routes,
        out string detail)
    {
        routes = [];
        detail = string.Empty;
        if (input is null || input.Count > SettingsAiGatewayLimitsV1.MaximumRoutes)
        {
            detail = string.Create(CultureInfo.InvariantCulture,
                $"Model tablosu zorunludur (boş liste \"model yok\" demektir) ve en fazla {SettingsAiGatewayLimitsV1.MaximumRoutes} satır olabilir.");
            return false;
        }

        var read = new List<SettingsAiModelRouteV1>(input.Count);
        foreach (var route in input)
        {
            if (route is null || !TryParseName(route.TaskKind, out AiTaskKindV1 task) || !TryParseName(route.DataClass, out AiDataClassV1 dataClass)
                || !TryParseName(route.Placement, out AiModelPlacementV1 placement))
            {
                detail = "Her satırda tanımlı görev türü, veri sınıfı ve yerleşim (Cloud veya OnPremises) olmalıdır.";
                return false;
            }

            var deployment = route.ModelDeployment?.Trim() ?? string.Empty;
            if (deployment.Length == 0 || deployment.Length > SettingsAiGatewayLimitsV1.ModelDeploymentMaximumLength
                || deployment.Any(char.IsControl))
            {
                detail = string.Create(CultureInfo.InvariantCulture,
                    $"Model adı zorunludur ve en fazla {SettingsAiGatewayLimitsV1.ModelDeploymentMaximumLength} karakter olabilir.");
                return false;
            }

            if (!AiGatewayPlacementRulesV1.PlacementAllows(placement, dataClass))
            {
                detail = "Bulut model yalnız Public veya Internal veri sınıfında kullanılabilir; gizli ve çok gizli veri şirket dışına çıkmaz.";
                return false;
            }

            if (read.Any(existing => existing.TaskKind == task && existing.DataClass == dataClass))
            {
                detail = "Aynı görev türü ve veri sınıfı için tek satır girilebilir.";
                return false;
            }

            read.Add(new(task, dataClass, placement, deployment));
        }

        routes = read.OrderBy(route => route.TaskKind).ThenBy(route => route.DataClass).ToArray();
        return true;
    }

    /// <summary>An exact, case-sensitive member name of a defined value; numbers and aliases are refused.</summary>
    private static bool TryParseName<TEnum>(string? name, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        return !string.IsNullOrEmpty(name) && !char.IsAsciiDigit(name[0]) && name[0] != '-'
            && Enum.TryParse(name, ignoreCase: false, out value) && Enum.IsDefined(value)
            && string.Equals(value.ToString(), name, StringComparison.Ordinal);
    }
}
