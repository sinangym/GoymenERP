namespace GoymenERP.Modules.Documents.Domain;

/// <summary>The stable codes of the document content rules; tests and the wire assert the code, never the text.</summary>
public static class DocumentRuleCodes
{
    /// <summary>The upload carried no bytes.</summary>
    public const string Empty = "document_empty";

    /// <summary>The upload is larger than the configured bound (OPEN-015: the bound is configuration, not a decided number).</summary>
    public const string TooLarge = "document_too_large";

    /// <summary>The file's extension is not on the allowlist (DEC-DOC-009), or it has none.</summary>
    public const string TypeNotAllowed = "document_type_not_allowed";

    /// <summary>The bytes are not what the extension or the declared type says (DEC-DOC-009 MIME validation).</summary>
    public const string ContentMismatch = "document_content_mismatch";

    /// <summary>The file name is blank or has no usable characters left after the safe conversion.</summary>
    public const string FileNameInvalid = "document_file_name_invalid";
}
