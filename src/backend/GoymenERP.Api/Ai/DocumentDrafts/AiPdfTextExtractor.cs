using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace GoymenERP.Api.Ai.DocumentDrafts;

/// <summary>
/// Tier 2: local text extraction from a PDF whose text is selectable (no network, no model, no third-party parser).
/// It walks the content streams (uncompressed or FlateDecode), skips image XObjects, and collects the literal strings
/// of the text-showing operators (Tj, TJ, ', ") with a line break at T*, Td/TD and ET. A PDF whose fonts use a custom
/// encoding, or a scanned PDF, yields little or no text: the caller then falls to tier 3 (OCR). Inflated bytes are
/// bounded so a compression bomb cannot exhaust memory. The output is quarantined external text, never instructions.
/// </summary>
public static partial class AiPdfTextExtractor
{
    private const int CopyBufferBytes = 81920;
    private const int OctalEscapeDigits = 3;

    [GeneratedRegex(@"<<(?<dict>(?:(?!>>\s*stream).){0,4096}?)>>\s*stream\r?\n", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex StreamStartPattern();

    public static bool LooksLikePdf(ReadOnlySpan<byte> content) =>
        content.Length > 5 && content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F' && content[4] == '-';

    public static string Extract(ReadOnlyMemory<byte> content, long maximumExpandedBytes)
    {
        if (!LooksLikePdf(content.Span)) throw new AiDocumentUnreadableException("pdf_invalid");
        // Latin-1 maps every byte to one char, so offsets in the string equal offsets in the bytes.
        var raw = Encoding.Latin1.GetString(content.Span);
        var text = new StringBuilder();
        long expanded = 0;
        foreach (Match match in StreamStartPattern().Matches(raw))
        {
            var dictionary = match.Groups["dict"].Value;
            var start = match.Index + match.Length;
            var end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) break;
            if (dictionary.Contains("/Image", StringComparison.Ordinal) || dictionary.Contains("/FontFile", StringComparison.Ordinal)) continue;
            var body = content.Slice(start, end - start);
            byte[] decoded;
            if (dictionary.Contains("/FlateDecode", StringComparison.Ordinal))
            {
                decoded = Inflate(body, maximumExpandedBytes - expanded);
            }
            else if (dictionary.Contains("/Filter", StringComparison.Ordinal))
            {
                continue; // another filter (DCT, LZW, ...): not text this tier reads
            }
            else
            {
                decoded = body.ToArray();
            }

            expanded += decoded.Length;
            if (expanded > maximumExpandedBytes) throw new AiDocumentUnreadableException("pdf_expanded_limit");
            ReadTextOperators(Encoding.Latin1.GetString(decoded), text);
        }

        return text.ToString();
    }

    private static byte[] Inflate(ReadOnlyMemory<byte> body, long budget)
    {
        try
        {
            using var input = new ZLibStream(new MemoryStream(body.ToArray(), writable: false), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[CopyBufferBytes];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > budget) throw new AiDocumentUnreadableException("pdf_expanded_limit");
                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    private static void ReadTextOperators(string content, StringBuilder text)
    {
        var pending = new List<string>();
        var index = 0;
        while (index < content.Length)
        {
            var character = content[index];
            if (character == '(')
            {
                pending.Add(ReadLiteral(content, ref index));
                continue;
            }

            if (char.IsLetter(character) || character is '\'' or '"' or '*')
            {
                var start = index;
                while (index < content.Length && (char.IsLetter(content[index]) || content[index] is '\'' or '"' or '*')) index++;
                var op = content[start..index];
                switch (op)
                {
                    case "Tj" or "TJ":
                        text.Append(string.Concat(pending));
                        break;
                    case "'" or "\"":
                        text.Append('\n').Append(string.Concat(pending));
                        break;
                    case "T*" or "Td" or "TD" or "ET":
                        if (text.Length > 0 && text[^1] != '\n') text.Append('\n');
                        break;
                }

                pending.Clear();
                continue;
            }

            index++;
        }
    }

    private static string ReadLiteral(string content, ref int index)
    {
        var value = new StringBuilder();
        var depth = 0;
        index++; // '('
        while (index < content.Length)
        {
            var character = content[index++];
            if (character == '\\' && index < content.Length)
            {
                var escaped = content[index++];
                switch (escaped)
                {
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'b' or 'f': break;
                    case >= '0' and <= '7':
                        var octal = escaped - '0';
                        for (var digit = 1; digit < OctalEscapeDigits && index < content.Length && content[index] is >= '0' and <= '7'; digit++)
                            octal = octal * 8 + (content[index++] - '0');
                        value.Append((char)(octal & 0xFF));
                        break;
                    case '\r' or '\n': break; // line continuation
                    default: value.Append(escaped); break;
                }
            }
            else if (character == '(') { depth++; value.Append(character); }
            else if (character == ')')
            {
                if (depth == 0) break;
                depth--; value.Append(character);
            }
            else value.Append(character);
        }

        return value.ToString();
    }
}
