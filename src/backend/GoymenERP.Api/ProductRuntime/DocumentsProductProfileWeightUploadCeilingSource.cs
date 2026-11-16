using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.ProductRuntime;

/// <summary>
/// URN-D23: the platform's general file upload bound for the kg/m import ceiling, read from the document vault's own bindings
/// (<see cref="DocumentsServiceBindings.MaximumUploadBytes"/>, <c>Documents:MaximumUploadBytes</c> or its default). No second
/// value is written here. When the vault is not composed in this host the bound cannot be read and the import refuses 503
/// (fail-closed; the vault would refuse the file anyway).
/// </summary>
public sealed class DocumentsProductProfileWeightUploadCeilingSource(IServiceProvider services) : IProductProfileWeightUploadCeilingSourceV1
{
    public Task<long> GetPlatformMaximumUploadBytesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return services.GetService<DocumentsServiceBindings>() is { } bindings
            ? Task.FromResult(bindings.MaximumUploadBytes)
            : Task.FromException<long>(new InvalidOperationException("The document vault's upload bound is not composed in this host."));
    }
}
