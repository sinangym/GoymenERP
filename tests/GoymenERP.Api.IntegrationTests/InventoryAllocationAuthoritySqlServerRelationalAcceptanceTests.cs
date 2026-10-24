using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Physical_allocation_writer_revalidates_real_authority_after_SQL_reads_before_effects_or_replay()
    {
        foreach (var operation in new[] { "create", "release", "change", "replay" })
        {
            await using var run = await Run.StartAsync();
            var source = await SeedFreePhysicalSourceAsync(run, CompanyA, $"SYN-LATE-{operation}", 100m);
            var orderId = Guid.NewGuid();
            var lineId = Guid.NewGuid();
            var allocationId = Guid.NewGuid();
            var actor = await GrantAllocationAsync(run, TestUsers.CompanyA, CompanyA,
                ErpPermissions.InventoryPhysicalAllocationWrite);
            if (operation is "release" or "change")
            {
                await using var seed = run.Store(CompanyA);
                await seed.Store.CreateAsync(AllocationRequest(source, "60") with
                {
                    AllocationId = allocationId, SalesOrderId = orderId, SalesOrderLineId = lineId,
                }, Metadata(run));
                source = source with
                {
                    LabelVersion = await run.ScalarAsync<long>(
                        "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", source.LabelId)),
                };
            }

            var pause = new AllocationAuthorityReadPause(operation);
            var answer = new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.Approved,
                new SalesOrderLineReferenceV1(CompanyA, orderId, lineId, 1, Guid.NewGuid(), Guid.NewGuid(),
                    source.ProductId, "7", Guid.NewGuid(), "10", 0, 4), null);
            await using var host = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISalesOrderLineReferencePortV1>();
                services.AddSingleton<ISalesOrderLineReferencePortV1>(new FixedApprovedSalesLine(answer));
                services.AddDbContext<InventoryDbContext>((_, options) => options.AddInterceptors(pause));
            }));
            using var client = ClientFor(host, actor, CompanyA);
            var key = $"SYN-LATE-{operation}-{Guid.NewGuid():N}";
            HttpRequestMessage Request()
            {
                if (operation is "create" or "replay")
                    return AllocationPost(allocationId, orderId, lineId, source, key);

                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/v1/inventory/physical-allocations/{allocationId:D}/{operation}")
                {
                    Content = operation == "release"
                        ? JsonContent.Create(new ReleasePhysicalAllocationV1("1", "SENTETIK late release"))
                        : JsonContent.Create(new ChangeInventoryPhysicalAllocationRequestV1(
                            "1", orderId, lineId, source.LabelId, source.LabelVersion.ToString(),
                            "70", "SENTETIK late increase", null)),
                };
                request.Headers.Add("Idempotency-Key", key);
                return request;
            }

            if (operation == "replay")
            {
                using var initialRequest = Request();
                using var initial = await client.SendAsync(initialRequest);
                Assert.Equal(HttpStatusCode.Created, initial.StatusCode);
            }

            var before = await AllocationAuthoritySnapshotAsync(run, source.LabelId);
            pause.Arm();
            using var pendingRequest = Request();
            var pending = client.SendAsync(pendingRequest);
            Exception? revocationFailure = null;
            try
            {
                await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
                // Real disposable Identity state is changed while the actual Inventory reader is paused.
                // Neither the authority provider nor the persistence writer is replaced by a fake.
                await using var identityScope = run.Factory.Services.CreateAsyncScope();
                var identity = identityScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var userId = await identity.ExternalIdentities
                    .Where(value => value.Issuer == BootstrapIdentityOptions.Issuer && value.Subject == actor)
                    .Select(value => value.UserId).SingleAsync();
                var roleIds = identity.RoleAssignments.Where(value => value.UserId == userId).Select(value => value.RoleId);
                var removed = await identity.RolePermissions
                    .Where(value => value.PermissionCode == ErpPermissions.InventoryPhysicalAllocationWrite
                        && roleIds.Contains(value.RoleId)).ExecuteDeleteAsync();
                Assert.True(removed > 0);
            }
            catch (Exception exception)
            {
                revocationFailure = exception;
            }
            finally
            {
                pause.Continue.TrySetResult();
            }

            // Observe and dispose the response even if the barrier/revocation step failed.
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            if (revocationFailure is not null)
                ExceptionDispatchInfo.Capture(revocationFailure).Throw();
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before, await AllocationAuthoritySnapshotAsync(run, source.LabelId));
        }
    }

    private static async Task<(long LabelVersion, string Allocations, int Commands, int Audits,
        int Changes, int Movements, int Outbox, decimal Stock)> AllocationAuthoritySnapshotAsync(Run run, Guid labelId) =>
        (
            await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", labelId)),
            await run.ScalarAsync<string>("SELECT Id, Version, Status, AllocatedKg, RemainingKg FROM inventory.Allocations "
                + "WHERE LabelId = @id ORDER BY Id FOR JSON PATH", ("@id", labelId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockAllocation'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.AllocationChanges"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"),
            await run.ScalarAsync<decimal>("SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId = @id", ("@id", labelId))
        );

    private sealed class AllocationAuthorityReadPause(string operation) : DbCommandInterceptor
    {
        private int armed;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Volatile.Write(ref armed, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var target = operation switch
            {
                "release" => command.CommandText.Contains("FROM [inventory].[Labels]", StringComparison.Ordinal),
                "replay" => command.CommandText.Contains("FROM [inventory].[Commands]", StringComparison.Ordinal),
                _ => command.CommandText.Contains("FROM [inventory].[Allocations]", StringComparison.Ordinal)
                    && command.CommandText.Contains("SUM(", StringComparison.Ordinal),
            };
            if (target && Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Reached.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
