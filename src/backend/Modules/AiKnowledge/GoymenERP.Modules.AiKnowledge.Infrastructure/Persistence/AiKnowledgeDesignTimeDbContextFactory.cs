using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

public sealed class AiKnowledgeDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AiKnowledgeDbContext>
{
    public AiKnowledgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AiKnowledgeDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AiKnowledgeDbContext.Schema))
            .Options;
        return new AiKnowledgeDbContext(options, new DesignTimeCompanyContext());
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;
        public Guid CompanyId => Guid.Empty;
        public void Set(Guid companyId) => throw new InvalidOperationException("Design-time AI knowledge DbContext does not resolve a company.");
    }
}
