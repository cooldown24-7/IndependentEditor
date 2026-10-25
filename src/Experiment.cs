using System.Globalization;
using System.Text.Json;
namespace IndependentDev;

internal static class Experiment
{
    internal static bool SafePathComponent(string component)
    {
        if(component.Length==0||component.EndsWith(' ')||component.EndsWith('.')||component.IndexOfAny(Path.GetInvalidFileNameChars())>=0)return false;
        var stem=component.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL")&&!(stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&"123456789¹²³".Contains(stem[3]));
    }
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if((File.Exists(Path.Combine(dir.FullName,".independent-editor-root"))&&File.Exists(Path.Combine(dir.FullName,"src","IndependentDev.csproj")))||(File.Exists(Path.Combine(dir.FullName,"IndependentEditor.exe"))&&File.Exists(Path.Combine(dir.FullName,"fixtures","preview","scene.json"))))return dir.FullName;
        }
        throw new InvalidOperationException("Editor root marker is missing from the project or application folder");
    }
    public static void CheckOutput(string path)
    {
        var prefix=Path.GetPathRoot(path)??"";
        if(path[prefix.Length..].Split(['\\','/']).Any(component=>component.Length>0&&component is not ("." or "..")&&!SafePathComponent(component)))throw new InvalidOperationException("Unsafe output path component");
        path=Path.GetFullPath(path);var root=Path.Combine(Root(),"output");
        if (!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Output outside independent output directory");
        if(Path.GetRelativePath(root,path).Split(['\\','/']).Any(component=>!SafePathComponent(component)))throw new InvalidOperationException("Unsafe output path component");
        for(var current=path;current.Length>=root.Length;current=Path.GetDirectoryName(current)!)
        {
            if ((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new InvalidOperationException("Linked output path");
            if(string.Equals(current,root,StringComparison.OrdinalIgnoreCase))break;
        }
    }
    internal static void CheckSavePath(string path)
    {
        var prefix=Path.GetPathRoot(path)??"";
        if(path[prefix.Length..].Split(['\\','/']).Any(c=>c.Length>0&&c is not ("." or "..")&&!SafePathComponent(c)))throw new InvalidOperationException("Unsafe save path component");
        for(string? current=Path.GetFullPath(path);current is not null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Linked save path");
    }
    public static void Export(Scene scene,string output,string skin,string? animation,double time)
    {
        CheckOutput(output);var svgPath=Path.Combine(output,"preview.svg");var geometryPath=Path.Combine(output,"geometry.json");
        if(File.Exists(svgPath)||Directory.Exists(svgPath)||File.Exists(geometryPath)||Directory.Exists(geometryPath))throw new IOException("Preview export destination exists");
        var triangles=Playback.Triangles(scene,skin,animation,time);var points=triangles.SelectMany(t=>t.Select(v=>v.Position)).ToArray();
        if(points.Length==0) throw new InvalidDataException("No visible triangles");
        var minX=points.Min(p=>p.X)-1;var minY=points.Min(p=>p.Y)-1;var width=points.Max(p=>p.X)-minX+1;var height=points.Max(p=>p.Y)-minY+1;
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)throw new InvalidDataException("Preview bounds");
        string F(double d)=>d.ToString("R",CultureInfo.InvariantCulture);
        var polygons=triangles.Select(t=>"<polygon points=\""+string.Join(" ",t.Select(v=>F(v.Position.X)+","+F(v.Position.Y)))+"\" fill=\"#59a8da\" fill-opacity=\"0.35\" stroke=\"#13567c\" stroke-width=\"0.02\"/>");
        var svg="<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"640\" height=\"480\" viewBox=\""+F(minX)+" "+F(minY)+" "+F(width)+" "+F(height)+"\"><rect x=\""+F(minX)+"\" y=\""+F(minY)+"\" width=\""+F(width)+"\" height=\""+F(height)+"\" fill=\"#fafafa\"/>"+string.Join("",polygons)+"</svg>";
        Directory.CreateDirectory(output);
        BundleSave.InstallNew(svgPath,stream=>{using var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false),1024,true);writer.Write(svg);});
        // shortcut: files commit separately; add a transaction when exports must succeed or fail as one bundle.
        BundleSave.InstallNew(geometryPath,stream=>JsonSerializer.Serialize(stream,new {scene.Version,skin,animation,time,coordinateConvention="source numeric XY; SVG y-down diagnostic",triangles}));
    }
}


