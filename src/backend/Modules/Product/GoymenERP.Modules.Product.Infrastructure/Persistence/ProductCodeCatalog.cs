using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed class ProductCodeGroupRow
{
    private ProductCodeGroupRow() { }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Prefix { get; private set; } = string.Empty;

    // Catalog provisioning is explicit; this does not seed a real default group.
    public static ProductCodeGroupRow Define(Guid id, string name, string prefix)
    {
        if (id == Guid.Empty) throw new ArgumentException("A code group ID is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 200 || name.Any(char.IsControl))
            throw new ArgumentException("The code group display name is invalid.", nameof(name));
        var sampleCode = ProductCode.FromSequence(prefix, 1).Value;
        return new() { Id = id, Name = name.Trim(), Prefix = sampleCode[..sampleCode.LastIndexOf('-')] };
    }

    public ProductCodeGroup ToContract() => new(Id, Name, Prefix);
}

// The counter is per canonical PREFIX, not per company or group UUID. Different
// groups using the same prefix must still share the global code namespace.
public sealed class ProductCodeCounter
{
    private ProductCodeCounter() { }
    public string Prefix { get; private set; } = string.Empty;
    public long LastSequence { get; private set; }

    public static ProductCodeCounter ForGroup(ProductCodeGroupRow group) => new() { Prefix = group.Prefix };

    public ProductCode Advance()
    {
        var next = checked(LastSequence + 1);
        var code = ProductCode.FromSequence(Prefix, next);
        LastSequence = next;
        return code;
    }
}

/// <summary>
/// Stages a prefix counter advance. It never saves or starts a transaction.
/// The owner atomic writer must include this change, Product, code registration,
/// canonical audit and the global terminal in one commit, and retry the complete
/// command on counter/key/code races. No code is returned to HTTP before commit.
/// </summary>
public sealed class ProductCodeAllocator(ProductDbContext context)
{
    private int started;

    public async Task<ProductCode> StageNextAsync(GroupSharedMasterCommandContext authority, Guid groupId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.Operation != GroupSharedMasterOperation.DraftCreate)
            throw new UnauthorizedAccessException("Code allocation requires Product create authority.");
        // A command/retry owns a fresh context and allocator. Never resume a prior
        // tracked allocation after cancellation or failure; retry the whole unit.
        if (Interlocked.Exchange(ref started, 1) != 0)
            throw new InvalidOperationException("A Product code allocation attempt cannot be reused.");
        var group = await context.CodeGroups.AsNoTracking().SingleOrDefaultAsync(row => row.Id == groupId, cancellationToken)
            ?? throw new ProductValidationException([new("groupId", "invalid_reference")]);
        var counter = await context.CodeCounters.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Prefix == group.Prefix, cancellationToken);
        if (counter is null)
        {
            counter = ProductCodeCounter.ForGroup(group);
            context.CodeCounters.Add(counter);
        }
        else
        {
            // Attach the database snapshot before advancing so the original
            // sequence remains the optimistic concurrency predicate. An existing
            // tracked instance conflicts rather than supplying cached authority.
            context.CodeCounters.Attach(counter);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = counter.Advance();
            // Retired aliases reserve a code just as a current code does.
            if (!await context.CodeRegistrations.AnyAsync(row => row.Code == candidate.Value, cancellationToken))
                return candidate;
        }
    }
}
