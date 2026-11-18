using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Api.FinanceRuntime;

public static class FinancePartyBalanceTransferHostV1
{
    public static IServiceCollection AddFinancePartyBalanceTransfersV1(this IServiceCollection services)
    {
        services.AddScoped<IFinancePaymentAllocationCandidatesV1>(CreatePaymentAllocationCandidates);
        services.AddScoped<IFinancePartyBalanceTransferOwnerPortV1, CounterpartyFinancePartyBalanceTransferOwnerPortV1>();
        services.AddScoped<IFinancePartyBalanceTransferEvidencePortV1, DocumentsFinancePartyBalanceTransferEvidencePortV1>();
        services.AddScoped<IFinancePartyBalanceTransferServiceV1>(provider =>
        {
            var permissions = provider.GetRequiredService<FinancePermissionPort>();
            var correlation = provider.GetRequiredService<ICorrelationContext>();
            return new FinancePartyBalanceTransferServiceV1(provider.GetRequiredService<FinanceDbContext>(),
                provider.GetRequiredService<ICompanyContext>(), provider.GetRequiredService<ICurrentUser>(), permissions,
                provider.GetRequiredService<IFinancePartyBalanceTransferOwnerPortV1>(),
                provider.GetRequiredService<IFinancePartyBalanceTransferEvidencePortV1>(),
                provider.GetRequiredService<IPartyLedgerService>(), provider.GetRequiredService<IFinanceRecordPeriodRule>(),
                provider.GetRequiredService<IAuditEventCollector>(),
                new(FinancePartyBalanceTransferPermissionsV1.Record, permissions.RoleCodesOf(FinanceAction.RecordPartyBalanceTransfer),
                    correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent),
                provider.GetRequiredService<TimeProvider>(), provider.GetService<IAuditTransactionWriterV1>());
        });
        return services;
    }

    private static IFinancePaymentAllocationCandidatesV1 CreatePaymentAllocationCandidates(IServiceProvider provider) =>
        new FinancePaymentAllocationCandidatesV1(provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(), provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<FinancePermissionPort>());
}
