using System.IO.Abstractions;
using System.Text;

// ReSharper disable EnforceForeachStatementBraces

namespace Grillisoft.Tools.DatabaseDeploy;

/// <summary>
/// The one encoding the files of a deployment folder are allowed to be in: UTF-8 without BOM, which
/// is what deployment reads them back as and what <c>dbdeploy format</c> writes them as. A BOM
/// announces itself, but a script saved as Latin1 does not: nothing on disk gives it away, and it
/// would only turn into mojibake once deployed.
/// </summary>
internal static class ScriptEncoding
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    // ReSharper disable once InconsistentNaming
    private static readonly byte[][] BOMs =
    [
        [0x2b, 0x2f, 0x76],       //UTF-7
        [0xef, 0xbb, 0xbf],       //UTF-8
        [0xff, 0xfe],             //UTF-16 LE, and the UTF-32 LE that starts with it
        [0xfe, 0xff],             //UTF-16 BE
        [0x00, 0x00, 0xfe, 0xff]  //UTF-32 BE
    ];

    /// <summary>
    /// Whether an encoding is the one above, whichever instance it was built from.
    /// </summary>
    public static bool IsUtf8NoBom(this Encoding encoding) =>
        encoding.CodePage == Utf8NoBom.CodePage && encoding.GetPreamble().Length == 0;

    /// <summary>
    /// What is wrong with the encoding of a file, or <c>null</c> when there is nothing to report.
    /// </summary>
    public static async Task<string?> Validate(IFileInfo file)
    {
        var buffer = new byte[8192];
        var chars = new char[Utf8NoBom.GetMaxCharCount(buffer.Length)];

        // Decoding rather than sniffing: the decoder throws on anything that is not UTF-8, and it
        // carries its state across the chunks so a character split over two reads still decodes.
        var decoder = new UTF8Encoding(false, true).GetDecoder();
        var first = true;

        await using var stream = file.OpenRead();

        int count;
        while ((count = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            var read = buffer.AsSpan(0, count);

            if (first && HasBOM(read))
                return $"BOM detected on file {file.FullName}. Convert the file to UTF8 without BOM";

            first = false;

            // A UTF-16 or UTF-32 file that lost its BOM decodes as valid UTF-8 whenever its content
            // is ASCII, since every other byte is then a NUL. Nothing else puts a NUL in a script.
            if (read.Contains<byte>(0x00))
                return NotUtf8(file, "it contains NUL bytes, so it is probably UTF-16 or UTF-32 without BOM");

            try
            {
                decoder.GetChars(buffer, 0, count, chars, 0, flush: false);
            }
            catch (DecoderFallbackException)
            {
                return NotUtf8(file, "it contains an invalid byte sequence");
            }
        }

        try
        {
            //flushing catches a multi byte character the file ends in the middle of
            decoder.GetChars([], 0, 0, chars, 0, flush: true);
        }
        catch (DecoderFallbackException)
        {
            return NotUtf8(file, "it ends in the middle of a character");
        }

        return null;
    }

    private static string NotUtf8(IFileInfo file, string reason) =>
        $"File {file.FullName} is not UTF8: {reason}. Convert the file to UTF8 without BOM";

    // ReSharper disable once InconsistentNaming
    private static bool HasBOM(ReadOnlySpan<byte> bytes)
    {
        foreach (var bom in BOMs)
            if (bytes.StartsWith(bom))
                return true;

        return false;
    }
}
