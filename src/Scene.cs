using System.Text.Json;
using System.Text.Json.Nodes;
namespace IndependentDev;

internal sealed record Joint(string Name, int Parent, double X, double Y, double Rotation, double ScaleX, double ScaleY, double ShearX, double ShearY, string Transform, double Length = 0,bool SkinRequired=false);
internal sealed record Slot(string Name, int Bone, string? Attachment, string Blend,JsonObject Data);
internal sealed record Influence(int Bone, Point Local, double Weight);
internal sealed record Mesh(string Skin, string Slot, string Name, JsonObject Data, Point[] UV, int[] Triangles, Point[] Local, Influence[][] Weights, string? EndSlot, string? DeformKey = null)
{
    internal int DeformLength {get;}=Weights.Length==0?Local.Length*2:Weights.Sum(w=>w.Length)*2;
}

internal sealed class Scene
{
    public JsonObject Data { get; }
    public Joint[] Bones { get; }
    public Slot[] Slots { get; }
    public Dictionary<string, Mesh> Meshes { get; } = new(StringComparer.Ordinal);
    readonly Dictionary<string,int> slotBones;
    public string Version { get; }
    readonly HashSet<(string Slot,string Name)> attachmentNames=[];
    readonly HashSet<string> attachmentKeys=new(StringComparer.Ordinal);
    public bool HasAttachment(string slot,string name)=>attachmentNames.Contains((slot,name));
    internal bool HasSkinAttachment(string skin,string slot,string name)=>attachmentKeys.Contains(Key(skin,slot,name));
    public bool TryMesh(string skin,string slot,string name,out Mesh mesh)=>Meshes.TryGetValue(Key(skin,slot,name),out mesh!)||Meshes.TryGetValue(Key("default",slot,name),out mesh!);
    Scene(Scene source)
    {
        Data=source.Data;Version=source.Version;Bones=source.Bones;Slots=source.Slots;slotBones=source.slotBones;attachmentNames=source.attachmentNames;attachmentKeys=source.attachmentKeys;Meshes=new(source.Meshes,StringComparer.Ordinal);
    }
    internal Scene PreviewMeshVertices(string key,double[] vertices)
    {
        var mesh=Meshes[key];if(Text(mesh.Data["type"])!="mesh"||vertices.Length!=(Array(mesh.Data["vertices"])?.Count??0)||vertices.Any(value=>!double.IsFinite(value)))throw new InvalidDataException("Preview mesh coordinates");
        var attachment=(JsonObject)mesh.Data.DeepClone();
        attachment["vertices"]=new JsonArray(vertices.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray());
        var data=new JsonObject{["skeleton"]=new JsonObject{["spine"]=Version},["bones"]=Data["bones"]!.DeepClone(),["slots"]=new JsonArray(new JsonObject{["name"]=mesh.Slot,["bone"]=Bones[slotBones[mesh.Slot]].Name,["attachment"]=mesh.Name}),["skins"]=new JsonArray(new JsonObject{["name"]=mesh.Skin,["attachments"]=new JsonObject{[mesh.Slot]=new JsonObject{[mesh.Name]=attachment}}})};
        var parsed=new Scene(data).Meshes[key];var next=new Scene(this);
        // A drag view shares immutable source JSON; only derived geometry changes. The editor commits through binary validation.
        foreach(var item in Meshes)if(ReferenceEquals(item.Value.Data,mesh.Data))next.Meshes[item.Key]=item.Value with{Data=parsed.Data,Local=parsed.Local,Weights=parsed.Weights};return next;
    }
    public static double Number(JsonNode? n, double fallback = 0) => n is null ? fallback : Finite(n.GetValue<double>());
    static double Finite(double n) => double.IsFinite(n) ? n : throw new InvalidDataException("Nonfinite number");
    public static string Text(JsonNode? n, string fallback = "") => n?.GetValue<string>() ?? fallback;
    public static JsonObject? Object(JsonNode? n) => n is null ? null : n as JsonObject ?? throw new InvalidDataException("Object required");
    public static JsonArray? Array(JsonNode? n) => n is null ? null : n as JsonArray ?? throw new InvalidDataException("Array required");
    public static string Key(string skin, string slot, string attachment) => skin + "\0" + slot + "\0" + attachment;
    public static Scene Parse(string text)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 16 * 1024 * 1024) throw new InvalidDataException("Input size limit");
        using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
        var numericCount = 0;var valueCount=0;
        static bool NonzeroSignificand(JsonElement number)
        {
            foreach(var c in number.GetRawText()){if(c is 'e' or 'E')break;if(c is >= '1' and <= '9')return true;}return false;
        }
        void Check(JsonElement e)
        {
            if(++valueCount>1_100_000)throw new InvalidDataException("JSON value count limit");
            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate property"); Check(p.Value); }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Check(v);
            else if (e.ValueKind == JsonValueKind.Number)
            {
                if (++numericCount > 1_000_000 || !e.TryGetDouble(out var d) || !double.IsFinite(d)) throw new InvalidDataException("Numeric limit");
                if(d==0&&!e.TryGetInt64(out _)&&NonzeroSignificand(e))throw new InvalidDataException("Numeric underflow");
            }
        }
        Check(json.RootElement);
        return new Scene(JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("Object required"));
    }
    Scene(JsonObject data)
    {
        Data = data; Version = Text(data["skeleton"]?["spine"]);
        foreach(var field in new[]{"bones","slots","skins"}) if(data[field] is not null && data[field] is not JsonArray)throw new InvalidDataException("Array required: "+field);
        if(data["animations"] is not null && data["animations"] is not JsonObject)throw new InvalidDataException("Animations object");
        var profile=Version.StartsWith("3.8.",StringComparison.Ordinal)?"3.8.":Version.StartsWith("4.1.",StringComparison.Ordinal)?"4.1.":null;
        if(profile is null||Version.Length<=profile.Length||Version.Length>32||!Version[profile.Length..].All(char.IsAsciiDigit))throw new NotSupportedException("Only experimental 3.8/4.1 JSON profiles"+(Version.Length==0?"":": "+Version[..Math.Min(Version.Length,64)]));
        var bones = data["bones"] as JsonArray ?? throw new InvalidDataException("Bones required");
        if (bones.Count is < 1 or > 4096) throw new InvalidDataException("Bone count");
        var names = new Dictionary<string, int>(StringComparer.Ordinal); var joints = new List<Joint>();
        foreach (var node in bones)
        {
            var b = node as JsonObject ?? throw new InvalidDataException("Bone object"); var name = Text(b["name"]);
            if (name.Length == 0 || name.Contains('\0') || names.ContainsKey(name)) throw new InvalidDataException("Duplicate/empty bone");
            var parent = -1;
            if (b["parent"] is not null && !names.TryGetValue(Text(b["parent"]), out parent)) throw new InvalidDataException("Parent must precede child");
            var length=Number(b["length"]);if(length<0)throw new InvalidDataException("Bone length");
            var transform=Text(b["transform"],"normal");if(transform is not ("normal" or "onlyTranslation" or "noScale" or "noScaleOrReflection" or "noRotationOrReflection"))throw new NotSupportedException("Bone inheritance "+transform);
            joints.Add(new(name, parent, Number(b["x"]), Number(b["y"]), Number(b["rotation"]), Number(b["scaleX"], 1), Number(b["scaleY"], 1), Number(b["shearX"]), Number(b["shearY"]), transform,length,b["skin"]?.GetValue<bool>()??false));
            names.Add(name, joints.Count - 1);
        }
        Bones = joints.ToArray(); var slots = new List<Slot>(); var slotNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in data["slots"] as JsonArray ?? [])
        {
            if (slots.Count >= 8192 || n is not JsonObject s || !slotNames.Add(Text(s["name"])) || Text(s["name"]).Length == 0 || Text(s["name"]).Contains('\0') || !names.TryGetValue(Text(s["bone"]), out var bone)) throw new InvalidDataException("Invalid slot");
            if(s["color"] is not null)Rgba.Parse(Text(s["color"]));
            if(s["dark"] is not null){var dark=Text(s["dark"]);if(dark.Length!=6)throw new InvalidDataException("Slot dark RGB");Rgba.Parse(dark);}
            var blend = Text(s["blend"], "normal");
            if (blend is not ("normal" or "additive" or "multiply" or "screen")) throw new InvalidDataException("Unknown blend");
            slots.Add(new(Text(s["name"]), bone, s["attachment"]?.GetValue<string>(), blend,s));
        }
        Slots = slots.ToArray();
        slotBones=Slots.ToDictionary(s=>s.Name,s=>s.Bone,StringComparer.Ordinal);
        var definitions = new Dictionary<string, (string Skin, string Slot, string Name, JsonObject Data)>(); var skins = new HashSet<string>();long identityCharacters=0;
        foreach (var n in data["skins"] as JsonArray ?? [])
        {
            if (n is not JsonObject skin) throw new InvalidDataException("Skin object"); var skinName = Text(skin["name"]);
            if (skinName.Length == 0 || skinName.Contains('\0') || !skins.Add(skinName)) throw new InvalidDataException("Duplicate/empty skin");
            if(skin["attachments"] is not null && skin["attachments"] is not JsonObject)throw new InvalidDataException("Attachments object");
            foreach (var slot in skin["attachments"] as JsonObject ?? [])
            {
                if (!slotNames.Contains(slot.Key) || slot.Value is not JsonObject attachments) throw new InvalidDataException("Invalid attachment slot");
                foreach (var a in attachments)
                {
                    if(definitions.Count>=65536 || a.Key.Length==0 || a.Key.Contains('\0'))throw new InvalidDataException("Attachment count/name limit");
                    // A short JSON skin declaration can otherwise expand into gigabytes of repeated key prefixes.
                    identityCharacters+=(long)skinName.Length+slot.Key.Length+a.Key.Length+2;
                    if(identityCharacters>16*1024*1024)throw new InvalidDataException("Attachment identity size limit");
                    var key=Key(skinName,slot.Key,a.Key);definitions.Add(key, (skinName, slot.Key, a.Key, a.Value as JsonObject ?? throw new InvalidDataException("Attachment object")));attachmentNames.Add((slot.Key,a.Key));attachmentKeys.Add(key);
                }
            }
        }
        foreach(var slot in Slots)if(slot.Attachment is string attachment&&!HasAttachment(slot.Name,attachment))throw new InvalidDataException("Slot attachment reference");
        var resolving = new HashSet<string>();
        Mesh Resolve(string key)
        {
            if (Meshes.TryGetValue(key, out var existing)) return existing;
            if(resolving.Count>=128)throw new InvalidDataException("Linked chain depth limit");
            if (!resolving.Add(key)) throw new InvalidDataException("Linked mesh cycle");
            if (!definitions.TryGetValue(key, out var def)) throw new InvalidDataException("Missing linked parent");
            var a = def.Data; var type = Text(a["type"], "region"); Mesh mesh;
            if((type is "region" or "mesh" or "linkedmesh" or "clipping")&&a["color"] is not null)Rgba.Parse(Text(a["color"]));
            if (type == "linkedmesh")
            {
                if(a["timelines"] is not null)throw new NotSupportedException("Linked timelines flag schema pending");
                var parent = Resolve(Key(Text(a["skin"], "default"), def.Slot, Text(a["parent"])));
                if (Text(parent.Data["type"]) != "mesh") throw new InvalidDataException("Linked parent is not mesh");
                mesh = parent with { Skin = def.Skin, Name = def.Name, DeformKey = a["deform"] is null || a["deform"]!.GetValue<bool>() ? parent.DeformKey : key };
            }
            else if (type is "mesh" or "clipping" or "path" or "boundingbox")
            {
                var uvNumbers = Values(a["uvs"]); if (uvNumbers.Length % 2 != 0) throw new InvalidDataException("Odd UV count");
                var count = type == "mesh" ? uvNumbers.Length / 2 : Integer(a["vertexCount"]);
                if (count < 3 || count > 100_000) throw new InvalidDataException("Vertex count");
                var uv = type == "mesh" ? Enumerable.Range(0, count).Select(i => new Point(uvNumbers[2 * i], uvNumbers[2 * i + 1])).ToArray() : new Point[count];
                var raw = Values(a["vertices"]); Point[] local = []; Influence[][] weights = [];
                if (raw.Length == count * 2) local = Enumerable.Range(0, count).Select(i => new Point(raw[2 * i], raw[2 * i + 1])).ToArray();
                else
                {
                    weights = new Influence[count][]; var cursor = 0;
                    double Next() => cursor < raw.Length ? raw[cursor++] : throw new InvalidDataException("Truncated weighted vertices");
                    int Index(double d, int max) => d == Math.Truncate(d) && d >= 0 && d < max ? (int)d : throw new InvalidDataException("Weighted index/count");
                    for (var i = 0; i < count; i++)
                    {
                        var amount = Index(Next(), Bones.Length + 1); if (amount == 0) throw new InvalidDataException("Zero influences");
                        weights[i] = new Influence[amount];
                        for (var j = 0; j < amount; j++) { var bi = Index(Next(), Bones.Length); var p = new Point(Next(), Next()); var w = Next(); if (w < 0) throw new InvalidDataException("Negative weight"); weights[i][j] = new(bi, p, w); }
                        if (weights[i].Sum(w => w.Weight) == 0) throw new InvalidDataException("Zero total weight");
                    }
                    if (cursor != raw.Length) throw new InvalidDataException("Trailing weighted data");
                }
                var triangles = type == "mesh" ? (a["triangles"] as JsonArray ?? throw new InvalidDataException("Triangles required")).Select(Integer).ToArray() : [];
                if (triangles.Length % 3 != 0 || triangles.Any(i => i < 0 || i >= count)) throw new InvalidDataException("Triangle indices");
                var end = type=="clipping"?a["end"]?.GetValue<string>():null; if (end is not null && !slotNames.Contains(end)) throw new InvalidDataException("Clip end slot");
                mesh = new(def.Skin, def.Slot, def.Name, a, uv, triangles, local, weights, end);
                if(type=="path")
                {
                    var curves=BezierPath.CurveCount(count,a["closed"]?.GetValue<bool>()??false);var constantSpeed=a["constantSpeed"]?.GetValue<bool>()??true;
                    var lengths=Values(a["lengths"]);if(!constantSpeed)BezierPath.ValidateSetupLengths(lengths,curves);
                }
            }
            else if(type=="point")mesh=new(def.Skin,def.Slot,def.Name,a,[new()],[],[new(Number(a["x"]),Number(a["y"]))],[],null);
            else if (type == "region")
            {
                var width = Number(a["width"]); var height = Number(a["height"]);
                if (width <= 0 || height <= 0) throw new InvalidDataException("Region dimensions");
                var transform = Affine.Local(Number(a["x"]), Number(a["y"]), Number(a["rotation"]), Number(a["scaleX"],1), Number(a["scaleY"],1),0,0);
                Point[] corners = [new(-width/2,-height/2),new(width/2,-height/2),new(width/2,height/2),new(-width/2,height/2)];
                mesh = new(def.Skin, def.Slot, def.Name, a, [new(0,0),new(1,0),new(1,1),new(0,1)], [0,1,2,0,2,3], corners.Select(transform.Apply).ToArray(), [], null);
            }
            else { resolving.Remove(key); throw new NotSupportedException("Attachment type " + type); }
            if (type != "linkedmesh") mesh = mesh with { DeformKey = key };
            resolving.Remove(key); Meshes.Add(key, mesh); return mesh;
        }
        foreach (var key in definitions.Keys)
        {
            var type = Text(definitions[key].Data["type"], "region");
            if (type is "region" or "mesh" or "linkedmesh" or "clipping" or "path" or "boundingbox" or "point") Resolve(key);
        }
        ActiveBones("default");
    }
    public bool[] ActiveBones(string skin)
    {
        var known=Bones.Select(b=>b.Name).ToHashSet(StringComparer.Ordinal);var selected=new HashSet<string>(StringComparer.Ordinal);var found=skin=="default";
        var constraints=new[]{"ik","transform","path"}.ToDictionary(kind=>kind,kind=>(Array(Data[kind])??[]).Select(c=>Text(c?["name"])).ToHashSet(StringComparer.Ordinal));
        foreach(var node in Array(Data["skins"])??[])
        {
            var definition=Object(node)!;var current=Text(definition["name"])==skin;found|=current;
            var listed=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in Array(definition["bones"])??[]){var name=Text(entry);if(!known.Contains(name)||!listed.Add(name))throw new InvalidDataException("Skin bone reference");if(current)selected.Add(name);}
            foreach(var kind in new[]{"ik","transform","path"})
            {
                listed.Clear();
                foreach(var entry in Array(definition[kind])??[]){var name=Text(entry);if(!constraints[kind].Contains(name)||!listed.Add(name))throw new InvalidDataException("Skin constraint reference");}
            }
        }
        if(!found)throw new InvalidDataException("Unknown skin");
        var active=new bool[Bones.Length];for(var i=0;i<Bones.Length;i++)active[i]=(!Bones[i].SkinRequired||selected.Contains(Bones[i].Name))&&(Bones[i].Parent<0||active[Bones[i].Parent]);return active;
    }
    public static int Integer(JsonNode? n)
    {
        var v = Number(n, -1); return v == Math.Truncate(v) && v >= int.MinValue && v <= int.MaxValue ? (int)v : throw new InvalidDataException("Integer required");
    }
    public static double[] Values(JsonNode? n) => n is JsonArray a ? a.Select(v => v is null ? throw new InvalidDataException("Null numeric component") : Number(v)).ToArray() : n is null ? [] : throw new InvalidDataException("Numeric array required");
    public Affine[] Pose(string? animation = null, double time = 0, string skin = "default")
    {
        if (!double.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        var modern=Version.StartsWith("4.1.");
        foreach (var kind in new[] { "ik", "transform", "path" }) Array(Data[kind]);
        var anim = animation is null ? null : Object(Data["animations"]?[animation]) ?? throw new InvalidDataException("Unknown animation");
        if (anim is not null && anim.Any(p => p.Key is not ("bones" or "attachments" or "deform" or "slots" or "draworder" or "drawOrder" or "ik" or "transform" or "path" or "events"))) throw new NotSupportedException("Unsupported animation channel");
        EventTimeline.Validate(this,anim);
        Playback.Order(this,animation,time);
        var animatedBones = Object(anim?["bones"]); var animatedSlots = Object(anim?["slots"]);
        Object(anim?["attachments"]); Object(anim?["deform"]);
        if (anim?[modern ? "deform" : "attachments"] is not null) throw new NotSupportedException("Animation profile mismatch");
        if(animatedBones is {Count:>0})
        {
            var knownBones=Bones.Select(b=>b.Name).ToHashSet(StringComparer.Ordinal);
            if(animatedBones.Any(p=>!knownBones.Contains(p.Key)))throw new InvalidDataException("Timeline bone reference");
        }
        HashSet<string>? knownSlots=null;
        if(animatedSlots is {Count:>0})
        {
            knownSlots=Slots.Select(s=>s.Name).ToHashSet(StringComparer.Ordinal);
            if(animatedSlots.Any(p=>!knownSlots.Contains(p.Key)))throw new InvalidDataException("Timeline slot reference");
        }
        if(animatedSlots is not null)foreach(var slot in Slots)if(animatedSlots.ContainsKey(slot.Name)){ColorTimeline.Slot(this,slot,animation,time);Playback.Attachment(this,slot,animation,time);}
        var deformSkins = Object(anim?[modern ? "attachments" : "deform"]);
        if (deformSkins is {Count:>0})
        {
            var knownSkins=(Data["skins"] as JsonArray??[]).Select(s=>Text(s?["name"])).ToHashSet(StringComparer.Ordinal);
            knownSlots??=Slots.Select(s=>s.Name).ToHashSet(StringComparer.Ordinal);
            foreach(var deformSkin in deformSkins)
            {
                if (!knownSkins.Contains(deformSkin.Key)) throw new InvalidDataException("Deform attachment reference");
                foreach (var slot in Object(deformSkin.Value) ?? throw new InvalidDataException("Deform slots object required"))
                {
                    if (!knownSlots.Contains(slot.Key)) throw new InvalidDataException("Deform attachment reference");
                    foreach (var attachment in Object(slot.Value) ?? throw new InvalidDataException("Deform attachments object required"))
                    {
                        if (!Meshes.TryGetValue(Key(deformSkin.Key, slot.Key, attachment.Key), out var mesh)) throw new InvalidDataException("Deform attachment reference");
                        if (modern)
                        {
                            var channels = Object(attachment.Value) ?? throw new InvalidDataException("Attachment timelines object required");
                            if (channels.Any(p => p.Key != "deform")) throw new NotSupportedException("Unsupported attachment timeline");
                            Playback.ValidateDeformKeys(mesh, Array(channels["deform"]));
                        }
                        else Playback.ValidateDeformKeys(mesh, Array(attachment.Value));
                    }
                }
            }
        }
        var result = new Affine[Bones.Length];var locals=new LocalPose[Bones.Length];
        for (var i = 0; i < Bones.Length; i++)
        {
            var b = Bones[i]; var channels = Object(animatedBones?[b.Name]);
            if (channels is not null && channels.Any(p => p.Key is not ("rotate" or "translate" or "scale" or "shear"))) throw new NotSupportedException("Bone channel");
            if (channels is not null) foreach (var channel in channels)
            {
                if (channel.Value is not JsonArray frames) throw new InvalidDataException("Timeline array required");
                foreach (var frame in frames)
                {
                    if (frame is not JsonObject f || f.Any(p => p.Key is not ("time" or "curve") && (channel.Key == "rotate" ? p.Key != (modern?"value":"angle") : p.Key is not ("x" or "y")))) throw new NotSupportedException("Unverified timeline key schema");
                }
            }
            double Sample(string channel, string field, double fallback = 0) => Timeline.Sample(channels?[channel] as JsonArray, time, field, fallback,modern,field=="y"?1:0,channel=="rotate"?1:2,Version=="4.1.11");
            locals[i]=new(new(b.X+Sample("translate","x"),b.Y+Sample("translate","y")),Affine.DegreeSum(b.Rotation,Sample("rotate",modern?"value":"angle")),new(b.ScaleX*Sample("scale","x",1),b.ScaleY*Sample("scale","y",1)),new(Affine.DegreeSum(b.ShearX,Sample("shear","x")),Affine.DegreeSum(b.ShearY,Sample("shear","y"))));
        }
        var active=ActiveBones(skin);PoseMath.Rebuild(Bones,locals,result,active);Constraints.Apply(this,locals,result,animation,time,skin,active);
        return result;
    }
    public Vertex[] Vertices(Mesh mesh, Affine[] pose, double[]? deform = null)
    {
        if (pose.Length != Bones.Length) throw new InvalidDataException("Pose size");
        var needed = mesh.DeformLength;
        if (deform is not null && deform.Length != needed) throw new InvalidDataException("Deform size");
        if(!slotBones.TryGetValue(mesh.Slot,out var bone))throw new InvalidDataException("Mesh slot reference");
        var cursor = 0; var output = new Vertex[mesh.UV.Length];
        Point Offset() { var p = deform is null ? new Point() : new Point(deform[cursor], deform[cursor + 1]); cursor += 2; p.Validate(); return p; }
        Point Weighted(Influence influence)
        {
            var offset=Offset();var transform=pose[influence.Bone];
            if(influence.Weight==0){influence.Local.Validate();transform.Validate();return new();}
            return transform.Apply(influence.Local+offset)*influence.Weight;
        }
        for (var i = 0; i < output.Length; i++)
        {
            var p = new Point();
            if (mesh.Weights.Length == 0) p = pose[bone].Apply(mesh.Local[i] + Offset());
            else
            {
                var influences=mesh.Weights[i];var start=cursor;
                var nonzeroX=false;var nonzeroY=false;
                foreach(var w in influences){var term=Weighted(w);nonzeroX|=term.X!=0;nonzeroY|=term.Y!=0;p+=term;}
                var retryX=!double.IsFinite(p.X)||p.X==0&&nonzeroX;var retryY=!double.IsFinite(p.Y)||p.Y==0&&nonzeroY;
                if(retryX||retryY)
                {
                    cursor=start;var terms=new Point[influences.Length];
                    for(var j=0;j<terms.Length;j++){terms[j]=Weighted(influences[j]);terms[j].Validate();}
                    p=new(retryX?Numeric.RetrySum(p.X,terms.Select(static t=>t.X).ToArray()):p.X,retryY?Numeric.RetrySum(p.Y,terms.Select(static t=>t.Y).ToArray()):p.Y);
                }
            }
            p.Validate(); output[i] = new(p, mesh.UV[i]);
        }
        return output;
    }
}

internal static class Timeline
{
    public static double Sample(JsonArray? keys, double time, string field, double fallback = 0,bool absolute=false,int component=0,int components=1,bool forwardTime=false)
    {
        if (keys is null || keys.Count == 0) return fallback;
        double previous = -1;
        for(var frameIndex=0;frameIndex<keys.Count;frameIndex++)
        {
            var k=keys[frameIndex];
            var frame = Scene.Object(k) ?? throw new InvalidDataException("Timeline frame object required");
            var t = Scene.Number(frame["time"]);
            if (t < 0 || t <= previous) throw new InvalidDataException("Timeline order");
            previous = t;
            try { Scene.Number(frame[field], fallback); }
            catch (InvalidOperationException e) { throw new InvalidDataException("Timeline numeric value", e); }
            if(absolute&&frame["curve"] is JsonArray)
            {
                var controls=Scene.Values(frame["curve"]);if(controls.Length!=components*4)throw new InvalidDataException("Absolute curve components");
                if(frameIndex+1<keys.Count)
                {
                    var next=Scene.Number(keys[frameIndex+1]?["time"]);if(next<=t)throw new InvalidDataException("Timeline order");
                    if(!forwardTime)for(var c=0;c<components;c++)if(controls[c*4]<t||controls[c*4]>next||controls[c*4+2]<t||controls[c*4+2]>next)throw new InvalidDataException("Absolute curve time controls");
                }
            }
            else Fraction(frame["curve"], 0);
        }
        if (time < Scene.Number(keys[0]?["time"])) return fallback;
        var index = 0; while (index + 1 < keys.Count && Scene.Number(keys[index + 1]?["time"]) <= time) index++;
        var a = keys[index]!; var av = Scene.Number(a[field], fallback);
        if (index == keys.Count - 1) return av;
        var b = keys[index + 1]!; var ratio = (time - Scene.Number(a["time"])) / (Scene.Number(b["time"]) - Scene.Number(a["time"]));
        if(absolute&&a["curve"] is JsonArray)return Curves.Absolute(time,Scene.Number(a["time"]),Scene.Number(b["time"]),av,Scene.Number(b[field],fallback),Scene.Values(a["curve"]),component*4,forwardTime);
        ratio = Fraction(a["curve"], ratio);
        var value=Curves.Interpolate(av,Scene.Number(b[field], fallback),ratio);
        return double.IsFinite(value)?value:throw new InvalidDataException("Timeline numeric range");
    }
    public static double Fraction(JsonNode? curve, double ratio)
    {
        if (curve is null) return ratio;
        if (curve is JsonValue value) return value.GetValue<string>() == "stepped" ? 0 : throw new NotSupportedException("Curve");
        var c = Scene.Values(curve);
        if (c.Length != 4) throw new NotSupportedException("Curve controls");
        return Curves.Ease(ratio, c[0], c[1], c[2], c[3]);
    }
    public static JsonNode? NormalizedCurve(JsonObject frame)
    {
        foreach(var field in new[]{"c2","c3","c4"})if(frame[field] is not null)Scene.Number(frame[field]);
        if(frame["curve"] is JsonValue value&&value.TryGetValue<double>(out var x))return new JsonArray(x,Scene.Number(frame["c2"]),Scene.Number(frame["c3"],1),Scene.Number(frame["c4"],1));
        return frame["curve"];
    }
}

