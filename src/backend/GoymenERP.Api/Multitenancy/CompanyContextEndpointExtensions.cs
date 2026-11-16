namespace GoymenERP.Api.Multitenancy;

public static class CompanyContextEndpointExtensions
{
    public static TBuilder RequireCompanyContext<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
            endpointBuilder.Metadata.Add(new RequiresCompanyContextAttribute()));
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(
            new Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(409, typeof(GoymenERP.Api.OpenApi.ErpProblemDetailsContract), ["application/problem+json"])));
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(
            new Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(503, typeof(GoymenERP.Api.OpenApi.ErpProblemDetailsContract), ["application/problem+json"])));
        return builder;
    }
}
