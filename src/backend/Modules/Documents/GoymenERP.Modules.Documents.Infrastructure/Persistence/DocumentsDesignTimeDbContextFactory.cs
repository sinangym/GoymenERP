using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

public sealed class DocumentsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", DocumentsDbContext.Schema))
            .Options;
        return new DocumentsDbContext(options, new DesignTimeCompanyContext());
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;

        public Guid CompanyId => Guid.Empty;

        public void Set(Guid companyId) => throw new InvalidOperationException("Design-time Documents DbContext şirket bağlamı çözmez.");
    }
}
