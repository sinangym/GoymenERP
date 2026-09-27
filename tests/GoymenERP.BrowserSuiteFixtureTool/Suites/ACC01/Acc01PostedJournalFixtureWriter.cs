using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.ACC01;

/// <summary>
/// The acc01 posted-journal writer, test-side (FİNANS, Accounting owner; ACC-V1 reversal UI phase 1). It reaches ONE
/// port: Accounting's fixed SENTETIK posted journal (<see cref="IAccountingPostedJournalFixtureStore"/>), which the host
/// registers only inside the complete browser-runner gate (<c>C01E2eFixtureWriterActivation</c>, central). Outside that
/// gate the port does not exist and this writer fails; it also re-checks the two gate facts a seed can see from the
/// in-process host (the Testing environment and <c>BrowserSuiteFixture:Host=CONFIRMED</c>). The port accepts no
/// caller-selected identity, date, amount or actor, and posts through Accounting's own posting path.
/// </summary>
internal sealed class Acc01PostedJournalFixtureWriter(IServiceProvider services)
{
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!environment.IsEnvironment("Testing")
            || !string.Equals(configuration["BrowserSuiteFixture:Host"], "CONFIRMED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The acc01 posted-journal fixture runs only inside the browser runner's gated Testing host.");
        }

        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<IAccountingPostedJournalFixtureStore>()
            ?? throw new InvalidOperationException(
                "The gated host does not register Accounting's posted-journal fixture port "
                + "(C01E2eFixtureWriterActivation: AddAccountingPostedJournalFixtureStore).");
        await store.EnsureAsync(cancellationToken);
    }
}
