using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryMovementTimelineTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Timeline_returns_a_bounded_newest_first_page_and_protected_continuation()
    {
        var world = new World();
        var result = await world.Service().SearchAsync(new(2));

        Assert.Equal(world.Reader.Window.Items.Select(item => item.MovementId),
            result.Items.Select(item => item.MovementId));
        Assert.Equal("next", result.NextCursor);
        Assert.Equal(1, world.Reader.Reads);
        Assert.Equal([
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
        ], world.Authority.Calls);
        Assert.Equal((Company, world.User.UserId, 2, world.Reader.Window.LastPosition), world.Cursors.Protected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Invalid_limits_are_refused_before_the_owner_read(int limit)
    {
        var world = new World();
        await Refused(world.Service().SearchAsync(new(limit)), 400,
            InventoryMovementTimelineCodesV1.InvalidQuery);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Fact]
    public async Task Cursor_is_bound_to_the_company_actor_and_limit()
    {
        var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-07T12:00:00Z"));
        using var codec = new InventoryMovementTimelineCursorCodec(
            new InventoryMovementTimelineCursorRuntimeOptions(key), clock);
        var position = new InventoryMovementTimelinePosition(
            DateTimeOffset.Parse("2026-10-07T11:59:00Z"), Guid.NewGuid());
        var cursor = codec.Protect(Company, "reader", 25, position);

        Assert.Equal(position, codec.Unprotect(Company, "reader", 25, cursor));
        await Refused(Task.Run(() => codec.Unprotect(Company, "other", 25, cursor)), 400,
            InventoryMovementTimelineCodesV1.InvalidQuery);
        await Refused(Task.Run(() => codec.Unprotect(Company, "reader", 50, cursor)), 400,
            InventoryMovementTimelineCodesV1.InvalidQuery);
        var tampered = cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');
        await Refused(Task.Run(() => codec.Unprotect(Company, "reader", 25, tampered)), 400,
            InventoryMovementTimelineCodesV1.InvalidQuery);
        clock.Advance(TimeSpan.FromMinutes(30));
        await Refused(Task.Run(() => codec.Unprotect(Company, "reader", 25, cursor)), 400,
            InventoryMovementTimelineCodesV1.InvalidQuery);
    }

    [Fact]
    public async Task Missing_cursor_provider_or_blank_cursor_fails_closed()
    {
        var world = new World();
        await Refused(new InventoryMovementTimelineService(
            world.Context, world.User, world.Authority, world.Reader).SearchAsync(new(25, "cursor")),
            503, InventoryMovementTimelineCodesV1.Unavailable);
        await Refused(world.Service().SearchAsync(new(25, " ")), 400,
            InventoryMovementTimelineCodesV1.InvalidQuery);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Fact]
    public async Task Both_existing_permissions_are_company_wide_and_fresh_after_the_read()
    {
        var denied = new World();
        denied.Authority.Denied = ErpPermissions.InventoryBalancesRead;
        await Refused(denied.Service().SearchAsync(new()), 403, InventoryMovementTimelineCodesV1.Forbidden);
        Assert.Equal(0, denied.Reader.Reads);

        var revoked = new World();
        revoked.Reader.During = () => revoked.Authority.Denied = ErpPermissions.InventoryLabelsRead;
        await Refused(revoked.Service().SearchAsync(new()), 403, InventoryMovementTimelineCodesV1.Forbidden);
        Assert.Equal(1, revoked.Reader.Reads);

        var scoped = new World();
        scoped.Authority.CompanyWide = false;
        await Refused(scoped.Service().SearchAsync(new()), 403, InventoryMovementTimelineCodesV1.Forbidden);
    }

    [Fact]
    public async Task Actor_or_company_scope_change_during_the_read_is_refused()
    {
        var actor = new World();
        actor.Reader.During = () => actor.User.UserId = "changed";
        await Refused(actor.Service().SearchAsync(new()), 403, InventoryMovementTimelineCodesV1.Forbidden);

        var company = new World();
        company.Reader.During = () => company.Context.Set(Guid.NewGuid());
        await Refused(company.Service().SearchAsync(new()), 403, InventoryMovementTimelineCodesV1.Forbidden);
    }

    [Fact]
    public async Task Missing_or_failing_provider_has_no_positive_fallback()
    {
        var world = new World();
        await Refused(new InventoryMovementTimelineService(
            world.Context, world.User, world.Authority, null, world.Cursors).SearchAsync(new()),
            503, InventoryMovementTimelineCodesV1.Unavailable);

        world.Reader.Error = new InvalidOperationException("provider failed");
        await Refused(world.Service().SearchAsync(new()), 503, InventoryMovementTimelineCodesV1.Unavailable);
    }

    [Fact]
    public async Task Malformed_order_lines_or_continuation_are_refused()
    {
        var order = new World();
        order.Reader.Window = order.Reader.Window with
        {
            Items = [order.Reader.Window.Items[1], order.Reader.Window.Items[0]],
        };
        await Refused(order.Service().SearchAsync(new(2)), 503, InventoryMovementTimelineCodesV1.Unavailable);

        var line = new World();
        line.Reader.Window = line.Reader.Window with
        {
            Items = [line.Reader.Window.Items[0] with
            {
                Lines = [line.Reader.Window.Items[0].Lines[0] with { QuantityKg = "01" }],
            }, line.Reader.Window.Items[1]],
        };
        await Refused(line.Service().SearchAsync(new(2)), 503, InventoryMovementTimelineCodesV1.Unavailable);

        var continuation = new World();
        continuation.Reader.Window = continuation.Reader.Window with { LastPosition = null };
        await Refused(continuation.Service().SearchAsync(new(2)), 503,
            InventoryMovementTimelineCodesV1.Unavailable);

        var count = new World();
        var counted = count.Reader.Window.Items[0];
        count.Reader.Window = count.Reader.Window with
        {
            Items = [counted with
            {
                Kind = "CountAdjustment",
                Lines = [counted.Lines[0], counted.Lines[0] with { Sequence = 2 }],
            }, count.Reader.Window.Items[1]],
        };
        await Refused(count.Service().SearchAsync(new(2)), 503,
            InventoryMovementTimelineCodesV1.Unavailable);
    }

    [Fact]
    public async Task Equal_timestamp_order_uses_sql_server_uniqueidentifier_semantics()
    {
        var at = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        var sqlFirst = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var sqlSecond = Guid.Parse("01000000-0000-0000-0000-000000000000");
        Assert.True(sqlFirst.CompareTo(sqlSecond) < 0);

        var accepted = new World();
        accepted.Reader.Window = new InventoryMovementTimelineWindow(
            [Movement(at, sqlFirst), Movement(at, sqlSecond)], false, null);
        Assert.Equal(new[] { sqlFirst, sqlSecond },
            (await accepted.Service().SearchAsync(new(2))).Items.Select(item => item.MovementId).ToArray());

        var reversed = new World();
        reversed.Reader.Window = new InventoryMovementTimelineWindow(
            [Movement(at, sqlSecond), Movement(at, sqlFirst)], false, null);
        await Refused(reversed.Service().SearchAsync(new(2)), 503,
            InventoryMovementTimelineCodesV1.Unavailable);
    }

    [Fact]
    public async Task Service_requests_only_the_resolved_company_without_inferring_owner_identity()
    {
        var world = new World();
        var item = world.Reader.Window.Items[0];
        world.Reader.Window = world.Reader.Window with
        {
            Items = [item with
            {
                Lines = [item.Lines[0] with { OwnerId = Guid.NewGuid() }],
            }, world.Reader.Window.Items[1]],
        };

        // A non-empty owner is structurally valid; the SQL reader is responsible for tenant filtering.
        // Prove the service never asks the provider for another company instead of guessing from the payload.
        var result = await world.Service().SearchAsync(new(2));
        Assert.Equal(Company, world.Reader.CompanyAsked);
        Assert.Equal(2, result.Items.Count);
    }

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryMovementTimelineException>(() => task);
        Assert.Equal((status, code), (error.StatusCode, error.Code));
    }

    private static InventoryMovementV1 Movement(DateTimeOffset occurredAt, Guid? movementId = null)
    {
        var labelId = Guid.NewGuid();
        return new InventoryMovementV1(
            movementId ?? Guid.NewGuid(),
            "Receipt",
            occurredAt,
            "inventory.opening-count",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Line",
            [new InventoryMovementLineV1(
                1,
                "In",
                labelId,
                $"LBL-{labelId:N}",
                "Company",
                Company,
                "Company",
                Company,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Free",
                "12.5")]);
    }

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Reader Reader { get; } = new();
        public Cursor Cursors { get; } = new();
        public InventoryMovementTimelineService Service() =>
            new(Context, User, Authority, Reader, Cursors);
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = "movement-reader";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string? Denied { get; set; }
        public bool CompanyWide { get; set; } = true;
        public List<string> Calls { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(permission);
            return Task.FromResult(new PermissionScope(
                permission != Denied,
                CompanyWide,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
        }
    }

    private sealed class Reader : IInventoryMovementTimelineReader
    {
        private readonly InventoryMovementV1 newer = Movement(
            DateTimeOffset.Parse("2026-10-07T12:01:00Z"));
        private readonly InventoryMovementV1 older = Movement(
            DateTimeOffset.Parse("2026-10-07T12:00:00Z"));
        public InventoryMovementTimelineWindow Window { get; set; }
        public Action? During { get; set; }
        public Exception? Error { get; set; }
        public int Reads { get; private set; }
        public Guid CompanyAsked { get; private set; }

        public Reader()
        {
            Window = new([newer, older], true,
                new InventoryMovementTimelinePosition(older.OccurredAtUtc, older.MovementId));
        }

        public async Task<InventoryMovementTimelineWindow> SearchAsync(
            Guid companyId,
            InventoryMovementTimelineReadQuery query,
            CancellationToken cancellationToken)
        {
            Reads++;
            CompanyAsked = companyId;
            await Task.Yield();
            During?.Invoke();
            if (Error is not null) throw Error;
            return Window;
        }
    }

    private sealed class Cursor : IInventoryMovementTimelineCursorCodec
    {
        public bool IsAvailable => true;
        public (Guid CompanyId, string? ActorId, int Limit, InventoryMovementTimelinePosition? Position) Protected { get; private set; }
        public string Protect(Guid companyId, string actorId, int limit, InventoryMovementTimelinePosition position)
        {
            Protected = (companyId, actorId, limit, position);
            return "next";
        }
        public InventoryMovementTimelinePosition Unprotect(
            Guid companyId, string actorId, int limit, string cursor) =>
            new(DateTimeOffset.Parse("2026-10-07T12:02:00Z"), Guid.NewGuid());
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
