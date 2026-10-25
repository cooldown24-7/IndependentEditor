using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace IndependentDev;

internal sealed record Texture(int Width, int Height, byte[] Rgba)
{
    public bool MinLinear {get;init;}
    public bool MagLinear {get;init;}
    public bool RepeatX {get;init;}
    public bool RepeatY {get;init;}
    public bool Pma {get;init;}
    public bool Mipmapped {get;init;}
    public bool MipLinear {get;init;}
    [System.Text.Json.Serialization.JsonIgnore] public int EncodedBytes {get;init;}
    [System.Text.Json.Serialization.JsonIgnore] public string SourceDigest {get;init;}="";
    public const int MaxPixels = 64 * 1024 * 1024;
    public void Validate()
    {
        if (Width <= 0 || Height <= 0 || (long)Width * Height > MaxPixels || Rgba.Length != (long)Width * Height * 4) throw new InvalidDataException("Texture dimensions");
    }
    public static Texture LoadPng(string path,int maxPixels=MaxPixels,int maxBytes=Input.MaxBytes)
    {
        if(maxPixels is <0 or >MaxPixels)throw new ArgumentOutOfRangeException(nameof(maxPixels));
        using var file = File.OpenRead(path);
        var bytes = Input.ReadBytes(file,maxBytes);
        if (!bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10})) throw new InvalidDataException("PNG signature");
        try
        {
        using var stream = new MemoryStream(bytes, false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > maxPixels) throw new InvalidDataException("Texture dimensions");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(frame.PixelWidth * frame.PixelHeight * 4)];
        converted.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i+2]) = (pixels[i+2], pixels[i]);
        return new(frame.PixelWidth, frame.PixelHeight, pixels){EncodedBytes=bytes.Length,SourceDigest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))};
        }
        catch(Exception e)when(e is System.Runtime.InteropServices.COMException or FormatException or NotSupportedException or ArgumentException or InvalidOperationException)
        {throw new InvalidDataException("PNG decode",e);}
    }
    public void SavePng(string path)
    {
        Validate(); Experiment.CheckOutput(path);
        var pixels = Rgba.ToArray();
        for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i+2]) = (pixels[i+2], pixels[i]);
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        BundleSave.InstallNew(path,stream=>encoder.Save(stream));
    }
}

internal sealed record TextureAtlas(Dictionary<string, AtlasRegion> Regions, Dictionary<string, Texture> Pages)
{
    public IEnumerable<AtlasRegion> AllRegions {get;init;}=Regions.Values;
    internal const int MaxEncodedBytes=64*1024*1024;
    public IReadOnlyDictionary<string,string> Sources {get;init;}=new Dictionary<string,string>();
    public string SourcePath {get;init;}="";
    public static TextureAtlas Load(string path)
    {
        byte[] original;using(var file=File.OpenRead(path))original=Input.ReadBytes(file);
        var text = Input.DecodeText(original); var pages = new Dictionary<string, Texture>(StringComparer.Ordinal);
        var sources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{Path.GetFullPath(path),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original))}};
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var root = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var first = true; long pixels = 0;var encoded=0;
        var lines=text.Replace("\r", "").Split('\n');
        for(var lineIndex=0;lineIndex<lines.Length;lineIndex++)
        {
            var line=lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line)) { first = true; continue; }
            if (!first) continue;
            first = false; var name = line.Trim();if(pages.Count>=1024)throw new InvalidDataException("Atlas page count");
            var full = Path.GetFullPath(Path.Combine(directory, name));
            if (Path.IsPathRooted(name) || !full.StartsWith(root, StringComparison.OrdinalIgnoreCase)||name.Split(['\\','/']).Any(component=>component.Length>0&&component is not ("." or "..")&&!Experiment.SafePathComponent(component))) throw new InvalidDataException("Atlas image outside directory");
            for (var current = full; current.Length >= directory.Length; current = Path.GetDirectoryName(current)!)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked atlas image path");
                if (string.Equals(current, directory, StringComparison.OrdinalIgnoreCase)) break;
            }
            var fields=new Dictionary<string,string>(StringComparer.Ordinal);
            for(var header=lineIndex+1;header<lines.Length&&lines[header].Contains(':');header++){var field=lines[header].Split(':',2);if(!fields.TryAdd(field[0].Trim(),field[1].Trim()))throw new InvalidDataException("Duplicate atlas field");}
            var sampling=AtlasReader.Sampling(fields);
            var image = Texture.LoadPng(full,(int)(Texture.MaxPixels-pixels),Math.Min(Input.MaxBytes,MaxEncodedBytes-encoded)) with{MinLinear=sampling.MinLinear,MagLinear=sampling.MagLinear,RepeatX=sampling.RepeatX,RepeatY=sampling.RepeatY,Pma=sampling.Pma,Mipmapped=sampling.Mipmapped,MipLinear=sampling.MipLinear}; pixels += (long)image.Width * image.Height;encoded+=image.EncodedBytes;sources[full]=image.SourceDigest;
            if (pixels > Texture.MaxPixels) throw new InvalidDataException("Atlas total pixels");
            if (!pages.TryAdd(name, image)) throw new InvalidDataException("Duplicate atlas page");
        }
        var records=AtlasReader.Parse(text,pages.ToDictionary(p=>p.Key,p=>(p.Value.Width,p.Value.Height),StringComparer.Ordinal));
        foreach (var region in records)
            if (!pages.TryGetValue(region.Page, out var image) || region.PageWidth != image.Width || region.PageHeight != image.Height) throw new InvalidDataException("Atlas page size mismatch");
        var regions=records.GroupBy(region=>region.Name,StringComparer.Ordinal).Where(group=>group.Count()==1).ToDictionary(group=>group.Key,group=>group.First(),StringComparer.Ordinal);
        return new(regions,pages){AllRegions=records,Sources=sources,SourcePath=Path.GetFullPath(path)};
    }
    public Mesh Geometry(Mesh mesh, JsonObject definition, out string page)
    {
        static double Scale(double value,double size,int original)
        {var result=value*size/original;return double.IsFinite(result)?result:value*(size/original);}
        var name = Scene.Text(definition["path"], Scene.Text(definition["name"], mesh.Name));
        if(!Regions.TryGetValue(name,out var region))
        {if(AllRegions.Any(candidate=>candidate.Name==name))throw new NotSupportedException("Indexed atlas region requires explicit frame selection: "+name);throw new InvalidDataException("Missing atlas region: "+name);}
        page = region.Page;
        if(region.Width==0&&region.Height==0)return mesh with{Triangles=[]};
        if (Scene.Text(mesh.Data["type"],"region") != "region")
        {
            return mesh with { UV = mesh.UV.Select(region.OriginalUV).ToArray() };
        }
        var transform = Affine.Local(Scene.Number(definition["x"]),Scene.Number(definition["y"]),Scene.Number(definition["rotation"]),Scale(Scene.Number(definition["scaleX"],1),Scene.Number(definition["width"]),region.OriginalWidth),Scale(Scene.Number(definition["scaleY"],1),Scene.Number(definition["height"]),region.OriginalHeight),0,0);
        Point[] pixels = [new(0,0),new(1,0),new(1,1),new(0,1)];
        return mesh with { Local = pixels.Select(p => transform.Apply(region.TrimmedLocal(p))).ToArray(), UV = new Point[]{new(0,1),new(1,1),new(1,0),new(0,0)}.Select(region.UV).ToArray() };
    }
}

internal sealed record RenderPart(Vertex[] Vertices, string Page, string Slot = "", string Attachment = "")
{
    public Rgba Tint { get; init; } = Rgba.White;
    public Rgba? DarkTint { get; init; }
    public string Blend { get; init; } = "normal";
}

internal static class TexturedRaster
{
    static Rgba Straight(Texture texture,Rgba color)=>!texture.Pma?color:color.A==0?new():new(Math.Min(1,color.R/color.A),Math.Min(1,color.G/color.A),Math.Min(1,color.B/color.A),color.A);
    internal static Rgba Sample(Texture texture,Point uv,bool linear)=>Straight(texture,SampleStored(texture,uv,linear));
    static Rgba SampleStored(Texture texture,Point uv,bool linear)
    {
        uv.Validate();
        double Normalize(double value,bool repeat)=>repeat?value-Math.Floor(value):Math.Clamp(value,0,1);
        var x=Normalize(uv.X,texture.RepeatX)*texture.Width;var y=Normalize(uv.Y,texture.RepeatY)*texture.Height;
        int Index(int value,int size,bool repeat)=>repeat?(value%size+size)%size:Math.Clamp(value,0,size-1);
        Rgba Pixel(int px,int py){var offset=(Index(py,texture.Height,texture.RepeatY)*texture.Width+Index(px,texture.Width,texture.RepeatX))*4;return new(texture.Rgba[offset]/255d,texture.Rgba[offset+1]/255d,texture.Rgba[offset+2]/255d,texture.Rgba[offset+3]/255d);}
        // A tiny negative UV can round its repeat fraction to1; nearest still belongs to the final texel.
        if(!linear)return Pixel(Math.Min((int)x,texture.Width-1),Math.Min((int)y,texture.Height-1));
        x-=.5;y-=.5;var ix=(int)Math.Floor(x);var iy=(int)Math.Floor(y);var dx=x-ix;var dy=y-iy;
        var a=Pixel(ix,iy);var b=Pixel(ix+1,iy);var c=Pixel(ix,iy+1);var d=Pixel(ix+1,iy+1);
        double Mix(double av,double bv,double cv,double dv)=>(av*(1-dx)+bv*dx)*(1-dy)+(cv*(1-dx)+dv*dx)*dy;
        return new(Mix(a.R,b.R,c.R,d.R),Mix(a.G,b.G,c.G,d.G),Mix(a.B,b.B,c.B,d.B),Mix(a.A,b.A,c.A,d.A));
    }
    internal static Texture[] Mipmaps(Texture source,ref long remainingPixels)
    {
        source.Validate();long needed=0;
        for(int w=source.Width,h=source.Height;w>1||h>1;){w=Math.Max(1,w/2);h=Math.Max(1,h/2);needed+=(long)w*h;}
        if(needed>remainingPixels)throw new InvalidDataException("Mipmap pixel budget");remainingPixels-=needed;
        var levels=new List<Texture>{source};
        while(source.Width>1||source.Height>1)
        {
            var width=Math.Max(1,source.Width/2);var height=Math.Max(1,source.Height/2);var bytes=new byte[checked(width*height*4)];
            for(var y=0;y<height;y++)for(var x=0;x<width;x++)
            {
                var left=(double)x*source.Width/width;var right=(double)(x+1)*source.Width/width;var top=(double)y*source.Height/height;var bottom=(double)(y+1)*source.Height/height;
                for(var channel=0;channel<4;channel++)
                {
                    double sum=0;
                    for(var py=(int)top;py<Math.Ceiling(bottom);py++)for(var px=(int)left;px<Math.Ceiling(right);px++)
                    {
                        var weight=(Math.Min(right,px+1)-Math.Max(left,px))*(Math.Min(bottom,py+1)-Math.Max(top,py));
                        sum+=source.Rgba[(py*source.Width+px)*4+channel]*weight;
                    }
                    bytes[(y*width+x)*4+channel]=(byte)Math.Round(Math.Clamp(sum/((right-left)*(bottom-top)),0,255));
                }
            }
            source=new(width,height,bytes){Pma=source.Pma,RepeatX=source.RepeatX,RepeatY=source.RepeatY};levels.Add(source);
        }
        return levels.ToArray();
    }
    internal static Rgba SampleMip(Texture source,Texture[] levels,Point uv,double lod)
    {
        if(!double.IsFinite(lod))throw new InvalidDataException("Mipmap LOD");lod=Math.Clamp(lod,0,levels.Length-1);
        if(!source.MipLinear)return Straight(source,SampleStored(levels[(int)Math.Ceiling(lod-.5)],uv,source.MinLinear));
        var first=(int)Math.Floor(lod);var a=SampleStored(levels[first],uv,source.MinLinear);var b=SampleStored(levels[Math.Min(first+1,levels.Length-1)],uv,source.MinLinear);
        // Both within-level and between-level filters operate on stored PMA channels before conversion.
        return Straight(source,Rgba.Lerp(a,b,lod-first));
    }
    internal static double Footprint(Vertex a,Vertex b,Vertex c,int width,int height,double area)
    {
        var ab=b.Position-a.Position;var ac=c.Position-a.Position;
        var dx=new Point(Numeric.Derivative(a.UV.X,b.UV.X,c.UV.X,ac.Y,ab.Y,width,area),Numeric.Derivative(a.UV.Y,b.UV.Y,c.UV.Y,ac.Y,ab.Y,height,area));
        var dy=new Point(Numeric.Derivative(a.UV.X,c.UV.X,b.UV.X,ab.X,ac.X,width,area),Numeric.Derivative(a.UV.Y,c.UV.Y,b.UV.Y,ab.X,ac.X,height,area));dx.Validate();dy.Validate();
        var footprint=Math.Max(dx.Length,dy.Length);if(!double.IsFinite(footprint))throw new InvalidDataException("Texture footprint");return footprint;
    }
    internal static RenderPart[] ValidateDrawInput(int width,int height,IEnumerable<RenderPart> parts,Affine worldToPixel)
    {
        worldToPixel.Validate();
        if (width <= 0 || height <= 0 || (long)width * height > 1_000_000) throw new InvalidDataException("Raster dimensions");
        var input=parts.Take(1_000_001).ToArray();if(input.Length>1_000_000)throw new InvalidDataException("Raster triangle limit");long work=0;
        foreach(var part in input)
        {
            if(part.Vertices.Length!=3)throw new InvalidDataException("Textured triangle/page");
            var a=worldToPixel.Apply(part.Vertices[0].Position);var b=worldToPixel.Apply(part.Vertices[1].Position);var c=worldToPixel.Apply(part.Vertices[2].Position);a.Validate();b.Validate();c.Validate();
            var left=Math.Max(Math.Floor(Math.Min(a.X,Math.Min(b.X,c.X))),0);var right=Math.Min(Math.Ceiling(Math.Max(a.X,Math.Max(b.X,c.X))),width-1);
            var top=Math.Max(Math.Floor(Math.Min(a.Y,Math.Min(b.Y,c.Y))),0);var bottom=Math.Min(Math.Ceiling(Math.Max(a.Y,Math.Max(b.Y,c.Y))),height-1);
            if(left>right||top>bottom)continue;
            work+=(long)(right-left+1)*(long)(bottom-top+1);if(work>64_000_000)throw new InvalidDataException("Raster work limit");
        }
        return input;
    }
    internal static bool OwnsEdge(Point from,Point to)=>to.Y>from.Y||to.Y==from.Y&&to.X<from.X;
    public static Texture Draw(int width, int height, IEnumerable<RenderPart> parts, IReadOnlyDictionary<string, Texture> pages, Affine worldToPixel)
    {
        var input=ValidateDrawInput(width,height,parts,worldToPixel);
        var output = new byte[width * height * 4];var mipmaps=new Dictionary<string,Texture[]>(StringComparer.Ordinal);long remainingMipPixels=Texture.MaxPixels;
        foreach (var part in input)
        {
            if (part.Vertices.Length != 3 || !pages.TryGetValue(part.Page, out var texture)) throw new InvalidDataException("Textured triangle/page");
            texture.Validate();
            part.Tint.Validate();part.DarkTint?.Validate();if(part.Blend is not ("normal" or "additive" or "multiply" or "screen"))throw new InvalidDataException("Raster blend");
            var vertices = part.Vertices.Select(v => v with { Position = worldToPixel.Apply(v.Position) }).ToArray();
            foreach (var v in vertices) { v.Position.Validate(); v.UV.Validate(); }
            var left=Math.Max(Math.Floor(vertices.Min(v=>v.Position.X)),0);var right=Math.Min(Math.Ceiling(vertices.Max(v=>v.Position.X)),width-1);
            var top=Math.Max(Math.Floor(vertices.Min(v=>v.Position.Y)),0);var bottom=Math.Min(Math.Ceiling(vertices.Max(v=>v.Position.Y)),height-1);
            if(left>right||top>bottom)continue;
            var area = Point.Cross(vertices[1].Position-vertices[0].Position,vertices[2].Position-vertices[0].Position);
            if (area == 0) continue;
            if (area < 0) { (vertices[1],vertices[2]) = (vertices[2],vertices[1]); area = -area; }
            var a=vertices[0];var b=vertices[1];var c=vertices[2];
            var constantU=a.UV.X==b.UV.X&&a.UV.X==c.UV.X;var constantV=a.UV.Y==b.UV.Y&&a.UV.Y==c.UV.Y;
            var linear=texture.MagLinear;Texture[]? levels=null;double lod=0;
            if(texture.Mipmapped||texture.MinLinear!=texture.MagLinear)
            {
                var footprint=Footprint(a,b,c,texture.Width,texture.Height,area);
                linear=footprint>1?texture.MinLinear:texture.MagLinear;
                if(texture.Mipmapped&&footprint>1)
                {
                    if(!mipmaps.TryGetValue(part.Page,out levels)){levels=Mipmaps(texture,ref remainingMipPixels);mipmaps.Add(part.Page,levels);}
                    lod=Math.Log2(footprint);
                }
            }
            var minX=(int)left;var maxX=(int)right;var minY=(int)top;var maxY=(int)bottom;
            bool Inside(double edge,Point from,Point to,Point p)
            {
                // One origin for both directions prevents rounded shared edges from covering a pixel twice.
                if(from.X>to.X||from.X==to.X&&from.Y>to.Y)edge=-Point.Cross(from-to,p-to);
                return edge>0 || edge==0 && OwnsEdge(from,to);
            }
            for(var y=minY;y<=maxY;y++)for(var x=minX;x<=maxX;x++)
            {
                var p=new Point(x+.5,y+.5);
                var ea=Point.Cross(c.Position-b.Position,p-b.Position);var eb=Point.Cross(a.Position-c.Position,p-c.Position);var ec=Point.Cross(b.Position-a.Position,p-a.Position);
                if(!Inside(ea,b.Position,c.Position,p)||!Inside(eb,c.Position,a.Position,p)||!Inside(ec,a.Position,b.Position,p))continue;
                var uv=a.UV*(ea/area)+b.UV*(eb/area)+c.UV*(ec/area);
                uv=new(constantU?a.UV.X:uv.X,constantV?a.UV.Y:uv.Y);uv.Validate();
                var sample=levels is null?Sample(texture,uv,linear):SampleMip(texture,levels,uv,lod);var target=(y*width+x)*4;
                var alpha=sample.A*part.Tint.A;var previous=output[target+3]/255d;var total=part.Blend=="additive"?Math.Min(1,alpha+previous):alpha+previous*(1-alpha);
                for(var channel=0;channel<3;channel++)
                {
                    var tint=channel==0?part.Tint.R:channel==1?part.Tint.G:part.Tint.B;
                    var texel=channel==0?sample.R:channel==1?sample.G:sample.B;
                    var dark=part.DarkTint is Rgba color?(channel==0?color.R:channel==1?color.G:color.B):0;
                    var src=texel*tint+(1-texel)*dark;var dst=output[target+channel]/255d;
                    var blend=part.Blend switch{"multiply"=>src*dst,"screen"=>src+dst-src*dst,_=>src};
                    var premultiplied=part.Blend=="additive"?Math.Min(1,src*alpha+dst*previous):(1-alpha)*dst*previous+(1-previous)*src*alpha+alpha*previous*blend;
                    output[target+channel]=total==0?(byte)0:(byte)Math.Round(Math.Clamp(premultiplied/total,0,1)*255);
                }
                output[target+3]=(byte)Math.Round(total*255);
            }
        }
        return new(width,height,output);
    }
    public static void Export(Scene scene, TextureAtlas atlas, string output, string skin, string? animation, double time,bool skia=false)
    {
        Experiment.CheckOutput(output);var imagePath=Path.Combine(output,"render.png");var geometryPath=Path.Combine(output,"textured-geometry.json");
        if(File.Exists(imagePath)||Directory.Exists(imagePath)||File.Exists(geometryPath)||Directory.Exists(geometryPath))throw new IOException("Texture export destination exists");
        var parts=Playback.Parts(scene,skin,animation,time,atlas);var points=parts.SelectMany(p=>p.Vertices.Select(v=>v.Position)).ToArray();
        if(points.Length==0)throw new InvalidDataException("No visible triangles");
        var minX=Math.Floor(points.Min(p=>p.X));var maxY=Math.Ceiling(points.Max(p=>p.Y));
        var width=Math.Ceiling(points.Max(p=>p.X))-minX+2;var height=maxY-Math.Floor(points.Min(p=>p.Y))+2;
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<1||height<1||width*height>1_000_000)throw new InvalidDataException("Raster dimensions");
        var worldToPixel=new Affine(1,0,0,-1,1-minX,1+maxY);var image=skia?SkiaRaster.Draw((int)width,(int)height,parts,atlas.Pages,worldToPixel):Draw((int)width,(int)height,parts,atlas.Pages,worldToPixel);
        Directory.CreateDirectory(output);image.SavePng(imagePath);
        // shortcut: files commit separately; add a transaction when exports must succeed or fail as one bundle.
        BundleSave.InstallNew(geometryPath,stream=>JsonSerializer.Serialize(stream,new{scene.Version,skin,animation,time,scale=1,image.Width,image.Height,worldToPixel,parts}));
    }
}
