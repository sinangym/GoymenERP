namespace GoymenERP.Modules.Documents.Application.Contracts;

/// <summary>
/// The document vault's own permission names (ERP-000G / AT-065). Company-wide, not critical; the central catalogue
/// (<c>ErpPermissions</c>) registers them with the central writer's step, and any grant is a human gate. The service
/// measures them afresh at every fence, and ON TOP of them the owner of the linked record answers whether the actor may
/// read that record (a document is never more visible than its owner record). An unregistered name is measured as
/// Denied by the central authority, so the routes answer 403 until the catalogue knows them.
/// </summary>
public static class DocumentsPermissionManifestV1
{
    /// <summary>List the documents of an owner record and download a version's content (with the owner's read).</summary>
    public const string FilesRead = "documents.files.read";

    /// <summary>Upload a document to an owner record (with the owner's read). Its own name: reading confers no upload.</summary>
    public const string FilesUpload = "documents.files.upload";

    public static readonly IReadOnlyList<string> Names = [FilesRead, FilesUpload];
}
