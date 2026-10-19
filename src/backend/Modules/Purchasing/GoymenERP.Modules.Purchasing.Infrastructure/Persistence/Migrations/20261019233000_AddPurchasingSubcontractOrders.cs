using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;

public partial class AddPurchasingSubcontractOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "SubcontractOrders", schema: "purchasing", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            SupplierPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            SupplierPartyVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CompanyAccountVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            Status = table.Column<int>(type: "int", nullable: false), Version = table.Column<long>(type: "bigint", nullable: false),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
            CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SubcontractOrders", x => x.Id);
            table.UniqueConstraint("AK_SubcontractOrders_CompanyId_Id", x => new { x.CompanyId, x.Id });
            table.CheckConstraint("CK_SubcontractOrders_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SupplierPartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000'");
            table.CheckConstraint("CK_SubcontractOrders_Status", "[Status] IN (1, 2)");
            table.CheckConstraint("CK_SubcontractOrders_Version", "[Version] >= 1");
            table.ForeignKey("FK_SubcontractOrders_Companies_CompanyId", x => x.CompanyId, "organization", "Companies", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable(name: "SubcontractOrderLines", schema: "purchasing", columns: table => new
        {
            CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            LineNo = table.Column<int>(type: "int", nullable: false), LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), ServiceVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            ServiceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), ServiceQuantity = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            SourcePhysicalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), SourcePhysicalRequestVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SubcontractOrderLines", x => new { x.CompanyId, x.OrderId, x.LineNo });
            table.UniqueConstraint("AK_SubcontractOrderLines_CompanyId_OrderId_LineId", x => new { x.CompanyId, x.OrderId, x.LineId });
            table.ForeignKey("FK_SubcontractOrderLines_SubcontractOrders_CompanyId_OrderId", x => new { x.CompanyId, x.OrderId }, "purchasing", "SubcontractOrders", new[] { "CompanyId", "Id" }, onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable(name: "SubcontractAttempts", schema: "purchasing", columns: table => new
        {
            CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
            IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"), RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
            ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false), Outcome = table.Column<int>(type: "int", nullable: false), OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true), CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false), RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SubcontractAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
            table.ForeignKey("FK_SubcontractAttempts_Companies_CompanyId", x => x.CompanyId, "organization", "Companies", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SubcontractAttempts_SubcontractOrders_CompanyId_OrderId", x => new { x.CompanyId, x.OrderId }, "purchasing", "SubcontractOrders", new[] { "CompanyId", "Id" }, onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable(name: "SubcontractAdmissions", schema: "purchasing", columns: table => new
        {
            CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), ServiceVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), ServiceCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false), ServiceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
            ExpectedOrderVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), MaterialOwnerKind = table.Column<int>(type: "int", nullable: false), MaterialOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ExternalLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), ExternalLocationVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), OperatorPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), OperatorPartyVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            SourcePhysicalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), SourcePhysicalRequestVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), AdmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SubcontractAdmissions", x => new { x.CompanyId, x.OrderId, x.LineId });
            table.ForeignKey("FK_SubcontractAdmissions_SubcontractOrderLines_CompanyId_OrderId_LineId", x => new { x.CompanyId, x.OrderId, x.LineId }, "purchasing", "SubcontractOrderLines", new[] { "CompanyId", "OrderId", "LineId" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_SubcontractAttempts_CompanyId_OrderId", "SubcontractAttempts", new[] { "CompanyId", "OrderId" }, schema: "purchasing");
        migrationBuilder.CreateIndex("IX_SubcontractOrderLines_CompanyId_SourcePhysicalRequestId", "SubcontractOrderLines", new[] { "CompanyId", "SourcePhysicalRequestId" }, schema: "purchasing");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [purchasing].[SubcontractOrders]) THROW 51081, 'SubcontractOrders contains durable history; downgrade is refused.', 1;");
        migrationBuilder.DropTable("SubcontractAdmissions", "purchasing");
        migrationBuilder.DropTable("SubcontractAttempts", "purchasing");
        migrationBuilder.DropTable("SubcontractOrderLines", "purchasing");
        migrationBuilder.DropTable("SubcontractOrders", "purchasing");
    }
}
