using System.Globalization;
using System.Text;
namespace IndependentDev;

internal sealed record AtlasRegion(string Name, string Page, int X, int Y, int Width, int Height, int OriginalWidth, int OriginalHeight, int OffsetX, int OffsetY, int Rotation, int PageWidth, int PageHeight,int Index=-1)
{
    static double Coordinate(double value,int size,double offset,int page)
    {
        var mapped=(offset+value*size)/page;
        return double.IsFinite(mapped)?mapped:offset/page+value*(size/(double)page);
    }
    public Point UV(Point p)
    {
        p.Validate();
        var q = Rotation switch { 0 => p, 90 => new Point(p.Y, 1 - p.X), 180 => new Point(1 - p.X, 1 - p.Y), 270 => new Point(1 - p.Y, p.X), _ => throw new InvalidDataException("Rotation") };
        var packedWidth = Rotation is 90 or 270 ? Height : Width; var packedHeight = Rotation is 90 or 270 ? Width : Height;
        var result=new Point(Coordinate(q.X,packedWidth,X,PageWidth),Coordinate(q.Y,packedHeight,Y,PageHeight));result.Validate();return result;
    }
    internal Point OriginalUV(Point p)
    {
        p.Validate();var top=OriginalHeight-OffsetY-Height;
        var trimmed=new Point((p.X*OriginalWidth-OffsetX)/Width,(p.Y*OriginalHeight-top)/Height);
        if(double.IsFinite(trimmed.X)&&double.IsFinite(trimmed.Y))return UV(trimmed);
        // Compose trim and page normalization when the temporary region coordinate overflows.
        var result=Rotation switch
        {
            0=>new Point(Coordinate(p.X,OriginalWidth,(double)X-OffsetX,PageWidth),Coordinate(p.Y,OriginalHeight,(double)Y-top,PageHeight)),
            90=>new Point(Coordinate(p.Y,OriginalHeight,(double)X-top,PageWidth),Coordinate(-p.X,OriginalWidth,(double)Y+Width+OffsetX,PageHeight)),
            180=>new Point(Coordinate(-p.X,OriginalWidth,(double)X+Width+OffsetX,PageWidth),Coordinate(-p.Y,OriginalHeight,(double)Y+Height+top,PageHeight)),
            270=>new Point(Coordinate(-p.Y,OriginalHeight,(double)X+Height+top,PageWidth),Coordinate(p.X,OriginalWidth,(double)Y-OffsetX,PageHeight)),
            _=>throw new InvalidDataException("Rotation")
        };
        result.Validate();return result;
    }
    public Point TrimmedLocal(Point p) => new(OffsetX + p.X * Width - OriginalWidth / 2.0, OffsetY + p.Y * Height - OriginalHeight / 2.0);
}
internal static class AtlasReader
{
    internal static (bool MinLinear,bool MagLinear,bool RepeatX,bool RepeatY,bool Pma,bool Mipmapped,bool MipLinear) Sampling(IReadOnlyDictionary<string,string> fields)
    {
        if(fields.TryGetValue("format",out var format)&&format is not ("Alpha" or "Intensity" or "LuminanceAlpha" or "RGB565" or "RGBA4444" or "RGB888" or "RGBA8888"))throw new InvalidDataException("Atlas format");
        var filter=fields.GetValueOrDefault("filter","Nearest,Nearest").Split(',',StringSplitOptions.TrimEntries);
        if(filter.Length!=2||filter[0] is not ("Nearest" or "Linear" or "MipMapNearestNearest" or "MipMapLinearNearest" or "MipMapNearestLinear" or "MipMapLinearLinear")||filter[1] is not ("Nearest" or "Linear"))throw new NotSupportedException("Atlas filter unsupported; use explicit minification filter and Nearest/Linear magnification");
        var repeat=fields.GetValueOrDefault("repeat","none");if(repeat is not ("none" or "x" or "y" or "xy"))throw new InvalidDataException("Atlas repeat");
        var pma=fields.GetValueOrDefault("pma","false");if(pma is not ("true" or "false"))throw new InvalidDataException("Atlas PMA flag");
        return(filter[0] is "Linear" or "MipMapLinearNearest" or "MipMapLinearLinear",filter[1]=="Linear",repeat.Contains('x'),repeat.Contains('y'),pma=="true",filter[0].StartsWith("MipMap",StringComparison.Ordinal),filter[0] is "MipMapNearestLinear" or "MipMapLinearLinear");
    }
    // Own application metadata contract and public format fields; no official runtime source used.
    public static List<AtlasRegion> Parse(string text, IReadOnlyDictionary<string, (int Width, int Height)>? pageSizes = null)
    {
        if (Encoding.UTF8.GetByteCount(text) > 16 * 1024 * 1024) throw new InvalidDataException("Atlas size");
        var lines = text.Replace("\r", "").Split('\n'); var result = new List<AtlasRegion>();var identities=new HashSet<(string Name,int Index)>(); var i = 0;
        Dictionary<string, string> Fields()
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            while (i < lines.Length && lines[i].Contains(':'))
            {
                var parts = lines[i++].Split(':', 2); if (!fields.TryAdd(parts[0].Trim(), parts[1].Trim())) throw new InvalidDataException("Duplicate atlas field");
            }
            return fields;
        }
        int[] Numbers(Dictionary<string, string> f, string key, int count, int[]? fallback = null)
        {
            if (!f.TryGetValue(key, out var value)) return fallback ?? throw new InvalidDataException("Missing " + key);
            var parts = value.Split(','); if (parts.Length != count) throw new InvalidDataException("Atlas tuple " + key);
            return parts.Select(v => int.Parse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture)).ToArray();
        }
        while (i < lines.Length)
        {
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++; if (i == lines.Length) break;
            var page = lines[i++].Trim(); var pf = Fields();
            Sampling(pf);
            var size = Numbers(pf, "size", 2, pageSizes is not null && pageSizes.TryGetValue(page, out var actual) ? [actual.Width,actual.Height] : null);
            if (page.Length == 0 || size.Any(n => n <= 0) || (long)size[0] * size[1] > 64 * 1024 * 1024) throw new InvalidDataException("Atlas page size");
            if(pageSizes is not null&&pageSizes.TryGetValue(page,out var loaded)&&(size[0]!=loaded.Width||size[1]!=loaded.Height))throw new InvalidDataException("Atlas page size mismatch");
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                var name = lines[i++].Trim(); var f = Fields(); var bounds = f.ContainsKey("bounds") ? Numbers(f, "bounds", 4) : Numbers(f, "xy", 2).Concat(Numbers(f, "size", 2)).ToArray();
                var offsets = f.ContainsKey("offsets") ? Numbers(f, "offsets", 4) : Numbers(f, "offset", 2, [0, 0]).Concat(Numbers(f, "orig", 2, [bounds[2], bounds[3]])).ToArray();
                var rotation = f.GetValueOrDefault("rotate", "false") switch { "true" => 90, "false" => 0, var r => int.Parse(r, CultureInfo.InvariantCulture) };
                if (rotation is not (0 or 90 or 180 or 270) || bounds.Any(n => n < 0) || (bounds[2] == 0) != (bounds[3] == 0) || offsets[2] == 0 || offsets[3] == 0 || offsets.Any(n => n < 0) || offsets[0] + (long)bounds[2] > offsets[2] || offsets[1] + (long)bounds[3] > offsets[3]) throw new InvalidDataException("Atlas bounds/trim");
                var w = rotation is 90 or 270 ? bounds[3] : bounds[2]; var h = rotation is 90 or 270 ? bounds[2] : bounds[3];
                if (bounds[0] + (long)w > size[0] || bounds[1] + (long)h > size[1]) throw new InvalidDataException("Region outside page");
                if(!int.TryParse(f.GetValueOrDefault("index","-1"),NumberStyles.Integer,CultureInfo.InvariantCulture,out var index)||index< -1)throw new InvalidDataException("Atlas region index");
                if(!identities.Add((name,index)))throw new InvalidDataException("Duplicate region");
                result.Add(new(name,page,bounds[0],bounds[1],bounds[2],bounds[3],offsets[2],offsets[3],offsets[0],offsets[1],rotation,size[0],size[1],index));
            }
        }
        return result;
    }
}

internal sealed class BinaryCursor
{
    readonly byte[] data;
    public int Offset { get; private set; }
    public BinaryCursor(byte[] bytes) { if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Binary size"); data = bytes.ToArray(); }
    byte Next() => Offset < data.Length ? data[Offset++] : throw new EndOfStreamException();
    public bool Boolean() => Next() switch{0=>false,1=>true,_=>throw new InvalidDataException("Binary boolean")};
    public ushort Short()
    {Require(2);var value=System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(Offset,2));Offset+=2;return value;}
    public uint Int()
    {Require(4);var value=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(Offset,4));Offset+=4;return value;}
    void Require(int count) {if(count<0||count>data.Length-Offset)throw new EndOfStreamException();}
    public void Skip(int count) {Require(count);Offset+=count;}
    public byte[] Bytes(int count)
    {
        Require(count);
        var bytes=data.AsSpan(Offset,count).ToArray();Offset+=count;return bytes;
    }
    public uint VarUInt()
    {
        uint value = 0;
        for (var shift = 0; shift <= 28; shift += 7)
        {
            var b = Next(); if (shift == 28 && (b & 0xf0) != 0) throw new InvalidDataException("Varint overflow");
            value |= (uint)(b & 127) << shift; if ((b & 128) == 0) return value;
        }
        throw new InvalidDataException("Varint length");
    }
    public int VarInt() { var u = VarUInt(); return (int)(u >> 1) ^ -((int)u & 1); }
    public float Float()
    {
        var f = BitConverter.Int32BitsToSingle(unchecked((int)Int())); return float.IsFinite(f) ? f : throw new InvalidDataException("Nonfinite binary float");
    }
    public string? String(int maxBytes=Input.MaxBytes)
    {
        if(maxBytes is <0 or >Input.MaxBytes)throw new ArgumentOutOfRangeException(nameof(maxBytes));
        var length = VarUInt(); if (length == 0) return null; if (length == 1) return "";
        if(length-1>maxBytes)throw new InvalidDataException("Binary string budget");
        if (length - 1 > data.Length - Offset) throw new EndOfStreamException();
        var s = new UTF8Encoding(false, true).GetString(data, Offset, checked((int)length - 1)); Offset += (int)length - 1; return s;
    }
    public byte[] UnchangedBytes() => data.ToArray();
}

