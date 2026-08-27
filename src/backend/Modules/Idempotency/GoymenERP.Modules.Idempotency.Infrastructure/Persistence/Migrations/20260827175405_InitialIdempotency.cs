using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "idempotency");

            migrationBuilder.CreateTable(
                name: "CommandExecutions",
                schema: "idempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandExecutions", x => x.Id);
                    table.CheckConstraint("CK_CommandExecutions_CompanyId_NotEmpty", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CommandExecutions_Hash_Lengths", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_CommandExecutions_HttpStatusCode_Valid", "[HttpStatusCode] BETWEEN 100 AND 599");
                    table.CheckConstraint("CK_CommandExecutions_ResponseJson_Valid", "ISJSON([ResponseJson]) = 1");
                    table.CheckConstraint("CK_CommandExecutions_SchemaVersion_Positive", "[SchemaVersion] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandExecutions_CompanyId_CommandName_IdempotencyKeyHash",
                schema: "idempotency",
                table: "CommandExecutions",
                columns: new[] { "CompanyId", "CommandName", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandExecutions_CompanyId_CompletedAtUtc_Id",
                schema: "idempotency",
                table: "CommandExecutions",
                columns: new[] { "CompanyId", "CompletedAtUtc", "Id" });

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [idempotency].[TR_CommandExecutions_RejectMutation]
                ON [idempotency].[CommandExecutions]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51010, 'Command execution records are immutable.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandExecutions",
                schema: "idempotency");
        }
    }
}
