using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class SqlPartyCodeAllocator(CounterpartyDbContext dbContext)
    : IPartyCodeAllocator
{
    public async Task<long> NextAsync(CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            throw new InvalidOperationException(
                "PartyCode allocator relational SQL sequence gerektirir.");
        }

        return await dbContext.Database
            .SqlQueryRaw<long>(
                "SELECT NEXT VALUE FOR [counterparty].[PartyCodeSequence] AS [Value]")
            .SingleAsync(cancellationToken);
    }
}
