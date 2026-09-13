using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence;

public sealed class GroupSharedCommandDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GroupSharedCommandDbContext>
{
    public GroupSharedCommandDbContext CreateDbContext(string[] args) => new GroupSharedCommandDbContext(
        new DbContextOptionsBuilder<GroupSharedCommandDbContext>().UseSqlServer(
            "Server=localhost;Database=GoymenERP_DesignTime;Integrated Security=true;TrustServerCertificate=true",
            sql => sql.MigrationsHistoryTable("__GroupSharedCommandMigrationsHistory", "idempotency")).Options);
}
