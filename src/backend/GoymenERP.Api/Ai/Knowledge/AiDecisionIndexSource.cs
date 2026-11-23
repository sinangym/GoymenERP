using System.Security.Cryptography;
using GoymenERP.Modules.AiKnowledge.Domain;

namespace GoymenERP.Api.Ai.Knowledge;

/// <summary>Where the decision index comes from. Null means "no index": the lookup tool answers unavailable.</summary>
public interface IAiDecisionIndexSource
{
    AiDecisionIndex? Current { get; }
}

/// <summary>Where the release behaviour knowledge comes from. Null means "no package": the tool answers unavailable.</summary>
public interface IAiReleaseKnowledgeSource
{
    AiReleaseKnowledge? Current { get; }
}

/// <summary>
/// WP-A4: the versioned AI knowledge packages that ship inside the API publish output. <c>GoymenERP.Api.csproj</c> copies
/// <c>contracts/ai/decision-index-v1.json</c> and <c>contracts/ai/release-knowledge-v1.json</c> next to the API assembly
/// (build and publish), so the knowledge always belongs to the very build that serves it; no path is configured any more
/// (the former <c>Ai:Knowledge:DecisionIndexPath</c> is gone). The version of a package is the SHA-256 of its bytes; an
/// approval (<c>ai.knowledge.approve</c>) binds to exactly that value. A missing, unreadable or malformed file (or one from a
/// generator without masking at generation) yields no package and the tools answer unavailable (fail-closed).
/// </summary>
public static class AiKnowledgePackageFiles
{
    public const string DecisionIndexFileName = "decision-index-v1.json";
    public const string ReleaseKnowledgeFileName = "release-knowledge-v1.json";

    public static string PathOf(string fileName) => Path.Combine(AppContext.BaseDirectory, fileName);

    /// <summary>The bytes and their SHA-256 (lower-case hex), or null when the file is absent or unreadable.</summary>
    public static (byte[] Bytes, string Sha256)? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            return (bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>The packaged decision index, read once from the API output directory.</summary>
public sealed class PackagedAiDecisionIndexSource : IAiDecisionIndexSource
{
    private readonly Lazy<AiDecisionIndex?> index;

    public PackagedAiDecisionIndexSource()
        : this(AiKnowledgePackageFiles.PathOf(AiKnowledgePackageFiles.DecisionIndexFileName))
    {
    }

    public PackagedAiDecisionIndexSource(string path) =>
        index = new Lazy<AiDecisionIndex?>(() => AiDecisionIndex.TryLoad(path), LazyThreadSafetyMode.ExecutionAndPublication);

    public AiDecisionIndex? Current => index.Value;
}

/// <summary>The packaged release behaviour knowledge, read once from the API output directory.</summary>
public sealed class PackagedAiReleaseKnowledgeSource : IAiReleaseKnowledgeSource
{
    private readonly Lazy<AiReleaseKnowledge?> package;

    public PackagedAiReleaseKnowledgeSource()
        : this(AiKnowledgePackageFiles.PathOf(AiKnowledgePackageFiles.ReleaseKnowledgeFileName))
    {
    }

    public PackagedAiReleaseKnowledgeSource(string path) =>
        package = new Lazy<AiReleaseKnowledge?>(() => AiReleaseKnowledge.TryLoad(path), LazyThreadSafetyMode.ExecutionAndPublication);

    public AiReleaseKnowledge? Current => package.Value;
}

/// <summary>A fixed index (tests and tools).</summary>
public sealed class FixedAiDecisionIndexSource(AiDecisionIndex? index) : IAiDecisionIndexSource
{
    public AiDecisionIndex? Current { get; } = index;
}

/// <summary>A fixed release knowledge package (tests and tools).</summary>
public sealed class FixedAiReleaseKnowledgeSource(AiReleaseKnowledge? package) : IAiReleaseKnowledgeSource
{
    public AiReleaseKnowledge? Current { get; } = package;
}

/// <summary>
/// The host side of <see cref="IAiKnowledgePackageCatalogV1"/>: what this build has packaged, for the approval screen and
/// the approval command (an approver can only approve the bytes that are packaged now).
/// </summary>
public sealed class PackagedAiKnowledgeCatalog(IAiDecisionIndexSource decisionIndex, IAiReleaseKnowledgeSource releaseKnowledge)
    : IAiKnowledgePackageCatalogV1
{
    public AiKnowledgePackageInfoV1? Find(AiKnowledgeSourceKindV1 kind) => kind switch
    {
        AiKnowledgeSourceKindV1.DecisionIndex when decisionIndex.Current is { } index => new AiKnowledgePackageInfoV1(
            kind, index.PackageSha256, index.SourceSetSha256, index.Records.Count, index.ConflictCount,
            index.Records.Count(record => record.Masked)),
        AiKnowledgeSourceKindV1.ReleaseKnowledge when releaseKnowledge.Current is { } package => new AiKnowledgePackageInfoV1(
            kind, package.PackageSha256, package.SourceSetSha256, package.Notes.Count, package.ExcludedCount,
            package.Notes.Count(note => note.Masked)),
        _ => null,
    };
}

/// <summary>
/// Used only when the AiKnowledge module is not composed (e.g. a host or test without a database): nothing is approved, so
/// the knowledge tools stay unavailable (fail-closed).
/// </summary>
public sealed class UnconfiguredAiKnowledgeApprovalReader : IAiKnowledgeApprovalReaderV1
{
    public Task<AiKnowledgeApprovalStateV1> GetStateAsync(Guid companyId, AiKnowledgeSourceKindV1 kind, string packageSha256,
        CancellationToken cancellationToken = default) => Task.FromResult(AiKnowledgeApprovalStateV1.Unavailable);
}
