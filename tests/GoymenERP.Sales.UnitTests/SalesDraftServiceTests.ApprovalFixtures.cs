namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    /// <summary>YOL-D23: successful setup decisions use a different synthetic actor. The real command and permission fences run;
    /// no grant, production bypass or domain-state seeding is introduced. The scenario's original actor is restored afterwards.</summary>
    private static async Task<T> WithIndependentApproverAsync<T>(World world, Func<Task<T>> command)
    {
        const string first = "a6f91e4a-1ee6-4eed-8e55-cc218c48d431";
        const string second = "a6f91e4a-1ee6-4eed-8e55-cc218c48d432";
        var original = world.User.Id;
        world.User.Id = original == first ? second : first;
        try { return await command(); }
        finally { world.User.Id = original; }
    }
}
