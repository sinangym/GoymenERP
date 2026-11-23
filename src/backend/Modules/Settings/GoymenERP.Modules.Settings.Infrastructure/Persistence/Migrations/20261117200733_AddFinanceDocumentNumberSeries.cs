using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceDocumentNumberSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsDocumentNumberReservations_Identity",
                schema: "settings",
                table: "DocumentNumberReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsDocumentNumberFormats_Identity",
                schema: "settings",
                table: "DocumentNumberFormats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsDocumentNumberCounters_Identity",
                schema: "settings",
                table: "DocumentNumberCounters");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsDocumentNumberCancellations_Identity",
                schema: "settings",
                table: "DocumentNumberCancellations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsDocumentNumberReservations_Identity",
                schema: "settings",
                table: "DocumentNumberReservations",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4,5,6) AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [Year] = YEAR([BusinessDate]) AND [Sequence] > 0 AND [FormatVersion] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsDocumentNumberFormats_Identity",
                schema: "settings",
                table: "DocumentNumberFormats",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4,5,6) AND [Version] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsDocumentNumberCounters_Identity",
                schema: "settings",
                table: "DocumentNumberCounters",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4,5,6) AND [Year] BETWEEN 1 AND 9999 AND [LastSequence] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsDocumentNumberCancellations_Identity",
                schema: "settings",
                table: "DocumentNumberCancellations",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4,5,6) AND [DocumentId] <> '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[DocumentNumberFormats] WHERE [Series] IN (5,6))
                   OR EXISTS (SELECT 1 FROM [settings].[DocumentNumberCounters] WHERE [Series] IN (5,6))
                   OR EXISTS (SELECT 1 FROM [settings].[DocumentNumberReservations] WHERE [Series] IN (5,6))
                   OR EXISTS (SELECT 1 FROM [settings].[DocumentNumberCancellations] WHERE [Series] IN (5,6))
                BEGIN
                    THROW 51910, 'Finance receipt or voucher number history exists; AddFinanceDocumentNumberSeries is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsDocumentNumberReservations_Identity",
                schema: "settings",
                table: "DocumentNumberReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsDocumentNumberFormats_Identity",
                schema: "settings",
                table: "DocumentNumberFormats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsDocumentNumberCounters_Identity",
                schema: "settings",
                table: "DocumentNumberCounters");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsDocumentNumberCancellations_Identity",
                schema: "settings",
                table: "DocumentNumberCancellations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsDocumentNumberReservations_Identity",
                schema: "settings",
                table: "DocumentNumberReservations",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4) AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [Year] = YEAR([BusinessDate]) AND [Sequence] > 0 AND [FormatVersion] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsDocumentNumberFormats_Identity",
                schema: "settings",
                table: "DocumentNumberFormats",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4) AND [Version] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsDocumentNumberCounters_Identity",
                schema: "settings",
                table: "DocumentNumberCounters",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4) AND [Year] BETWEEN 1 AND 9999 AND [LastSequence] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsDocumentNumberCancellations_Identity",
                schema: "settings",
                table: "DocumentNumberCancellations",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4) AND [DocumentId] <> '00000000-0000-0000-0000-000000000000'");
        }
    }
}
