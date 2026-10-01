using GoymenERP.Api.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.ACC01;

internal sealed class Acc01NormalJournalSourceFixtureWriter(IServiceProvider services)
{
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!environment.IsEnvironment("Testing")
            || !string.Equals(configuration["BrowserSuiteFixture:Host"], "CONFIRMED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The ACC01 normal-journal source fixture runs only inside the gated Testing host.");
        }

        await using var scope = services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetService<C01E2eFinanceNormalJournalSourceFixtureWriter>()
            ?? throw new InvalidOperationException(
                "The gated host does not register the ACC01 Finance/Accounting source writer.");
        await writer.EnsureAsync(cancellationToken);
    }
}
