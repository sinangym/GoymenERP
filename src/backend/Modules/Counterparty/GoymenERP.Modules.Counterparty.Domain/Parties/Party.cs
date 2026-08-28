namespace GoymenERP.Modules.Counterparty.Domain.Parties;

public sealed class Party
{
    private Party()
    {
    }

    public Party(
        Guid id,
        PartyCode cariCode,
        PartyKind kind,
        string legalName,
        string? tradeName = null)
    {
        Id = RequireId(id, nameof(id));
        CariCode = cariCode;
        Kind = RequireKnownKind(kind);
        LegalName = RequireText(legalName, nameof(legalName), 250);
        TradeName = NormalizeOptionalText(tradeName, nameof(tradeName), 200);
        var now = DateTimeOffset.UtcNow;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public PartyCode CariCode { get; private set; }

    public PartyKind Kind { get; private set; }

    public string LegalName { get; private set; } = string.Empty;

    public string? TradeName { get; private set; }

    public bool IsActive { get; private set; } = true;

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void SetTradeName(string? tradeName)
    {
        TradeName = NormalizeOptionalText(tradeName, nameof(tradeName), 200);
    }

    public void ApplyApprovedLegalName(string legalName)
    {
        LegalName = RequireText(legalName, nameof(legalName), 250);
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Reactivate()
    {
        IsActive = true;
    }

    internal void Touch(DateTimeOffset utcNow)
    {
        UpdatedAtUtc = utcNow;
        Version++;
    }

    private static PartyKind RequireKnownKind(PartyKind kind)
    {
        return Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Geçersiz taraf türü.");
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        return value == Guid.Empty
            ? throw new ArgumentException("Kimlik boş olamaz.", parameterName)
            : value;
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Değer {maximumLength} karakterden uzun olamaz.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(
        string? value,
        string parameterName,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Değer {maximumLength} karakterden uzun olamaz.");
        }

        return normalized;
    }
}
