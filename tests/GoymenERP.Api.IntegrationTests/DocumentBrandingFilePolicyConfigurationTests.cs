using System.Text;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Documents.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>The real JSON configuration provider's absent-vs-explicit-empty semantics; no host or environment activation.</summary>
public sealed class DocumentBrandingFilePolicyConfigurationTests
{
    [Theory]
    [InlineData("{}", true, 3)]
    [InlineData("{\"Documents\":{\"Branding\":{\"AllowedContentTypes\":[\"image/png\"]}}}", true, 1)]
    [InlineData("{\"Documents\":{\"Branding\":{\"AllowedContentTypes\":[]}}}", false, 0)]
    [InlineData("{\"Documents\":{\"Branding\":{\"AllowedContentTypes\":{}}}}", false, 0)]
    [InlineData("{\"Documents\":{\"Branding\":{\"AllowedContentTypes\":null}}}", false, 0)]
    [InlineData("{\"Documents\":{\"Branding\":{\"AllowedContentTypes\":\"image/png\"}}}", false, 0)]
    [InlineData("{\"Documents\":{\"Branding\":{\"AllowedContentTypes\":[\"application/xml\"]}}}", false, 0)]
    public void The_real_JSON_provider_never_turns_an_explicit_invalid_subset_into_the_missing_default(string json, bool available, int count)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = configuration as IDisposable;
        var actual = new DocumentBrandingFilePolicy(configuration).Read();
        Assert.Equal(available, actual.Available); Assert.Equal(count, actual.Kinds.Count);
        Assert.All(actual.Kinds, kind => Assert.Contains(kind, DocumentContentPolicy.StandardKinds));
    }
}
