using GoymenERP.Api.Shell;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.U02;

/// <summary>
/// The u02 seed (ÜRÜN, U-02 new-product and basic-edit screens, U-01-360 activation). It writes only through
/// the two gated fixture writers (W1) — Product master authority for the authorized actor and for the
/// disposable activator, issued by the suite's synthetic issuer, and one synthetic code group — and then
/// reads back through the real API as each actor. It creates no Product card: the browser suite does that
/// through the real commands. Every value is synthetic (SENTETIK browser-suite:u02 …) and exists only in the
/// run's disposable database.
/// <para>
/// Activation (#466, user decision 2026-09-27): the Product master <c>Activate</c> operation is granted ONLY to
/// the disposable <see cref="ActivatorActor"/>, never to <see cref="AuthorizedActor"/>, a real user or a seeded
/// role. The activator holds Read + Activate and nothing else; the read-back below pins both capability sets,
/// so a grant that leaks Activate to the authorized actor, or create/edit to the activator, fails the run here.
/// </para>
/// <para>
/// Deactivation / reactivation (PRD-PERM-001 #99 names activate/deactivate/reactivate together): the same
/// disposable actor also holds Product master <c>Deactivate</c> and <c>Reactivate</c>, each its own explicit
/// assignment in this run's database only. No real user, production role or seeded role gains either; the
/// read-back below pins the actor's exact five Product capabilities. PENDING coordinator confirmation that the
/// 2026-09-27 disposable-activator fixture decision may carry the two lifecycle grants.
/// </para>
/// <para>
/// Decision C: neither writer writes an AuditEvent. This seed makes no audited call (the reads are not
/// audited), so the SUITE_FIXTURE line's <c>audit_delta=0</c> is that evidence on the real SQL.
/// </para>
/// </summary>
public sealed class U02BrowserSeed : IBrowserSuiteFixture
{
    public const string AuthorizedActor = "group-bootstrap-admin";
    public const string UnauthorizedActor = "company-a-user";

    /// <summary>
    /// The disposable activator: a BootstrapIdentity user of infra/browser-suites/u02.override.yml with its
    /// Identity user (subject u02-activator) and one synthetic company membership in GDC, the origin the Product
    /// authority requires. It is the only actor of the run that holds Product master Activate.
    /// </summary>
    public const string ActivatorActor = "u02-activator";

    /// <summary>The GDC company of the built-in organization seed (the context company of both web profiles).</summary>
    public static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    /// <summary>
    /// The suite's synthetic Product issuer (IdentitySeed via infra/browser-suites/u02.override.yml; the same
    /// values are the override's ProductRuntime__Issuer* keys; s03 shares them, D3). Not a sign-in actor.
    /// </summary>
    public static readonly Guid IssuerUserId = Guid.Parse("b2000000-0000-4000-8000-000000000201");
    public static readonly Guid IssuerExternalIdentityId = Guid.Parse("b2100000-0000-4000-8000-000000000201");

    /// <summary>
    /// group-bootstrap-admin's Identity user and the canonical SYSTEM_ADMINISTRATOR system role: constants
    /// of the built-in src/backend/GoymenERP.Api/Seed/identity-seed.json (a seed reads nothing with SQL).
    /// A wrong value cannot pass silently: the real-API read-back below needs the grant to resolve.
    /// </summary>
    public static readonly Guid AuthorizedActorUserId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    public static readonly Guid SystemAdministratorRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    /// <summary>
    /// The activator's Identity user: IdentitySeed via infra/browser-suites/u02.override.yml (the same value is the
    /// override's IdentitySeed user Id). Its grants below carry the SYSTEM_ADMINISTRATOR role id because the
    /// Identity domain requires that role code on every explicit Product master assignment
    /// (<c>ProductMasterAssignment.RequiredRoleCode</c>); the actor itself holds no SYSTEM_ADMINISTRATOR
    /// membership, only its synthetic GDC membership. A wrong value cannot pass silently: the read-back needs
    /// the grants to resolve.
    /// </summary>
    public static readonly Guid ActivatorUserId = Guid.Parse("b2000000-0000-4000-8000-000000000210");

    public static readonly Guid CodeGroupId = Guid.Parse("b6000000-0000-4000-8000-000000000201");
    public const string CodeGroupPrefix = "SU02";

    private static readonly DateTimeOffset ValidFromUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The authorized actor's operations U-02 needs: list/detail (Read), create, basic edit. Nothing more — in
    /// particular no Activate (user decision 2026-09-27).
    /// </summary>
    private static readonly (ProductMasterOperation Operation, Guid AssignmentId, Guid DecisionId)[] Grants =
    [
        (ProductMasterOperation.Read, Guid.Parse("b5000000-0000-4000-8000-000000000201"), Guid.Parse("b5100000-0000-4000-8000-000000000201")),
        (ProductMasterOperation.DraftCreate, Guid.Parse("b5000000-0000-4000-8000-000000000202"), Guid.Parse("b5100000-0000-4000-8000-000000000202")),
        (ProductMasterOperation.DraftBasicEdit, Guid.Parse("b5000000-0000-4000-8000-000000000203"), Guid.Parse("b5100000-0000-4000-8000-000000000203")),
    ];

    /// <summary>
    /// The activator's operations: the card (Read), the U-01-360 approval, and the deactivation / reactivation of a
    /// card in use. No create, no edit.
    /// </summary>
    private static readonly (ProductMasterOperation Operation, Guid AssignmentId, Guid DecisionId)[] ActivatorGrants =
    [
        (ProductMasterOperation.Read, Guid.Parse("b5000000-0000-4000-8000-000000000210"), Guid.Parse("b5100000-0000-4000-8000-000000000210")),
        (ProductMasterOperation.Activate, Guid.Parse("b5000000-0000-4000-8000-000000000211"), Guid.Parse("b5100000-0000-4000-8000-000000000211")),
        (ProductMasterOperation.Deactivate, Guid.Parse("b5000000-0000-4000-8000-000000000212"), Guid.Parse("b5100000-0000-4000-8000-000000000212")),
        (ProductMasterOperation.Reactivate, Guid.Parse("b5000000-0000-4000-8000-000000000213"), Guid.Parse("b5100000-0000-4000-8000-000000000213")),
    ];

    public string Suite => "u02";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        await using (var technicalScope = context.Services.CreateAsyncScope())
        {
            await technicalScope.ServiceProvider.GetRequiredService<C01E2eProductTechnicalCardFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        await using (var scope = context.Services.CreateAsyncScope())
        {
            var assignments = scope.ServiceProvider.GetRequiredService<C01E2eProductMasterAssignmentFixtureWriter>();
            foreach (var (operation, assignmentId, decisionId) in Grants)
            {
                await assignments.EnsureAsync(new ProductMasterAssignmentFixtureRow(
                    assignmentId,
                    AuthorizedActorUserId,
                    SystemAdministratorRoleId,
                    operation,
                    IssuerUserId,
                    IssuerExternalIdentityId,
                    decisionId,
                    context.Label($"Product master {operation} for {AuthorizedActor}"),
                    ValidFromUtc), cancellationToken);
            }

            foreach (var (operation, assignmentId, decisionId) in ActivatorGrants)
            {
                await assignments.EnsureAsync(new ProductMasterAssignmentFixtureRow(
                    assignmentId,
                    ActivatorUserId,
                    SystemAdministratorRoleId,
                    operation,
                    IssuerUserId,
                    IssuerExternalIdentityId,
                    decisionId,
                    context.Label($"Product master {operation} for {ActivatorActor}"),
                    ValidFromUtc), cancellationToken);
            }

            await scope.ServiceProvider.GetRequiredService<C01E2eProductCodeGroupFixtureWriter>().EnsureAsync(
                new ProductCodeGroupFixtureRow(CodeGroupId, context.Label("kod grubu"), CodeGroupPrefix),
                cancellationToken);
        }

        // Read back through the real host, as each actor: the fixture is only done when the product
        // runtime actually answers with it — this is also the proof that the issuer binding (the override's
        // IdentitySeed user) and the constants above hold: a grant that does not resolve opens no screen.
        var authorized = await context.GetJsonAsync(AuthorizedActor, CompanyId, "/api/v1/shell/capabilities", cancellationToken);
        var authorizedProduct = ProductCapabilities(authorized);
        if (!authorizedProduct.SequenceEqual(["product.items.create", "product.items.detail", "product.items.edit", "product.items.list"]))
        {
            throw new InvalidOperationException(
                $"The authorized actor should see the four Product screens; it sees [{string.Join(", ", authorizedProduct)}].");
        }

        var groups = await context.GetJsonAsync(AuthorizedActor, CompanyId, "/api/v1/products/code-groups", cancellationToken);
        if (!groups.EnumerateArray().Any(group => group.GetProperty("id").GetGuid() == CodeGroupId))
        {
            throw new InvalidOperationException("The synthetic code group is not offered to the authorized actor.");
        }

        var unauthorized = await context.GetJsonAsync(UnauthorizedActor, CompanyId, "/api/v1/shell/capabilities", cancellationToken);
        if (ProductCapabilities(unauthorized).Length != 0)
        {
            throw new InvalidOperationException("The unauthorized actor must see no Product screen.");
        }

        // The activator reaches the card (list/detail, Read) and its activation control, and nothing that
        // writes a card: no create screen, no edit screen. The authorized actor's exact four above carry no
        // activation control, so the activator is the only actor the shell offers it to.
        var activator = await context.GetJsonAsync(ActivatorActor, CompanyId, "/api/v1/shell/capabilities", cancellationToken);
        var activatorProduct = ProductCapabilities(activator);
        if (!activatorProduct.SequenceEqual(["product.items.activate", "product.items.deactivate", "product.items.detail",
                "product.items.list", "product.items.reactivate"]))
        {
            throw new InvalidOperationException(
                $"The activator should see the Product card and its three lifecycle controls only; it sees [{string.Join(", ", activatorProduct)}].");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["companyId"] = CompanyId.ToString("D"),
            ["codeGroupId"] = CodeGroupId.ToString("D"),
            ["technicalFamilyId"] = ProductTechnicalCardFixture.FamilyId.ToString("D"),
            ["authorizedActor"] = AuthorizedActor,
            ["unauthorizedActor"] = UnauthorizedActor,
            ["activatorActor"] = ActivatorActor,
        };
    }

    private static string[] ProductCapabilities(System.Text.Json.JsonElement snapshot) =>
        [.. snapshot.GetProperty("capabilities").EnumerateArray()
            .Select(capability => capability.GetString()!)
            .Where(capability => capability.StartsWith("product.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
}
