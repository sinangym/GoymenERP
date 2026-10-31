using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D123 (EK18): an order is approved only with due terms. A SENTETIK "nokta vade" (order date + 30 days, one instalment) for
/// the tests whose subject is not the due terms; it is test data, never a product default (SAT-D127).
/// </summary>
internal static class TestDueTerms
{
    public static SalesDueTerms PointDue30 { get; } = SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.OrderDate, 30, "100")]);

    public static SalesDueTermsV1 PointDue30Wire { get; } = new([new SalesDueInstalmentV1(1, SalesDueBasisNamesV1.OrderDate, 30, "100")]);
}
