using System.Text.Json;
using GoymenERP.Modules.Purchasing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseIncomingInvoiceDiagnosticsTests
{
    [Fact]
    public void Finance_handoff_warning_keeps_safe_typed_categories_without_exception_details()
    {
        var logger = new CaptureLogger();
        var diagnostics = new PurchaseIncomingInvoiceDiagnostics(logger);
        var company = Guid.NewGuid(); var invoice = Guid.NewGuid();
        foreach (var (error, category) in new (Exception, string)[]
        {
            (new JsonException("SENTETIK-secret"), nameof(JsonException)),
            (new DbUpdateConcurrencyException("SENTETIK-secret"), nameof(DbUpdateConcurrencyException)),
            (new DbUpdateException("SENTETIK-secret"), nameof(DbUpdateException)),
            (new ArgumentException("SENTETIK-secret"), nameof(ArgumentException)),
            (new UnknownFailure(), nameof(Exception))
        })
        {
            diagnostics.FinanceHandoffFailed(company, invoice, "finance_offer", error);
            var row = logger.Rows.Last();
            Assert.Equal(LogLevel.Warning, row.Level);
            Assert.Null(row.Exception);
            Assert.Equal(category, row.Fields["ErrorType"]);
            Assert.Equal(company, row.Fields["CompanyId"]);
            Assert.Equal(invoice, row.Fields["InvoiceId"]);
            Assert.Equal("finance_offer", row.Fields["Phase"]);
            Assert.Contains("the invoice remains pending", row.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("SENTETIK-secret", row.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(UnknownFailure), row.Message, StringComparison.Ordinal);
        }
        Assert.Equal(5, logger.Rows.Count);
    }

    private sealed class UnknownFailure() : Exception("SENTETIK-secret");
    private sealed class CaptureLogger : ILogger<PurchaseIncomingInvoiceDiagnostics>
    {
        internal readonly List<(LogLevel Level, Exception? Exception, string Message, Dictionary<string, object?> Fields)> Rows = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
            Rows.Add((logLevel, exception, formatter(state, exception), fields.ToDictionary(x => x.Key, x => x.Value)));
        }
    }
}
