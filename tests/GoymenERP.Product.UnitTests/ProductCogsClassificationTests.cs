using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductCogsClassificationTests
{
    [Fact]
    public void Known_card_without_an_explicit_class_has_no_default_or_account()
    {
        var view = ProductCogsClassificationParticipant.View(Guid.NewGuid(),Guid.NewGuid(),3,null);
        Assert.Equal(("3","0"),(view.ProductVersion,view.ClassificationVersion));
        Assert.Null(view.Classification); Assert.Null(view.ChangedInCompanyId); Assert.Null(view.UpdatedAtUtc);
        Assert.Equal("product.cogs-classification.v1",view.SourceVersion);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void Owner_projection_preserves_explicit_kind_origin_and_independent_versions(int kind)
    {
        var product = Guid.NewGuid(); var company = Guid.NewGuid(); var row = Row(product,company,kind);
        var view = ProductCogsClassificationParticipant.View(company,product,9,row);
        Assert.Equal(("9","2"),(view.ProductVersion,view.ClassificationVersion));
        Assert.Equal((ProductCogsClassificationKindV1)kind,view.Classification); Assert.Equal(company,view.ChangedInCompanyId);
    }
    [Theory]
    [InlineData("kind")] [InlineData("version")] [InlineData("product-version")]
    [InlineData("product")] [InlineData("origin")] [InlineData("actor")]
    [InlineData("reason")] [InlineData("authority")] [InlineData("correlation")] [InlineData("timestamp")]
    public void Invalid_stored_evidence_never_becomes_an_available_class(string change)
    {
        var product = Guid.NewGuid(); var company = Guid.NewGuid(); var row = Row(product,company,1);
        switch(change)
        {
            case "kind":row.Kind=0;break; case "version":row.Version=0;break;
            case "product-version":row.RecordedProductVersion=10;break; case "product":row.ProductId=Guid.NewGuid();break;
            case "origin":row.OriginCompanyId=Guid.Empty;break;case "actor":row.ActorId=Guid.Empty;break;
            case "reason":row.Reason=" ";break;case "authority":row.AuthorityRevision=" revision ";break;
            case "correlation":row.CorrelationId="bad\n";break;
            case "timestamp":row.RecordedAtUtc=row.RecordedAtUtc.ToOffset(TimeSpan.FromHours(3));break;
        }
        Assert.Throws<InvalidOperationException>(() => ProductCogsClassificationParticipant.View(company,product,9,row));
    }
    [Fact]
    public void The_global_append_only_model_has_product_fk_and_origin_evidence_without_company_partition_or_seed()
    {
        using var db = Context(); var model=db.GetService<IDesignTimeModel>().Model;
        var row=model.FindEntityType(typeof(ProductCogsClassificationRow))!;
        Assert.Equal("product",row.GetSchema()); Assert.Equal("CogsClassifications",row.GetTableName());
        Assert.Equal(new[]{"ProductId","Version"},row.FindPrimaryKey()!.Properties.Select(x=>x.Name));
        Assert.Empty(row.GetDeclaredQueryFilters()); Assert.Empty(row.GetSeedData());
        Assert.Equal(DeleteBehavior.Restrict,Assert.Single(row.GetForeignKeys()).DeleteBehavior);
        Assert.Contains(row.GetCheckConstraints(),x=>x.Name=="CK_CogsClassifications_Kind" && x.Sql=="[Kind] IN (1,2)");
        Assert.Equal(1000,row.FindProperty("Reason")!.GetMaxLength());
    }
    [Theory]
    [InlineData(EntityState.Added)] [InlineData(EntityState.Modified)] [InlineData(EntityState.Deleted)]
    public void Public_Product_writes_cannot_insert_or_mutate_classification_history(EntityState state)
    {
        using var db=Context(); var row=Row(Guid.NewGuid(),Guid.NewGuid(),1);db.Attach(row);db.Entry(row).State=state;
        Assert.Throws<InvalidOperationException>(()=>db.SaveChanges());
    }
    [Fact]
    public void The_generated_migration_only_adds_the_owner_history_and_refuses_lossy_Down_first()
    {
        using var db=Context();Assert.False(db.Database.HasPendingModelChanges());
        var migration=new AddProductCogsClassifications();
        Assert.Equal("CogsClassifications",Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        Assert.DoesNotContain(migration.UpOperations,x=>x is AlterColumnOperation or AddColumnOperation or SqlOperation);
        Assert.Contains("THROW 51978",Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql,StringComparison.Ordinal);
    }
    [Fact]
    public void Capability_requires_only_its_explicit_global_operation_and_never_inherits_a_company_permission()
    {
        var capability=Assert.Single(new ProductShellCapabilityProvider().GetDeclarations(),x=>x.CapabilityId=="product.items.set-cogs-classification");
        Assert.Equal(GroupSharedMasterOperation.SetCogsClassification,capability.GlobalProductOperation);
        Assert.Empty(capability.RequiredPermissions);Assert.Equal("product.items.detail",capability.ParentCapabilityId);
        Assert.Equal("/urun/:productId",capability.RoutePath);
        const string name="product.cogs-classifications.manage";
        Assert.Single(ErpPermissions.All,x=>x==name);Assert.False(ErpPermissions.IsCritical(name));
        Assert.Equal(new[]{name},ProductCogsClassificationPermissionManifestV1.Names);
    }
    private static ProductDbContext Context()=>new(new DbContextOptionsBuilder<ProductDbContext>()
        .UseSqlServer("Server=localhost;Database=GoymenERP_ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options);
    private static ProductCogsClassificationRow Row(Guid product,Guid company,int kind)=>new()
    {
        ProductId=product,Version=2,RecordedProductVersion=4,Kind=kind,OriginCompanyId=company,ActorId=Guid.NewGuid(),
        AuthorityRevision="source-revision",Reason="SENTETIK explicit class",CorrelationId="synthetic-correlation",
        RecordedAtUtc=new(2026,11,17,0,0,0,TimeSpan.Zero),
    };
}
