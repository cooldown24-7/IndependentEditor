using System.Text;
namespace IndependentDev;

internal static class Input
{
    public const int MaxBytes = 16 * 1024 * 1024;
    public static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadText(stream);
    }
    public static byte[] ReadBytes(Stream stream,int maxBytes=MaxBytes)
    {
        if(maxBytes is <0 or >MaxBytes)throw new ArgumentOutOfRangeException(nameof(maxBytes));
        using var bytes = new MemoryStream(); var buffer = new byte[64 * 1024];
        while (bytes.Length < maxBytes)
        {
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, maxBytes - (int)bytes.Length));
            if (read == 0) break;
            bytes.Write(buffer, 0, read);
        }
        if (bytes.Length == maxBytes && stream.ReadByte() != -1) throw new InvalidDataException("Input size limit");
        return bytes.ToArray();
    }
    public static string ReadText(Stream stream)
        =>DecodeText(ReadBytes(stream));
    public static string DecodeText(ReadOnlySpan<byte> data)
    {
        if(data.Length>MaxBytes)throw new InvalidDataException("Input size limit");
        Encoding encoding = new UTF8Encoding(false, true); var skip = 0;
        // UTF32 LE shares the UTF16 LE prefix, so identify four-byte BOMs first.
        if (data.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, false, true); skip = 4; }
        else if (data.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); skip = 4; }
        else if (data.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) skip = 3;
        else if (data.StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (data.StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        return encoding.GetString(data[skip..]);
    }
}
