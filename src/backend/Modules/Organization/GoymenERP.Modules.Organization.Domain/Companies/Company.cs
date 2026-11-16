namespace GoymenERP.Modules.Organization.Domain.Companies;

public sealed class Company
{
    /// <summary>A Turkish tax identity number (VKN) is exactly this many ASCII digits.</summary>
    public const int VknLength = 10;

    public Company(
        Guid id,
        string code,
        string legalName,
        string? vkn,
        bool isPlaceholder,
        bool invoiceCapable)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(id));
        }

        Id = id;
        Code = RequireText(code, nameof(code), 64);
        LegalName = RequireText(legalName, nameof(legalName), 250);
        Vkn = RequireVkn(vkn);
        if (!isPlaceholder && Vkn is null)
        {
            // KIM-D28: a real (non-placeholder) company always carries its VKN.
            throw new ArgumentException(
                "Placeholder olmayan şirket geçerli VKN taşımalıdır.",
                nameof(vkn));
        }

        if (invoiceCapable && (isPlaceholder || Vkn is null))
        {
            throw new ArgumentException(
                "Resmî fatura üretimine açık şirket placeholder olamaz ve geçerli VKN taşımalıdır.",
                nameof(invoiceCapable));
        }

        IsPlaceholder = isPlaceholder;
        InvoiceCapable = invoiceCapable;
    }

    private Company()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string LegalName { get; private set; } = string.Empty;

    public string? Vkn { get; private set; }

    public bool IsPlaceholder { get; private set; }

    public bool InvoiceCapable { get; private set; }

    public bool IsActive { get; private set; } = true;

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>True when <paramref name="value"/> is already in the stored canonical VKN shape (no trimming).</summary>
    public static bool IsCanonicalVkn(string? value) =>
        value is { Length: VknLength } && value.All(character => character is >= '0' and <= '9');

    /// <summary>
    /// Applies a VKN that passed the maker-checker change flow (KIM-D28). Only the approval of a
    /// <see cref="CompanyTaxIdChangeRequest"/> calls this; the proposed value is never written directly.
    /// </summary>
    public void ApplyApprovedVkn(string vkn, DateTimeOffset utcNow)
    {
        var normalized = RequireVkn(vkn)
            ?? throw new ArgumentException("Onaylanan VKN boş olamaz.", nameof(vkn));
        if (string.Equals(normalized, Vkn, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Onaylanan VKN şirketin mevcut VKN'si ile aynı olamaz.");
        }

        Vkn = normalized;
        Version++;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>
    /// KIM-D41/KIM-D48: applies an approved staged correction (<see cref="CompanyTaxIdCorrection"/>). The company must
    /// still carry <paramref name="stagedVkn"/>. A null <paramref name="correctedVkn"/> clears the VKN and is allowed only
    /// on a placeholder company that issues no invoice (the schema's CK_Companies_Vkn_RequiredUnlessPlaceholder and
    /// CK_Companies_Placeholder_InvoiceDisabled say the same).
    /// </summary>
    public void ApplyTaxIdCorrection(string stagedVkn, string? correctedVkn, DateTimeOffset utcNow)
    {
        if (!string.Equals(Vkn, stagedVkn, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Şirketin VKN'si düzeltilecek değerle eşleşmiyor.");
        }

        if (correctedVkn is null)
        {
            if (!IsPlaceholder || InvoiceCapable)
            {
                throw new InvalidOperationException(
                    "VKN yalnız fatura üretmeyen placeholder şirkette boşaltılabilir.");
            }

            Vkn = null;
        }
        else
        {
            var normalized = RequireVkn(correctedVkn)
                ?? throw new ArgumentException("Düzeltilmiş VKN boş olamaz.", nameof(correctedVkn));
            if (string.Equals(normalized, Vkn, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Düzeltilmiş VKN şirketin mevcut VKN'si ile aynı olamaz.");
            }

            Vkn = normalized;
        }

        Version++;
        UpdatedAtUtc = utcNow;
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length > maximumLength
            ? throw new ArgumentOutOfRangeException(parameterName)
            : normalized;
    }

    private static string? RequireVkn(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (!IsCanonicalVkn(normalized))
        {
            throw new ArgumentException(
                "VKN tam olarak 10 ASCII rakamdan oluşmalıdır.",
                nameof(value));
        }

        return normalized;
    }
}
