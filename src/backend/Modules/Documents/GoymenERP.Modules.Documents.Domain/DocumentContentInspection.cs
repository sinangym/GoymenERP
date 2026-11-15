using System.Buffers.Binary;
using System.Text;

namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// B-26 / FNS-D52: the structural content checks of the bank-statement import kinds, which have no single magic number
/// the way PDF, PNG and JPEG do. Every check is a pure, bounded pass over the bytes the client sent (already inside the
/// configured upload bound): nothing is decompressed, executed, parsed into a workbook or decoded into business rows. A
/// check only says whether the bytes are plausibly the named kind and carry none of the active parts the vault refuses;
/// reading the statement is the importing module's job, and the malware scanner still runs on every upload afterwards.
/// </summary>
internal static class DocumentContentInspection
{
    // ZIP (PKWARE APPNOTE 6.3.x) record signatures and fixed lengths.
    private const uint LocalFileHeaderSignature = 0x04034B50;
    private const uint CentralDirectoryHeaderSignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const int LocalFileHeaderLength = 30;
    private const int CentralDirectoryHeaderLength = 46;
    private const int EndOfCentralDirectoryLength = 22;
    private const int MaximumArchiveCommentLength = ushort.MaxValue;

    // General purpose bit 0: the entry is encrypted; an encrypted workbook cannot be checked or scanned.
    private const ushort EncryptedEntryFlag = 0x0001;

    // A ZIP64 archive marks its real sizes/offsets with these; a statement workbook never needs ZIP64 inside the bound.
    private const ushort Zip64Marker16 = ushort.MaxValue;
    private const uint Zip64Marker32 = uint.MaxValue;

    private const byte LowestPrintableAscii = 0x20;
    private const byte HighestPrintableAscii = 0x7E;
    private const byte Delete = 0x7F;
    private const byte Tab = 0x09;
    private const byte LineFeed = 0x0A;
    private const byte CarriageReturn = 0x0D;
    private const byte Space = 0x20;
    private const byte Quote = 0x22;

    private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>The two parts every SpreadsheetML package has (ECMA-376 Part 2 and Part 1 §12.3.23).</summary>
    private const string ContentTypesPart = "[content_types].xml";

    private const string WorkbookPart = "xl/workbook.xml";

    /// <summary>
    /// Parts that make a workbook active or a carrier: VBA projects and their signatures, Excel 4.0 macro sheets, ActiveX
    /// controls, embedded OLE objects/packages (which can carry executables) and external links (DDE/remote workbooks).
    /// A bank statement needs none of them; a package naming any of them is refused, whatever its extension says.
    /// </summary>
    private static readonly string[] ActivePartMarkers = ["vbaproject", "/macrosheets/", "/activex/", "/embeddings/", "/externallinks/"];

    /// <summary>The field delimiters a delimited statement table may use (comma, semicolon — common in Turkish locales — and tab).</summary>
    private static readonly byte[] TableDelimiters = [(byte)',', (byte)';', Tab];

    private const int MinimumTableFields = 2;

    /// <summary>SWIFT block 1 (basic header) of an MT940 message carried with its envelope.</summary>
    private static readonly byte[] SwiftBasicHeader = "{1:"u8.ToArray();

    /// <summary>SWIFT block 4 (text block) opening; the statement tags follow it on their own lines.</summary>
    private static readonly byte[] SwiftTextBlock = "{4:"u8.ToArray();

    private static readonly byte[] TransactionReferenceTag = ":20:"u8.ToArray();
    private static readonly byte[] AccountIdentificationTag = ":25:"u8.ToArray();
    private static readonly byte[] StatementNumberTag = ":28C:"u8.ToArray();
    private static readonly byte[] FinalOpeningBalanceTag = ":60F:"u8.ToArray();
    private static readonly byte[] IntermediateOpeningBalanceTag = ":60M:"u8.ToArray();
    private static readonly byte[] FinalClosingBalanceTag = ":62F:"u8.ToArray();
    private static readonly byte[] IntermediateClosingBalanceTag = ":62M:"u8.ToArray();

    /// <summary>
    /// An Office Open XML workbook the vault accepts: a single-disk, non-ZIP64 ZIP whose central directory lies inside the
    /// bytes, whose every entry is unencrypted with a safe relative part name (no drive, root, back-slash or "..", no
    /// duplicates), which has the package's content-types part and the workbook part, and which names no active part
    /// (<see cref="ActivePartMarkers"/>). Nothing is inflated, so the check is bounded by the directory size alone.
    /// </summary>
    public static bool IsPassiveWorkbook(ReadOnlySpan<byte> content)
    {
        if (content.Length < LocalFileHeaderLength + CentralDirectoryHeaderLength + EndOfCentralDirectoryLength
            || BinaryPrimitives.ReadUInt32LittleEndian(content) != LocalFileHeaderSignature)
        {
            return false;
        }

        var end = FindEndOfCentralDirectory(content);
        if (end < 0)
        {
            return false;
        }

        var diskNumber = ReadUInt16(content, end + 4);
        var directoryDisk = ReadUInt16(content, end + 6);
        var entriesOnDisk = ReadUInt16(content, end + 8);
        var entries = ReadUInt16(content, end + 10);
        var directorySize = ReadUInt32(content, end + 12);
        var directoryOffset = ReadUInt32(content, end + 16);
        if (diskNumber != 0 || directoryDisk != 0 || entriesOnDisk != entries || entries == 0 || entries == Zip64Marker16
            || directorySize == Zip64Marker32 || directoryOffset == Zip64Marker32 || (long)directoryOffset + directorySize > end)
        {
            return false;
        }

        var position = (int)directoryOffset;
        var directoryEnd = position + (int)directorySize;
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries; index++)
        {
            if (directoryEnd - position < CentralDirectoryHeaderLength || ReadUInt32(content, position) != CentralDirectoryHeaderSignature)
            {
                return false;
            }

            var flags = ReadUInt16(content, position + 8);
            var compressedSize = ReadUInt32(content, position + 20);
            var uncompressedSize = ReadUInt32(content, position + 24);
            var nameLength = ReadUInt16(content, position + 28);
            var extraLength = ReadUInt16(content, position + 30);
            var commentLength = ReadUInt16(content, position + 32);
            var localHeaderOffset = ReadUInt32(content, position + 42);
            var recordLength = CentralDirectoryHeaderLength + nameLength + extraLength + commentLength;
            if ((flags & EncryptedEntryFlag) != 0 || compressedSize == Zip64Marker32 || uncompressedSize == Zip64Marker32
                || localHeaderOffset == Zip64Marker32 || nameLength == 0 || directoryEnd - position < recordLength
                || (long)localHeaderOffset + LocalFileHeaderLength > directoryOffset
                || ReadUInt32(content, (int)localHeaderOffset) != LocalFileHeaderSignature)
            {
                return false;
            }

            var name = PartName(content.Slice(position + CentralDirectoryHeaderLength, nameLength));
            if (name is null || !names.Add(name) || ActivePartMarkers.Any(marker => ("/" + name).Contains(marker, StringComparison.Ordinal)))
            {
                return false;
            }

            position += recordLength;
        }

        return position == directoryEnd && names.Contains(ContentTypesPart) && names.Contains(WorkbookPart);
    }

    /// <summary>
    /// A SWIFT MT940 customer statement as plain text (<see cref="IsPlainText"/>): it opens with the SWIFT basic header
    /// block or directly with the transaction reference tag, and carries, each at the start of a line and in this order,
    /// the transaction reference (:20:), account (:25:), statement number (:28C:), opening balance (:60F:/:60M:) and
    /// closing balance (:62F:/:62M:) tags. The tag contents are the importing module's to read.
    /// </summary>
    public static bool IsMt940Statement(ReadOnlySpan<byte> content)
    {
        if (!IsPlainText(content))
        {
            return false;
        }

        var text = SkipLeadingBlank(WithoutByteOrderMark(content));
        if (!text.StartsWith(SwiftBasicHeader) && !text.StartsWith(TransactionReferenceTag))
        {
            return false;
        }

        var reference = FirstTagAtLineStart(text, TransactionReferenceTag);
        var account = FirstTagAtLineStart(text, AccountIdentificationTag);
        var number = FirstTagAtLineStart(text, StatementNumberTag);
        var opening = Earliest(FirstTagAtLineStart(text, FinalOpeningBalanceTag), FirstTagAtLineStart(text, IntermediateOpeningBalanceTag));
        var closing = Earliest(FirstTagAtLineStart(text, FinalClosingBalanceTag), FirstTagAtLineStart(text, IntermediateClosingBalanceTag));
        return reference >= 0 && reference < account && account < number && number < opening && opening < closing;
    }

    /// <summary>
    /// A delimited text table as plain text (<see cref="IsPlainText"/>): not markup (it does not open with "&lt;", so an
    /// HTML or XML page renamed .csv is refused), its first non-blank line splits into at least two fields by one of
    /// <see cref="TableDelimiters"/>, and its double quotes are balanced (RFC 4180 doubles an escaped quote). The column
    /// meaning is the per-bank mapping the importing side keeps; the vault never interprets a cell.
    /// </summary>
    public static bool IsDelimitedTable(ReadOnlySpan<byte> content)
    {
        if (!IsPlainText(content))
        {
            return false;
        }

        var text = SkipLeadingBlank(WithoutByteOrderMark(content));
        if (text.IsEmpty || text[0] == (byte)'<' || content.Count(Quote) % 2 != 0)
        {
            return false;
        }

        var lineEnd = text.IndexOfAny(LineFeed, CarriageReturn);
        var firstLine = lineEnd < 0 ? text : text[..lineEnd];
        foreach (var delimiter in TableDelimiters)
        {
            if (FieldsOutsideQuotes(firstLine, delimiter) >= MinimumTableFields)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Text a person or a bank system wrote, in an 8-bit or UTF-8 encoding: an optional UTF-8 byte-order mark, then no
    /// NUL, no C0 control other than tab/line feed/carriage return, and no DEL. Bytes from 0x80 up are allowed (UTF-8 and
    /// the legacy Turkish code pages banks still export). A UTF-16 file, a binary renamed as text and a file carrying
    /// terminal or form-control bytes are refused.
    /// </summary>
    private static bool IsPlainText(ReadOnlySpan<byte> content)
    {
        foreach (var value in WithoutByteOrderMark(content))
        {
            if (value is Tab or LineFeed or CarriageReturn)
            {
                continue;
            }

            if (value < LowestPrintableAscii || value == Delete)
            {
                return false;
            }
        }

        return true;
    }

    private static ReadOnlySpan<byte> WithoutByteOrderMark(ReadOnlySpan<byte> content) =>
        content.StartsWith(Utf8ByteOrderMark) ? content[Utf8ByteOrderMark.Length..] : content;

    private static ReadOnlySpan<byte> SkipLeadingBlank(ReadOnlySpan<byte> text)
    {
        var index = 0;
        while (index < text.Length && text[index] is Space or Tab or LineFeed or CarriageReturn)
        {
            index++;
        }

        return text[index..];
    }

    /// <summary>The offset of the first <paramref name="tag"/> that starts a line (or follows the "{4:" opening), or -1.</summary>
    private static int FirstTagAtLineStart(ReadOnlySpan<byte> text, ReadOnlySpan<byte> tag)
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var found = text[offset..].IndexOf(tag);
            if (found < 0)
            {
                return -1;
            }

            var at = offset + found;
            if (at == 0 || text[at - 1] == LineFeed
                || (at >= SwiftTextBlock.Length && text.Slice(at - SwiftTextBlock.Length, SwiftTextBlock.Length).SequenceEqual(SwiftTextBlock)))
            {
                return at;
            }

            offset = at + 1;
        }

        return -1;
    }

    private static int Earliest(int first, int second) => first < 0 ? second : second < 0 ? first : Math.Min(first, second);

    private static int FieldsOutsideQuotes(ReadOnlySpan<byte> line, byte delimiter)
    {
        var fields = 1;
        var quoted = false;
        foreach (var value in line)
        {
            if (value == Quote)
            {
                quoted = !quoted;
            }
            else if (value == delimiter && !quoted)
            {
                fields++;
            }
        }

        return fields;
    }

    /// <summary>
    /// The end-of-central-directory record's offset: searched backwards over the longest possible archive comment, and
    /// only where the record's own comment length reaches the exact end of the bytes. -1 when there is none.
    /// </summary>
    private static int FindEndOfCentralDirectory(ReadOnlySpan<byte> content)
    {
        var lowest = Math.Max(0, content.Length - EndOfCentralDirectoryLength - MaximumArchiveCommentLength);
        for (var offset = content.Length - EndOfCentralDirectoryLength; offset >= lowest; offset--)
        {
            if (ReadUInt32(content, offset) == EndOfCentralDirectorySignature
                && offset + EndOfCentralDirectoryLength + ReadUInt16(content, offset + 20) == content.Length)
            {
                return offset;
            }
        }

        return -1;
    }

    /// <summary>
    /// A ZIP entry name as a lower-case OPC part name, or null when it is unsafe: only printable ASCII (an OPC part name
    /// percent-encodes everything else), no back-slash, no root or drive, no empty, "." or ".." segment (a trailing "/"
    /// directory entry is allowed).
    /// </summary>
    private static string? PartName(ReadOnlySpan<byte> raw)
    {
        foreach (var value in raw)
        {
            if (value < LowestPrintableAscii || value > HighestPrintableAscii || value == (byte)'\\' || value == (byte)':')
            {
                return null;
            }
        }

        var name = Encoding.ASCII.GetString(raw).ToLowerInvariant();
        var segments = name.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var isTrailingDirectoryMarker = index == segments.Length - 1 && index > 0 && segment.Length == 0;
            if ((segment.Length == 0 && !isTrailingDirectoryMarker) || segment is "." or "..")
            {
                return null;
            }
        }

        return name;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> content, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(content[offset..]);

    private static uint ReadUInt32(ReadOnlySpan<byte> content, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(content[offset..]);
}
