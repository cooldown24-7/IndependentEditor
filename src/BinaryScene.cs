using System.Text.Json.Nodes;
namespace IndependentDev;

internal static class BinaryScene
{
    sealed class SceneBuffer:MemoryStream
    {
        public override void Write(byte[] bytes,int offset,int count)
        {if(count>Input.MaxBytes-Position)throw new InvalidDataException("Binary scene output size limit");base.Write(bytes,offset,count);}
        public override void WriteByte(byte value)
        {if(Position>=Input.MaxBytes)throw new InvalidDataException("Binary scene output size limit");base.WriteByte(value);}
    }
    public static Scene Read(BinaryDocument document)=>Read(document,false);
    public static Scene ReadAnimation(BinaryDocument document)=>Read(document,true);
    public static Scene ReadStatic4117(BinaryDocument document)=>Read(document,false,true);
    public static Scene ReadSetupPose41(BinaryDocument document)=>Read(document,false,true,true);
    public static Scene ReadAnimations41(BinaryDocument document,BinaryAnimationBody41 body)
    {
        var scene=ReadSetupPose41(document);scene.Data["animations"]=body.Animations.DeepClone();return Scene.Parse(scene.Data.ToJsonString());
    }
    public static Scene ReadAttachmentPrefix41(BinaryDocument document,BinaryAnimationSlots4117 prefix)
    {
        var scene=ReadSetupPose41(document);var slots=document.ReadSlots4117().Slots;var tracks=new JsonObject();
        foreach(var track in prefix.SlotAttachments)
        {
            var frames=new JsonArray();foreach(var frame in track.Frames)frames.Add(new JsonObject{["time"]=(double)frame.Time,["name"]=frame.Name});
            tracks.Add(slots[track.SlotIndex].Name,new JsonObject{["attachment"]=frames});
        }
        // shortcut: diagnostic projection of decoded attachment keys; never save this incomplete scene as the source skeleton.
        scene.Data["animations"]=new JsonObject{[prefix.Name]=new JsonObject{["slots"]=tracks}};
        return Scene.Parse(scene.Data.ToJsonString());
    }
    static Scene Read(BinaryDocument document,bool rotation,bool static4117=false,bool preserveAnimations=false)
    {
        var setup41=static4117?document.ReadSetup4117():null;
        if(setup41 is not null&&!setup41.Complete&&!preserveAnimations)throw new NotSupportedException("4.1 scene requires complete setup or preserved animations");
        var body=static4117?null:rotation?document.ReadAnimation3855():document.ReadDeform3855();var skin=body?.Setup.DefaultSkin;var transform=skin?.TransformSection;var ik=transform?.IkSection??setup41?.DefaultSkin.PathSection.TransformSection.IkSection;var slots=setup41?.DefaultSkin.PathSection.TransformSection.IkSection.SlotSection.Slots??ik!.SlotSection.Slots;var bones=setup41?.DefaultSkin.PathSection.TransformSection.IkSection.SlotSection.BoneSection.Bones??ik!.SlotSection.BoneSection.Bones;var groups=setup41?.DefaultSkin.SlotGroups??skin!.SlotGroups;var metadata=static4117?document.ReadMetadata4117():document.ReadPublicMetadata38();var components=0;
        JsonArray Numbers(IEnumerable<double> values){var array=new JsonArray();foreach(var value in values){if(++components>1000000)throw new InvalidDataException("Binary scene numeric component limit");array.Add(value);}return array;}
        JsonArray Points(Point[] points)=>Numbers(points.SelectMany(p=>new[]{p.X,p.Y}));
        JsonArray BoneNames(int[] indices)=>new(indices.Select(i=>(JsonNode?)JsonValue.Create(bones[i].Name)).ToArray());
        string Color(uint value)=>value.ToString("x8",System.Globalization.CultureInfo.InvariantCulture);
        var boneNodes=new JsonArray();foreach(var bone in bones)
        {
            var node=new JsonObject{["name"]=bone.Name,["rotation"]=(double)bone.Rotation,["x"]=(double)bone.X,["y"]=(double)bone.Y,["scaleX"]=(double)bone.ScaleX,["scaleY"]=(double)bone.ScaleY,["shearX"]=(double)bone.ShearX,["shearY"]=(double)bone.ShearY,["length"]=(double)bone.Length,["transform"]=bone.TransformMode,["color"]=Color(bone.Color)};
            if(bone.ParentIndex>=0)node["parent"]=bones[bone.ParentIndex].Name;boneNodes.Add(node);
        }
        var slotNodes=new JsonArray();foreach(var slot in slots){var node=new JsonObject{["name"]=slot.Name,["bone"]=bones[slot.BoneIndex].Name,["color"]=Color(slot.Color),["blend"]=slot.Blend};if(slot.DarkColor is uint dark)node["dark"]=dark.ToString("x6",System.Globalization.CultureInfo.InvariantCulture);if(slot.Attachment is not null)node["attachment"]=slot.Attachment;slotNodes.Add(node);}
        var attachments=new JsonObject();foreach(var group in groups)
        {
            var items=new JsonObject();foreach(var attachment in group.Attachments)
            {
                if(attachment.Name is{} alias&&alias!=attachment.Placeholder)throw new NotSupportedException(document.Header.Version+" binary scene aliased name pending");var node=new JsonObject{["type"]=attachment.Type};
                if(attachment.Region is{} region)
                {
                    if(region.Path is not null)node["path"]=region.Path;node["rotation"]=(double)region.Rotation;node["x"]=(double)region.X;node["y"]=(double)region.Y;node["scaleX"]=(double)region.ScaleX;node["scaleY"]=(double)region.ScaleY;node["width"]=(double)region.Width;node["height"]=(double)region.Height;node["color"]=Color(region.Color);
                }
                else if(attachment.Mesh is{} mesh)
                {
                    if(mesh.Path is not null)node["path"]=mesh.Path;node["color"]=Color(mesh.Color);node["uvs"]=Points(mesh.Uvs);node["triangles"]=Numbers(mesh.Triangles.Select(v=>(double)v));node["hull"]=(double)mesh.Hull;node["edges"]=Numbers(mesh.Edges.Select(v=>(double)v));node["width"]=(double)mesh.Width;node["height"]=(double)mesh.Height;
                    node["vertices"]=mesh.Weights.Length==0?Points(mesh.Vertices):Numbers(mesh.Weights.SelectMany(vertex=>new[]{(double)vertex.Influences.Length}.Concat(vertex.Influences.SelectMany(w=>new[]{(double)w.Bone,w.X,w.Y,w.Weight}))));
                }
                else if(attachment.Path is{} path)
                {
                    node["closed"]=path.Closed;node["constantSpeed"]=path.ConstantSpeed;node["vertexCount"]=path.Weights.Length==0?path.Vertices.Length:path.Weights.Length;node["lengths"]=Numbers(path.Lengths.Select(value=>(double)value));node["color"]=Color(path.Color);node["vertices"]=path.Weights.Length==0?Points(path.Vertices):Numbers(path.Weights.SelectMany(vertex=>new[]{(double)vertex.Influences.Length}.Concat(vertex.Influences.SelectMany(w=>new[]{(double)w.Bone,w.X,w.Y,w.Weight}))));
                }
                else
                {
                    var polygon=attachment.Box??attachment.Clip!.Polygon;node["vertexCount"]=polygon.Vertices.Length;node["vertices"]=Points(polygon.Vertices);node["color"]=Color(polygon.Color);if(attachment.Clip is{} clip)node["end"]=slots[clip.EndSlot].Name;
                }
                items.Add(attachment.Placeholder,node);
            }
            attachments.Add(slots[group.SlotIndex].Name,items);
        }
        var ikNodes=new JsonArray();foreach(var constraint in ik?.Constraints??[])
        {
            if(constraint.Order>int.MaxValue)throw new NotSupportedException("Binary constraint order exceeds scene model");ikNodes.Add(new JsonObject{["name"]=constraint.Name,["order"]=(int)constraint.Order,["bones"]=BoneNames(constraint.Bones),["target"]=bones[constraint.Target].Name,["mix"]=(double)constraint.Mix,["softness"]=(double)constraint.Softness,["bendPositive"]=constraint.BendPositive,["compress"]=constraint.Compress,["stretch"]=constraint.Stretch,["uniform"]=constraint.Uniform});
        }
        var transformNodes=new JsonArray();foreach(var constraint in transform?.Constraints??[])
        {
            if(constraint.Order>int.MaxValue)throw new NotSupportedException("Binary constraint order exceeds scene model");transformNodes.Add(new JsonObject{["name"]=constraint.Name,["order"]=(int)constraint.Order,["skin"]=constraint.SkinRequired,["bones"]=BoneNames(constraint.Bones),["target"]=bones[constraint.Target].Name,["local"]=false,["relative"]=false,["rotation"]=(double)constraint.Rotation,["x"]=(double)constraint.X,["y"]=(double)constraint.Y,["scaleX"]=(double)constraint.ScaleX,["scaleY"]=(double)constraint.ScaleY,["shearY"]=(double)constraint.ShearY,["rotateMix"]=(double)constraint.RotateMix,["translateMix"]=(double)constraint.TranslateMix,["scaleMix"]=(double)constraint.ScaleMix,["shearMix"]=(double)constraint.ShearMix});
        }
        foreach(var constraint in setup41?.DefaultSkin.PathSection.TransformSection.Constraints??[])
        {
            if(constraint.Order>int.MaxValue)throw new NotSupportedException("Binary constraint order exceeds scene model");
            var v=constraint.Values;transformNodes.Add(new JsonObject{["name"]=constraint.Name,["order"]=(int)constraint.Order,["skin"]=constraint.SkinRequired,["local"]=constraint.Local,["relative"]=constraint.Relative,["bones"]=BoneNames([constraint.Bone]),["target"]=bones[constraint.Target].Name,["rotation"]=(double)v[0],["x"]=(double)v[1],["y"]=(double)v[2],["scaleX"]=(double)v[3],["scaleY"]=(double)v[4],["shearY"]=(double)v[5],["mixRotate"]=(double)v[6],["mixX"]=(double)v[7],["mixY"]=(double)v[8],["mixScaleX"]=(double)v[9],["mixScaleY"]=(double)v[10],["mixShearY"]=(double)v[11]});
        }
        var animations=new JsonObject();foreach(var animation in body?.Animations??[])
        {
            var channels=new JsonObject();foreach(var group in animation.Slots)
            {
                var items=new JsonObject();foreach(var timeline in group.Timelines)
                {
                    var frames=new JsonArray();foreach(var frame in timeline.Frames)
                    {
                        var node=new JsonObject{["time"]=(double)frame.Time};if(frame.Values.Length>0){node["offset"]=(double)frame.Offset;node["vertices"]=Numbers(frame.Values.Select(v=>(double)v));}if(frame.Curve is{Kind:1})node["curve"]="stepped";else if(frame.Curve is{Kind:2} curve)node["curve"]=Numbers(curve.Controls.Select(v=>(double)v));frames.Add(node);
                    }
                    items.Add(timeline.Name,frames);
                }
                channels.Add(slots[group.SlotIndex].Name,items);
            }
            var animationNode=channels.Count==0?new JsonObject():new JsonObject{["deform"]=new JsonObject{["default"]=channels}};
            if(animation.Rotations.Length>0||animation.Translations.Length>0||animation.Scales.Length>0||animation.Shears.Length>0)
            {
                var tracks=new JsonObject();foreach(var track in animation.Rotations){var frames=new JsonArray();foreach(var frame in track.Frames){var node=new JsonObject{["time"]=(double)frame.Time,["angle"]=(double)frame.Angle};if(frame.Curve is{Kind:1})node["curve"]="stepped";else if(frame.Curve is{Kind:2} curve)node["curve"]=Numbers(curve.Controls.Select(v=>(double)v));frames.Add(node);}tracks.Add(bones[track.Bone].Name,new JsonObject{["rotate"]=frames});}animationNode["bones"]=tracks;
                foreach(var channel in new[]{(Kind:"translate",Tracks:animation.Translations),(Kind:"scale",Tracks:animation.Scales),(Kind:"shear",Tracks:animation.Shears)})foreach(var track in channel.Tracks){var frames=new JsonArray();foreach(var frame in track.Frames){var node=new JsonObject{["time"]=(double)frame.Time,["x"]=(double)frame.X,["y"]=(double)frame.Y};if(frame.Curve is{Kind:1})node["curve"]="stepped";else if(frame.Curve is{Kind:2} curve)node["curve"]=Numbers(curve.Controls.Select(v=>(double)v));frames.Add(node);}var name=bones[track.Bone].Name;var bone=tracks[name] as JsonObject;if(bone is null){bone=new JsonObject();tracks.Add(name,bone);}bone.Add(channel.Kind,frames);}
            }
            if(animation.SlotAttachments.Length>0||animation.SlotColors.Length>0)
            {
                var slotTracks=new JsonObject();foreach(var track in animation.SlotAttachments){var frames=new JsonArray();foreach(var frame in track.Frames)frames.Add(new JsonObject{["time"]=(double)frame.Time,["name"]=frame.Name});slotTracks.Add(slots[track.SlotIndex].Name,new JsonObject{["attachment"]=frames});}animationNode["slots"]=slotTracks;
                foreach(var track in animation.SlotColors){var frames=new JsonArray();foreach(var frame in track.Frames){var node=new JsonObject{["time"]=(double)frame.Time,[frame.DarkColor is null?"color":"light"]=Color(frame.Color)};if(frame.DarkColor is uint dark)node["dark"]=dark.ToString("x6",System.Globalization.CultureInfo.InvariantCulture);if(frame.Curve is{Kind:1})node["curve"]="stepped";else if(frame.Curve is{Kind:2} curve)node["curve"]=Numbers(curve.Controls.Select(v=>(double)v));frames.Add(node);}var name=slots[track.SlotIndex].Name;var slot=slotTracks[name] as JsonObject;if(slot is null){slot=new JsonObject();slotTracks.Add(name,slot);}slot.Add(track.Frames[0].DarkColor is null?"color":"twoColor",frames);}
            }
            if(animation.Ik.Length>0)
            {
                var tracks=new JsonObject();foreach(var track in animation.Ik){var frame=track.Frames[0];tracks.Add(ik!.Constraints[track.Constraint].Name,new JsonArray(new JsonObject{["time"]=(double)frame.Time,["mix"]=(double)frame.Mix,["softness"]=0,["bendPositive"]=frame.BendPositive,["compress"]=false,["stretch"]=false}));}animationNode["ik"]=tracks;
            }
            if(animation.Transforms.Length>0)
            {
                var tracks=new JsonObject();foreach(var track in animation.Transforms){var frame=track.Frames[0];tracks.Add(transform!.Constraints[track.Constraint].Name,new JsonArray(new JsonObject{["time"]=(double)frame.Time,["rotateMix"]=(double)frame.RotateMix,["translateMix"]=0,["scaleMix"]=0,["shearMix"]=0}));}animationNode["transform"]=tracks;
            }
            animations.Add(animation.Name,animationNode);
        }
        var pathNodes=new JsonArray();foreach(var constraint in setup41?.DefaultSkin.PathSection.Constraints??[]){var v=constraint.Values;pathNodes.Add(new JsonObject{["name"]=constraint.Name,["order"]=(int)constraint.Order,["bones"]=BoneNames(constraint.Bones),["target"]=slots[constraint.Target].Name,["positionMode"]=constraint.PositionMode==0?"fixed":"percent",["spacingMode"]=constraint.SpacingMode switch{0=>"length",1=>"fixed",2=>"percent",_=>throw new NotSupportedException("Path spacing mode")},["rotateMode"]=constraint.RotateMode switch{0=>"tangent",1=>"chain",2=>"chainScale",_=>throw new InvalidDataException("Path rotate mode")},["rotation"]=(double)v[0],["position"]=(double)v[1],["spacing"]=(double)v[2],["mixRotate"]=(double)v[3],["mixX"]=(double)v[4],["mixY"]=(double)v[5]});}
        var events=new JsonObject();foreach(var item in setup41?.Events??body!.Setup.Events)events.Add(item.Name,new JsonObject{["int"]=item.Int,["float"]=(double)item.Float,["string"]=item.Text});
        // Promote decoded Single values before JSON serialization; Single's short decimal text is not its exact Double value.
        var data=new JsonObject{["skeleton"]=new JsonObject{["spine"]=document.Header.Version,["hash"]=document.Header.Hash,["x"]=(double)metadata.X,["y"]=(double)metadata.Y,["width"]=(double)metadata.Width,["height"]=(double)metadata.Height,["fps"]=(double)metadata.Fps!.Value,["images"]=metadata.Images,["audio"]=metadata.Audio},["bones"]=boneNodes,["slots"]=slotNodes,["ik"]=ikNodes,["transform"]=transformNodes,["skins"]=new JsonArray(new JsonObject{["name"]="default",["attachments"]=attachments}),["animations"]=animations,["events"]=events};
        if(pathNodes.Count>0)data["path"]=pathNodes;
        // Shared names can expand across many animation references; bound UTF8 before allocating the final string.
        using var buffer=new SceneBuffer();using(var writer=new System.Text.Json.Utf8JsonWriter(buffer))data.WriteTo(writer);
        return Scene.Parse(System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(),0,(int)buffer.Length));
    }
}


