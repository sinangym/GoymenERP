namespace GoymenERP.Modules.Organization.Domain.Companies;

public sealed class Company
{
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

        if (normalized.Length != 10
            || normalized.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException(
                "VKN tam olarak 10 ASCII rakamdan oluşmalıdır.",
                nameof(value));
        }

        return normalized;
    }
}
