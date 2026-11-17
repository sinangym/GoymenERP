using GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class DocumentVaultSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Branding_migration_Down_allows_empty_history_and_refuses_51936_while_a_terminal_attempt_is_retained()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddDocumentBrandingPublications().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + " SELECT 1");
        await run.ScalarAsync<int>("""
            INSERT INTO documents.DocumentBrandingAttempts
                (CompanyId,CommandName,IdempotencyKeyHash,RequestHash,ActorId,CorrelationId,Status,Code,ResultJson)
            VALUES (@company,N'documents.template.prepare.v1',@key,@hash,N'SENTETIK-maker',N'SENTETIK-branding',409,N'SENTETIK-conflict',NULL);
            SELECT 1;
            """, ("@company", CompanyA), ("@key", new string('a', 64)), ("@hash", new string('b', 64)));
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"));
        Assert.Equal(51936, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.DocumentBrandingAttempts WHERE CompanyId=@company", ("@company", CompanyA)));
    }
}
