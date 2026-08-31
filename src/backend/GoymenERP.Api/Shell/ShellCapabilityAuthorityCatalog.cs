using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace GoymenERP.Api.Shell;

public sealed partial class ShellCapabilityAuthorityCatalog
{
    private readonly IReadOnlySet<string> _acceptedRegistryRevisions;

    public ShellCapabilityAuthorityCatalog(
        IEnumerable<string> acceptedRegistryRevisions)
    {
        ArgumentNullException.ThrowIfNull(acceptedRegistryRevisions);

        var revisions = acceptedRegistryRevisions.ToArray();
        if (revisions.Any(revision =>
                string.IsNullOrWhiteSpace(revision)
                || !string.Equals(revision, revision.Trim(), StringComparison.Ordinal)
                || !RevisionPattern().IsMatch(revision))
            || revisions.Distinct(StringComparer.Ordinal).Count() != revisions.Length)
        {
            throw new InvalidOperationException(
                "Shell capability authority catalog geçersiz veya yinelenen revision içeriyor.");
        }

        _acceptedRegistryRevisions = revisions.ToFrozenSet(StringComparer.Ordinal);
    }

    public bool Accepts(string revision) =>
        _acceptedRegistryRevisions.Contains(revision);

    [GeneratedRegex("^sha256:[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();
}
