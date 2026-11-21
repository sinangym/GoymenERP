using System.Net;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingPostingAccountMappingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Provider_constraint_failure_is_unavailable_without_consuming_mapping_prepare_or_activation_keys()
    {
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Prepare);
        await run.GrantAsync(Checker, AccountingPostingAccountMappingPermissionNamesV1.Activate);
        using var preparer = run.Client(Preparer);
        using var checker = run.Client(Checker);
        var prepareKey = Key("provider-prepare");
        var before = await run.CountsAsync();
        // Disposable database only: SQL 547 is an unexpected provider failure, not a version winner.
        await run.ExecuteAsync("ALTER TABLE accounting.PostingAccountMappingVersions ADD CONSTRAINT CK_SYNTHETIC_MappingProviderFailure CHECK ([Status] <> 1)");
        try
        {
            using var failed = await PrepareAsync(preparer, 0, prepareKey);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal(before, await run.CountsAsync());
        }
        finally
        {
            await run.ExecuteAsync("ALTER TABLE accounting.PostingAccountMappingVersions DROP CONSTRAINT CK_SYNTHETIC_MappingProviderFailure");
        }
        using (var recovered = await PrepareAsync(preparer, 0, prepareKey)) Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        using (var replay = await PrepareAsync(preparer, 0, prepareKey))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        }

        var activationKey = Key("provider-activate");
        before = await run.CountsAsync();
        await run.ExecuteAsync("ALTER TABLE accounting.PostingAccountMappingVersions ADD CONSTRAINT CK_SYNTHETIC_MappingProviderFailure CHECK ([Status] <> 2)");
        try
        {
            using var failed = await ActivateAsync(checker, 1, 1, 0, activationKey);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal(before, await run.CountsAsync());
            Assert.Equal((1, 1L), await run.VersionAsync(1));
        }
        finally
        {
            await run.ExecuteAsync("ALTER TABLE accounting.PostingAccountMappingVersions DROP CONSTRAINT CK_SYNTHETIC_MappingProviderFailure");
        }
        using (var recovered = await ActivateAsync(checker, 1, 1, 0, activationKey)) Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        using (var replay = await ActivateAsync(checker, 1, 1, 0, activationKey))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        }
    }
}
