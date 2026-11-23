using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// KIM-D39: observing the second active user closes the relaxation durably before a successful policy answer.
/// A fresh owner scope supplies the configured Settings context and audit collector/interceptor. Its separate connection
/// means a consumer's pending writes or transaction cannot defer, flush or roll back this system closure.
/// The new version and its system audit are committed together; the version key arbitrates concurrent readers.
/// </summary>
internal static class SegregationOfDutiesAutomaticClosure
{
    private const string ClosedEvent = "settings.segregation-of-duties.closed-automatically.v1";
    private const string Reason =
        "Şirkette ikinci aktif kullanıcı olduğu için hazırlayanın onaylaması gevşetmesi sistem tarafından kapatıldı (KIM-D39).";

    public static async Task<SegregationOfDutiesPolicyVersionRow> CloseAsync(IServiceScopeFactory? scopes,
        Guid companyId, string observedBy, ICorrelationContext? correlation, TimeProvider time,
        Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A DI scope is not a transaction boundary. Every entry path must refuse ambient enlistment;
        // otherwise SaveChanges could return while the caller can still roll back the version and audit.
        if (System.Transactions.Transaction.Current is not null) throw Unavailable();
        if (scopes is null || correlation is null || !correlation.IsResolved || string.IsNullOrWhiteSpace(correlation.CorrelationId))
            throw Unavailable();

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
            var database = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
            var collector = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
            if (database.ChangeTracker.Entries().Any() || database.Database.CurrentTransaction is not null)
                throw Unavailable();
            var latest = await SegregationOfDutiesPolicyReads.LatestAsync(database, cancellationToken);
            if (latest is null || latest.CompanyId != companyId || latest.Version <= 0) throw Unavailable();
            if (!latest.PreparerMayApprove) return latest;

            var row = new SegregationOfDutiesPolicyVersionRow
            {
                CompanyId = companyId,
                Version = checked(latest.Version + 1),
                PreparerMayApprove = false,
                RecordedAtUtc = time.GetUtcNow(),
                RecordedBy = SegregationOfDutiesRuleV1.AutomaticCloseActorId,
                Reason = Reason,
            };
            database.SegregationOfDutiesPolicyVersions.Add(row);
            collector.Enqueue(new AuditEventDraft(
                companyId, ClosedEvent, "Settings.SegregationOfDutiesPolicy", companyId.ToString("D"),
                "CloseAutomatically", latest.Version, row.Version, AuditActorTypes.System,
                SegregationOfDutiesRuleV1.AutomaticCloseActorId, SettingsSegregationOfDutiesPolicyPermissionManifestV1.Manage,
                [], "{}", JsonSerializer.Serialize(new
                {
                    CompanyId = companyId,
                    row.Version,
                    row.PreparerMayApprove,
                    PreviousPreparerMayApprove = true,
                    Trigger = "SecondActiveUser",
                    ObservedBy = observedBy,
                    Reason,
                }, JsonSerializerOptions.Web),
                "Görev ayrılığı gevşetmesi ikinci aktif kullanıcı nedeniyle sistem tarafından kapatıldı",
                ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, SourceChannels.System,
                correlation.ClientIpAddress, correlation.UserAgent, correlation.CorrelationId, correlation.CausationId,
                string.Create(CultureInfo.InvariantCulture, $"{ClosedEvent}:{companyId:D}:{row.Version}")));
            await requireStill(cancellationToken);
            if (System.Transactions.Transaction.Current is not null) throw Unavailable();
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return row;
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // Only a committed false version proves another reader completed this closure, including its audit.
                var winner = await SegregationOfDutiesPolicyReads.LatestAsync(database, cancellationToken);
                if (winner is not null && winner.CompanyId == companyId && !winner.PreparerMayApprove
                    && winner.Version >= row.Version) return winner;
                throw Unavailable();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException
            and not UnauthorizedAccessException)
        {
            throw Unavailable();
        }
    }

    private static SettingsPrerequisiteException Unavailable() =>
        new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);
}
