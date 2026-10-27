using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesDiscountLimitGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesDiscountLimitGroups",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LimitVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDiscountLimitGroups", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SettingsDiscountLimitGroups_Actors", "LEN(LTRIM(RTRIM([CreatedBy]))) > 0 AND LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsDiscountLimitGroups_Code", "LEN([Code]) BETWEEN 1 AND 32 AND [Code] NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_SettingsDiscountLimitGroups_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsDiscountLimitGroups_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsDiscountLimitGroups_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_SettingsDiscountLimitGroups_Version", "[Version] > 0 AND [LimitVersion] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "SalesDiscountLimitMemberships",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDiscountLimitMemberships", x => new { x.CompanyId, x.UserId });
                    table.CheckConstraint("CK_SettingsDiscountLimitMemberships_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsDiscountLimitMemberships_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsDiscountLimitMemberships_User", "[UserId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsDiscountLimitMemberships_Version", "[Version] > 0");
                });

            migrationBuilder.CreateTable(
                name: "SalesDiscountLimitVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LimitGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    LinkedGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExtraPoints = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDiscountLimitVersions", x => new { x.CompanyId, x.LimitGroupId, x.Version });
                    table.CheckConstraint("CK_SettingsDiscountLimitVersions_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsDiscountLimitVersions_ExtraPoints", "[ExtraPoints] IS NULL OR ([ExtraPoints] >= 0 AND [ExtraPoints] <= 100)");
                    table.CheckConstraint("CK_SettingsDiscountLimitVersions_Kind", "[Kind] IN ('Fixed', 'Linked')");
                    table.CheckConstraint("CK_SettingsDiscountLimitVersions_Percent", "[Percent] IS NULL OR ([Percent] >= 0 AND [Percent] <= 100)");
                    table.CheckConstraint("CK_SettingsDiscountLimitVersions_Profile", "[ProfileId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsDiscountLimitVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsDiscountLimitVersions_Shape", "([Kind] = 'Fixed' AND [Percent] IS NOT NULL AND [LinkedGroupId] IS NULL AND [ExtraPoints] IS NULL) OR ([Kind] = 'Linked' AND [Percent] IS NULL AND [LinkedGroupId] IS NOT NULL AND [ExtraPoints] IS NOT NULL AND [LinkedGroupId] <> [LimitGroupId])");
                    table.CheckConstraint("CK_SettingsDiscountLimitVersions_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_SalesDiscountLimitVersions_SalesDiscountLimitGroups_CompanyId_LimitGroupId",
                        columns: x => new { x.CompanyId, x.LimitGroupId },
                        principalSchema: "settings",
                        principalTable: "SalesDiscountLimitGroups",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDiscountLimitVersions_SalesDiscountLimitGroups_CompanyId_LinkedGroupId",
                        columns: x => new { x.CompanyId, x.LinkedGroupId },
                        principalSchema: "settings",
                        principalTable: "SalesDiscountLimitGroups",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesDiscountLimitMembershipGroups",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipVersion = table.Column<long>(type: "bigint", nullable: false),
                    LimitGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDiscountLimitMembershipGroups", x => new { x.CompanyId, x.UserId, x.MembershipVersion, x.LimitGroupId });
                    table.CheckConstraint("CK_SettingsDiscountLimitMembershipGroups_Version", "[MembershipVersion] > 0");
                    table.ForeignKey(
                        name: "FK_SalesDiscountLimitMembershipGroups_SalesDiscountLimitGroups_CompanyId_LimitGroupId",
                        columns: x => new { x.CompanyId, x.LimitGroupId },
                        principalSchema: "settings",
                        principalTable: "SalesDiscountLimitGroups",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDiscountLimitMembershipGroups_SalesDiscountLimitMemberships_CompanyId_UserId",
                        columns: x => new { x.CompanyId, x.UserId },
                        principalSchema: "settings",
                        principalTable: "SalesDiscountLimitMemberships",
                        principalColumns: new[] { "CompanyId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesDiscountLimitGroups_CompanyId_Code",
                schema: "settings",
                table: "SalesDiscountLimitGroups",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDiscountLimitGroups_CompanyId_Name",
                schema: "settings",
                table: "SalesDiscountLimitGroups",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDiscountLimitMembershipGroups_CompanyId_LimitGroupId",
                schema: "settings",
                table: "SalesDiscountLimitMembershipGroups",
                columns: new[] { "CompanyId", "LimitGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesDiscountLimitVersions_CompanyId_LinkedGroupId",
                schema: "settings",
                table: "SalesDiscountLimitVersions",
                columns: new[] { "CompanyId", "LinkedGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesDiscountLimitVersions_ProfileId",
                schema: "settings",
                table: "SalesDiscountLimitVersions",
                column: "ProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SAT-D61…D70: a limit group, a limit record or a binding is policy data entered by an authorized person (with its
            // audit trail); dropping it would silently return every affected discount to "no limit". While any row exists the
            // rollback is refused with this migration's own error (51095, used by no other migration on main or an open
            // branch) before any operation; on an empty store the tables drop freely. Forward fix otherwise.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[SalesDiscountLimitGroups])
                    OR EXISTS (SELECT 1 FROM [settings].[SalesDiscountLimitMemberships])
                    THROW 51095, 'Settings discount limit groups or bindings exist; AddSalesDiscountLimitGroups is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "SalesDiscountLimitMembershipGroups",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "SalesDiscountLimitVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "SalesDiscountLimitMemberships",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "SalesDiscountLimitGroups",
                schema: "settings");
        }
    }
}
