using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// F1 (STK-S4): Logistics's answer to <see cref="ILogisticsSubcontractDeliveryNetWeightReadPortV1"/>. It reads the instruction
/// through the store's company-filtered, untracked rebuild (<see cref="ILogisticsInstructionStore.FindInstructionAsync"/>, which
/// replays the weighing at its share position and checks every stored total) and maps it with
/// <see cref="SubcontractDeliveryNetWeightReader"/>. It writes nothing and measures no permission (LOG-4 D6).
/// <list type="bullet">
/// <item>No resolved company: <see cref="InvalidOperationException"/>, before any read.</item>
/// <item>A <c>companyId</c> other than the context company, or an empty instruction id: NoSource, nothing is read.</item>
/// <item>A stored record the rebuild refuses (its consistency checks or a domain rule on replay): Unavailable.</item>
/// </list>
/// </summary>
public sealed class LogisticsSubcontractDeliveryNetWeightReadPort(ILogisticsInstructionStore store, ICompanyContext companyContext)
    : ILogisticsSubcontractDeliveryNetWeightReadPortV1
{
    public async Task<LogisticsSubcontractDeliveryNetWeightReadV1> GetAsync(Guid companyId, Guid loadingInstructionId, Guid weighingId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contextCompanyId = RequireCompany();
        if (companyId != contextCompanyId || loadingInstructionId == Guid.Empty)
        {
            // Another company's key reads as no source — never that company's data, never a 403.
            return LogisticsSubcontractDeliveryNetWeightReadV1.NoSource(companyId, loadingInstructionId, weighingId,
                LogisticsSubcontractDeliveryNetWeightReasonCodesV1.InstructionNotFound);
        }

        StoredLoadingInstruction? stored;
        try
        {
            stored = await store.FindInstructionAsync(loadingInstructionId, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            // The rebuild refused the stored rows (a contradiction it checks, or a domain rule on replay): fail closed.
            return LogisticsSubcontractDeliveryNetWeightReadV1.Unavailable(companyId, loadingInstructionId, weighingId,
                LogisticsSubcontractDeliveryNetWeightReasonCodesV1.RecordUnreadable);
        }

        return SubcontractDeliveryNetWeightReader.Read(companyId, loadingInstructionId, weighingId, stored?.Instruction);
    }

    private Guid RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Fason teslim tartımı okuması yalnız çözümlenmiş şirket bağlamında yapılır.");
        }

        return companyContext.CompanyId;
    }
}
