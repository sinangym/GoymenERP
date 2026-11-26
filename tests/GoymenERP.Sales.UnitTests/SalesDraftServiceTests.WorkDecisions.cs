using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task YOL_D23_does_not_exclude_an_authorized_contributor_who_neither_created_nor_submitted(bool approve)
    {
        var world=new World().AllowDiscountsUpTo(10m);
        var created=(await world.Service().CreateDraftAsync(Request(),Metadata("k-contributor-create"))).Detail;
        world.User.Id="synthetic-editor";
        var edited=await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision("2",new SalesDraftLineCommercialV1(created.Lines[0].LineId,"13.00",KgUnit,"5")),Metadata("k-contributor-edit"));
        world.User.Id="synthetic-submitter";
        var submitted=await world.Service().SubmitDraftAsync(created.Draft.Id,Command(edited.Detail.Draft.Version),Metadata("k-contributor-submit"));
        world.User.Id="synthetic-editor";
        var decided=approve?await world.Service().ApproveDraftAsync(created.Draft.Id,Command(submitted.Detail.Draft.Version),Metadata("k-contributor-decide"))
            :await world.Service().RejectDraftAsync(created.Draft.Id,Command(submitted.Detail.Draft.Version),Metadata("k-contributor-decide"));
        Assert.Equal(approve?"Approved":"Rejected",decided.Detail.Draft.ApprovalState);
        Assert.Equal("synthetic-editor",decided.Detail.Approval.DecidedBy);
        Assert.Equal("synthetic-submitter",decided.Detail.Approval.SubmittedBy);
    }
}
