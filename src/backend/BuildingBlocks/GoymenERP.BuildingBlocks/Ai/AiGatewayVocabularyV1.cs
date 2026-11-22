namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>
/// WP-A1 (AI-D22, AI-D24, DALGA_PLANI_v2 A.1): the shared vocabulary of the AI gateway (<c>ai-gateway.v1</c>). The
/// numeric values are stored by the Settings owner (gateway policy versions, budget ledger) and are a contract: a value is
/// never renumbered or reused; a new member takes the next number.
/// </summary>
public enum AiFeatureV1
{
    /// <summary>Screen-bound side panel answer (WP-B1).</summary>
    Panel = 1,

    /// <summary>Approval card summary and risk flags (WP-B2).</summary>
    Summary = 2,

    /// <summary>Draft from a document (WP-B3). Free text of a document is read only by a company-internal model (AI-D24).</summary>
    Document = 3,

    /// <summary>Narrow natural language query (WP-B4).</summary>
    Query = 4,

    /// <summary>Approved write pipeline preview text (WP-C1). The gateway never executes a command.</summary>
    Write = 5,

    /// <summary>General chat mode with ERP tools closed (WP-B5, AI-D28 b).</summary>
    Chat = 6,

    /// <summary>Scheduled tasks and morning summaries (WP-C5).</summary>
    Scheduled = 7,

    /// <summary>Personal memory (WP-C6).</summary>
    Memory = 8,

    /// <summary>E-mail drafts and incoming reply reading (WP-C7). E-mail free text is company-internal only (AI-D24/AI-D27).</summary>
    Email = 9,
}

/// <summary>The task kind half of the "task × data class" model table (AI-D24).</summary>
public enum AiTaskKindV1
{
    /// <summary>Answer a question from tool results (sources required).</summary>
    Answer = 1,

    /// <summary>Summarize grounded content.</summary>
    Summarize = 2,

    /// <summary>Extract fields from content; the owner validates every field deterministically (AI-D34 c).</summary>
    Extract = 3,

    /// <summary>Classify content (never removes an approval requirement, AI-D34 b).</summary>
    Classify = 4,

    /// <summary>Draft a text the user reviews.</summary>
    Draft = 5,

    /// <summary>Free conversation (general chat only, no ERP data).</summary>
    Converse = 6,
}

/// <summary>Where a model runs. Cloud is outside the company; Local is a company-internal model (OPEN-014).</summary>
public enum AiModelPlacementV1
{
    Cloud = 1,
    Local = 2,
}

/// <summary>
/// The fixed safety rules of the model table that no Settings version can loosen (AI-D3/AI-D4, AI-D24, AI-D34 d). They
/// are restated in the Settings check constraint and enforced again by the gateway on every call.
/// </summary>
public static class AiGatewayPlacementRulesV1
{
    /// <summary>The highest data class a cloud model may ever receive (personal, confidential and strictly confidential stay in).</summary>
    public const AiDataClassV1 CloudMaximumDataClass = AiDataClassV1.Internal;

    /// <summary>Whether a route of <paramref name="placement"/> may carry content of <paramref name="dataClass"/>.</summary>
    public static bool PlacementAllows(AiModelPlacementV1 placement, AiDataClassV1 dataClass) =>
        Enum.IsDefined(dataClass)
        && placement switch
        {
            AiModelPlacementV1.Cloud => dataClass <= CloudMaximumDataClass,
            AiModelPlacementV1.Local => true,
            _ => false,
        };

    /// <summary>Document and e-mail free text is read only by a company-internal model (AI-D24, AI-D27).</summary>
    public static bool FeatureAllows(AiFeatureV1 feature, AiModelPlacementV1 placement) =>
        Enum.IsDefined(feature)
        && Enum.IsDefined(placement)
        && !(placement == AiModelPlacementV1.Cloud && feature is AiFeatureV1.Document or AiFeatureV1.Email);
}
