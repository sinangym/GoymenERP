using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.ProductRuntime;

/// <summary>
/// SAT-D382: only Cari-owned active Party + active company account + Customer-role proof is admitted.
/// The owner measures Product's existing maintenance/approval function, with no Cari read or Manufacturing alias.
/// Product's customer DTO remains identity/presentation only; actual owner revisions are validated here.
/// </summary>
public sealed class CounterpartyProductPackageStandardCustomerPortV3(IServiceProvider services, ICompanyContext companyContext,
    ICurrentUser currentUser) : IProductPackageStandardCustomerPortV3
{
    public async Task<ProductPackageStandardCustomerAnswerV3> ReadAsync(IReadOnlyList<Guid> customerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customerIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent()) return Closed(ProductPackageStandardCustomerOutcomeV3.Denied);
        var companyId = companyContext.CompanyId; var actorId = currentUser.UserId!;
        if (customerIds.Count > CounterpartyProductPackageStandardCustomerContractV1.MaximumPartyCount
            || customerIds.Any(id => id == Guid.Empty) || customerIds.Distinct().Count() != customerIds.Count)
            return Closed(ProductPackageStandardCustomerOutcomeV3.Denied);
        var asked = customerIds.ToArray();
        try
        {
            if (services.GetService<ICounterpartyProductPackageStandardCustomerSourceV1>() is not { } owner)
                return Closed(ProductPackageStandardCustomerOutcomeV3.Unavailable);
            var answer = await owner.ReadAuthorizedAsync(new(companyId, actorId, Array.AsReadOnly(asked)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Still(companyId, actorId)) return Closed(ProductPackageStandardCustomerOutcomeV3.Denied);
            if (answer is null || answer.Items is null) return Closed(ProductPackageStandardCustomerOutcomeV3.Unavailable);
            if (answer.Outcome == CounterpartyProductPackageStandardCustomerOutcomeV1.Denied)
                return Closed(answer.Items.Count == 0 ? ProductPackageStandardCustomerOutcomeV3.Denied : ProductPackageStandardCustomerOutcomeV3.Unavailable);
            if (answer.Outcome != CounterpartyProductPackageStandardCustomerOutcomeV1.Found
                || answer.CompanyId != companyId || answer.ActorId != actorId
                || answer.SourceVersion != CounterpartyProductPackageStandardCustomerContractV1.SourceVersion || answer.Items.Count > asked.Length)
                return Closed(ProductPackageStandardCustomerOutcomeV3.Unavailable);
            var seen = new HashSet<Guid>(); var accounts = new HashSet<Guid>(); var items = new List<ProductPackageStandardCustomerV3>();
            foreach (var value in answer.Items)
            {
                if (value is null || value.CompanyId != companyId || !asked.Contains(value.PartyId) || !seen.Add(value.PartyId)
                    || value.CompanyAccountId == Guid.Empty || !accounts.Add(value.CompanyAccountId)
                    || !value.PartyIsActive || !value.AccountIsActive || !value.HasCustomerRole
                    || !Version(value.PartyVersion) || !Version(value.AccountVersion) || !Code(value.Code) || !Text(value.LegalName, 250)
                    || value.TradeName is not null && !Text(value.TradeName, 200)) return Closed(ProductPackageStandardCustomerOutcomeV3.Unavailable);
                items.Add(new(value.PartyId, value.Code, value.TradeName ?? value.LegalName));
            }
            return new(ProductPackageStandardCustomerOutcomeV3.Found, items.AsReadOnly());
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return Closed(ProductPackageStandardCustomerOutcomeV3.Denied); }
        catch { return Closed(Still(companyId, actorId) ? ProductPackageStandardCustomerOutcomeV3.Unavailable : ProductPackageStandardCustomerOutcomeV3.Denied); }
    }
    private static bool Version(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number > 0 && value == number.ToString(CultureInfo.InvariantCulture);
    private static bool Code(string value) => value is { Length: >= 10 and <= 23 } && value.StartsWith("CAR-", StringComparison.Ordinal)
        && value.Skip(4).All(char.IsAsciiDigit)
        && long.TryParse(value.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0;
    private static bool Text(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    private bool Still(Guid companyId, string actorId) => IsCurrent() && companyContext.CompanyId == companyId && currentUser.UserId == actorId;
    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
    private static ProductPackageStandardCustomerAnswerV3 Closed(ProductPackageStandardCustomerOutcomeV3 outcome) => new(outcome, []);
}
