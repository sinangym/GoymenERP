namespace GoymenERP.Modules.Counterparty.Domain.Parties;

public sealed class Party
{
    /// <summary>Upper bound of the official legal name (resmi unvan), as the persisted column.</summary>
    public const int LegalNameMaximumLength = 250;

    /// <summary>Upper bound of the optional trade name (ticari ad), as the persisted column.</summary>
    public const int TradeNameMaximumLength = 200;

    private Party()
    {
    }

    public Party(
        Guid id,
        PartyCode partyCode,
        PartyKind kind,
        string legalName,
        string? tradeName = null)
    {
        Id = RequireId(id, nameof(id));
        Code = PartyCode.Parse(partyCode.Value);
        Kind = RequireKnownKind(kind);
        LegalName = RequireText(legalName, nameof(legalName), LegalNameMaximumLength);
        TradeName = NormalizeOptionalText(tradeName, nameof(tradeName), TradeNameMaximumLength);
        var now = DateTimeOffset.UtcNow;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public PartyCode Code { get; private set; }

    public PartyKind Kind { get; private set; }

    public string LegalName { get; private set; } = string.Empty;

    public string? TradeName { get; private set; }

    /// <summary>
    /// AT-009: the party's VKN or TCKN (canonical digits, checksum-valid), or null while none has been entered. One number
    /// per party: the party is the legal counterparty shared by every Göymen company (DEC-CAR-001).
    /// </summary>
    public string? TaxIdentityNumber { get; private set; }

    public bool IsActive { get; private set; } = true;

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// AT-009 first entry: allowed only while the party has no number. Replacing an existing number is never done here:
    /// it is a change request a second person approves (<see cref="ApplyApprovedTaxIdentityChange"/>).
    /// </summary>
    internal void EnterTaxIdentityNumber(TaxIdentityNumber number, DateTimeOffset utcNow)
    {
        RequireParsed(number);
        if (TaxIdentityNumber is not null)
        {
            throw new InvalidOperationException(
                "Kayıtlı vergi/kimlik numarası doğrudan değiştirilemez; değişiklik ikinci kişi onayı gerektirir.");
        }

        TaxIdentityNumber = number.Value;
        Touch(utcNow);
    }

    /// <summary>
    /// AT-009 approved change: the approved request's proposal replaces the number that was current when the request was
    /// made. A party whose number moved since then is not changed (the request is stale).
    /// </summary>
    internal void ApplyApprovedTaxIdentityChange(
        string? expectedCurrentNumber,
        TaxIdentityNumber proposed,
        DateTimeOffset utcNow)
    {
        RequireParsed(proposed);
        if (!string.Equals(TaxIdentityNumber, expectedCurrentNumber, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Vergi/kimlik numarası talepten sonra değişmiş; talep güncel değil.");
        }

        if (string.Equals(TaxIdentityNumber, proposed.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Önerilen vergi/kimlik numarası mevcut numarayla aynı.");
        }

        TaxIdentityNumber = proposed.Value;
        Touch(utcNow);
    }

    /// <summary>
    /// F-027 / C-02 trade name edit: the optional trade name (ticari ad) is a basic card field, changed directly with the
    /// basic-update permission and audited (CARI-DEC-004 lists only the official legal name, VKN/TCKN, MERSİS and IBAN as
    /// approval-bound). Blank clears it. Returns false and leaves the version untouched when the normalized value is the
    /// one already stored, so a repeated save is not a business change. The legal name is never changed here.
    /// </summary>
    internal bool ChangeTradeName(string? tradeName, DateTimeOffset utcNow)
    {
        var normalized = NormalizeOptionalText(tradeName, nameof(tradeName), TradeNameMaximumLength);
        if (string.Equals(TradeName, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        TradeName = normalized;
        Touch(utcNow);
        return true;
    }

    /// <summary>The trade name as <see cref="ChangeTradeName"/> would store it (trimmed, blank as null, bounded).</summary>
    public static string? NormalizeTradeName(string? tradeName) =>
        NormalizeOptionalText(tradeName, nameof(tradeName), TradeNameMaximumLength);

    internal void Touch(DateTimeOffset utcNow)
    {
        UpdatedAtUtc = utcNow;
        Version++;
    }

    private static void RequireParsed(TaxIdentityNumber number)
    {
        if (string.IsNullOrEmpty(number.Value))
        {
            throw new ArgumentException("Vergi/kimlik numarası doğrulanmadan kaydedilemez.", nameof(number));
        }
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
