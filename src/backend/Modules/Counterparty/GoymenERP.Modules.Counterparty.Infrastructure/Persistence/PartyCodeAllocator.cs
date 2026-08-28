using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class PartyCodeAllocator(CounterpartyDbContext dbContext)
{
    public async Task<PartyCode> AllocateAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var counter = await dbContext.PartyCodeCounters
                .SingleAsync(x => x.Id == PartyCodeCounter.SingletonId, cancellationToken);
            var sequence = counter.Next();

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.Entry(counter).State = EntityState.Detached;
                return PartyCode.FromSequence(sequence);
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.Entry(counter).State = EntityState.Detached;
            }
        }
    }
}
