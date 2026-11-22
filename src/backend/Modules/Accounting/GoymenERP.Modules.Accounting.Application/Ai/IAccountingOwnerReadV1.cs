namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// WP-A3c: the read-only part of the authorized ACC-V0 surface (<see cref="AccountingApiService"/>), as a narrow view for
/// in-process readers such as the AI READ tools. Each call measures its own narrow permission for the server's actor and
/// company exactly as the HTTP endpoint does; nothing here writes.
/// </summary>
public interface IAccountingOwnerReadV1
{
    Task<AccountingApiResult<PeriodPageV1>> ListPeriodsAsync(int? pageSize, string? cursor, CancellationToken cancellationToken);

    Task<AccountingApiResult<JournalPageV1>> ListJournalsAsync(Guid? periodId, int? pageSize, string? cursor,
        CancellationToken cancellationToken);

    Task<AccountingApiResult<PostingQueuesV1>> GetPostingQueuesAsync(CancellationToken cancellationToken);
}
