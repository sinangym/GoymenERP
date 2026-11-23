using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Counterparty.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>
/// WP-A3a Cari READ tools over a SENTETIK owner read: the actor and session passed to the owner, a natural person's names
/// masked (AI-D36), only the context company's account released, another company's account / an unknown kind or role
/// releasing nothing, Forbidden as Denied, a missing owner count marking the answer incomplete, and an unscoped argument
/// refused before the owner is read.
/// </summary>
public sealed class CounterpartyReadAiToolsTests
{
    private const string PersonMask = "[KİŞİ]";
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000b2");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private const string Actor = "11111111-1111-4111-8111-111111111111";
    private const string Session = "v1.session-sentetik";

    [Fact]
    public void The_cari_descriptors_are_read_tools_under_the_party_read_permission_with_their_data_class()
    {
        var owner = new Owner();
        var lookup = new CounterpartyPartyLookupAiTool(owner, new FixedTime(Now)).Descriptor;
        var summary = new CounterpartyPartySummaryAiTool(owner, new FixedTime(Now)).Descriptor;
        var counts = new CounterpartyRoleCountsAiTool(owner, new FixedTime(Now)).Descriptor;

        Assert.All(new[] { lookup, summary, counts }, descriptor =>
        {
            Assert.Equal("Counterparty", descriptor.OwnerModule);
            Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
            Assert.Null(descriptor.OwnerCommandName);
            Assert.Equal(new[] { "counterparty.parties.read" }, descriptor.RequiredPermissions);
        });
        // AI-D42: the party title, code, roles and counts are Internal; only the authorised-person tool is Confidential.
        Assert.Equal(AiDataClassV1.Internal, lookup.DataClass);
        Assert.Equal(AiDataClassV1.Internal, summary.DataClass);
        Assert.Equal(AiDataClassV1.Internal, counts.DataClass);
        var contacts = new CounterpartyPartyContactsAiTool(new ContactOwner(), new FixedTime(Now)).Descriptor;
        Assert.Equal(AiDataClassV1.Confidential, contacts.DataClass);
        Assert.Equal(new[] { "counterparty.parties.read" }, contacts.RequiredPermissions);
        Assert.Equal(new[] { "limit", "partyId" }, contacts.FieldAllowlist);
        Assert.Equal(new[] { "limit", "query", "role" }, lookup.FieldAllowlist);
        Assert.Equal(new[] { "partyId" }, summary.FieldAllowlist);
        Assert.Empty(counts.FieldAllowlist);
    }

    [Fact]
    public void The_counterparty_module_registers_the_four_read_tools_as_scoped_owner_tools()
    {
        var services = new ServiceCollection();
        services.AddCounterpartyModule(new TestConfiguration());

        var tools = services.Where(descriptor => descriptor.ServiceType == typeof(IAiOwnerToolV1)).ToArray();
        Assert.Equal(
            new[]
            {
                typeof(CounterpartyPartyContactsAiTool), typeof(CounterpartyPartyLookupAiTool), typeof(CounterpartyPartySummaryAiTool),
                typeof(CounterpartyRoleCountsAiTool),
            },
            tools.Select(descriptor => descriptor.ImplementationType!).OrderBy(type => type.Name, StringComparer.Ordinal).ToArray());
        Assert.All(tools, descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
    }

    [Fact]
    public async Task A_lookup_masks_natural_person_names_releases_the_owner_count_and_passes_actor_and_session()
    {
        var owner = new Owner
        {
            Page = new CounterpartyPage(
                [Item(1, PartyKind.LegalEntity, "SENTETIK Metal A.Ş.", "SENTETIK Metal", PartyRole.Customer),
                 Item(2, PartyKind.NaturalPerson, "Sentetik Kişi Adı", "Sentetik Ticaret", PartyRole.Customer | PartyRole.Supplier)],
                "next", true, 7, "filtered-authorized-visible-parties"),
        };

        var result = await new CounterpartyPartyLookupAiTool(owner, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"query\":\" SENTETIK \",\"role\":\"Customer\",\"limit\":2}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.Equal(7, root.GetProperty("matchCount").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
        var person = root.GetProperty("items")[1];
        Assert.Equal(PersonMask, person.GetProperty("legalName").GetString());
        Assert.Equal(PersonMask, person.GetProperty("tradeName").GetString());
        Assert.True(person.GetProperty("nameMasked").GetBoolean());
        Assert.Equal(new[] { "Customer", "Supplier" },
            person.GetProperty("account").GetProperty("roles").EnumerateArray().Select(role => role.GetString()).ToArray());
        Assert.Equal("SENTETIK Metal A.Ş.", root.GetProperty("items")[0].GetProperty("legalName").GetString());
        Assert.DoesNotContain("Sentetik Kişi Adı", result.ContentJson!, StringComparison.Ordinal);
        var request = Assert.Single(owner.Requests);
        Assert.Equal((Actor, Session), (request.ActorId, request.SessionBinding));
        Assert.Equal("SENTETIK", request.Query.Query);
        Assert.Equal((PartyRole.Customer, CounterpartyRecordState.Active, 2, true),
            (request.Query.Roles, request.Query.PartyState, request.Query.PageSize, request.Query.IncludeCount));
        Assert.Equal(AiDataClassV1.Internal, result.Source!.DataClass);
        Assert.Equal("Internal", root.GetProperty("dataClass").GetString());
        Assert.Equal("AI-D42", root.GetProperty("fieldPolicy").GetString());
        Assert.All(root.GetProperty("fieldClasses").EnumerateObject(), field => Assert.Equal("Internal", field.Value.GetString()));
        Assert.Contains(root.GetProperty("fieldClasses").EnumerateObject(), field => field.Name == "legalName");
        Assert.DoesNotContain("phone", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_lookup_without_an_owner_count_is_incomplete_and_forbidden_is_denied()
    {
        var uncounted = new Owner { Page = new CounterpartyPage([Item(1, PartyKind.LegalEntity, "SENTETIK", null, PartyRole.Customer)], null, false, null, null) };
        var result = await new CounterpartyPartyLookupAiTool(uncounted, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"query\":\"SENTETIK\"}"), Context());
        using (var content = JsonDocument.Parse(result.ContentJson!))
        {
            Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("matchCount").ValueKind);
        }

        var forbidden = new Owner { Execution = CounterpartyPageExecution.Forbidden() };
        Assert.Equal(AiToolOutcomeV1.Denied, (await new CounterpartyPartyLookupAiTool(forbidden, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"query\":\"SENTETIK\"}"), Context())).Outcome);
        var unavailable = new Owner { Execution = CounterpartyPageExecution.ServiceUnavailable() };
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new CounterpartyPartyLookupAiTool(unavailable, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"query\":\"SENTETIK\"}"), Context())).Outcome);
    }

    [Fact]
    public async Task A_party_with_another_companys_account_or_an_unknown_role_releases_nothing()
    {
        var foreign = Item(1, PartyKind.LegalEntity, "SENTETIK", null, PartyRole.Customer) with
        {
            CompanyAccounts = [new CounterpartyListAccount(Guid.NewGuid(), OtherCompany, PartyRole.Customer, true, 1)],
        };
        var twoCompanies = Item(2, PartyKind.LegalEntity, "SENTETIK", null, PartyRole.Customer) with
        {
            CompanyAccounts =
            [
                new CounterpartyListAccount(Guid.NewGuid(), Company, PartyRole.Customer, true, 1),
                new CounterpartyListAccount(Guid.NewGuid(), OtherCompany, PartyRole.Customer, true, 1),
            ],
        };
        var unknownRole = Item(3, PartyKind.LegalEntity, "SENTETIK", null, (PartyRole)64);
        foreach (var item in new[] { foreign, twoCompanies, unknownRole })
        {
            var owner = new Owner { Page = new CounterpartyPage([item], null, false, 1, "filtered-authorized-visible-parties") };

            var result = await new CounterpartyPartyLookupAiTool(owner, new FixedTime(Now))
                .ReadAsync(new AiToolInputV1("{\"query\":\"SENTETIK\"}"), Context());

            Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
            Assert.Null(result.ContentJson);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"query\":\"S\"}")]
    [InlineData("{\"query\":\"SENTETIK\",\"role\":\"customer\"}")]
    [InlineData("{\"query\":\"SENTETIK\",\"limit\":21}")]
    [InlineData("{\"query\":\"SENTETIK\",\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000000a1\"}")]
    [InlineData("{\"query\":\"SENTETIK\",\"includeContacts\":true}")]
    public async Task A_missing_malformed_or_unscoped_lookup_argument_is_invalid_before_the_owner_is_read(string arguments)
    {
        var owner = new Owner();

        var result = await new CounterpartyPartyLookupAiTool(owner, new FixedTime(Now)).ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Empty(owner.Requests);
    }

    [Fact]
    public async Task A_summary_releases_the_context_company_account_and_masks_a_natural_persons_names()
    {
        var party = Id(1);
        var owner = new Owner
        {
            Detail = new CounterpartyDetailView(party, "C-0001", PartyKind.NaturalPerson, "Sentetik Kişi Adı", null, true, "4",
                [new CounterpartyDetailAccountView(Id(11), Company, PartyRole.Customer, true, "2")]),
        };

        var result = await new CounterpartyPartySummaryAiTool(owner, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"partyId\":\"{party:D}\"}}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var released = content.RootElement.GetProperty("party");
        Assert.Equal(PersonMask, released.GetProperty("legalName").GetString());
        Assert.Equal(JsonValueKind.Null, released.GetProperty("tradeName").ValueKind);
        Assert.Equal("C-0001", released.GetProperty("code").GetString());
        Assert.Equal(new[] { "Customer" },
            released.GetProperty("account").GetProperty("roles").EnumerateArray().Select(role => role.GetString()).ToArray());
        Assert.DoesNotContain("Sentetik Kişi Adı", result.ContentJson!, StringComparison.Ordinal);
        Assert.Equal(party, Assert.Single(owner.DetailIds));
        Assert.Equal(party, result.Source!.TargetId);
    }

    [Fact]
    public async Task A_summary_is_not_found_when_the_owner_answers_nothing_and_unavailable_for_another_party_or_company()
    {
        var party = Id(1);
        Assert.Equal(AiToolOutcomeV1.NotFound, (await new CounterpartyPartySummaryAiTool(new Owner(), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"partyId\":\"{party:D}\"}}"), Context())).Outcome);

        foreach (var detail in new[]
                 {
                     new CounterpartyDetailView(Id(2), "C-2", PartyKind.LegalEntity, "SENTETIK", null, true, "1",
                         [new CounterpartyDetailAccountView(Id(12), Company, PartyRole.Customer, true, "1")]),
                     new CounterpartyDetailView(party, "C-1", PartyKind.LegalEntity, "SENTETIK", null, true, "1",
                         [new CounterpartyDetailAccountView(Id(12), OtherCompany, PartyRole.Customer, true, "1")]),
                 })
        {
            var result = await new CounterpartyPartySummaryAiTool(new Owner { Detail = detail }, new FixedTime(Now))
                .ReadAsync(new AiToolInputV1($"{{\"partyId\":\"{party:D}\"}}"), Context());
            Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
            Assert.Null(result.ContentJson);
        }
    }

    [Fact]
    public async Task Role_counts_are_the_owner_counts_per_role_and_a_missing_count_is_null_and_incomplete()
    {
        var owner = new Owner
        {
            CountsByRole = new Dictionary<PartyRole, long?>
            {
                [PartyRole.None] = 12, [PartyRole.Customer] = 9, [PartyRole.Supplier] = 5, [PartyRole.Tolling] = 0, [PartyRole.Service] = null,
            },
        };

        var result = await new CounterpartyRoleCountsAiTool(owner, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(12, root.GetProperty("activePartyCount").GetInt32());
        Assert.Equal(9, root.GetProperty("customerCount").GetInt32());
        Assert.Equal(5, root.GetProperty("supplierCount").GetInt32());
        Assert.Equal(0, root.GetProperty("tollingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("serviceCount").ValueKind);
        Assert.False(root.GetProperty("complete").GetBoolean());
        Assert.Equal(5, owner.Requests.Count);
        Assert.All(owner.Requests, request => Assert.Equal((1, true, CounterpartyRecordState.Active, CounterpartyRecordState.Active),
            (request.Query.PageSize, request.Query.IncludeCount, request.Query.PartyState, request.Query.AccountState)));
        Assert.DoesNotContain("SENTETIK", result.ContentJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Role_counts_are_denied_when_forbidden_and_refuse_any_argument_before_the_owner_is_read()
    {
        var forbidden = new Owner { Execution = CounterpartyPageExecution.Forbidden() };
        Assert.Equal(AiToolOutcomeV1.Denied,
            (await new CounterpartyRoleCountsAiTool(forbidden, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var owner = new Owner();
        var invalid = await new CounterpartyRoleCountsAiTool(owner, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000000a1\"}"), Context());
        Assert.Equal(AiToolOutcomeV1.Invalid, invalid.Outcome);
        Assert.Empty(owner.Requests);
    }

    [Fact]
    public async Task Contacts_release_names_and_channels_as_confidential_with_per_field_classes()
    {
        var party = Id(1);
        var owner = new ContactOwner
        {
            View = ContactView(party, Company, "0212 000 00 00", "info@sentetik.example",
            [
                new CounterpartyOperationalContact(Id(21), "1", true, "Sentetik Yetkili", "Satın Alma", "purchasing",
                [
                    new CounterpartyContactChannel(Id(31), "1", true, "phone", "0532 000 00 00", null),
                    new CounterpartyContactChannel(Id(32), "1", false, "email", "eski@sentetik.example", null),
                ]),
                new CounterpartyOperationalContact(Id(22), "1", false, "Pasif Kisi", null, "other", []),
            ]),
        };

        var result = await new CounterpartyPartyContactsAiTool(owner, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"partyId\":\"{party:D}\"}}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal("Confidential", root.GetProperty("dataClass").GetString());
        var classes = root.GetProperty("fieldClasses").EnumerateObject().ToDictionary(field => field.Name, field => field.Value.GetString());
        Assert.Equal("Confidential", classes["contactName"]);
        Assert.Equal("Confidential", classes["phone"]);
        Assert.Equal("Confidential", classes["email"]);
        Assert.Equal("Internal", classes["contactJobTitle"]);
        Assert.Equal(1, root.GetProperty("activeContactCount").GetInt32());
        var contact = Assert.Single(root.GetProperty("contacts").EnumerateArray());
        Assert.Equal("Sentetik Yetkili", contact.GetProperty("displayName").GetString());
        Assert.Single(contact.GetProperty("channels").EnumerateArray());
        Assert.DoesNotContain("eski@sentetik.example", result.ContentJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("Pasif Kisi", result.ContentJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTETIK Sokak", result.ContentJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("sentetik.example/web", result.ContentJson!, StringComparison.Ordinal);
        Assert.Equal(AiDataClassV1.Confidential, result.Source!.DataClass);
    }

    [Fact]
    public async Task An_account_without_person_phone_or_email_answers_internal()
    {
        var party = Id(1);
        var owner = new ContactOwner { View = ContactView(party, Company, null, null, []) };

        var result = await new CounterpartyPartyContactsAiTool(owner, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"partyId\":\"{party:D}\"}}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.Equal("Internal", content.RootElement.GetProperty("dataClass").GetString());
        Assert.Empty(content.RootElement.GetProperty("fieldClasses").EnumerateObject());
        Assert.Equal(AiDataClassV1.Internal, result.Source!.DataClass);
    }

    [Fact]
    public async Task Contacts_fail_closed_for_another_company_an_unknown_channel_a_refusal_or_nothing()
    {
        var party = Id(1);
        foreach (var view in new[]
                 {
                     ContactView(party, OtherCompany, "0212", null, []),
                     ContactView(Id(2), Company, "0212", null, []),
                     ContactView(party, Company, null, null,
                         [new CounterpartyOperationalContact(Id(21), "1", true, "Sentetik", null, "other",
                             [new CounterpartyContactChannel(Id(31), "1", true, "fax", "0212", null)])]),
                 })
        {
            var result = await new CounterpartyPartyContactsAiTool(new ContactOwner { View = view }, new FixedTime(Now))
                .ReadAsync(new AiToolInputV1($"{{\"partyId\":\"{party:D}\"}}"), Context());
            Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
            Assert.Null(result.ContentJson);
        }

        Assert.Equal(AiToolOutcomeV1.Denied, (await new CounterpartyPartyContactsAiTool(
                new ContactOwner { Failure = new UnauthorizedAccessException("SENTETIK") }, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"partyId\":\"{party:D}\"}}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.NotFound, (await new CounterpartyPartyContactsAiTool(new ContactOwner(), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"partyId\":\"{party:D}\"}}"), Context())).Outcome);
        var unread = new ContactOwner();
        Assert.Equal(AiToolOutcomeV1.Invalid, (await new CounterpartyPartyContactsAiTool(unread, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"partyId\":\"{party:D}\",\"includeTckn\":true}}"), Context())).Outcome);
        Assert.Empty(unread.PartyIds);
    }

    private static CounterpartyContactInformationView ContactView(Guid party, Guid company, string? phone, string? email,
        IReadOnlyList<CounterpartyOperationalContact> contacts) => new(
        party, Id(11), company, "3", true,
        new CounterpartyContactInformation(phone, phone is null ? null : "12", email, "https://sentetik.example/web",
            [new CounterpartyOperationalAddress(Id(41), "1", true, "billing", "TR", "SENTETIK Sokak 1", null, "İstanbul", null, null)],
            contacts));

    /// <summary>A SENTETIK Cari contact read: one fixed view, a failure, or nothing.</summary>
    private sealed class ContactOwner : ICounterpartyContactInformationService
    {
        public CounterpartyContactInformationView? View { get; init; }
        public Exception? Failure { get; init; }
        public List<Guid> PartyIds { get; } = [];

        public Task<CounterpartyContactInformationView?> GetContactInformationAsync(Guid partyId, CancellationToken cancellationToken)
        {
            PartyIds.Add(partyId);
            return Failure is null ? Task.FromResult(View) : Task.FromException<CounterpartyContactInformationView?>(Failure);
        }

        public Task<CounterpartyCommandExecution> UpdateContactInformationAsync(Guid partyId, UpdateCounterpartyContactInformation command,
            CounterpartyCommandMetadata metadata, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The tool never writes.");
    }

    private static AiServerContextV1 Context() => new(Company, Actor, Session, $"company:{Company:D}", "roles:sentetik",
        "corr-sentetik", null, null, null);

    private static Guid Id(int number) => new($"00000000-0000-4000-8000-{number:D12}");

    private static CounterpartyListItem Item(int number, PartyKind kind, string legalName, string? tradeName, PartyRole roles) => new(
        Id(number), $"C-{number:D4}", kind, legalName, tradeName, true, number,
        [new CounterpartyListAccount(Id(100 + number), Company, roles, true, number)]);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A SENTETIK Cari owner read: a fixed page, per-role counts, a fixed execution, or a fixed detail.</summary>
    private sealed class Owner : ICounterpartyReadService
    {
        public CounterpartyPage? Page { get; init; }
        public CounterpartyPageExecution? Execution { get; init; }
        public Dictionary<PartyRole, long?>? CountsByRole { get; init; }
        public CounterpartyDetailView? Detail { get; init; }
        public List<CounterpartyReadRequest> Requests { get; } = [];
        public List<Guid> DetailIds { get; } = [];

        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Execution is not null)
            {
                return Task.FromResult(Execution);
            }

            if (CountsByRole is not null)
            {
                return Task.FromResult(CounterpartyPageExecution.Succeeded(new CounterpartyPage([], null, false,
                    CountsByRole[request.Query.Roles], CountsByRole[request.Query.Roles] is null ? null : "filtered-authorized-visible-parties")));
            }

            return Task.FromResult(Page is null ? CounterpartyPageExecution.ServiceUnavailable() : CounterpartyPageExecution.Succeeded(Page));
        }

        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default)
        {
            DetailIds.Add(partyId);
            return Task.FromResult(Detail);
        }
    }

    private sealed class TestConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => key switch
            {
                "DB_PROVIDER" => "SqlServer",
                "DB_CONNECTION_STRING" => "Server=127.0.0.1;Database=not-opened;User Id=x;Password=x;",
                _ => null,
            };
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
