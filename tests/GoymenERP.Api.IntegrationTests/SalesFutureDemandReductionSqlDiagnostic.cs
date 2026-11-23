using System.Data;
using System.Diagnostics;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Test-only closed diagnostics. The real participant and its original refusal remain unchanged.</summary>
internal sealed class SalesFutureDemandReductionSqlDiagnostic
{
    private string summary = "not_observed";
    public string Summary => Volatile.Read(ref summary);

    public void Register(IServiceCollection services)
    {
        services.RemoveAll<ISalesOrderFutureDemandReductionParticipantV1>();
        services.AddScoped<ISalesOrderFutureDemandReductionParticipantV1>(provider =>
            new ObservedParticipant(ActivatorUtilities.CreateInstance<SalesFutureDemandReductionHostParticipant>(provider), this));
    }

    private void Capture(Exception exception)
    {
        var kind = "unknown";
        var sql = "not_observed";
        var source = "unknown";
        for (var depth = 0; exception is not null && depth < 8; depth++, exception = exception.InnerException!)
        {
            var candidate = exception switch
            {
                SqlException => "sql_client",
                DbUpdateException => "db_update",
                SalesFutureAllocationReleaseException => "sales_future_release",
                InventoryAllocationException => "inventory_allocation",
                ArgumentException => "argument",
                InvalidOperationException => "invalid_operation",
                OperationCanceledException => "cancelled",
                _ => "unknown",
            };
            if (candidate != "unknown") kind = candidate;
            if (exception is SqlException error)
                sql = error.Number switch
                {
                    -2 => "sql_timeout", 1205 => "sql_deadlock", 207 => "sql_invalid_column",
                    208 => "sql_invalid_object", 468 => "sql_collation", 547 => "sql_constraint",
                    2601 or 2627 => "sql_unique", 4060 => "sql_database_unavailable",
                    18456 => "sql_authentication", _ => "sql_unknown",
                };
            var stack = new StackTrace(exception, fNeedFileInfo: false);
            for (var frame = 0; frame < Math.Min(stack.FrameCount, 32); frame++)
            {
                var type = stack.GetFrame(frame)?.GetMethod()?.DeclaringType?.FullName;
                var tag = type switch
                {
                    not null when type.Contains("InventoryFutureAllocationReleaseSession", StringComparison.Ordinal) => "inventory_future_release",
                    not null when type.Contains("SalesOrderLineSerialization", StringComparison.Ordinal) => "sales_line_lock",
                    not null when type.Contains("PurchaseLineSerializationLock", StringComparison.Ordinal) => "purchase_line_lock",
                    not null when type.Contains("SalesFutureDemandReductionHostParticipant", StringComparison.Ordinal) => "sales_future_demand",
                    _ => "unknown",
                };
                if (tag != "unknown") { source = tag; break; }
            }
        }
        Interlocked.Exchange(ref summary, $"kind={kind}; sql={sql}; source={source}");
    }

    private sealed class ObservedParticipant(SalesFutureDemandReductionHostParticipant actual,
        SalesFutureDemandReductionSqlDiagnostic diagnostic) : ISalesOrderFutureDemandReductionParticipantV1
    {
        public async Task ReduceForAcceptedRevisionAsync(IDbTransaction transaction,
            SalesOrderFutureDemandReductionRequestV1 request, CancellationToken cancellationToken = default)
        {
            try { await actual.ReduceForAcceptedRevisionAsync(transaction, request, cancellationToken); }
            catch (Exception exception) { diagnostic.Capture(exception); throw; }
        }
    }
}
