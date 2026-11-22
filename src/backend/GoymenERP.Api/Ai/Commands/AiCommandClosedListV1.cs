using System.Text;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Commands;

/// <summary>
/// AI-D25 closed list: approval and rejection commands, payment, period close, role/permission assignment and policy
/// publication are never executed through the AI path, under any configuration. Three independent layers, each of
/// which alone refuses: (1) the owner's declared effects must be exactly <see cref="AiCommandEffectV1.RecordChange"/>;
/// (2) no word of the tool id or owner command name names a closed family; (3) no required permission names one.
/// A tool with no effect declaration is refused (fail-closed). Layers 2 and 3 are defence in depth against a wrong
/// declaration; they can only refuse, never admit.
/// </summary>
public static class AiCommandClosedListV1
{
    /// <summary>Lower-case words that name a closed family in a tool id, command name or permission name.</summary>
    public static IReadOnlySet<string> ClosedWords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "approve", "approves", "approval", "approvals", "approved",
        "reject", "rejects", "rejection", "rejections", "rejected", "decide",
        "pay", "pays", "payment", "payments", "payout", "payouts", "disburse", "disbursement",
        "period", "periods", "period-close", "closing",
        "role", "roles", "permission", "permissions", "grant", "grants", "authority",
        "publish", "publishes", "publication", "policy", "policies",
    };

    public static bool IsClosed(IAiOwnerCommandToolV1 tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var descriptor = tool.Descriptor;
        if (tool is not IAiOwnerCommandEffectsV1 { Effects: { Count: > 0 } effects }
            || effects.Any(effect => effect != AiCommandEffectV1.RecordChange))
        {
            return true;
        }

        return NamesClosedFamily(descriptor.ToolId)
            || NamesClosedFamily(descriptor.OwnerCommandName)
            || descriptor.RequiredPermissions.Any(NamesClosedFamily);
    }

    /// <summary>Splits on '.', '-', '_', '/', ':' and camel-case boundaries; a null name is treated as closed.</summary>
    public static bool NamesClosedFamily(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var words = Words(name);
        for (var index = 0; index < words.Count; index++)
        {
            if (ClosedWords.Contains(words[index])
                || (index + 1 < words.Count && ClosedWords.Contains(words[index] + "-" + words[index + 1])))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            if (char.IsUpper(character) && current.Length > 0 && index > 0 && !char.IsUpper(name[index - 1]))
            {
                Flush();
            }

            current.Append(char.ToLowerInvariant(character));
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
    }
}
