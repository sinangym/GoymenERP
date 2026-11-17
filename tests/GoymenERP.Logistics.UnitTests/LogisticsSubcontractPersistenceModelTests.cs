using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Logistics.UnitTests;

public sealed class LogisticsSubcontractPersistenceModelTests
{
    [Fact]
    public void Shipment_kind_is_immutable_and_defaults_legacy_instructions_to_sale_while_corrections_are_company_bound_append_only()
    {
        using var context = Context(); var model = context.GetService<IDesignTimeModel>().Model;
        var instruction = model.FindEntityType(typeof(LoadingInstructionRow))!;
        Assert.Equal(1, instruction.FindProperty(nameof(LoadingInstructionRow.ShipmentKind))!.GetDefaultValue());
        Assert.Contains(instruction.GetCheckConstraints(), check => check.Name == "CK_LoadingInstructions_ShipmentKind" && check.Sql == "[ShipmentKind] IN (1, 2)");
        var corrections = model.FindEntityType(typeof(VehicleWeighingCorrectionRow))!;
        Assert.Equal(("logistics", "VehicleWeighingCorrections"), (corrections.GetSchema(), corrections.GetTableName()));
        Assert.Equal(["CompanyId", "InstructionId", "Revision"], corrections.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.Single(corrections.GetDeclaredQueryFilters());
        Assert.Contains(corrections.GetForeignKeys(), foreign => foreign.PrincipalEntityType.ClrType == typeof(VehicleWeighingRow)
            && foreign.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "InstructionId"]));
        Assert.Contains(corrections.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "CorrectionId"]));
        Assert.Contains(corrections.GetCheckConstraints(), check => check.Sql == LogisticsDbContext.WeighingCorrectionWeightsCheck);
        Assert.Contains(corrections.GetCheckConstraints(), check => check.Sql == LogisticsDbContext.WeighingCorrectionNetCheck);
        Assert.Contains(corrections.GetCheckConstraints(), check => check.Sql == "[DifferenceKg] = [NetKg] - [LoadedKgAtWeighing]");
        Assert.Equal((18, 3), (corrections.FindProperty(nameof(VehicleWeighingCorrectionRow.NetKg))!.GetPrecision(),
            corrections.FindProperty(nameof(VehicleWeighingCorrectionRow.NetKg))!.GetScale()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_correction_can_never_be_rewritten_or_deleted_before_any_database_connection(bool delete)
    {
        await using var context = Context();
        var row = new VehicleWeighingCorrectionRow { CompanyId = contextCompany, InstructionId = Guid.NewGuid(), Revision = 2,
            CorrectionId = Guid.NewGuid(), Reason = "SENTETIK", CorrectedBy = "SENTETIK" };
        context.WeighingCorrections.Attach(row);
        if (delete) context.Entry(row).State = EntityState.Deleted; else row.Reason = "SENTETIK değişti";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Shipment_kind_cannot_be_changed_in_place_before_any_database_connection()
    {
        await using var context = Context(); var row = new LoadingInstructionRow { Id = Guid.NewGuid(), CompanyId = contextCompany, ShipmentKind = 1 };
        context.Instructions.Attach(row); row.ShipmentKind = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    private static readonly Guid contextCompany = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static LogisticsDbContext Context()
    {
        var company = new CompanyContext(); company.Set(contextCompany);
        return new(new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer("Server=(local);Database=SENTETIK;Integrated Security=true;TrustServerCertificate=true").Options, company);
    }
}
