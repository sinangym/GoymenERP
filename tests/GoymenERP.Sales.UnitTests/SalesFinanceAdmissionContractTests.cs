using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// MOD-04 (F2a) / F-005 D5 / FNS-D19: the versioned shape of <see cref="SalesFinanceAdmissionRequestV1"/>, the request Finance's host
/// adapter binds to. The official number and ETTN are optional and appended last: a caller written before them still compiles and sends
/// <c>null</c>, so an offer made before they existed is the same request and replays under its key. Every value is SENTETIK.
/// </summary>
public sealed class SalesFinanceAdmissionContractTests
{
    private static readonly SalesFinanceInstalmentV1[] Instalments =
        [new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), Guid.Parse("00000000-0000-0000-0000-0000000000b1"), 1,
            new DateOnly(2026, 12, 31), "100.00", null)];

    private static readonly Guid Company = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Admission = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Invoice = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Party = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid Currency = Guid.Parse("00000000-0000-0000-0000-000000000006");

    [Fact]
    public void The_admission_request_v1_shape_is_pinned_with_the_official_reference_appended_last_and_optional()
    {
        var constructor = Assert.Single(typeof(SalesFinanceAdmissionRequestV1).GetConstructors(),
            candidate => candidate.GetParameters().All(parameter => parameter.ParameterType != typeof(SalesFinanceAdmissionRequestV1)));
        var parameters = constructor.GetParameters();

        Assert.Equal(["CompanyId", "ActorId", "CorrelationId", "CausationId", "IdempotencyKey", "AdmissionId", "InvoiceId", "InvoiceVersion",
            "PartyId", "CompanyAccountId", "CurrencyId", "ExchangeRate", "Instalments", "OfficialInvoiceNo", "Ettn"],
            parameters.Select(parameter => parameter.Name!));
        Assert.All(parameters[^2..], parameter =>
        {
            Assert.Equal(typeof(string), parameter.ParameterType);
            Assert.True(parameter.HasDefaultValue);
            Assert.Null(parameter.DefaultValue);
        });
        Assert.All(parameters[..^2], parameter => Assert.False(parameter.HasDefaultValue));
    }

    [Fact]
    public void A_request_built_without_the_official_reference_is_the_same_request_as_one_with_explicit_nulls()
    {
        var before = new SalesFinanceAdmissionRequestV1(Company, "synthetic-actor", "synthetic-correlation", null,
            "sales-invoice:synthetic:4", Admission, Invoice, "4", Party, Account, Currency, null, Instalments);
        var withNulls = before with { OfficialInvoiceNo = null, Ettn = null };
        var withReference = before with { OfficialInvoiceNo = "GIB2026000000001", Ettn = "00000000-0000-0000-0000-0000000000e1" };

        Assert.Equal((null, null), (before.OfficialInvoiceNo, before.Ettn));
        Assert.Equal(before, withNulls);
        Assert.NotEqual(before, withReference);
        Assert.Equal(before, withReference with { OfficialInvoiceNo = null, Ettn = null });
    }
}
