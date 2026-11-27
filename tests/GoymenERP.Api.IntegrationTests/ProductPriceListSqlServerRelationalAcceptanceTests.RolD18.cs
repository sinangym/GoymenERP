using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Rol_D18_new_preparer_permission_never_reexecutes_a_stored_legacy_maker_refusal_key()
    {
        await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run);
        const string oldKey = "SENTETIK-legacy-maker-refusal";
        var body = new PublishPriceListVersionRequestV1("1", 1);
        // Synthetic historical native receipt only. INSERT of the old immutable outcome; no UPDATE/trigger bypass,
        // and no claim that the current owner command still produces the superseded maker prohibition.
        await using (var legacy = run.Request())
        {
            legacy.Context.Attempts.Add(new PriceListAttemptRow
            {
                CompanyId = TestCompanyIds.CompanyA, CommandName = ProductPriceListCommandsV1.PublishV1,
                IdempotencyKeyHash = ProductPriceListStore.IdempotencyKeyHash(oldKey),
                RequestHash = ProductPriceListWire.PublishRequestHash(id, body), ActorId = run.CreatorActorId.ToString("D"),
                Outcome = 2, PriceListId = null, ResultJson = null,
                RefusalJson = JsonSerializer.Serialize(new PriceListTerminalRefusal(409, ProductPriceListCodesV1.StateConflict,
                    [new("actor", PriceListRuleCodes.PublisherPreparedVersion)]), ProductPriceListWire.Json),
                CorrelationId = "SENTETIK-original-refusal", RecordedAtUtc = DateTimeOffset.UtcNow,
            });
            await legacy.Context.SaveChangesAsync();
        }
        await using var scope = new PublishSafetyScope(run, run.CreatorActorId);
        scope.Authority.Granted = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, oldKey))).Status);
        scope.Reset();
        var originalJson = await run.ScalarAsync<string>("SELECT RefusalJson FROM product.PriceListAttempts WHERE IdempotencyKeyHash=@id", ProductPriceListStore.IdempotencyKeyHash(oldKey));
        for (var i = 0; i < 2; i++)
        {
            var error = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, oldKey));
            Assert.Equal((409, ProductPriceListCodesV1.StateConflict), (error.Status, error.Code));
            Assert.Equal(PriceListRuleCodes.PublisherPreparedVersion, Assert.Single(error.Errors).Code);
            await PublishSafetyDurable(run, id, false, 1, 0);
        }
        var success = await scope.Publish(id, "SENTETIK-new-policy-new-key");
        Assert.False(success.IsReplay); Assert.Equal(run.CreatorActorId.ToString("D"), Assert.Single(success.PriceList.Versions).PublishedBy);
        var oldAgain = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, oldKey));
        Assert.Equal(PriceListRuleCodes.PublisherPreparedVersion, Assert.Single(oldAgain.Errors).Code);
        Assert.Equal(ProductPriceListCodesV1.IdempotencyConflict,
            (await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, oldKey, "2"))).Code);
        Assert.Equal(originalJson, await run.ScalarAsync<string>("SELECT RefusalJson FROM product.PriceListAttempts WHERE IdempotencyKeyHash=@id", ProductPriceListStore.IdempotencyKeyHash(oldKey)));
        await PublishSafetyDurable(run, id, true, 2, 1);
        // Restore/ordinary native reads must accept the authorized preparer's published actor history as well.
        await using var read = run.Request();
        Assert.Equal(run.CreatorActorId.ToString("D"), Assert.Single((await read.Service.GetAsync(id)).Versions).PublishedBy);
    }
}
