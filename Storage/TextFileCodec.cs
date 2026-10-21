using System.Text;

namespace SodiumStudio.Storage;

public enum LineEnding
{
    Lf,
    CrLf,
    Cr,
}

/// <param name="Text">Decoded text with every line ending normalized to LF.</param>
/// <param name="LineEnding">The dominant line ending of the original file; used again on save.</param>
/// <param name="HasBom">Whether the file started with a UTF-8 byte order mark.</param>
/// <param name="HadMixedLineEndings">True when several line-ending styles were present.</param>
public sealed record DecodedText(string Text, LineEnding LineEnding, bool HasBom, bool HadMixedLineEndings);

/// <summary>
/// Strict UTF-8 text file handling. Files that are not valid UTF-8 (or contain NUL bytes)
/// are refused rather than silently corrupted on save.
/// </summary>
public static class TextFileCodec
{
    /// <summary>Largest file the editor opens in this build.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool TryDecode(byte[] bytes, out DecodedText? result, out string error)
    {
        result = null;

        if (bytes.Length > MaxBytes)
        {
            error = $"The file is larger than {MaxBytes / (1024 * 1024)} MB, which this build cannot open.";
            return false;
        }

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var offset = hasBom ? 3 : 0;

        if (Array.IndexOf(bytes, (byte)0, offset) >= 0)
        {
            error = "The file looks like a binary file (it contains NUL bytes) and was not opened.";
            return false;
        }

        string raw;
        try
        {
            raw = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            error = "The file is not valid UTF-8 text. Other encodings are not supported yet.";
            return false;
        }

        var crlf = 0;
        var lf = 0;
        var cr = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            switch (raw[i])
            {
                case '\r' when i + 1 < raw.Length && raw[i + 1] == '\n':
                    crlf++;
                    i++;
                    break;
                case '\r':
                    cr++;
                    break;
                case '\n':
                    lf++;
                    break;
            }
        }

        var dominant = crlf > lf && crlf >= cr ? LineEnding.CrLf
            : cr > lf && cr > crlf ? LineEnding.Cr
            : LineEnding.Lf;
        var styles = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);

        result = new DecodedText(NormalizeToLf(raw), dominant, hasBom, styles > 1);
        error = string.Empty;
        return true;
    }

    public static byte[] Encode(string text, LineEnding lineEnding, bool withBom)
    {
        var normalized = NormalizeToLf(text);
        var converted = lineEnding switch
        {
            LineEnding.CrLf => normalized.Replace("\n", "\r\n", StringComparison.Ordinal),
            LineEnding.Cr => normalized.Replace("\n", "\r", StringComparison.Ordinal),
            _ => normalized,
        };

        var body = Encoding.UTF8.GetBytes(converted);
        if (!withBom)
        {
            return body;
        }

        var result = new byte[body.Length + 3];
        result[0] = 0xEF;
        result[1] = 0xBB;
        result[2] = 0xBF;
        Buffer.BlockCopy(body, 0, result, 3, body.Length);
        return result;
    }

    public static string DisplayName(LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.CrLf => "CRLF",
        LineEnding.Cr => "CR",
        _ => "LF",
    };

    private static string NormalizeToLf(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
