namespace GoymenERP.Modules.Product.Domain.Products;

/// <summary>
/// Group-shared master identity (PRD-SCOPE-001). Physical lot/quality and company
/// ownership belong to downstream records. Creation does not activate the card.
/// </summary>
public sealed class Product
{
    public const int MaximumNameLength = 250;

    private Product(Guid id, ProductCode code, string name, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ürün kimliği boş olamaz.", nameof(id));
        }

        Code = ProductCode.Parse(code.Value);
        Id = id;
        Name = NormalizeName(name);
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public ProductCode Code { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ProductLifecycle Lifecycle { get; private set; } = ProductLifecycle.Draft;
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid? ClassId { get; private set; }
    public Guid? FamilyId { get; private set; }
    public ProductRoles Roles { get; private set; } = ProductRoles.NotEvaluated;

    public static Product CreateDraft(Guid id, ProductCode code, string name, Guid groupId,
        Guid? classId, Guid? familyId, ProductRoles roles, DateTimeOffset now)
    {
        RequireReference(groupId, nameof(groupId));
        RequireReference(classId, nameof(classId));
        RequireReference(familyId, nameof(familyId));
        roles.Validate();
        return new Product(id, code, name, now)
        {
            GroupId = groupId, ClassId = classId, FamilyId = familyId, Roles = roles,
        };
    }

    internal static Product Restore(Guid id, string code, string name, Guid groupId,
        Guid? classId, Guid? familyId, ProductRoles roles, ProductLifecycle lifecycle,
        long version, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        if (!Enum.IsDefined(lifecycle) || version < 1 || updatedAtUtc < createdAtUtc)
        {
            throw new InvalidOperationException("Saklanan ürün yaşam döngüsü/sürüm/zaman bilgisi geçersiz.");
        }

        var product = CreateDraft(id, ProductCode.Parse(code), name, groupId,
            classId, familyId, roles, createdAtUtc);
        product.Lifecycle = lifecycle;
        product.Version = version;
        product.UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        return product;
    }

    public void EditBasic(long expectedVersion, string name, Guid? classId, Guid? familyId,
        ProductRoles roles, string reason, DateTimeOffset now)
    {
        if (expectedVersion != Version)
        {
            throw new ProductVersionConflictException(expectedVersion, Version);
        }

        if (Lifecycle != ProductLifecycle.Draft)
        {
            throw new InvalidOperationException("Yalnız taslak ürün temel bilgileri düzenlenebilir.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalizedName = NormalizeName(name);
        RequireReference(classId, nameof(classId));
        RequireReference(familyId, nameof(familyId));
        roles.Validate();
        PreserveDerived(Roles.Purchasable, roles.Purchasable);
        PreserveDerived(Roles.Sellable, roles.Sellable);
        PreserveDerived(Roles.ProductionInput, roles.ProductionInput);
        PreserveDerived(Roles.ProductionOutput, roles.ProductionOutput);
        var nextVersion = checked(Version + 1);
        Name = normalizedName;
        ClassId = classId;
        FamilyId = familyId;
        Roles = roles;
        UpdatedAtUtc = now.ToUniversalTime();
        Version = nextVersion;
    }

    /// <summary>
    /// DEC-PRD-010: a Draft card is approved for firm use. Only Draft becomes Active, at the version
    /// the approver read. Completeness is not a precondition here (no source makes it one); the
    /// operational eligibility reads still judge the whole card at the moment of use.
    /// </summary>
    public void Activate(long expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion != Version)
        {
            throw new ProductVersionConflictException(expectedVersion, Version);
        }

        if (Lifecycle != ProductLifecycle.Draft)
        {
            throw new InvalidOperationException("Yalnız taslak ürün kartı onaylanabilir.");
        }

        var nextVersion = checked(Version + 1);
        Lifecycle = ProductLifecycle.Active;
        UpdatedAtUtc = now.ToUniversalTime();
        Version = nextVersion;
    }

    private static void PreserveDerived(ProductRoleState before, ProductRoleState after)
    {
        if (before.Provenance == ProductRoleProvenance.Derived && before != after)
        {
            throw new InvalidOperationException("Türetilmiş ürün rolü elle değiştirilemez.");
        }
    }

    private static void RequireReference(Guid? value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Referans kimliği boş olamaz.", name);
        }
    }

    public static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > MaximumNameLength || value.Any(char.IsControl))
        {
            throw new ArgumentException("Ürün adı uzunluğu veya karakterleri geçersiz.", nameof(value));
        }

        return normalized;
    }
}

public sealed class ProductVersionConflictException(long expected, long current)
    : Exception("Ürün sürümü değişti.")
{
    public long ExpectedVersion { get; } = expected;
    public long CurrentVersion { get; } = current;
}
