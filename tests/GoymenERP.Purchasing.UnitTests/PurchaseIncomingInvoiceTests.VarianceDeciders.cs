namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseIncomingInvoiceTests
{
    [Fact]
    public void The_variance_may_not_decide_set_is_the_recorder_the_matcher_and_the_matched_orders_approvers()
    {
        var invoice = Invoice();
        invoice.Match(1, [Match(invoice) with { ExpectedNet = 90m }], Policy(), "matcher");

        // WP1 D4 / ROL-D10: the purchase order approver may not decide the invoice variance; YOL-D23: no one else is added.
        var excluded = invoice.VarianceDeciderExclusions(["order-approver", " order-approver ", null, "", "matcher"]);

        Assert.Equal(["recorder", "matcher", "order-approver"], excluded);
        Assert.DoesNotContain("manager", excluded);
    }
}
