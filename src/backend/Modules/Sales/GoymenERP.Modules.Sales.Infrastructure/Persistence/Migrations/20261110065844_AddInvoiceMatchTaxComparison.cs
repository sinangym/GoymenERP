using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceMatchTaxComparison : Migration
    {
        // SAT-D241 (EK48): the official match compares the VAT and the withholding separately from the payable amount. Six nullable
        // columns (NULL on a match recorded before), an all-or-nothing check, and the approval check widened to any of the three
        // differences. Existing rows keep satisfying both checks (their tax columns are NULL). Down guard THROW 51242.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceMatches_Amounts",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceVatAmount",
                schema: "sales",
                table: "InvoiceMatches",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceWithholdingAmount",
                schema: "sales",
                table: "InvoiceMatches",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OfficialVatAmount",
                schema: "sales",
                table: "InvoiceMatches",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OfficialWithholdingAmount",
                schema: "sales",
                table: "InvoiceMatches",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatDifference",
                schema: "sales",
                table: "InvoiceMatches",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingDifference",
                schema: "sales",
                table: "InvoiceMatches",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceMatches_Amounts",
                schema: "sales",
                table: "InvoiceMatches",
                sql: "[Difference] >= 0 AND [Tolerance] >= 0 AND [InvoiceAmount] >= 0 AND [OfficialAmount] >= 0 AND ([Difference] = ABS([InvoiceAmount] - [OfficialAmount])) AND (([Difference] <= [Tolerance] AND ISNULL([VatDifference], 0) <= [Tolerance] AND ISNULL([WithholdingDifference], 0) <= [Tolerance] AND [DifferenceApprovedBy] IS NULL AND [DifferenceReason] IS NULL) OR (([Difference] > [Tolerance] OR ISNULL([VatDifference], 0) > [Tolerance] OR ISNULL([WithholdingDifference], 0) > [Tolerance]) AND [DifferenceApprovedBy] IS NOT NULL AND [DifferenceReason] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceMatches_TaxAmounts",
                schema: "sales",
                table: "InvoiceMatches",
                sql: "([InvoiceVatAmount] IS NULL AND [OfficialVatAmount] IS NULL AND [VatDifference] IS NULL AND [InvoiceWithholdingAmount] IS NULL AND [OfficialWithholdingAmount] IS NULL AND [WithholdingDifference] IS NULL) OR ([InvoiceVatAmount] >= 0 AND [OfficialVatAmount] >= 0 AND [InvoiceWithholdingAmount] >= 0 AND [OfficialWithholdingAmount] >= 0 AND [VatDifference] = ABS([InvoiceVatAmount] - [OfficialVatAmount]) AND [WithholdingDifference] = ABS([InvoiceWithholdingAmount] - [OfficialWithholdingAmount]))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a match recorded since SAT-D241 carries its VAT/withholding comparison (and possibly an approval that only a
            // VAT or withholding difference required); dropping the columns would lose it, so Down refuses while any such row exists.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[InvoiceMatches] WHERE [VatDifference] IS NOT NULL OR [WithholdingDifference] IS NOT NULL
                    OR [InvoiceVatAmount] IS NOT NULL OR [OfficialVatAmount] IS NOT NULL OR [InvoiceWithholdingAmount] IS NOT NULL
                    OR [OfficialWithholdingAmount] IS NOT NULL)
                BEGIN
                    THROW 51242, 'A sales invoice match carries its SAT-D241 VAT/withholding comparison; AddInvoiceMatchTaxComparison is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceMatches_Amounts",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceMatches_TaxAmounts",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.DropColumn(
                name: "InvoiceVatAmount",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.DropColumn(
                name: "InvoiceWithholdingAmount",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.DropColumn(
                name: "OfficialVatAmount",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.DropColumn(
                name: "OfficialWithholdingAmount",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.DropColumn(
                name: "VatDifference",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.DropColumn(
                name: "WithholdingDifference",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceMatches_Amounts",
                schema: "sales",
                table: "InvoiceMatches",
                sql: "[Difference] >= 0 AND [Tolerance] >= 0 AND [InvoiceAmount] >= 0 AND [OfficialAmount] >= 0 AND ([Difference] = ABS([InvoiceAmount] - [OfficialAmount])) AND (([Difference] <= [Tolerance] AND [DifferenceApprovedBy] IS NULL AND [DifferenceReason] IS NULL) OR ([Difference] > [Tolerance] AND [DifferenceApprovedBy] IS NOT NULL AND [DifferenceReason] IS NOT NULL))");
        }
    }
}
