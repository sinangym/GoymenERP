using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CounterpartyDbContext))]
[Migration("20260828150000_AddCounterpartyWriteSupport")]
public sealed class AddCounterpartyWriteSupport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence<long>(
            name: "PartyCodeSequence",
            schema: "counterparty",
            startValue: 1L);

        migrationBuilder.Sql(
            """
            DECLARE @NextPartyCode bigint =
                ISNULL((
                    SELECT MAX(TRY_CONVERT(bigint, SUBSTRING([Code], 5, 28)))
                    FROM [counterparty].[Parties]
                ), 0) + 1;
            DECLARE @RestartSql nvarchar(256) =
                N'ALTER SEQUENCE [counterparty].[PartyCodeSequence] RESTART WITH '
                + CONVERT(nvarchar(30), @NextPartyCode)
                + N';';
            EXEC sys.sp_executesql @RestartSql;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropSequence(
            name: "PartyCodeSequence",
            schema: "counterparty");
    }
}
