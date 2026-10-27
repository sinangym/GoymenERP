using System.Reflection;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// MOD-08 user directory port, on the in-memory provider: the company fence, the membership predicate, the fail-closed
/// request shape and what never leaves the module. The real SQL translation is measured by the SqlServerFact suite.
/// </summary>
public sealed class UserDirectoryReadPortTests
{
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();

    [Fact]
    public async Task Resolve_names_members_of_the_company_and_never_a_person_of_another_company()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        var answer = await Port(db, CompanyA, seeded.Caller).ResolveAsync(new UserDirectoryResolveRequestV1(
            CompanyA, Actor(seeded.Caller), [seeded.Alice, seeded.Retired, seeded.OnlyInB, Guid.NewGuid()]));

        Assert.Equal(UserDirectoryOutcomeV1.Found, answer.Outcome);
        Assert.Equal(
            new[]
            {
                new UserDirectoryEntryV1(seeded.Alice, "Alice", true),
                // A former member keeps a name for the records they acted on, marked as no longer active here.
                new UserDirectoryEntryV1(seeded.Retired, "Emekli", false),
            }.OrderBy(entry => entry.UserId),
            answer.Items);

        // Company B: B's person and Alice (who also holds a B assignment) — never A's retired member.
        var inB = await Port(db, CompanyB, seeded.CallerInB).ResolveAsync(new UserDirectoryResolveRequestV1(
            CompanyB, Actor(seeded.CallerInB), [seeded.Alice, seeded.Retired, seeded.OnlyInB]));
        Assert.Equal(UserDirectoryOutcomeV1.Found, inB.Outcome);
        Assert.Equal(new[] { seeded.Alice, seeded.OnlyInB }.Order(), inB.Items.Select(item => item.UserId));
    }

    [Fact]
    public async Task A_passive_user_scopeless_revoked_or_passive_role_membership_is_named_but_not_active()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        var passiveUser = AddUser(db, "Pasif");
        Assign(db, passiveUser, CompanyA, scoped: true);
        var scopeless = AddUser(db, "Kapsamsiz");
        Assign(db, scopeless, CompanyA, scoped: false);
        var revoked = AddUser(db, "Iptal");
        var revokedAssignment = Assign(db, revoked, CompanyA, scoped: true);
        var passiveRoleHolder = AddUser(db, "PasifRol");
        var passiveRoleAssignment = Assign(db, passiveRoleHolder, CompanyA, scoped: true);
        var scheduled = AddUser(db, "Ileride");
        Assign(db, scheduled, CompanyA, scoped: true, validFrom: DateTimeOffset.UtcNow.AddDays(2));
        await db.SaveChangesAsync();
        db.Entry(db.Users.Single(user => user.Id == passiveUser)).Property(nameof(UserAccount.IsActive)).CurrentValue = false;
        db.Entry(revokedAssignment).Property(nameof(RoleAssignment.Status)).CurrentValue = RoleAssignmentStatus.Revoked;
        db.Entry(db.Roles.Single(role => role.Id == passiveRoleAssignment.RoleId))
            .Property(nameof(RoleDefinition.IsActive)).CurrentValue = false;
        await db.SaveChangesAsync();

        var answer = await Port(db, CompanyA, seeded.Caller).ResolveAsync(new UserDirectoryResolveRequestV1(
            CompanyA, Actor(seeded.Caller), [passiveUser, scopeless, revoked, passiveRoleHolder, scheduled]));

        Assert.Equal(UserDirectoryOutcomeV1.Found, answer.Outcome);
        Assert.Equal(5, answer.Items.Count);
        Assert.All(answer.Items, item => Assert.False(item.IsActiveInCompany));

        var search = await Port(db, CompanyA, seeded.Caller).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), null, UserDirectoryLimitsV1.MaximumSearchLimit));
        Assert.DoesNotContain(search.Items, item =>
            new[] { passiveUser, scopeless, revoked, passiveRoleHolder, scheduled }.Contains(item.UserId));
    }

    [Fact]
    public async Task Search_lists_only_active_members_of_the_context_company_in_name_order_and_pages()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        var all = await Port(db, CompanyA, seeded.Caller).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), "  ", UserDirectoryLimitsV1.MaximumSearchLimit));
        Assert.Equal(UserDirectoryOutcomeV1.Found, all.Outcome);
        Assert.False(all.HasMore);
        Assert.Equal(["Alice", "Arayan", "Bora", "Cagri"], all.Items.Select(item => item.DisplayName));
        Assert.All(all.Items, item => Assert.True(item.IsActiveInCompany));

        var narrowed = await Port(db, CompanyA, seeded.Caller).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), " Bor ", 10));
        Assert.Equal([seeded.Bora], narrowed.Items.Select(item => item.UserId));

        var page = await Port(db, CompanyA, seeded.Caller).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), null, 2));
        Assert.True(page.HasMore);
        Assert.Equal(["Alice", "Arayan"], page.Items.Select(item => item.DisplayName));

        var inB = await Port(db, CompanyB, seeded.CallerInB).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyB, Actor(seeded.CallerInB), null, 10));
        Assert.Equal(["Alice", "Bdeki", "Cem"], inB.Items.Select(item => item.DisplayName));
    }

    [Fact]
    public async Task A_request_that_does_not_match_the_server_context_is_denied_before_anything_is_read()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();
        var actor = Actor(seeded.Caller);

        var cases = new (IUserDirectoryReadPortV1 Port, UserDirectoryResolveRequestV1 Request)[]
        {
            // The request names another company than the resolved one.
            (Port(db, CompanyA, seeded.Caller), new(CompanyB, actor, [seeded.Alice])),
            // The request names another actor than the caller.
            (Port(db, CompanyA, seeded.Caller), new(CompanyA, Actor(seeded.Alice), [seeded.Alice])),
            // Unresolved company context.
            (new UserDirectoryReadPortV1(db, new CompanyContext(), new FixedUser(actor, CompanyA)),
                new(CompanyA, actor, [seeded.Alice])),
            // The caller's token does not carry the company.
            (new UserDirectoryReadPortV1(db, Context(CompanyA), new FixedUser(actor, CompanyB)),
                new(CompanyA, actor, [seeded.Alice])),
            // Not authenticated.
            (new UserDirectoryReadPortV1(db, Context(CompanyA), new FixedUser(actor, CompanyA, authenticated: false)),
                new(CompanyA, actor, [seeded.Alice])),
            // Malformed requests: empty company, no ids, an empty id, a duplicate, too many.
            (Port(db, CompanyA, seeded.Caller), new(Guid.Empty, actor, [seeded.Alice])),
            (Port(db, CompanyA, seeded.Caller), new(CompanyA, actor, [])),
            (Port(db, CompanyA, seeded.Caller), new(CompanyA, actor, [Guid.Empty])),
            (Port(db, CompanyA, seeded.Caller), new(CompanyA, actor, [seeded.Alice, seeded.Alice])),
            (Port(db, CompanyA, seeded.Caller), new(CompanyA, actor,
                Enumerable.Range(0, UserDirectoryLimitsV1.MaximumResolveCount + 1).Select(_ => Guid.NewGuid()).ToArray())),
        };

        foreach (var (port, request) in cases)
        {
            var answer = await port.ResolveAsync(request);
            Assert.Equal(UserDirectoryOutcomeV1.Denied, answer.Outcome);
            Assert.Empty(answer.Items);
        }

        Assert.Equal(UserDirectoryOutcomeV1.Denied, (await Port(db, CompanyA, seeded.Caller).ResolveAsync(null!)).Outcome);

        foreach (var request in new UserDirectorySearchRequestV1[]
                 {
                     new(CompanyB, actor, null, 10),
                     new(CompanyA, actor, null, 0),
                     new(CompanyA, actor, null, UserDirectoryLimitsV1.MaximumSearchLimit + 1),
                     new(CompanyA, actor, new string('a', UserDirectoryLimitsV1.MaximumQueryLength + 1), 10),
                 })
        {
            var answer = await Port(db, CompanyA, seeded.Caller).SearchActiveAsync(request);
            Assert.Equal(UserDirectoryOutcomeV1.Denied, answer.Outcome);
            Assert.Empty(answer.Items);
            Assert.False(answer.HasMore);
        }
    }

    [Fact]
    public async Task A_caller_who_is_no_longer_an_active_member_of_the_company_is_denied()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        // The token still carries company A, but the caller's only A assignment ended: the fresh read refuses.
        var resolve = await Port(db, CompanyA, seeded.Retired).ResolveAsync(
            new UserDirectoryResolveRequestV1(CompanyA, Actor(seeded.Retired), [seeded.Alice]));
        var search = await Port(db, CompanyA, seeded.Retired).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Retired), null, 10));

        Assert.Equal((UserDirectoryOutcomeV1.Denied, 0), (resolve.Outcome, resolve.Items.Count));
        Assert.Equal((UserDirectoryOutcomeV1.Denied, 0), (search.Outcome, search.Items.Count));

        // A caller of company B is not a member of A even with a token claim for A.
        var outsider = await Port(db, CompanyA, seeded.OnlyInB).ResolveAsync(
            new UserDirectoryResolveRequestV1(CompanyA, Actor(seeded.OnlyInB), [seeded.Alice]));
        Assert.Equal(UserDirectoryOutcomeV1.Denied, outsider.Outcome);
    }

    [Fact]
    public async Task An_owner_failure_is_unavailable_with_no_item()
    {
        var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();
        var port = Port(db, CompanyA, seeded.Caller);
        await db.DisposeAsync();

        var resolve = await port.ResolveAsync(
            new UserDirectoryResolveRequestV1(CompanyA, Actor(seeded.Caller), [seeded.Alice]));
        var search = await port.SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), null, 10));

        Assert.Equal((UserDirectoryOutcomeV1.Unavailable, 0), (resolve.Outcome, resolve.Items.Count));
        Assert.Equal((UserDirectoryOutcomeV1.Unavailable, 0), (search.Outcome, search.Items.Count));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port(db, CompanyA, seeded.Caller).ResolveAsync(
            new UserDirectoryResolveRequestV1(CompanyA, Actor(seeded.Caller), [seeded.Alice]), cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port(db, CompanyA, seeded.Caller).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), null, 10), cancelled.Token));
    }

    [Fact]
    public void No_directory_record_carries_contact_external_identity_role_or_grant_data()
    {
        // SAT-D27: the search REQUEST may name one permission to filter by (input only); it is the single exception, pinned
        // below, and no answer or entry carries any grant data back.
        Assert.Equal(
            ["ActorId", "CompanyId", "HoldingPermission", "Limit", "Query"],
            typeof(UserDirectorySearchRequestV1).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
        var records = typeof(IUserDirectoryReadPortV1).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(IUserDirectoryReadPortV1).Namespace
                && type.Name.StartsWith("UserDirectory", StringComparison.Ordinal)
                && type != typeof(UserDirectorySearchRequestV1)
                && type is { IsClass: true, IsAbstract: false });
        var properties = records
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.Name)
            .ToArray();
        Assert.NotEmpty(properties);
        Assert.DoesNotContain(properties, name =>
            new[] { "Email", "Subject", "Issuer", "External", "Role", "Permission", "Scope", "Assignment", "Grant" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(
            ["DisplayName", "IsActiveInCompany", "UserId"],
            typeof(UserDirectoryEntryV1).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_holding_permission_lists_only_the_active_members_who_hold_it_in_the_company_now()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        var now = DateTimeOffset.UtcNow;
        const string companyWideName = ErpPermissions.SalesDraftsCreate;
        // Holds it company-wide in A: listed.
        var holder = AddUser(db, "Satisci");
        Grant(db, holder, CompanyA, companyWideName, AccessScopeType.Company, CompanyA);
        // Holds it under a site scope only: a company-wide-only name is not held (the authorization service's rule).
        var siteHolder = AddUser(db, "Saha");
        Grant(db, siteHolder, CompanyA, companyWideName, AccessScopeType.Company, CompanyA, permissionOnSecondRole: true);
        Grant(db, siteHolder, CompanyA, companyWideName, AccessScopeType.Site, Guid.NewGuid());
        // Holds it in B only (and is a plain member of A): not listed in A.
        var holderInB = AddUser(db, "Bdeki Satisci");
        Assign(db, holderInB, CompanyA, scoped: true);
        Grant(db, holderInB, CompanyB, companyWideName, AccessScopeType.Company, CompanyB);
        // Held under an expired, a revoked and a passive-role assignment (each a plain member otherwise): not listed.
        var expired = AddUser(db, "Suresi Gecmis");
        Assign(db, expired, CompanyA, scoped: true);
        Grant(db, expired, CompanyA, companyWideName, AccessScopeType.Company, CompanyA,
            validFrom: now.AddDays(-30), validUntil: now.AddDays(-1));
        var revoked = AddUser(db, "Geri Alinmis");
        Assign(db, revoked, CompanyA, scoped: true);
        var revokedGrant = Grant(db, revoked, CompanyA, companyWideName, AccessScopeType.Company, CompanyA);
        var passiveRole = AddUser(db, "Pasif Rollu");
        Assign(db, passiveRole, CompanyA, scoped: true);
        var passiveRoleGrant = Grant(db, passiveRole, CompanyA, companyWideName, AccessScopeType.Company, CompanyA);
        // A passive user holding it: not a member, not listed.
        var passiveUser = AddUser(db, "Pasif Kisi");
        Grant(db, passiveUser, CompanyA, companyWideName, AccessScopeType.Company, CompanyA);
        await db.SaveChangesAsync();
        db.Entry(revokedGrant).Property(nameof(RoleAssignment.Status)).CurrentValue = RoleAssignmentStatus.Revoked;
        db.Entry(db.Roles.Single(role => role.Id == passiveRoleGrant.RoleId)).Property(nameof(RoleDefinition.IsActive)).CurrentValue = false;
        db.Entry(db.Users.Single(user => user.Id == passiveUser)).Property(nameof(UserAccount.IsActive)).CurrentValue = false;
        await db.SaveChangesAsync();

        var port = Port(db, CompanyA, seeded.Caller);
        var holders = await port.SearchActiveAsync(new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), null, 50, companyWideName));
        Assert.Equal(UserDirectoryOutcomeV1.Found, holders.Outcome);
        Assert.Equal([new UserDirectoryEntryV1(holder, "Satisci", true)], holders.Items);
        Assert.Contains((await port.SearchActiveAsync(new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), "Saha", 50))).Items,
            item => item.UserId == siteHolder);

        // The fragment narrows the holders, it never widens them ("Bdeki Satisci" matches the fragment but holds the name in B).
        var narrowed = await port.SearchActiveAsync(new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), "Sat", 50, companyWideName));
        Assert.Equal([holder], narrowed.Items.Select(item => item.UserId));

        // Without the filter every active member is listed, holders or not (MOD-08 unchanged).
        var everyone = await port.SearchActiveAsync(new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), null, 50));
        Assert.Contains(everyone.Items, item => item.UserId == holderInB);
        Assert.Contains(everyone.Items, item => item.UserId == expired);

        // Company B lists its own holder only.
        var inB = await Port(db, CompanyB, seeded.CallerInB).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyB, Actor(seeded.CallerInB), null, 50, companyWideName));
        Assert.Equal([holderInB], inB.Items.Select(item => item.UserId));
    }

    [Fact]
    public async Task A_name_that_is_not_company_wide_only_is_held_under_any_scope_in_the_company()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        // Every Seed member holds organization.sites.read company-scoped; a site-scoped holder joins them.
        var siteHolder = AddUser(db, "Saha");
        Grant(db, siteHolder, CompanyA, ErpPermissions.OrganizationSitesRead, AccessScopeType.Site, Guid.NewGuid(),
            alsoCompanyScoped: false);
        await db.SaveChangesAsync();

        var answer = await Port(db, CompanyA, seeded.Caller).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), "Saha", 50, ErpPermissions.OrganizationSitesRead));
        Assert.Equal([siteHolder], answer.Items.Select(item => item.UserId));
        var nobody = await Port(db, CompanyA, seeded.Caller).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), null, 50, ErpPermissions.SalesQuotesWrite));
        Assert.Equal((UserDirectoryOutcomeV1.Found, 0), (nobody.Outcome, nobody.Items.Count));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" sales.drafts.create")]
    [InlineData("sales.drafts.create ")]
    [InlineData("SALES.DRAFTS.CREATE")]
    [InlineData("not.a.registered.permission")]
    public async Task An_unknown_or_misspelled_holding_permission_is_refused_never_everyone(string permission)
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        var answer = await Port(db, CompanyA, seeded.Caller).SearchActiveAsync(
            new UserDirectorySearchRequestV1(CompanyA, Actor(seeded.Caller), null, 10, permission));

        Assert.Equal((UserDirectoryOutcomeV1.Denied, 0, false), (answer.Outcome, answer.Items.Count, answer.HasMore));
    }

    /// <summary>A role carrying exactly <paramref name="permission"/>, assigned under one scope (plus the company scope when asked).</summary>
    private static RoleAssignment Grant(
        IdentityDbContext db,
        Guid user,
        Guid company,
        string permission,
        AccessScopeType scopeType,
        Guid scopeId,
        bool permissionOnSecondRole = false,
        bool alsoCompanyScoped = false,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validUntil = null)
    {
        var role = new RoleDefinition(Guid.NewGuid(), $"ROLE_{Guid.NewGuid():N}", "Sentetik", isSystem: false);
        // permissionOnSecondRole: the company-scoped assignment carries a DIFFERENT name, so membership alone is not a hold.
        role.AddPermission(permissionOnSecondRole ? ErpPermissions.OrganizationSitesRead : permission);
        db.Roles.Add(role);
        var assignment = new RoleAssignment(Guid.NewGuid(), user, role.Id, company,
            validFrom ?? DateTimeOffset.UtcNow.AddDays(-1), validUntil, null);
        assignment.AddScope(scopeType, scopeId);
        if (alsoCompanyScoped && scopeType != AccessScopeType.Company)
        {
            assignment.AddScope(AccessScopeType.Company, company);
        }

        db.RoleAssignments.Add(assignment);
        return assignment;
    }

    private static Seeded Seed(IdentityDbContext db)
    {
        var caller = AddUser(db, "Arayan");
        var callerInB = AddUser(db, "Bdeki");
        var alice = AddUser(db, "Alice");
        var bora = AddUser(db, "Bora");
        var cagri = AddUser(db, "Cagri");
        var retired = AddUser(db, "Emekli");
        var cem = AddUser(db, "Cem");
        Assign(db, caller, CompanyA, scoped: true);
        Assign(db, callerInB, CompanyB, scoped: true);
        Assign(db, alice, CompanyA, scoped: true);
        Assign(db, alice, CompanyB, scoped: true);
        Assign(db, bora, CompanyA, scoped: true);
        Assign(db, cagri, CompanyA, scoped: true);
        Assign(db, retired, CompanyA, scoped: true,
            validFrom: DateTimeOffset.UtcNow.AddDays(-30), validUntil: DateTimeOffset.UtcNow.AddDays(-1));
        Assign(db, cem, CompanyB, scoped: true);
        return new Seeded(caller, callerInB, alice, bora, retired, cem);
    }

    private static Guid AddUser(IdentityDbContext db, string displayName)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new UserAccount(id, displayName, $"{displayName.ToLowerInvariant()}@example.invalid"));
        return id;
    }

    private static RoleAssignment Assign(
        IdentityDbContext db,
        Guid user,
        Guid company,
        bool scoped,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validUntil = null)
    {
        var role = new RoleDefinition(Guid.NewGuid(), $"ROLE_{Guid.NewGuid():N}", "Sentetik", isSystem: false);
        role.AddPermission(ErpPermissions.OrganizationSitesRead);
        db.Roles.Add(role);
        var assignment = new RoleAssignment(Guid.NewGuid(), user, role.Id, company,
            validFrom ?? DateTimeOffset.UtcNow.AddDays(-1), validUntil, null);
        if (scoped)
        {
            assignment.AddScope(AccessScopeType.Company, company);
        }

        db.RoleAssignments.Add(assignment);
        return assignment;
    }

    private static string Actor(Guid userId) => userId.ToString("D");

    private static UserDirectoryReadPortV1 Port(IdentityDbContext db, Guid company, Guid caller) =>
        new(db, Context(company), new FixedUser(Actor(caller), company));

    private static CompanyContext Context(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return context;
    }

    private static IdentityDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-user-directory-{Guid.NewGuid():N}").Options);

    private sealed record Seeded(Guid Caller, Guid CallerInB, Guid Alice, Guid Bora, Guid Retired, Guid OnlyInB);

    private sealed class FixedUser(string userId, Guid company, bool authenticated = true) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;

        public string? UserId => userId;

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };

        public bool CanAccessCompany(Guid companyId) => companyId != Guid.Empty && AllowedCompanyIds.Contains(companyId);
    }
}
