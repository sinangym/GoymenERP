using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D22 reverse transfer: the strict request contract, the owner registration and the fenced command and query
/// services. The status, line, overage and already-reversed refusals are the store's and are proved against real SQL.
/// </summary>
public sealed partial class InventoryTransferTests
{
    private static readonly Guid ReverseOriginalId = Guid.Parse("92000000-0000-0000-0000-000000000001");
    private static readonly Guid ReverseTransferId = Guid.Parse("92000000-0000-0000-0000-000000000002");
    private static readonly Guid ReverseOriginalSource = Guid.Parse("32000000-0000-0000-0000-000000000001");
    private static readonly Guid ReverseOriginalCustodian = Guid.Parse("42000000-0000-0000-0000-000000000001");

    [Fact]
    public void Reverse_requests_are_strict_and_the_reason_is_canonical()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var valid = """
            {"reverseTransferId":"92000000-0000-0000-0000-000000000002","plannedAtUtc":"2026-10-01T12:00:00Z","reason":"SENTETIK yanlış depo","lines":[{"lineId":"72000000-0000-0000-0000-000000000001","originalLineId":"71000000-0000-0000-0000-000000000001","expectedLabelVersion":"4","plannedKg":"10"}]}
            """;
        var withoutReason = """
            {"reverseTransferId":"92000000-0000-0000-0000-000000000002","plannedAtUtc":"2026-10-01T12:00:00Z","lines":[]}
            """;
        var lineWithoutOriginal = """
            {"reverseTransferId":"92000000-0000-0000-0000-000000000002","plannedAtUtc":"2026-10-01T12:00:00Z","reason":"SENTETIK","lines":[{"lineId":"72000000-0000-0000-0000-000000000001","expectedLabelVersion":"4","plannedKg":"10"}]}
            """;
        var unmapped = """
            {"reverseTransferId":"92000000-0000-0000-0000-000000000002","plannedAtUtc":"2026-10-01T12:00:00Z","reason":"SENTETIK","lines":[],"destinationLocationId":"32000000-0000-0000-0000-000000000001"}
            """;

        var request = JsonSerializer.Deserialize<ReverseInventoryTransferV1>(valid, web)!;
        Assert.Equal((ReverseTransferId, "SENTETIK yanlış depo"), (request.ReverseTransferId, request.Reason));
        var line = Assert.Single(request.Lines);
        Assert.Equal(Guid.Parse(ReceiptLineId), line.OriginalLineId);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReverseInventoryTransferV1>(withoutReason, web));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ReverseInventoryTransferV1>(lineWithoutOriginal, web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReverseInventoryTransferV1>(unmapped, web));
    }

    [Fact]
    public void A_reverse_reason_is_trimmed_and_an_empty_long_or_multi_line_reason_is_not_canonical()
    {
        Assert.Equal("SENTETIK yanlış depo", InventoryTransferReverseApiV1.NormalizeReason("  SENTETIK yanlış depo "));
        Assert.Null(InventoryTransferReverseApiV1.NormalizeReason(" "));
        Assert.Null(InventoryTransferReverseApiV1.NormalizeReason(null));
        Assert.Null(InventoryTransferReverseApiV1.NormalizeReason("satır\nkırılımı"));
        Assert.Null(InventoryTransferReverseApiV1.NormalizeReason(
            new string('a', InventoryTransferReverseApiV1.MaximumReasonLength + 1)));
        Assert.NotNull(InventoryTransferReverseApiV1.NormalizeReason(
            new string('a', InventoryTransferReverseApiV1.MaximumReasonLength)));
    }

    [Fact]
    public void Reverse_refusal_codes_are_distinct_from_the_transfer_codes()
    {
        var codes = new[]
        {
            InventoryTransferReverseCodesV1.ReasonRequired,
            InventoryTransferReverseCodesV1.OriginalNotAccepted,
            InventoryTransferReverseCodesV1.LineNotAccepted,
            InventoryTransferReverseCodesV1.LineAlreadyReversed,
            InventoryTransferReverseCodesV1.OveragePending,
        };

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.StartsWith("inventory_transfer_reverse_", code, StringComparison.Ordinal));
    }

    [Fact]
    public void Owner_registration_adds_the_reverse_command_and_query_but_no_destination_adapter_or_grant()
    {
        var services = new ServiceCollection().AddInventoryTransfers();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferReverseWriterV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferReverseReaderV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferReverseCommandV1)
            && descriptor.ImplementationType == typeof(InventoryTransferReverseCommandService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferReverseQueryV1)
            && descriptor.ImplementationType == typeof(InventoryTransferReverseQueryService));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferDestinationPortV1));
    }

    [Fact]
    public async Task A_reverse_transfer_is_measured_and_written_only_under_the_transfer_write_permission()
    {
        var world = new ReverseWorld();

        var result = await world.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor"));

        Assert.Same(world.Writer.Result, result);
        Assert.NotEmpty(world.Authority.Measured);
        Assert.All(world.Authority.Measured,
            measured => Assert.Equal(ErpPermissions.InventoryStockTransferWrite, measured));
        Assert.Equal([ReverseOriginalId], world.Writer.ReplayCalls);
        var (originalId, metadata) = Assert.Single(world.Writer.Calls);
        Assert.Equal((ReverseOriginalId, "actor", ErpPermissions.InventoryStockTransferWrite, "key-1", "corr-1"),
            (originalId, metadata.ActorId, metadata.PermissionCode, metadata.IdempotencyKey, metadata.CorrelationId));
        Assert.Equal(1, world.Reader.Calls);
    }

    [Fact]
    public async Task The_reverse_destination_is_the_original_source_location_and_custodian()
    {
        var world = new ReverseWorld();

        await world.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor"));

        var checkedDestination = Assert.Single(world.Destination.Checks);
        Assert.Equal((Company, ReverseOriginalSource, "Company", ReverseOriginalCustodian), checkedDestination);
    }

    [Fact]
    public async Task A_stored_reverse_answer_replays_before_the_original_or_destination_read()
    {
        var replay = new InventoryTransferReverseResultV1(TransferView(Company), ReverseLink(), true, "original");
        var world = new ReverseWorld();
        world.Writer.ReplayResult = replay;
        world.Destination.Status = InventoryTransferDestinationStatusV1.Inactive;

        var answer = await world.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor"));

        Assert.Same(replay, answer);
        Assert.Empty(world.Writer.Calls);
        Assert.Equal((0, 0), (world.Reader.Calls, world.Destination.Checks.Count));
    }

    [Fact]
    public async Task An_unregistered_reverse_permission_is_a_setup_gap_and_never_measured_or_written()
    {
        var world = new ReverseWorld { Known = false };

        await Refused(world.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 503,
            InventoryTransferCodesV1.PermissionUnregistered);

        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.ReplayCalls);
        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task A_missing_grant_or_another_transport_actor_is_forbidden_before_the_reverse_writer()
    {
        var revoked = new ReverseWorld();
        revoked.Authority.Granted = false;
        var impersonated = new ReverseWorld();

        await Refused(revoked.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 403,
            InventoryTransferCodesV1.Forbidden);
        await Refused(impersonated.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(),
            Transport("someone-else")), 403, InventoryTransferCodesV1.Forbidden);

        Assert.Empty(revoked.Writer.ReplayCalls);
        Assert.Empty(impersonated.Writer.ReplayCalls);
        Assert.Empty(revoked.Writer.Calls);
        Assert.Empty(impersonated.Writer.Calls);
    }

    [Fact]
    public async Task An_unauthenticated_or_unresolved_company_reverse_is_refused_before_any_measurement()
    {
        var anonymous = new ReverseWorld();
        anonymous.User.IsAuthenticated = false;
        var unresolved = new ReverseWorld();
        unresolved.Context.CompanyId = Guid.Empty;

        await Refused(anonymous.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 401,
            InventoryTransferCodesV1.Unauthenticated);
        await Refused(unresolved.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 403,
            InventoryTransferCodesV1.Forbidden);

        Assert.Empty(anonymous.Authority.Measured);
        Assert.Empty(unresolved.Authority.Measured);
        Assert.Empty(anonymous.Writer.ReplayCalls);
        Assert.Empty(unresolved.Writer.ReplayCalls);
    }

    [Fact]
    public async Task Revocation_at_the_reverse_writer_fence_refuses_without_planning_the_reverse_transfer()
    {
        var world = new ReverseWorld();
        world.Writer.BeforeFence = () => world.Authority.Granted = false;

        await Refused(world.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 403,
            InventoryTransferCodesV1.Forbidden);

        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task Revocation_during_the_destination_check_refuses_before_the_reverse_writer()
    {
        var world = new ReverseWorld();
        world.Destination.During = () => world.Authority.Granted = false;

        await Refused(world.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 403,
            InventoryTransferCodesV1.Forbidden);

        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData(InventoryTransferDestinationStatusV1.NotFound, InventoryTransferCodesV1.DestinationNotFound)]
    [InlineData(InventoryTransferDestinationStatusV1.Inactive, InventoryTransferCodesV1.DestinationInactive)]
    [InlineData(InventoryTransferDestinationStatusV1.ContextMismatch,
        InventoryTransferCodesV1.DestinationContextMismatch)]
    public async Task A_reverse_rechecks_the_original_source_and_refuses_an_unusable_one_before_writing(
        InventoryTransferDestinationStatusV1 status,
        string code)
    {
        var world = new ReverseWorld();
        world.Destination.Status = status;

        await Refused(world.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 422, code);

        Assert.Equal((1, 1), (world.Reader.Calls, world.Destination.Checks.Count));
        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task An_unavailable_return_destination_owner_is_a_service_gap_and_never_written()
    {
        var failing = new ReverseWorld();
        failing.Destination.Failure = new InvalidOperationException("SENTETIK owner kesintisi");

        await Refused(failing.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 503,
            InventoryTransferCodesV1.Unavailable);

        Assert.Empty(failing.Writer.Calls);
    }

    [Fact]
    public async Task A_reverse_of_a_missing_or_foreign_original_is_refused_before_the_destination_or_writer()
    {
        var missing = new ReverseWorld();
        missing.Reader.Result = null;
        var foreign = new ReverseWorld();
        foreign.Reader.Result = OriginalView() with { CompanyId = Guid.NewGuid() };

        await Refused(missing.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 404,
            InventoryTransferCodesV1.TransferNotFound);
        await Refused(foreign.Service().ReverseAsync(ReverseOriginalId, ReverseRequest(), Transport("actor")), 503,
            InventoryTransferCodesV1.Unavailable);

        Assert.Equal((0, 0), (missing.Destination.Checks.Count, foreign.Destination.Checks.Count));
        Assert.Empty(missing.Writer.Calls);
        Assert.Empty(foreign.Writer.Calls);
    }

    [Fact]
    public async Task Reversal_facts_are_read_under_the_balances_read_permission_and_refuse_a_foreign_answer()
    {
        var authority = new TestAuthority();
        var stored = new InventoryTransferReversalFactsViewV1(ReverseOriginalId, Company, null,
            [new InventoryTransferReversedByViewV1(ReverseLink(), "Planned")]);
        var reader = new TestReverseReader { Result = stored };
        var query = new InventoryTransferReverseQueryService(new TestContext(), new TestUser(), authority, reader);

        var facts = await query.GetReversalFactsAsync(ReverseOriginalId);

        Assert.Same(stored, facts);
        Assert.Equal(2, authority.Measured.Count);
        Assert.All(authority.Measured, measured => Assert.Equal(ErpPermissions.InventoryBalancesRead, measured));

        reader.Result = stored with { CompanyId = Guid.NewGuid() };
        await Refused(query.GetReversalFactsAsync(ReverseOriginalId), 503, InventoryTransferCodesV1.Unavailable);
        reader.Result = null;
        await Refused(query.GetReversalFactsAsync(Guid.NewGuid()), 404, InventoryTransferCodesV1.TransferNotFound);
        reader.Failure = new InvalidOperationException("SENTETIK okuma kesintisi");
        await Refused(query.GetReversalFactsAsync(Guid.NewGuid()), 503, InventoryTransferCodesV1.Unavailable);
        reader.Failure = null;
        authority.Granted = false;
        await Refused(query.GetReversalFactsAsync(Guid.NewGuid()), 403, InventoryTransferCodesV1.Forbidden);
        Assert.Equal(4, reader.Calls);
    }

    private static ReverseInventoryTransferV1 ReverseRequest() =>
        new(ReverseTransferId, At.AddDays(1), "SENTETIK yanlış depo",
            [new ReverseInventoryTransferLineV1(Guid.NewGuid(), Guid.Parse(ReceiptLineId), "4", "10")]);

    private static InventoryTransferViewV1 OriginalView() => new(ReverseOriginalId, Company, "3", "Accepted",
        ReverseOriginalSource, "Company", ReverseOriginalCustodian, Destination, "Counterparty", TargetCustodian, At,
        At.AddHours(1), []);

    private static InventoryTransferReversalLinkViewV1 ReverseLink() =>
        new(ReverseTransferId, ReverseOriginalId, "SENTETIK yanlış depo", At.AddDays(1), "actor",
            [new InventoryTransferReversalLineViewV1(Guid.NewGuid(), Guid.Parse(ReceiptLineId))]);

    private sealed class ReverseWorld
    {
        public TestContext Context { get; } = new();
        public TestUser User { get; } = new();
        public TestAuthority Authority { get; } = new();
        public TestReverseDestination Destination { get; } = new();
        public TestReader Reader { get; } = new() { Result = OriginalView() };
        public TestReverseWriter Writer { get; } = new();
        public bool Known { get; init; } = true;

        public InventoryTransferReverseCommandService Service() =>
            new(Context, User, Authority, Destination, Reader, Writer, _ => Known);
    }

    private sealed class TestReverseDestination : IInventoryTransferDestinationPortV1
    {
        public InventoryTransferDestinationStatusV1 Status { get; set; } = InventoryTransferDestinationStatusV1.Active;
        public List<(Guid CompanyId, Guid LocationId, string CustodianKind, Guid CustodianId)> Checks { get; } = [];
        public Action? During { get; set; }
        public Exception? Failure { get; set; }

        public Task<InventoryTransferDestinationStatusV1> CheckAsync(
            Guid companyId,
            Guid locationId,
            string custodianKind,
            Guid custodianId,
            CancellationToken cancellationToken = default)
        {
            Checks.Add((companyId, locationId, custodianKind, custodianId));
            During?.Invoke();
            return Failure is null ? Task.FromResult(Status) : throw Failure;
        }
    }

    private sealed class TestReverseReader : IInventoryTransferReverseReaderV1
    {
        public InventoryTransferReversalFactsViewV1? Result { get; set; }
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<InventoryTransferReversalFactsViewV1?> ReadReversalFactsAsync(
            Guid transferId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Failure is null ? Task.FromResult(Result) : throw Failure;
        }
    }

    private sealed class TestReverseWriter : IInventoryTransferReverseWriterV1
    {
        public InventoryTransferReverseResultV1 Result { get; } =
            new(TransferView(Company), ReverseLink(), false, "corr-1");

        public List<(Guid OriginalTransferId, InventoryCommandMetadata Metadata)> Calls { get; } = [];
        public List<Guid> ReplayCalls { get; } = [];
        public Action? BeforeFence { get; set; }
        public InventoryTransferReverseResultV1? ReplayResult { get; set; }

        public async Task<InventoryTransferReverseResultV1?> ReplayReverseAsync(
            Guid originalTransferId,
            ReverseInventoryTransferV1 request,
            InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence,
            CancellationToken cancellationToken = default)
        {
            ReplayCalls.Add(originalTransferId);
            if (ReplayResult is null) return null;
            await freshAuthorityFence(cancellationToken);
            return ReplayResult;
        }

        public async Task<InventoryTransferReverseResultV1> ReverseAsync(
            Guid originalTransferId,
            ReverseInventoryTransferV1 request,
            InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence,
            CancellationToken cancellationToken = default)
        {
            BeforeFence?.Invoke();
            await freshAuthorityFence(cancellationToken);
            Calls.Add((originalTransferId, metadata));
            return Result;
        }
    }
}
