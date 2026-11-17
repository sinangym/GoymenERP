using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesPersistenceModelTests
{
    [Fact]
    public void Subcontract_replay_sequence_is_nullable_for_untouched_legacy_rows_and_protects_exact_command_history()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var attempt = model.FindEntityType(typeof(SubcontractInvoiceCommandAttemptRow))!;
        Assert.True(attempt.FindProperty(nameof(SubcontractInvoiceCommandAttemptRow.LifecycleSequence))!.IsNullable);
        Assert.Contains(attempt.GetCheckConstraints(), check => check.Sql == SalesDbContext.SubcontractCommandSequenceCheck);
        var migration = new AddSubcontractInvoiceReplaySequence();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal("LifecycleSequence", column.Name); Assert.True(column.IsNullable);
        Assert.Contains("THROW 51937,", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
}
