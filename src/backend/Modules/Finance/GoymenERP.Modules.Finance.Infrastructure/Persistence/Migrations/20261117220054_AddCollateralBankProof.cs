using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollateralBankProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankInstitutionId",
                schema: "finance",
                table: "PartyCollaterals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankProofJson",
                schema: "finance",
                table: "PartyCollaterals",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankSourceVersion",
                schema: "finance",
                table: "PartyCollaterals",
                type: "varchar(256)",
                unicode: false,
                maxLength: 256,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartyCollaterals_BankProof",
                schema: "finance",
                table: "PartyCollaterals",
                sql: "([BankInstitutionId] IS NULL AND [BankSourceVersion] IS NULL AND [BankProofJson] IS NULL) OR ([BankInstitutionId] IS NOT NULL AND [BankInstitutionId] <> '00000000-0000-0000-0000-000000000000' AND [BankSourceVersion] IS NOT NULL AND LEN([BankSourceVersion]) > 0 AND [BankProofJson] IS NOT NULL AND ISJSON([BankProofJson]) = 1 AND JSON_VALUE([BankProofJson], '$.companyId') IS NOT NULL AND TRY_CONVERT(uniqueidentifier, JSON_VALUE([BankProofJson], '$.companyId')) IS NOT NULL AND TRY_CONVERT(uniqueidentifier, JSON_VALUE([BankProofJson], '$.companyId')) = [CompanyId] AND JSON_VALUE([BankProofJson], '$.bankInstitutionId') IS NOT NULL AND TRY_CONVERT(uniqueidentifier, JSON_VALUE([BankProofJson], '$.bankInstitutionId')) IS NOT NULL AND TRY_CONVERT(uniqueidentifier, JSON_VALUE([BankProofJson], '$.bankInstitutionId')) = [BankInstitutionId] AND JSON_VALUE([BankProofJson], '$.sourceVersion') IS NOT NULL AND JSON_VALUE([BankProofJson], '$.sourceVersion') COLLATE Latin1_General_100_BIN2 = [BankSourceVersion] AND JSON_VALUE([BankProofJson], '$.name') IS NOT NULL AND LEN(JSON_VALUE([BankProofJson], '$.name')) > 0 AND JSON_VALUE([BankProofJson], '$.eftCode') IS NOT NULL AND LEN(JSON_VALUE([BankProofJson], '$.eftCode')) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[PartyCollaterals]
                           WHERE [BankInstitutionId] IS NOT NULL OR [BankSourceVersion] IS NOT NULL OR [BankProofJson] IS NOT NULL)
                BEGIN
                    THROW 51808, 'Explicit collateral bank proof exists; AddCollateralBankProof is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_PartyCollaterals_BankProof",
                schema: "finance",
                table: "PartyCollaterals");

            migrationBuilder.DropColumn(
                name: "BankInstitutionId",
                schema: "finance",
                table: "PartyCollaterals");

            migrationBuilder.DropColumn(
                name: "BankProofJson",
                schema: "finance",
                table: "PartyCollaterals");

            migrationBuilder.DropColumn(
                name: "BankSourceVersion",
                schema: "finance",
                table: "PartyCollaterals");
        }
    }
}
