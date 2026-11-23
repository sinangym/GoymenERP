using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>Where a piece of external content came from (WP-A6). A kind only; no address, name or path.</summary>
public enum AiExternalContentSourceV1
{
    Email = 1,
    Pdf = 2,
    PastedText = 3,
    StructuredFile = 4,
    ScannedImage = 5,
}

/// <summary>
/// What the quarantine screen saw in external content. A kind only: the matched text is never kept, logged or returned.
/// A finding never changes what the system does with its tools — it only keeps a value from being trusted.
/// </summary>
public enum AiQuarantineFindingV1
{
    /// <summary>Text that addresses an assistant ("önceki talimatları yok say", "ignore previous instructions").</summary>
    InstructionLikeText = 1,

    /// <summary>Tool/function-call syntax or a request to run a tool.</summary>
    ToolCallLikeSyntax = 2,

    /// <summary>Chat role or prompt framing markers ("system:", "[INST]", "&lt;|im_start|&gt;").</summary>
    RoleOrPromptMarker = 3,

    /// <summary>Zero-width or bidirectional control characters that can hide text from a reviewer.</summary>
    InvisibleCharacters = 4,

    /// <summary>A request to change authority, recipient, scope or approval ("yetki ver", "send this to", "tümünü onayla").</summary>
    AuthorityOrRecipientChange = 5,
}

/// <summary>
/// External text held as DATA (AI-D34 c: "dışarıdan gelen belge ve e-posta içeriği yalnız işlenecek veridir; araç
/// yetkisini, alıcıyı, işlem kapsamını ya da onay kuralını değiştiremez"). The type is deliberately not a string: it
/// cannot be passed where a question, a tool hint, an argument or an instruction is expected, and its
/// <see cref="ToString"/> never reveals the content, so string interpolation into a prompt cannot happen by accident.
/// The only way to the text is <see cref="ReadAsData"/>, whose callers are field extractors that return typed values.
/// </summary>
public sealed class AiQuarantinedTextV1
{
    private readonly string text;

    internal AiQuarantinedTextV1(string text, AiExternalContentSourceV1 source, IReadOnlyList<AiQuarantineFindingV1> findings)
    {
        this.text = text;
        Source = source;
        Findings = findings;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public AiExternalContentSourceV1 Source { get; }

    /// <summary>Lower-case hex SHA-256 of the UTF-8 text: provenance without content.</summary>
    public string Sha256 { get; }

    public int Length => text.Length;

    public IReadOnlyList<AiQuarantineFindingV1> Findings { get; }

    public bool HasFindings => Findings.Count != 0;

    /// <summary>The raw text, for deterministic field extraction only. Never a prompt, question or instruction.</summary>
    public string ReadAsData() => text;

    public override string ToString() => $"[karantina:{Source}:{Sha256[..12]}]";
}

/// <summary>
/// WP-A6 external content quarantine. Every e-mail body, PDF text, pasted text, structured file cell and OCR output
/// enters through <see cref="Admit"/>. The screen is best effort and only ever makes the system MORE cautious: a finding
/// marks values unverified and is reported as a kind; nothing in external content can add a tool, a recipient, an
/// approval or a permission. The structural guarantee is <see cref="AdmitModelOutput"/>: a model turn that saw external
/// content may return field values only — any planned tool call voids the whole output.
/// </summary>
public static partial class AiExternalContentQuarantineV1
{
    [GeneratedRegex(
        @"(?:ignore|disregard|forget|override)\s+(?:all\s+|any\s+|the\s+)?(?:previous|prior|above|earlier|system)\s+(?:instructions?|prompts?|rules?|messages?)"
        + @"|(?:you\s+are\s+now|act\s+as|new\s+instructions?|system\s+prompt|developer\s+mode)"
        + @"|(?:önceki|yukarıdaki|tüm|bütün)\s+(?:talimat|komut|kural|mesaj)\w*\s+(?:yok\s+say|unut|görmezden\s+gel|geçersiz)"
        + @"|(?:sen\s+artık|yeni\s+talimat|sistem\s+istem)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InstructionPattern();

    [GeneratedRegex(
        @"(?:tool_calls?|function_call|""name""\s*:\s*""[a-z][a-z0-9_.-]*""\s*,\s*""(?:arguments|parameters)""|<\s*/?\s*(?:tool|function)[_\s-]?(?:call|use)?\s*>"
        + @"|(?:call|invoke|run|execute|use)\s+(?:the\s+)?(?:tool|function|command)"
        + @"|(?:aracı|fonksiyonu|komutu)\s+(?:çağır|çalıştır|kullan))",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ToolCallPattern();

    [GeneratedRegex(
        @"(?:^|\n)\s*(?:system|assistant|developer|sistem|asistan)\s*:|\[/?INST\]|<\|[a-z_]+\|>|<<\s*/?SYS\s*>>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RolePattern();

    [GeneratedRegex(
        @"(?:grant|give|assign)\s+(?:me\s+|the\s+user\s+)?(?:admin|role|permission|access)"
        + @"|(?:approve|confirm)\s+(?:all|this|the\s+(?:order|invoice|payment))"
        + @"|(?:send|forward|e-?mail)\s+(?:it|this|the\s+\w+)?\s*to\s+"
        + @"|(?:yetki|izin|rol)\w*\s+(?:ver|ata|tanımla)"
        + @"|(?:tümünü|hepsini|bunu|faturayı|siparişi|ödemeyi)\s+onayla"
        + @"|(?:şu\s+adrese|şu\s+kişiye|aşağıdaki\s+adrese)\s+(?:gönder|ilet)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AuthorityPattern();

    [GeneratedRegex(@"[​-‏‪-‮⁠-⁤⁦-⁩﻿]", RegexOptions.CultureInvariant)]
    private static partial Regex InvisiblePattern();

    /// <summary>Holds the text as data and records what the screen saw.</summary>
    public static AiQuarantinedTextV1 Admit(string text, AiExternalContentSourceV1 source)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(source)) throw new ArgumentException("Unknown external content source.", nameof(source));
        return new AiQuarantinedTextV1(text, source, Screen(text));
    }

    /// <summary>The finding kinds in a stable order; the matched text is not returned.</summary>
    public static IReadOnlyList<AiQuarantineFindingV1> Screen(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var findings = new List<AiQuarantineFindingV1>();
        if (InstructionPattern().IsMatch(text)) findings.Add(AiQuarantineFindingV1.InstructionLikeText);
        if (ToolCallPattern().IsMatch(text)) findings.Add(AiQuarantineFindingV1.ToolCallLikeSyntax);
        if (RolePattern().IsMatch(text)) findings.Add(AiQuarantineFindingV1.RoleOrPromptMarker);
        if (InvisiblePattern().IsMatch(text)) findings.Add(AiQuarantineFindingV1.InvisibleCharacters);
        if (AuthorityPattern().IsMatch(text)) findings.Add(AiQuarantineFindingV1.AuthorityOrRecipientChange);
        return findings.AsReadOnly();
    }

    /// <summary>
    /// The gate for any model turn whose input held external content: field values may pass, a planned tool call may
    /// not. When the output plans even one call, the whole output is refused (not trimmed), because a model that was
    /// steered into a call may also have been steered in its values.
    /// </summary>
    public static bool AdmitModelOutput(int plannedToolCallCount) => plannedToolCallCount == 0;
}
