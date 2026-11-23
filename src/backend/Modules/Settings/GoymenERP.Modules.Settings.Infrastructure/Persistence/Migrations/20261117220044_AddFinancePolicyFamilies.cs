using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancePolicyFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankInstitutionPolicies",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    RetiredFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    LastReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankInstitutionPolicies", x => new { x.CompanyId, x.PolicyId });
                    table.CheckConstraint("CK_BankInstitutionPolicies_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [StateVersion] > 0");
                    table.CheckConstraint("CK_BankInstitutionPolicies_State", "([State]='Draft' AND [StateVersion]=1 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [RetiredFrom] IS NULL) OR ([State]='Published' AND [StateVersion]=2 AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NULL) OR ([State]='Retired' AND [StateVersion]=3 AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NOT NULL)");
                    table.CheckConstraint("CK_BankInstitutionPolicies_Text", "LEN([PreparedBy])>0 AND LEN([Reason])>0 AND LEN([LastReason])>0");
                    table.CheckConstraint("CK_BankInstitutionPolicies_Window", "[ValidThrough] IS NULL OR [ValidThrough] >= [ValidFrom]");
                });

            migrationBuilder.CreateTable(
                name: "CashCountDifferencePolicies",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    RetiredFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    LastReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CashAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashAccountVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountThreshold = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashCountDifferencePolicies", x => new { x.CompanyId, x.PolicyId });
                    table.CheckConstraint("CK_CashCountDifferencePolicies_Amount", "LEN([AmountThreshold]) BETWEEN 1 AND 64 AND [AmountThreshold] NOT LIKE '%[^0-9.]%' AND LEFT([AmountThreshold],1) <> '.' AND RIGHT([AmountThreshold],1) <> '.' AND LEN([AmountThreshold])-LEN(REPLACE([AmountThreshold],'.','')) <= 1");
                    table.CheckConstraint("CK_CashCountDifferencePolicies_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [StateVersion] > 0");
                    table.CheckConstraint("CK_CashCountDifferencePolicies_Scope", "[CashAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CashAccountVersion]) > 0");
                    table.CheckConstraint("CK_CashCountDifferencePolicies_State", "([State]='Draft' AND [StateVersion]=1 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [RetiredFrom] IS NULL) OR ([State]='Published' AND [StateVersion]=2 AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NULL) OR ([State]='Retired' AND [StateVersion]=3 AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NOT NULL)");
                    table.CheckConstraint("CK_CashCountDifferencePolicies_Text", "LEN([PreparedBy])>0 AND LEN([Reason])>0 AND LEN([LastReason])>0");
                    table.CheckConstraint("CK_CashCountDifferencePolicies_Window", "[ValidThrough] IS NULL OR [ValidThrough] >= [ValidFrom]");
                });

            migrationBuilder.CreateTable(
                name: "CollateralRatePolicies",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    RetiredFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    LastReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ScopeKey = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Scope = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CollateralTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CollateralTypeVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    BankInstitutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BankSourceVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    MaximumRemainingDays = table.Column<int>(type: "int", nullable: true),
                    RatePercent = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollateralRatePolicies", x => new { x.CompanyId, x.PolicyId });
                    table.CheckConstraint("CK_CollateralRatePolicies_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [StateVersion] > 0");
                    table.CheckConstraint("CK_CollateralRatePolicies_Rate", "LEN([RatePercent]) BETWEEN 1 AND 64 AND [RatePercent] NOT LIKE '%[^0-9.]%' AND LEFT([RatePercent], 1) <> '.' AND RIGHT([RatePercent], 1) <> '.' AND LEN([RatePercent]) - LEN(REPLACE([RatePercent], '.', '')) <= 1 AND (LEN(SUBSTRING(LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1), PATINDEX('%[^0]%', LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1) + '1'), 64)) <= 2 OR (SUBSTRING(LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1), PATINDEX('%[^0]%', LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1) + '1'), 64) = '100' AND REPLACE(SUBSTRING([RatePercent], CHARINDEX('.', [RatePercent] + '.') + 1, 64), '0', '') = ''))");
                    table.CheckConstraint("CK_CollateralRatePolicies_Scope", "([Scope]='Company' AND [ScopeKey]='Company' AND [CollateralTypeId] IS NULL AND [CollateralTypeVersion] IS NULL AND [BankInstitutionId] IS NULL AND [BankSourceVersion] IS NULL AND [MaximumRemainingDays] IS NULL) OR ([Scope] IN ('TypeBank','TypeBankRemainingDays') AND [CollateralTypeId] IS NOT NULL AND [CollateralTypeId] <> '00000000-0000-0000-0000-000000000000' AND [CollateralTypeVersion] IS NOT NULL AND LEN([CollateralTypeVersion])>0 AND [BankInstitutionId] IS NOT NULL AND [BankInstitutionId] <> '00000000-0000-0000-0000-000000000000' AND [BankSourceVersion] IS NOT NULL AND LEN([BankSourceVersion])>0 AND [ScopeKey]=[Scope]+':'+LOWER(CONVERT(varchar(36),[CollateralTypeId]))+':'+LOWER(CONVERT(varchar(36),[BankInstitutionId])) AND (([Scope]='TypeBank' AND [MaximumRemainingDays] IS NULL) OR ([Scope]='TypeBankRemainingDays' AND [MaximumRemainingDays] IS NOT NULL AND [MaximumRemainingDays]>=0)))");
                    table.CheckConstraint("CK_CollateralRatePolicies_State", "([State]='Draft' AND [StateVersion]=1 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [RetiredFrom] IS NULL) OR ([State]='Published' AND [StateVersion]=2 AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NULL) OR ([State]='Retired' AND [StateVersion]=3 AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NOT NULL)");
                    table.CheckConstraint("CK_CollateralRatePolicies_Text", "LEN([PreparedBy])>0 AND LEN([Reason])>0 AND LEN([LastReason])>0");
                    table.CheckConstraint("CK_CollateralRatePolicies_Window", "[ValidThrough] IS NULL OR [ValidThrough] >= [ValidFrom]");
                });

            migrationBuilder.CreateTable(
                name: "FinanceApprovalPolicies",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    RetiredFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    LastReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceApprovalPolicies", x => new { x.CompanyId, x.PolicyId });
                    table.CheckConstraint("CK_FinanceApprovalPolicies_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [StateVersion] > 0");
                    table.CheckConstraint("CK_FinanceApprovalPolicies_State", "([State]='Draft' AND [StateVersion]=1 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [RetiredFrom] IS NULL) OR ([State]='Published' AND [StateVersion]=2 AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NULL) OR ([State]='Retired' AND [StateVersion]=3 AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy])>0 AND [PublishedAtUtc] IS NOT NULL AND [RetiredFrom] IS NOT NULL)");
                    table.CheckConstraint("CK_FinanceApprovalPolicies_Text", "LEN([PreparedBy])>0 AND LEN([Reason])>0 AND LEN([LastReason])>0");
                    table.CheckConstraint("CK_FinanceApprovalPolicies_Window", "[ValidThrough] IS NULL OR [ValidThrough] >= [ValidFrom]");
                });

            migrationBuilder.CreateTable(
                name: "BankInstitutionEntries",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankInstitutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EftCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankInstitutionEntries", x => new { x.CompanyId, x.PolicyId, x.BankInstitutionId });
                    table.CheckConstraint("CK_BankInstitutionEntries_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [BankInstitutionId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_BankInstitutionEntries_Text", "LEN([Name])>0 AND LEN([EftCode])>0");
                    table.ForeignKey(
                        name: "FK_BankInstitutionEntries_BankInstitutionPolicies_CompanyId_PolicyId",
                        columns: x => new { x.CompanyId, x.PolicyId },
                        principalSchema: "settings",
                        principalTable: "BankInstitutionPolicies",
                        principalColumns: new[] { "CompanyId", "PolicyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinanceApprovalPolicyRules",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationPermission = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApproverRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApproverRoleCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RoleSourceVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ApprovalStep = table.Column<int>(type: "int", nullable: false),
                    AmountThreshold = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceApprovalPolicyRules", x => new { x.CompanyId, x.PolicyId, x.RuleId });
                    table.CheckConstraint("CK_FinanceApprovalPolicyRules_Amount", "LEN([AmountThreshold]) BETWEEN 1 AND 64 AND [AmountThreshold] NOT LIKE '%[^0-9.]%' AND LEFT([AmountThreshold],1) <> '.' AND RIGHT([AmountThreshold],1) <> '.' AND LEN([AmountThreshold])-LEN(REPLACE([AmountThreshold],'.','')) <= 1");
                    table.CheckConstraint("CK_FinanceApprovalPolicyRules_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [RuleId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [ApproverRoleId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_FinanceApprovalPolicyRules_Step", "[ApprovalStep] IN (1,2)");
                    table.CheckConstraint("CK_FinanceApprovalPolicyRules_Text", "LEN([OperationPermission]) > 0 AND LEN([ApproverRoleCode]) > 0 AND LEN([RoleSourceVersion]) > 0");
                    table.ForeignKey(
                        name: "FK_FinanceApprovalPolicyRules_FinanceApprovalPolicies_CompanyId_PolicyId",
                        columns: x => new { x.CompanyId, x.PolicyId },
                        principalSchema: "settings",
                        principalTable: "FinanceApprovalPolicies",
                        principalColumns: new[] { "CompanyId", "PolicyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankInstitutionEntries_CompanyId_PolicyId_EftCode",
                schema: "settings",
                table: "BankInstitutionEntries",
                columns: new[] { "CompanyId", "PolicyId", "EftCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankInstitutionPolicies_CompanyId_Version",
                schema: "settings",
                table: "BankInstitutionPolicies",
                columns: new[] { "CompanyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashCountDifferencePolicies_CompanyId_CashAccountId_CurrencyId_Version",
                schema: "settings",
                table: "CashCountDifferencePolicies",
                columns: new[] { "CompanyId", "CashAccountId", "CurrencyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollateralRatePolicies_CompanyId_ScopeKey_Version",
                schema: "settings",
                table: "CollateralRatePolicies",
                columns: new[] { "CompanyId", "ScopeKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceApprovalPolicies_CompanyId_Version",
                schema: "settings",
                table: "FinanceApprovalPolicies",
                columns: new[] { "CompanyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceApprovalPolicyRules_CompanyId_PolicyId_OperationPermission_CurrencyId_ApproverRoleId_ApprovalStep",
                schema: "settings",
                table: "FinanceApprovalPolicyRules",
                columns: new[] { "CompanyId", "PolicyId", "OperationPermission", "CurrencyId", "ApproverRoleId", "ApprovalStep" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[BankInstitutionEntries])
                   OR EXISTS (SELECT 1 FROM [settings].[BankInstitutionPolicies])
                   OR EXISTS (SELECT 1 FROM [settings].[CashCountDifferencePolicies])
                   OR EXISTS (SELECT 1 FROM [settings].[CollateralRatePolicies])
                   OR EXISTS (SELECT 1 FROM [settings].[FinanceApprovalPolicyRules])
                   OR EXISTS (SELECT 1 FROM [settings].[FinanceApprovalPolicies])
                BEGIN
                    THROW 51807, 'Financial policy or bank source history exists; AddFinancePolicyFamilies is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "BankInstitutionEntries",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "CashCountDifferencePolicies",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "CollateralRatePolicies",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "FinanceApprovalPolicyRules",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "BankInstitutionPolicies",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "FinanceApprovalPolicies",
                schema: "settings");
        }
    }
}
