using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Identity.UnitTests;

public sealed class ProductMasterCogsClassificationModelTests
{
    [Fact]
    public void Cogs_is_an_explicit_global_operation_seven_without_a_company_assignment_or_a_default_grant()
    {
        Assert.Equal(7, (int)ProductMasterOperation.SetCogsClassification);
        using var context = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=localhost;Database=MODEL_ONLY;Integrated Security=true;TrustServerCertificate=true").Options);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ProductMasterAssignment))!;
        Assert.Equal("identity", entity.GetSchema());
        Assert.Equal("ProductMasterAssignments", entity.GetTableName());
        Assert.Equal(ProductMasterAssignmentModel.OperationCheck,
            entity.GetCheckConstraints().Single(x => x.Name == "CK_ProductMasterAssignments_Operation").Sql);
        Assert.Equal("[Operation] IN (1, 2, 3, 4, 5, 6, 7)", ProductMasterAssignmentModel.OperationCheck);
        Assert.Null(entity.FindProperty("CompanyId"));
        Assert.True(entity.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "UserId", "Operation" })).IsUnique);
        Assert.True(entity.FindProperty(nameof(ProductMasterAssignment.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(4, entity.GetForeignKeys().Count());
        Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
    }
}
