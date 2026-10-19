using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PurchasingSubcontractSqlServerRelationalAcceptanceTests
{
    [Fact]
    public void Migration_creates_only_purchasing_runtime_tables_with_durable_history_fences()
    {
        var operations = new InspectableMigration().Build();
        var tables = operations.OfType<CreateTableOperation>().Select(x => (x.Schema, x.Name)).ToArray();
        Assert.Equal(new[]
        {
            ("purchasing", "SubcontractOrders"), ("purchasing", "SubcontractOrderLines"),
            ("purchasing", "SubcontractAttempts"), ("purchasing", "SubcontractAdmissions"),
        }, tables);
        Assert.DoesNotContain(operations.OfType<CreateTableOperation>(), operation =>
            operation.Schema is "inventory" or "finance" or "accounting" or "logistics");
        Assert.Contains(operations.OfType<CreateTableOperation>().Single(x => x.Name == "SubcontractOrders").Columns,
            column => column.Name == "RowVersion" && column.IsRowVersion);
    }

    private sealed class InspectableMigration : AddPurchasingSubcontractOrders
    {
        public IReadOnlyList<MigrationOperation> Build()
        {
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
            base.Up(builder);
            return builder.Operations;
        }
    }
}
