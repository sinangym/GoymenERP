using System.Data;
using System.Data.Common;
using System.Globalization;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// ID-3 serialization of one company's access administration (spec section ID-3, KIM-D17). Two fixed statements on the
/// caller's open EF transaction, parameters only, never SQL text:
/// <list type="number">
/// <item><c>sp_getapplock</c> (<c>Exclusive</c>, owner <c>Transaction</c>) on <c>identity.admin:{companyId}</c>, so two
/// grants, two revocations or a grant and a revocation of the same company never interleave between their checks and their
/// save. A negative answer is "busy" (the caller writes nothing); an answer that is not a lock code is a technical failure,
/// never "granted".</item>
/// <item>The count of the OTHER effective, open-ended, company-scoped holders of a role (the last Admin rule), read with
/// <c>READCOMMITTEDLOCK</c> so a row-versioned (RCSI) snapshot can never answer it.</item>
/// </list>
/// The lock is released with the transaction; there is no release call and no session-owned lock.
/// </summary>
internal static class IdentityAdminLock
{
    /// <summary>
    /// How long a second administrator's command waits for the first one's transaction before it is answered "busy" (503):
    /// a technical wait, not a business threshold; an access administration transaction holds the lock for a few statements.
    /// </summary>
    internal static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The command waits a little longer than the lock itself so the lock's own timeout (−1) answers first, not the driver.</summary>
    private static readonly TimeSpan CommandTimeoutMargin = TimeSpan.FromSeconds(5);

    private const string AcquireStatement =
        "DECLARE @result int; "
        + "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', "
        + "@LockTimeout = @timeout; "
        + "SELECT @result;";

    /// <summary>
    /// Another holder counts only while it is effective now and stays so: active, started, open-ended (a holder whose end is
    /// set will lapse by itself), user and role active, with the company scope of this company.
    /// </summary>
    private const string OtherHoldersStatement =
        "SELECT COUNT(DISTINCT a.[UserId]) FROM [identity].[RoleAssignments] AS a WITH (READCOMMITTEDLOCK) "
        + "INNER JOIN [identity].[Users] AS u WITH (READCOMMITTEDLOCK) ON u.[Id] = a.[UserId] "
        + "INNER JOIN [identity].[Roles] AS r WITH (READCOMMITTEDLOCK) ON r.[Id] = a.[RoleId] "
        + "WHERE a.[CompanyId] = @companyId AND a.[RoleId] = @roleId AND a.[Status] = N'Active' "
        + "AND a.[ValidFromUtc] <= @now AND a.[ValidUntilUtc] IS NULL AND a.[Id] <> @excludedId "
        + "AND u.[IsActive] = 1 AND r.[IsActive] = 1 "
        + "AND EXISTS (SELECT 1 FROM [identity].[RoleAssignmentScopes] AS s WITH (READCOMMITTEDLOCK) "
        + "WHERE s.[CompanyId] = a.[CompanyId] AND s.[RoleAssignmentId] = a.[Id] AND s.[ScopeType] = N'Company' "
        + "AND s.[ScopeId] = @companyId);";

    /// <summary>The lock resource of one company: the company identity only, never a user or a role name.</summary>
    internal static string ResourceName(Guid companyId) =>
        string.Create(CultureInfo.InvariantCulture, $"identity.admin:{companyId:N}");

    /// <summary>True when the lock is held (granted at once or after waiting); false when the company is busy.</summary>
    internal static async Task<bool> TryAcquireAsync(
        IdentityDbContext database,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new InvalidOperationException("Yönetim kilidi yalnız çözümlenmiş şirket bağlamında alınır.");
        }

        await using var command = CreateCommand(database, AcquireStatement,
            (int)Math.Ceiling((LockTimeout + CommandTimeoutMargin).TotalSeconds));
        var resource = ResourceName(companyId);
        AddParameter(command, "@resource", DbType.String, resource, resource.Length);
        AddParameter(command, "@timeout", DbType.Int32, (int)LockTimeout.TotalMilliseconds);

        var answer = await command.ExecuteScalarAsync(cancellationToken);
        if (answer is not int result)
        {
            throw new InvalidOperationException("Yönetim kilidi cevabı okunamadı.");
        }

        return result >= 0;
    }

    internal static async Task<int> CountOtherEffectiveHoldersAsync(
        IdentityDbContext database,
        Guid companyId,
        Guid roleId,
        Guid excludedAssignmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(database, OtherHoldersStatement, commandTimeout: null);
        AddParameter(command, "@companyId", DbType.Guid, companyId);
        AddParameter(command, "@roleId", DbType.Guid, roleId);
        AddParameter(command, "@excludedId", DbType.Guid, excludedAssignmentId);
        AddParameter(command, "@now", DbType.DateTimeOffset, now);

        var answer = await command.ExecuteScalarAsync(cancellationToken);
        return answer is int count ? count : throw new InvalidOperationException("Yönetici sayımı okunamadı.");
    }

    private static DbCommand CreateCommand(IdentityDbContext database, string text, int? commandTimeout)
    {
        var transaction = database.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Yönetim kilidi yalnız açık bir işlem içinde kullanılır.");
        var connection = database.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Yönetim kilidi yalnız açık bağlantıda alınır.");
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandType = CommandType.Text;
        command.CommandText = text;
        if (commandTimeout is { } seconds)
        {
            command.CommandTimeout = seconds;
        }

        return command;
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        if (size is { } length)
        {
            parameter.Size = length;
        }

        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
