using System.Globalization;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

// The Product-owned family registry (PRD-FAM-1). Rows are read-only here: nothing in the runtime
// writes them, no default family, template or entry is seeded, and a release is loaded only by the
// decided route (Business Q6). Every row carries the source it comes from.

/// <summary>A released canonical family (#66 5739827887); Cins/Tür/Tip are fixed by the family.</summary>
public sealed class ProductFamilyRow
{
    private ProductFamilyRow() { }
    internal ProductFamilyRow(ProductFamilyV2 value)
    {
        Id = value.Id; Code = value.Code; Name = value.Name; Cins = value.Cins; Tur = value.Tur;
        Tip = value.Tip; RuleSet = value.RuleSet; SourceRef = value.SourceRef;
    }
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Cins { get; private set; } = string.Empty;
    public string Tur { get; private set; } = string.Empty;
    public string Tip { get; private set; } = string.Empty;
    public string RuleSet { get; private set; } = string.Empty;
    public string SourceRef { get; private set; } = string.Empty;

    public ProductFamilyV2 ToContract() => new(Id, Code, Name, Cins, Tur, Tip, RuleSet, SourceRef);
}

/// <summary>One released template version (U-09: an immutable id; a changed field set is a new version).</summary>
public sealed class ProductTemplateRow
{
    private ProductTemplateRow() { }
    internal ProductTemplateRow(ProductTemplateV2 value)
    {
        Id = value.Id; Version = long.Parse(value.Version, CultureInfo.InvariantCulture);
        Code = value.Code; Name = value.Name; SourceRef = value.SourceRef;
    }
    public Guid Id { get; private set; }
    public long Version { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string SourceRef { get; private set; } = string.Empty;

    public string VersionText() => Version.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A family served by a template version.</summary>
public sealed class ProductTemplateFamilyRow
{
    private ProductTemplateFamilyRow() { }
    internal ProductTemplateFamilyRow(ProductTemplateRow template, Guid familyId)
    {
        TemplateId = template.Id; TemplateVersion = template.Version; FamilyId = familyId;
    }
    public Guid TemplateId { get; private set; }
    public long TemplateVersion { get; private set; }
    public Guid FamilyId { get; private set; }
}

/// <summary>One field of a template version, keyed as it appears in <c>references.attributes</c>.</summary>
public sealed class ProductTemplateFieldRow
{
    private ProductTemplateFieldRow() { }
    internal ProductTemplateFieldRow(ProductTemplateRow template, ProductTemplateFieldV2 value, int position)
    {
        TemplateId = template.Id; TemplateVersion = template.Version; Position = position;
        Key = value.Key; Kind = (int)value.Kind; Role = value.Role; Required = value.Required;
        Dictionary = value.Dictionary; AllowsCandidate = value.AllowsCandidate;
        AppliesWhenField = value.AppliesWhenField; AppliesWhenCode = value.AppliesWhenCode;
        ParentField = value.ParentField;
    }
    public Guid TemplateId { get; private set; }
    public long TemplateVersion { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public int Position { get; private set; }
    public int Kind { get; private set; }
    public string? Role { get; private set; }
    public bool Required { get; private set; }
    public string? Dictionary { get; private set; }
    public bool AllowsCandidate { get; private set; }
    public string? AppliesWhenField { get; private set; }
    public string? AppliesWhenCode { get; private set; }
    public string? ParentField { get; private set; }

    public ProductTemplateFieldV2 ToContract() => new(Key, (ProductTemplateFieldKindV2)Kind, Role, Required, Dictionary,
        AllowsCandidate, AppliesWhenField, AppliesWhenCode, ParentField);
}

/// <summary>
/// One controlled value. <see cref="Status"/> 1 approved, 2 candidate: a candidate carries no invented
/// meaning or mass and never satisfies a controlled field (#66 5739827887).
/// </summary>
public sealed class ProductDictionaryEntryRow
{
    private ProductDictionaryEntryRow() { }
    internal ProductDictionaryEntryRow(ProductDictionaryEntryV2 value)
    {
        Dictionary = value.Dictionary; Code = value.Code; DisplayName = value.DisplayName;
        Status = (int)value.Status; SourceRef = value.SourceRef; ParentCode = value.ParentCode;
        MassGm2 = value.MassGm2; IsExplicitNone = value.IsExplicitNone;
    }
    public string Dictionary { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public int Status { get; private set; }
    public string SourceRef { get; private set; } = string.Empty;
    public string? ParentCode { get; private set; }
    public decimal? MassGm2 { get; private set; }
    public bool IsExplicitNone { get; private set; }

    public ProductDictionaryEntryV2 ToContract() => new(Dictionary, Code, DisplayName,
        (ProductDefinitionEntryStatusV2)Status, SourceRef, ParentCode, MassGm2, IsExplicitNone);
}
