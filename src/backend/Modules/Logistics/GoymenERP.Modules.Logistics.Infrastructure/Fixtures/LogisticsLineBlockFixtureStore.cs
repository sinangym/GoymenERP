using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Logistics.Infrastructure.Fixtures;

/// <summary>
/// The gated Logistics fixture port of the browser suite l04 (LOG-4b; coordinator decision W1/C, the s04
/// Sales port's sibling): BLOCKS one line of an ISSUED loading instruction of the request's company, once,
/// with no attempt and no audit (the suite's evidence line counts audit_delta 0 for this writer). LOG-4a has
/// no block route — D2's block source arrives in LOG-5 — so this is the only suite state no API produces;
/// the instruction itself is created and issued through the real API by the seed. The write is exactly what
/// the domain's <c>BlockLine</c> leaves behind (the line's flag and one version step), so the real store
/// restores it unchanged and refuses a load on the line as <c>logistics_line_blocked</c>. A re-run is a
/// no-op; a Draft instruction, an unknown line, an unlabelled request or another company's context is
/// refused with nothing written. Logistics does not know the gate: the port is added to DI only by the
/// host's gated fixture-writer activation, behind the Testing + confirmed-host gate, and no route or
/// seeder reaches it.
/// </summary>
public interface ILogisticsLineBlockFixtureStore
{
    Task<LogisticsLineBlockFixtureOutcome> BlockAsync(LogisticsLineBlockFixtureRequest request, CancellationToken cancellationToken);
}

/// <param name="Label">The suite's label (<c>SENTETIK browser-suite:&lt;suite&gt; …</c>); the writer requires it, the port checks it too.</param>
public sealed record LogisticsLineBlockFixtureRequest(
    Guid InstructionId,
    Guid LineId,
    Guid CompanyId,
    string Label,
    DateTimeOffset At);

/// <param name="Blocked">True when this call set the block; false when the line was already blocked (no-op).</param>
public sealed record LogisticsLineBlockFixtureOutcome(Guid InstructionId, Guid LineId, long Version, bool Blocked);

public static class LogisticsLineBlockFixtureStoreRegistration
{
    public static IServiceCollection AddLogisticsLineBlockFixtureStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ILogisticsLineBlockFixtureStore, LogisticsLineBlockFixtureStore>();
        return services;
    }
}

internal sealed class LogisticsLineBlockFixtureStore(LogisticsDbContext dbContext, ICompanyContext companyContext) : ILogisticsLineBlockFixtureStore
{
    private const string LabelPrefix = "SENTETIK browser-suite:";

    public async Task<LogisticsLineBlockFixtureOutcome> BlockAsync(LogisticsLineBlockFixtureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.InstructionId == Guid.Empty || request.LineId == Guid.Empty || request.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("A fixture block names the instruction, the line and the company.");
        }

        if (string.IsNullOrWhiteSpace(request.Label) || !request.Label.StartsWith(LabelPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"A fixture block label must start '{LabelPrefix}'.");
        }

        // The context is the ambient one of the fixture runner's scope: bound here, and only to the request's company.
        if (!companyContext.IsResolved)
        {
            companyContext.Set(request.CompanyId);
        }
        else if (companyContext.CompanyId != request.CompanyId)
        {
            throw new InvalidOperationException("A fixture block is written in its own company's context only.");
        }

        LoadingInstructionRow? instruction = await dbContext.Instructions
            .SingleOrDefaultAsync(row => row.Id == request.InstructionId, cancellationToken);
        if (instruction is null)
        {
            throw new InvalidOperationException("The instruction to block is not this company's.");
        }

        if (instruction.Status != (int)LoadingInstructionStatus.Issued)
        {
            throw new InvalidOperationException("Only an issued instruction's line is blocked by the fixture.");
        }

        LoadingInstructionLineRow? line = await dbContext.Lines
            .SingleOrDefaultAsync(row => row.InstructionId == request.InstructionId && row.LineId == request.LineId, cancellationToken);
        if (line is null)
        {
            throw new InvalidOperationException("The line to block is not on this instruction.");
        }

        if (line.IsBlocked)
        {
            return new LogisticsLineBlockFixtureOutcome(instruction.Id, line.LineId, instruction.Version, Blocked: false);
        }

        // Exactly the domain's BlockLine footprint: the flag and one version step, nothing else (no attempt, no audit).
        line.IsBlocked = true;
        instruction.Version = checked(instruction.Version + 1);
        instruction.UpdatedAtUtc = request.At.ToUniversalTime();
        await dbContext.SaveChangesAsync(cancellationToken);
        return new LogisticsLineBlockFixtureOutcome(instruction.Id, line.LineId, instruction.Version, Blocked: true);
    }
}
