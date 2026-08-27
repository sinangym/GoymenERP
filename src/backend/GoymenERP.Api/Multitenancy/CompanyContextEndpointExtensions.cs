namespace GoymenERP.Api.Multitenancy;

public static class CompanyContextEndpointExtensions
{
    public static TBuilder RequireCompanyContext<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
            endpointBuilder.Metadata.Add(new RequiresCompanyContextAttribute()));
        return builder;
    }
}
