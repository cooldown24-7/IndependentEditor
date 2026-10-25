using System.Text.Json.Nodes;
namespace IndependentDev;

internal static class Playback
{
    public static Mesh DeformTarget(Scene scene, Mesh mesh) => mesh.DeformKey is string key ? scene.Meshes[key] : mesh;
    public static double[]? Deform(Scene scene, Mesh mesh, string? animation, double time)
    {
        if (animation is null) return null;
        if (!double.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        mesh = DeformTarget(scene,mesh);
        JsonNode? node = Scene.Object(scene.Data["animations"])?[animation];
        if (Scene.Object(node) is null) throw new InvalidDataException("Unknown animation");
        var modern = scene.Version.StartsWith("4.1.");
        foreach (var field in new[] { modern ? "attachments" : "deform", mesh.Skin, mesh.Slot, mesh.Name }) node = Scene.Object(node)?[field];
        if (modern) node = Scene.Object(node)?["deform"];
        var keys = Scene.Array(node);
        if (keys is null || keys.Count == 0) return null;
        var count = mesh.DeformLength;
        ValidateDeformKeys(mesh, keys);
        var index = -1;
        foreach (var k in keys) if (Scene.Number(k!["time"]) <= time) index++;
        var output = new double[count]; if (index < 0) return output;
        var left = keys[index]!; var right = keys[Math.Min(index + 1, keys.Count - 1)]!;
        var ratio = index == keys.Count - 1 ? 0 : Timeline.Fraction(Timeline.NormalizedCurve(left.AsObject()), (time - Scene.Number(left["time"])) / (Scene.Number(right["time"]) - Scene.Number(left["time"])));
        var av = Scene.Array(left["vertices"]); var bv = Scene.Array(right["vertices"]);
        var ao = left["offset"] is null ? 0 : Scene.Integer(left["offset"]); var bo = right["offset"] is null ? 0 : Scene.Integer(right["offset"]);
        for (var i = 0; i < count; i++)
        {
            var a = av is not null && i >= ao && i - ao < av.Count ? Scene.Number(av[i - ao]) : 0;
            var b = bv is not null && i >= bo && i - bo < bv.Count ? Scene.Number(bv[i - bo]) : 0;
            var value = Curves.Interpolate(a,b,ratio);
            if (!double.IsFinite(value)) throw new InvalidDataException("Deform numeric range");
            output[i] = value;
        }
        return output;
    }
    public static void ValidateDeformKeys(Mesh mesh, JsonArray? keys)
    {
        if (keys is null || keys.Count == 0) return;
        var count = mesh.DeformLength;
        double previous = -1;
        foreach (var k in keys)
        {
            var frame = Scene.Object(k) ?? throw new InvalidDataException("Deform frame object required");
            if (frame.Any(p => p.Key is not ("time" or "offset" or "vertices" or "curve" or "c2" or "c3" or "c4"))) throw new NotSupportedException("Deform key schema");
            var at = Scene.Number(frame["time"]);
            if (at < 0 || at <= previous) throw new InvalidDataException("Timeline order");
            previous = at;
            Timeline.Fraction(Timeline.NormalizedCurve(frame), 0);
            var values = Scene.Array(frame["vertices"]); var offset = frame["offset"] is null ? 0 : Scene.Integer(frame["offset"]);
            if (offset < 0 || offset > count || (values?.Count ?? 0) > count - offset) throw new InvalidDataException("Deform range");
            if (values is not null) foreach (var v in values) { if (v is null) throw new InvalidDataException("Null numeric component"); Scene.Number(v); }
        }
    }
    public static List<Vertex[]> Triangles(Scene scene, string skin, string? animation = null, double time = 0)
        => Parts(scene, skin, animation, time).ConvertAll(p => p.Vertices);
    public static List<RenderPart> Parts(Scene scene, string skin, string? animation = null, double time = 0, TextureAtlas? atlas = null)
        =>Parts(scene,skin,out _,animation,time,atlas);
    public static List<RenderPart> Parts(Scene scene,string skin,out Affine[] pose,string? animation=null,double time=0,TextureAtlas? atlas=null)
    {
        if (skin!="default"&&!(scene.Data["skins"] as JsonArray ?? []).Any(s => Scene.Text(s?["name"]) == skin)) throw new InvalidDataException("Unknown skin");
        var active=scene.ActiveBones(skin);pose = scene.Pose(animation, time,skin); var triangles = new List<RenderPart>(); Point[]? mask = null;int[] clipCells=[];long clipWork=0; string? end = null;
        var sourceSkins=new Dictionary<string,JsonNode>(StringComparer.Ordinal);
        void Add(RenderPart part){if(triangles.Count>=1_000_000)throw new InvalidDataException("Output triangle limit");triangles.Add(part);}
        foreach (var slot in Order(scene,animation,time))
        {
            if(!active[slot.Bone]){if(end==slot.Name){mask=null;end=null;}continue;}
            var slotColor=ColorTimeline.Slot(scene,slot,animation,time);var slotTint=slotColor.Light;var darkTint=slotColor.Dark;
            var name=Attachment(scene,slot,animation,time);
            if (name is not null)
            {
                if (!scene.TryMesh(skin,slot.Name,name,out var mesh))
                {
                    if(!scene.HasAttachment(slot.Name,name)||scene.HasSkinAttachment(skin,slot.Name,name)||scene.HasSkinAttachment("default",slot.Name,name))throw new NotSupportedException("Missing or unsupported attachment: " + name);
                    if(end==slot.Name){mask=null;end=null;}
                    continue;
                }
                if (name != mesh.Name) throw new InvalidDataException("Attachment identity");
                if(!sourceSkins.TryGetValue(mesh.Skin,out var skinSource))
                {
                    skinSource=scene.Data["skins"]!.AsArray().Single(s=>Scene.Text(s?["name"])==mesh.Skin)!;
                    sourceSkins.Add(mesh.Skin,skinSource);
                }
                var definition = Scene.Object(skinSource["attachments"]?[slot.Name]?[name]) ?? throw new InvalidDataException("Attachment definition");
                if(Scene.Text(definition["type"]) is "path" or "boundingbox" or "point")
                {if(end==slot.Name){mask=null;end=null;}continue;}
                if (definition["sequence"] is not null) throw new NotSupportedException("Attachment sequence unsupported");
                var tint=slotTint*Rgba.Parse(Scene.Text(definition["color"],"ffffffff"));
                var page = "";
                var geometry = atlas is not null && Scene.Text(mesh.Data["type"]) != "clipping" ? atlas.Geometry(mesh,definition,out page) : mesh;
                if(mesh.Weights.Any(weights=>weights.Any(w=>!active[w.Bone])))throw new InvalidDataException("Attachment uses inactive skin bone");
                var vertices = scene.Vertices(geometry, pose, Deform(scene, mesh, animation, time));
                if (Scene.Text(mesh.Data["type"]) == "clipping")
                {
                    if (mask is not null) throw new NotSupportedException("Nested clipping");
                    mask = vertices.Select(v => v.Position).ToArray();if(mask.Length is <3 or >4096)throw new InvalidDataException("Polygon size");
                    var boundary=new List<Point>();foreach(var point in mask)if(boundary.Count==0||boundary[^1]!=point)boundary.Add(point);
                    if(boundary.Count>1&&boundary[0]==boundary[^1])boundary.RemoveAt(boundary.Count-1);mask=boundary.ToArray();
                    clipCells=Polygons.IsCollinear(mask)?[]:Polygons.Triangulate(mask); end = mesh.EndSlot;
                }
                else
                {
                    if(mask is not null){clipWork+=geometry.Triangles.Length/3L*(clipCells.Length/3);if(clipWork>1_000_000)throw new InvalidDataException("Clipping work limit");}
                    for (var i = 0; i < geometry.Triangles.Length; i += 3)
                    {
                    Vertex[] tri=[vertices[geometry.Triangles[i]],vertices[geometry.Triangles[i+1]],vertices[geometry.Triangles[i+2]]];
                    if (mask is null) Add(new(tri,page,slot.Name,name){Tint=tint,DarkTint=darkTint,Blend=slot.Blend});
                    else
                    {
                        foreach (var piece in Polygons.ClipTriangle(tri, mask,clipCells)) for (var j = 1; j + 1 < piece.Length; j++) Add(new([piece[0], piece[j], piece[j + 1]],page,slot.Name,name){Tint=tint,DarkTint=darkTint,Blend=slot.Blend});
                    }
                    }
                }
            }
            if (end == slot.Name) { mask = null; end = null; }
        }
        return triangles;
    }
    public static string? Attachment(Scene scene,Slot slot,string? animation,double time)
    {
        var channel=animation is null?null:Scene.Object(scene.Data["animations"]?[animation]?["slots"]?[slot.Name]);var name=slot.Attachment;
        if(Scene.Array(channel?["attachment"]) is JsonArray keys)
        {
            double previous=-1;
            foreach(var k in keys)
            {
                if(k is not JsonObject frame||frame.Any(p=>p.Key is not ("time" or "name")))throw new InvalidDataException("Attachment timeline key schema");
                var at=Scene.Number(frame["time"]);if(at<0||at<=previous)throw new InvalidDataException("Attachment timeline order");previous=at;
                var value=frame["name"]?.GetValue<string>();if(value is not null&&!scene.HasAttachment(slot.Name,value))throw new InvalidDataException("Attachment timeline reference");if(at<=time)name=value;
            }
        }
        return name;
    }
    public static Slot[] Order(Scene scene,string? animation,double time)
    {
        if(!double.IsFinite(time)||time<0)throw new ArgumentOutOfRangeException(nameof(time));
        var anim=animation is null?null:Scene.Object(scene.Data["animations"]?[animation]);
        if(anim?["draworder"] is not null&&anim["drawOrder"] is not null)throw new InvalidDataException("Duplicate draw order channel");
        var keys=Scene.Array(anim?["draworder"]??anim?["drawOrder"]);var result=scene.Slots;
        if(keys is null)return result;
        var indices=scene.Slots.Select((slot,index)=>(slot.Name,index)).ToDictionary(p=>p.Name,p=>p.index,StringComparer.Ordinal);
        double previous=-1;JsonObject? selected=null;
        foreach(var node in keys)
        {
            var frame=Scene.Object(node)??throw new InvalidDataException("Draw order frame object");
            if(frame.Any(p=>p.Key is not ("time" or "offsets")))throw new NotSupportedException("Draw order key schema");
            var at=Scene.Number(frame["time"]);if(at<0||at<=previous)throw new InvalidDataException("Draw order timeline order");previous=at;
            var offsets=Scene.Array(frame["offsets"]);var moved=new HashSet<int>();var destinations=new HashSet<int>();
            if(offsets is not null)foreach(var n in offsets)
            {
                var change=Scene.Object(n)??throw new InvalidDataException("Draw order offset object");
                if(change.Any(p=>p.Key is not ("slot" or "offset"))||!indices.TryGetValue(Scene.Text(change["slot"]),out var source))throw new InvalidDataException("Draw order slot reference");
                var delta=change["offset"] is null?0:Scene.Integer(change["offset"]);var target=(long)source+delta;
                if(target<0||target>=scene.Slots.Length||!moved.Add(source)||!destinations.Add((int)target))throw new InvalidDataException("Draw order offset collision/range");
            }
            if(at<=time)selected=frame;
        }
        var changes=Scene.Array(selected?["offsets"]);if(changes is null||changes.Count==0)return result;
        var ordered=new Slot[scene.Slots.Length];var sources=new HashSet<int>();
        foreach(var change in changes){var source=indices[Scene.Text(change!["slot"])];var delta=change["offset"] is null?0:Scene.Integer(change["offset"]);ordered[source+delta]=scene.Slots[source];sources.Add(source);}
        var remaining=new Queue<Slot>(scene.Slots.Where((_,i)=>!sources.Contains(i)));
        for(var i=0;i<ordered.Length;i++)ordered[i]??=remaining.Dequeue();
        return ordered;
    }
}

