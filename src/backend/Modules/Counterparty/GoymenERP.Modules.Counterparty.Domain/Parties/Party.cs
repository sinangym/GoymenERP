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

    /// <summary>
    /// F-027 / EK-18 #76: the surviving party this duplicate was merged into, or null while it was never merged. Set once
    /// and never cleared: there is no unmerge (#77); a wrong merge is corrected by opening a new card and reversing the
    /// moved links with a reason chain, never by editing or deleting this value.
    /// </summary>
    public Guid? MergedIntoPartyId { get; private set; }

    /// <summary>
    /// YOL-D31 (WP14): the Göymen group company this party IS, when someone linked it on the card; null otherwise. A party is a
    /// group company only through this link — never by a tax-number match. Group-wide like the party itself.
    /// </summary>
    public Guid? GroupCompanyId { get; private set; }

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
    /// basic-update permission and audited (ALS-D97). Blank clears it. Returns false and leaves the version untouched when
    /// the normalized value is the one already stored, so a repeated save is not a business change. The legal name is
    /// never changed here (ALS-D111/D113 give it its own maker-checker path).
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

    /// <summary>
    /// YOL-D31 (WP14): link the party to a group company, or clear the link with null. Returns false and leaves the version
    /// untouched when the value is the stored one, so a repeated save is not a business change.
    /// </summary>
    internal bool ChangeGroupCompany(Guid? groupCompanyId, DateTimeOffset utcNow)
    {
        if (groupCompanyId == Guid.Empty)
        {
            throw new ArgumentException("Grup şirketi kimliği boş olamaz.", nameof(groupCompanyId));
        }

        if (GroupCompanyId == groupCompanyId)
        {
            return false;
        }

        GroupCompanyId = groupCompanyId;
        Touch(utcNow);
        return true;
    }

    /// <summary>The trade name as <see cref="ChangeTradeName"/> would store it (trimmed, blank as null, bounded).</summary>
    public static string? NormalizeTradeName(string? tradeName) =>
        NormalizeOptionalText(tradeName, nameof(tradeName), TradeNameMaximumLength);

    /// <summary>
    /// The official legal name (resmi/hukuki unvan) write. It stays required (trimmed, at most
    /// <see cref="LegalNameMaximumLength"/>); blank is refused, never stored. Returns false and leaves the version untouched
    /// when the normalized value is the one already stored, so a repeated save is not a business change. ALS-D111/D113
    /// (replacing the one-step ALS-D110 path): the only command that reaches it is the approval of a
    /// <see cref="PartyLegalNameChangeRequest"/> by a person other than its proposer
    /// (<see cref="ApplyApprovedLegalNameChange"/>); the name does not move before that approval.
    /// </summary>
    internal bool ChangeLegalName(string legalName, DateTimeOffset utcNow)
    {
        var normalized = NormalizeLegalName(legalName);
        if (string.Equals(LegalName, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        LegalName = normalized;
        Touch(utcNow);
        return true;
    }

    /// <summary>
    /// ALS-D111/D113 approved change: the approved request's proposal replaces the legal name that was current when the
    /// request was made. A party whose legal name moved since then is not changed (the request is stale), and a proposal
    /// equal to the stored name is never an approval.
    /// </summary>
    internal void ApplyApprovedLegalNameChange(string expectedCurrentLegalName, string proposedLegalName, DateTimeOffset utcNow)
    {
        if (!string.Equals(LegalName, expectedCurrentLegalName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resmi unvan talepten sonra değişmiş; talep güncel değil.");
        }

        if (!ChangeLegalName(proposedLegalName, utcNow))
        {
            throw new InvalidOperationException("Önerilen resmi unvan mevcut unvanla aynı.");
        }
    }

    /// <summary>
    /// The legal name as <see cref="ChangeLegalName"/> would store it (trimmed, bounded). Blank throws
    /// <see cref="ArgumentException"/>; too long throws <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public static string NormalizeLegalName(string? legalName) =>
        RequireText(legalName!, nameof(legalName), LegalNameMaximumLength);

    /// <summary>
    /// F-027 / EK-18 #76: this duplicate is merged into <paramref name="survivor"/>. The duplicate becomes passive at
    /// group level and points at the survivor for good (no unmerge, #77). A party is never merged into itself, a party
    /// already merged is never merged again, and a party already merged away is never a survivor. Nothing else changes
    /// here: the duplicate keeps its code, names and tax identity as history.
    /// </summary>
    internal void MergeInto(Party survivor, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(survivor);
        if (survivor.Id == Id)
        {
            throw new InvalidOperationException("Cari kendisiyle birleştirilemez.");
        }

        if (MergedIntoPartyId is not null)
        {
            throw new InvalidOperationException("Bu cari daha önce başka bir cariyle birleştirilmiş.");
        }

        if (survivor.MergedIntoPartyId is not null)
        {
            throw new InvalidOperationException("Başka bir cariyle birleştirilmiş cari birleştirme hedefi olamaz.");
        }

        MergedIntoPartyId = survivor.Id;
        IsActive = false;
        Touch(utcNow);
    }

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
