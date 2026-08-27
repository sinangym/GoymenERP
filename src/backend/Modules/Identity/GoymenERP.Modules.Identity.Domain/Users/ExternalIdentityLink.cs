namespace GoymenERP.Modules.Identity.Domain.Users;

public sealed class ExternalIdentityLink
{
    private ExternalIdentityLink()
    {
    }

    public ExternalIdentityLink(Guid id, Guid userId, string issuer, string subject)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Kimlik bağlantısı için geçerli kimlikler zorunludur.");
        }

        Id = id;
        UserId = userId;
        Issuer = Required(issuer, nameof(issuer));
        Subject = Required(subject, nameof(subject));
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Issuer { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return value.Trim();
    }
}
