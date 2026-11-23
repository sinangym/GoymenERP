namespace GoymenERP.Modules.Identity.Domain.Bootstrap;

/// <summary>
/// ID-2 (KIM-D5/KIM-D18): the one-way marker that the founder account was created. The table holds at most one
/// row (fixed <see cref="SingletonId"/> plus a check constraint); once the row exists <c>bootstrap-founder</c>
/// refuses. The row is never updated or deleted by the application; recovery is a separate command (ID-2r).
/// </summary>
public sealed class IdentityBootstrapState
{
    /// <summary>The only allowed key of <c>identity.IdentityBootstrapState</c>; a structural value, not a policy.</summary>
    public const byte SingletonId = 1;

    private IdentityBootstrapState()
    {
    }

    public IdentityBootstrapState(Guid founderUserId, DateTimeOffset completedAtUtc)
    {
        if (founderUserId == Guid.Empty)
        {
            throw new ArgumentException("Kurucu kullanıcı kimliği boş olamaz.", nameof(founderUserId));
        }

        if (completedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Tamamlanma anı UTC olmalıdır.", nameof(completedAtUtc));
        }

        Id = SingletonId;
        FounderUserId = founderUserId;
        CompletedAtUtc = completedAtUtc;
    }

    public byte Id { get; private set; }

    public Guid FounderUserId { get; private set; }

    public DateTimeOffset CompletedAtUtc { get; private set; }
}
