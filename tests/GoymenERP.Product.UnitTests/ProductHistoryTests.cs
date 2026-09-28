using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductHistoryTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid ProductId = Guid.Parse("59c57d4c-9890-4dad-823c-b3b4030b42d1");
    private static readonly Guid GroupId = Guid.Parse("f4fc7672-0b46-41ba-a017-393401ba61dd");
    private static readonly DateTimeOffset T0 = new(2026, 9, 13, 0, 3, 0, TimeSpan.Zero);

    [Fact]
    public void Create_and_edit_project_newest_first_with_exact_changed_keys_and_no_request_metadata()
    {
        var v1 = Card(1);
        var v2 = Card(2, name: "Galvaniz bant — düzeltilen", familyId: Guid.NewGuid(),
            sellable: new(true, "manual"), units: new(Sales: Guid.NewGuid()));
        var history = ProductHistoryProjector.Project(ProductId,
        [
            Edit(v1, v2, T0.AddMinutes(2), "Ad ve aile düzeltmesi"),
            Create(v1, T0),
        ]);

        Assert.False(history.Truncated);
        Assert.Equal(2, history.Entries.Count);
        var edit = history.Entries[0];
        Assert.Equal((ProductHistoryApiV1.DraftBasicEdit, "1", "2", "Ad ve aile düzeltmesi"),
            (edit.Operation, edit.VersionBefore, edit.VersionAfter, edit.Reason));
        Assert.Equal(["name", "familyId", "roles.sellable", "references.units.sales"], edit.ChangedFields);
        var create = history.Entries[1];
        Assert.Equal((ProductHistoryApiV1.DraftCreate, (string?)null, "1"), (create.Operation, create.VersionBefore, create.VersionAfter));
        Assert.Empty(create.ChangedFields);

        // The wire shape carries no actor, address, agent, correlation or key material.
        var names = JsonDocument.Parse(JsonSerializer.Serialize(edit, Web)).RootElement.EnumerateObject().Select(p => p.Name);
        Assert.Equal(["eventId", "operation", "occurredAtUtc", "versionBefore", "versionAfter", "reason", "changedFields"], names);
    }

    [Fact]
    public void Edit_that_changes_no_tracked_field_has_an_empty_change_list_not_a_guess()
    {
        var history = ProductHistoryProjector.Project(ProductId, [Edit(Card(1), Card(2), T0.AddMinutes(1), "Gerekçe"), Create(Card(1), T0)]);
        Assert.Empty(history.Entries[0].ChangedFields);
    }

    [Fact]
    public void Activation_is_a_versioned_lifecycle_change_in_the_same_canonical_chain()
    {
        var draft = Card(1);
        var active = Card(2, lifecycle: "Active");
        var history = ProductHistoryProjector.Project(ProductId,
            [Activate(draft, active, T0.AddMinutes(1)), Create(draft, T0)]);

        var activation = history.Entries[0];
        Assert.Equal((ProductHistoryApiV1.Activate, "1", "2", "Ürün kartı onayı"),
            (activation.Operation, activation.VersionBefore, activation.VersionAfter, activation.Reason));
        Assert.Equal(["lifecycle"], activation.ChangedFields);
    }

    [Fact]
    public void Deactivation_and_reactivation_carry_the_actors_reason_in_the_same_canonical_chain()
    {
        var draft = Card(1);
        var active = Card(2, lifecycle: "Active");
        var inactive = Card(3, lifecycle: "Inactive");
        var again = Card(4, lifecycle: "Active");
        var history = ProductHistoryProjector.Project(ProductId,
        [
            Lifecycle(ProductHistoryApiV1.Reactivate, inactive, again, T0.AddMinutes(3), "Tedarik yeniden başladı"),
            Lifecycle(ProductHistoryApiV1.Deactivate, active, inactive, T0.AddMinutes(2), "Üretimden kalktı"),
            Activate(draft, active, T0.AddMinutes(1)),
            Create(draft, T0),
        ]);

        Assert.Equal(4, history.Entries.Count);
        var reactivation = history.Entries[0];
        Assert.Equal((ProductHistoryApiV1.Reactivate, "3", "4", "Tedarik yeniden başladı"),
            (reactivation.Operation, reactivation.VersionBefore, reactivation.VersionAfter, reactivation.Reason));
        var deactivation = history.Entries[1];
        Assert.Equal((ProductHistoryApiV1.Deactivate, "2", "3", "Üretimden kalktı", T0.AddMinutes(2)),
            (deactivation.Operation, deactivation.VersionBefore, deactivation.VersionAfter, deactivation.Reason,
                deactivation.OccurredAtUtc));
        Assert.Equal(["lifecycle"], deactivation.ChangedFields);
        Assert.Equal(["lifecycle"], reactivation.ChangedFields);
    }

    [Theory]
    [InlineData("Deactivate", "Draft", "Inactive")]
    [InlineData("Deactivate", "Active", "Active")]
    [InlineData("Reactivate", "Active", "Active")]
    [InlineData("Reactivate", "Inactive", "Draft")]
    public void A_lifecycle_event_whose_snapshots_contradict_its_operation_is_an_integrity_fault(string operation,
        string from, string to)
    {
        var draft = Card(1);
        var before = Card(2, lifecycle: from);
        var after = Card(3, lifecycle: to);
        Assert.Throws<InvalidOperationException>(() => ProductHistoryProjector.Project(ProductId,
        [
            Lifecycle(operation, before, after, T0.AddMinutes(2), "Gerekçe"),
            Edit(draft, before, T0.AddMinutes(1), "Önceki"),
            Create(draft, T0),
        ]));
    }

    [Fact]
    public void Missing_unit_container_and_explicit_null_unit_are_the_same_value()
    {
        var before = Card(1, units: null);
        var after = Card(2, units: new(Stock: null));
        var history = ProductHistoryProjector.Project(ProductId,
            [Edit(before, after, T0.AddMinutes(1), "Birim bağı"), Create(before, T0)]);
        Assert.Empty(history.Entries[0].ChangedFields);
    }

    [Fact]
    public void Role_provenance_change_is_a_change_even_when_the_value_is_the_same()
    {
        var unchangedCreate = Card(1, sellable: new(null, "not-evaluated"));
        var history = ProductHistoryProjector.Project(ProductId,
        [
            Edit(unchangedCreate, Card(2, sellable: new(null, "not-evaluated")) with { }, T0.AddMinutes(1), "x"),
            Create(unchangedCreate, T0),
        ]);
        Assert.Empty(history.Entries[0].ChangedFields);
        var changedCreate = Card(1, sellable: new(false, "manual"));
        var changed = ProductHistoryProjector.Project(ProductId,
        [
            Edit(changedCreate, Card(2, sellable: new(false, "derived")), T0.AddMinutes(1), "x"),
            Create(changedCreate, T0),
        ]);
        Assert.Equal(["roles.sellable"], changed.Entries[0].ChangedFields);
    }

    [Fact]
    public void Versions_above_javascript_precision_stay_lossless_strings()
    {
        // A history this far from v1 can only be a cut list: an uncut one must reach the
        // creation event, and 9 quadrillion edits do not fit in a page.
        var history = ProductHistoryProjector.Project(ProductId,
            Descending(9007199254740993, ProductHistoryApiV1.PageSize + 1));
        Assert.True(history.Truncated);
        Assert.Equal(("9007199254740992", "9007199254740993"), (history.Entries[0].VersionBefore, history.Entries[0].VersionAfter));
    }

    [Fact]
    public void More_than_one_page_is_cut_to_the_page_size_and_marked_truncated()
    {
        var records = Enumerable.Range(0, ProductHistoryApiV1.PageSize + 1)
            .Select(i => Edit(Card(100 - i), Card(101 - i), T0.AddMinutes(-i), $"Değişiklik {i}"))
            .ToArray();
        var history = ProductHistoryProjector.Project(ProductId, records);
        Assert.True(history.Truncated);
        Assert.Equal(ProductHistoryApiV1.PageSize, history.Entries.Count);
        Assert.Equal("101", history.Entries[0].VersionAfter);
    }

    [Fact]
    public void A_gap_across_the_published_page_and_discarded_sentinel_fails_closed()
    {
        var records = Descending(101, ProductHistoryApiV1.PageSize + 1);
        // The published 50th row is 51 -> 52. This sentinel is internally valid but
        // ends at 50, so version 51 is missing exactly where the response is cut.
        records[^1] = Edit(Card(49), Card(50), T0.AddMinutes(-ProductHistoryApiV1.PageSize), "Sınır boşluğu");

        Assert.Throws<InvalidOperationException>(() => ProductHistoryProjector.Project(ProductId, records));
    }

    [Fact]
    public void Exactly_one_page_is_the_whole_history_and_one_more_is_cut()
    {
        var full = ProductHistoryProjector.Project(ProductId, Complete(ProductHistoryApiV1.PageSize - 1));
        Assert.False(full.Truncated);
        Assert.Equal(ProductHistoryApiV1.PageSize, full.Entries.Count);

        var cut = ProductHistoryProjector.Project(ProductId, Complete(ProductHistoryApiV1.PageSize));
        Assert.True(cut.Truncated);
        Assert.Equal(ProductHistoryApiV1.PageSize, cut.Entries.Count);
    }

    [Theory]
    [InlineData("other-product")]
    [InlineData("unknown-operation")]
    [InlineData("create-with-before-version")]
    [InlineData("create-not-version-1")]
    [InlineData("create-with-before-snapshot")]
    [InlineData("edit-version-gap")]
    [InlineData("edit-without-before-version")]
    [InlineData("after-snapshot-version")]
    [InlineData("before-snapshot-product")]
    [InlineData("malformed-json")]
    [InlineData("missing-references")]
    [InlineData("blank-reason")]
    [InlineData("non-utc")]
    [InlineData("out-of-order")]
    [InlineData("duplicate-event")]
    [InlineData("empty-event-id")]
    [InlineData("missing-create-untruncated")]
    [InlineData("version-gap-between-entries")]
    [InlineData("equal-time-reverse-order")]
    [InlineData("create-not-oldest")]
    public void Integrity_faults_fail_closed_instead_of_being_skipped(string fault)
    {
        var create = Create(Card(1), T0);
        var edit = Edit(Card(1), Card(2), T0.AddMinutes(1), "Gerekçe");
        IReadOnlyList<ProductAuditRecordV1> records = fault switch
        {
            "other-product" => [edit with { ProductId = Guid.NewGuid() }, create],
            "unknown-operation" => [edit with { Operation = "Retire" }, create],
            "create-with-before-version" => [create with { VersionBefore = 0 }],
            "create-not-version-1" => [Create(Card(2), T0) with { VersionAfter = 2 }],
            "create-with-before-snapshot" => [create with { BeforeJson = Json(Card(1)) }],
            "edit-version-gap" => [Edit(Card(1), Card(3), T0, "x") with { VersionAfter = 3 }, create],
            "edit-without-before-version" => [edit with { VersionBefore = null }, create],
            "after-snapshot-version" => [edit with { AfterJson = Json(Card(5)) }, create],
            "before-snapshot-product" =>
                [edit with { BeforeJson = Json(Card(1) with { Product = Card(1).Product with { Id = Guid.NewGuid() } }) }, create],
            "malformed-json" => [edit with { AfterJson = "{" }, create],
            "missing-references" =>
                [edit with { AfterJson = """{"product":{"id":"59c57d4c-9890-4dad-823c-b3b4030b42d1","version":"2"}}""" }, create],
            "blank-reason" => [edit with { Reason = " " }, create],
            "non-utc" =>
            [
                edit with { OccurredAtUtc = new DateTimeOffset(2026, 9, 13, 3, 0, 0, TimeSpan.FromHours(3)) },
                create with { OccurredAtUtc = T0.AddHours(-1) },
            ],
            "out-of-order" => [create, edit],
            "duplicate-event" => [edit, create with { EventId = edit.EventId }],
            // The creation event is simply absent and the server did not cut the list.
            "missing-create-untruncated" => [edit],
            // 2 -> 3 never happened as far as this list is concerned.
            "version-gap-between-entries" =>
                [Edit(Card(3), Card(4), T0.AddMinutes(2), "x"), Edit(Card(1), Card(2), T0.AddMinutes(1), "y"), create],
            // Same instant, reversed: the Audit owner orders equal times by a random Guid.
            "equal-time-reverse-order" =>
                [Edit(Card(1), Card(2), T0, "x"), Edit(Card(2), Card(3), T0, "y"), Create(Card(1), T0)],
            // A creation in the middle of the list has events "before" the product existed.
            "create-not-oldest" => [Create(Card(1), T0.AddMinutes(1)), Edit(Card(1), Card(2), T0, "x")],
            _ => [edit with { EventId = Guid.Empty }],
        };
        Assert.Throws<InvalidOperationException>(() => ProductHistoryProjector.Project(ProductId, records));
    }

    [Fact]
    public async Task Reader_reads_one_extra_row_and_projects_after_the_card_proves_existence()
    {
        var products = new Products(Card(2));
        var audit = new Audit([Edit(Card(1), Card(2), T0.AddMinutes(1), "Gerekçe"), Create(Card(1), T0)]);
        var history = await new ProductHistoryReader(products, audit).GetAsync(ProductId, default);
        Assert.Equal(2, history.Entries.Count);
        Assert.Equal((ProductId, ProductHistoryApiV1.PageSize + 1), (audit.RequestedProduct, audit.RequestedLimit));
        Assert.Equal(1, products.Reads);
    }

    [Fact]
    public async Task History_that_stops_before_the_card_version_is_an_integrity_fault()
    {
        // The chain is intact and reaches the creation event; it just stops early, so only
        // the card can reveal that the newest events are missing.
        var products = new Products(Card(3));
        var audit = new Audit([Edit(Card(1), Card(2), T0.AddMinutes(1), "Gerekçe"), Create(Card(1), T0)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProductHistoryReader(products, audit).GetAsync(ProductId, default));
    }

    [Fact]
    public async Task History_ahead_of_the_card_is_an_edit_between_the_two_reads_not_a_fault()
    {
        var products = new Products(Card(1));
        var audit = new Audit([Edit(Card(1), Card(2), T0.AddMinutes(1), "Gerekçe"), Create(Card(1), T0)]);
        var history = await new ProductHistoryReader(products, audit).GetAsync(ProductId, default);
        Assert.Equal("2", history.Entries[0].VersionAfter);
    }

    [Fact]
    public async Task Card_version_that_is_not_a_canonical_number_is_an_integrity_fault()
    {
        // "+1" is the same number to a lenient parser; a stored version is canonical decimal
        // or nothing, because the comparison above decides whether newer events are missing.
        var card = Card(1);
        var products = new Products(card with { Product = card.Product with { Version = "+1" } });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProductHistoryReader(products, new Audit([Create(Card(1), T0)])).GetAsync(ProductId, default));
    }

    [Fact]
    public async Task Missing_card_is_not_found_and_the_audit_owner_is_never_read()
    {
        var audit = new Audit([Create(Card(1), T0)]);
        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            new ProductHistoryReader(new Products(null), audit).GetAsync(ProductId, default));
        Assert.Equal((404, ProductApiV1.NotFound), (error.StatusCode, error.Code));
        Assert.Equal(0, audit.Reads);
    }

    [Fact]
    public async Task Denied_card_read_propagates_before_any_audit_read()
    {
        var audit = new Audit([Create(Card(1), T0)]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ProductHistoryReader(new Products(null, deny: true), audit).GetAsync(ProductId, default));
        Assert.Equal(0, audit.Reads);
    }

    [Fact]
    public async Task Existing_card_without_any_audit_event_is_an_integrity_fault_not_an_empty_history()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProductHistoryReader(new Products(Card(1)), new Audit([])).GetAsync(ProductId, default));
    }

    [Fact]
    public async Task Empty_product_id_is_not_found_without_reads()
    {
        var products = new Products(Card(1));
        var audit = new Audit([]);
        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            new ProductHistoryReader(products, audit).GetAsync(Guid.Empty, default));
        Assert.Equal(404, error.StatusCode);
        Assert.Equal((0, 0), (products.Reads, audit.Reads));
    }

    /// <summary>A whole history: <paramref name="edits"/> edits above the creation, newest first.</summary>
    private static ProductAuditRecordV1[] Complete(int edits) =>
    [
        .. Enumerable.Range(0, edits)
            .Select(i => Edit(Card(edits - i), Card(edits - i + 1), T0.AddMinutes(edits - i), $"Değişiklik {edits - i}")),
        Create(Card(1), T0),
    ];

    /// <summary>Edits chaining down from <paramref name="newestAfter"/>, newest first, no creation.</summary>
    private static ProductAuditRecordV1[] Descending(long newestAfter, int count) =>
    [
        .. Enumerable.Range(0, count)
            .Select(i => Edit(Card(newestAfter - i - 1), Card(newestAfter - i), T0.AddMinutes(-i), $"Değişiklik {i}")),
    ];

    private static ProductAuditRecordV1 Create(ProductCardView after, DateTimeOffset at) =>
        new(Guid.NewGuid(), ProductId, ProductHistoryApiV1.DraftCreate, at, null, long.Parse(after.Product.Version),
            "Ürün taslağı oluşturma", "{}", Json(after));

    private static ProductAuditRecordV1 Edit(ProductCardView before, ProductCardView after, DateTimeOffset at, string reason) =>
        new(Guid.NewGuid(), ProductId, ProductHistoryApiV1.DraftBasicEdit, at, long.Parse(before.Product.Version),
            long.Parse(after.Product.Version), reason, Json(before), Json(after));

    private static ProductAuditRecordV1 Activate(ProductCardView before, ProductCardView after, DateTimeOffset at) =>
        new(Guid.NewGuid(), ProductId, ProductHistoryApiV1.Activate, at, long.Parse(before.Product.Version),
            long.Parse(after.Product.Version), "Ürün kartı onayı", Json(before), Json(after));

    private static ProductAuditRecordV1 Lifecycle(string operation, ProductCardView before, ProductCardView after,
        DateTimeOffset at, string reason) =>
        new(Guid.NewGuid(), ProductId, operation, at, long.Parse(before.Product.Version),
            long.Parse(after.Product.Version), reason, Json(before), Json(after));

    private static string Json(ProductCardView card) => JsonSerializer.Serialize(card, Web);

    private static ProductCardView Card(long version, string name = "Galvaniz bant", Guid? familyId = null,
        ProductRoleView? sellable = null, ProductUnitReferences? units = null, string lifecycle = "Draft") =>
        new(new ProductDraftView(ProductId, "PSQL-1", name, GroupId, null, familyId,
                new(new(null, "not-evaluated"), sellable ?? new(null, "not-evaluated"), new(null, "not-evaluated"), new(null, "not-evaluated")),
                lifecycle, version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                T0, T0.AddMinutes(version % 1000), new("Sac", null, null, null), new("NotEvaluated", [])),
            new ProductReferences(Units: units));

    private sealed class Products(ProductCardView? card, bool deny = false) : IProductService
    {
        public int Reads { get; private set; }

        public Task<ProductCardView?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            Reads++;
            if (deny) throw new UnauthorizedAccessException();
            return Task.FromResult(card);
        }

        public Task<ProductPage> GetPageAsync(ProductListQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductCodeGroup>> GetCodeGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductDuplicateCandidates> GetDuplicateCandidatesAsync(ProductDuplicateQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductReferenceOptions> GetReferenceOptionsAsync(ProductReferenceQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductMutationResponse> CreateDraftAsync(CreateProductDraftRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductMutationResponse> EditDraftAsync(Guid id, EditProductDraftRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductMutationResponse> ActivateAsync(Guid id, ActivateProductRequest request,
            string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductMutationResponse> DeactivateAsync(Guid id, ChangeProductLifecycleRequest request,
            string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductMutationResponse> ReactivateAsync(Guid id, ChangeProductLifecycleRequest request,
            string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Audit(IReadOnlyList<ProductAuditRecordV1> records) : IProductAuditHistorySourceV1
    {
        public int Reads { get; private set; }
        public Guid RequestedProduct { get; private set; }
        public int RequestedLimit { get; private set; }

        public Task<IReadOnlyList<ProductAuditRecordV1>> ReadNewestAsync(Guid productId, int limit, CancellationToken cancellationToken)
        {
            Reads++;
            (RequestedProduct, RequestedLimit) = (productId, limit);
            return Task.FromResult(records);
        }
    }
}
