namespace GoymenERP.Modules.Identity.Infrastructure.Bootstrap;

/// <summary>
/// <c>Identity:AdminRoleCode</c> (spec ID-2, KIM-D5/KIM-D18): the role the founder receives in every company. There is
/// no code default; the bootstrap command refuses while it is unset (fail-closed). The role itself, its permissions
/// and its Critical classification are a human gate and are never created here.
/// </summary>
public sealed class IdentityBootstrapOptions
{
    public const string SectionName = "Identity";

    public string? AdminRoleCode { get; set; }
}
