using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// MOD-08: central, record-bound adapter between the authorized Sales read seam and Identity's user directory. It exposes
/// no endpoint and passes only the ids an authorized Sales record already carries; Identity re-fences the company and the
/// caller and answers display names only.
/// </summary>
public sealed class SalesUserDisplayHost(IUserDirectoryReadPortV1 directory) : ISalesUserDisplaySourceV1
{
    public async Task<SalesUserDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
        SalesUserDisplayBatchRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = await directory.ResolveAsync(
            new UserDirectoryResolveRequestV1(request.CompanyId, request.ActorId, request.UserIds),
            cancellationToken);

        if (answer is null || answer.Items is null)
        {
            return Closed(SalesUserDisplayBatchOutcomeV1.Unavailable);
        }

        return answer.Outcome switch
        {
            UserDirectoryOutcomeV1.Found => new SalesUserDisplayBatchAnswerV1(
                SalesUserDisplayBatchOutcomeV1.Success,
                [.. answer.Items.Select(item => new SalesUserDisplayV1(
                    item.UserId,
                    item.DisplayName,
                    item.IsActiveInCompany))]),
            UserDirectoryOutcomeV1.Denied => Closed(SalesUserDisplayBatchOutcomeV1.Denied),
            _ => Closed(SalesUserDisplayBatchOutcomeV1.Unavailable),
        };
    }

    private static SalesUserDisplayBatchAnswerV1 Closed(SalesUserDisplayBatchOutcomeV1 outcome) =>
        new(outcome, []);
}
