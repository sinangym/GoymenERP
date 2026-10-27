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
        var records = typeof(IUserDirectoryReadPortV1).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(IUserDirectoryReadPortV1).Namespace
                && type.Name.StartsWith("UserDirectory", StringComparison.Ordinal)
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
