using System.Text;
using System.Text.Json.Nodes;
namespace IndependentDev;

internal sealed partial class BinaryEditor
{
    internal bool CanEditMeshStructure=>!setup4117Profile||!PreservesAnimations||PreservedAnimationCount==0||playback41 is not null;
    static int End(ByteSpan span)=>checked(span.Offset+span.Length);
    sealed record MeshPatch(ByteSpan Span,byte[] Bytes);
    static byte[] EncodedUInt(uint value){using var output=new MemoryStream();UInt(output,value);return output.ToArray();}
    static void Short(Stream stream,int value)
    {
        if(value is <0 or >ushort.MaxValue)throw new InvalidDataException("SKEL mesh index exceeds uint16");
        stream.WriteByte((byte)(value>>8));stream.WriteByte((byte)value);
    }
    internal Scene EditMeshStructure(Action<Editor> operation,Action<Scene>? validate)
    {
        if(!CanEditMeshStructure)throw new NotSupportedException("이 SKEL의 애니메이션을 완전히 판독하지 못해 메시 구조를 안전하게 변경할 수 없습니다.");
        var source=ReadModel();var editor=new Editor(source.Data.ToJsonString(),recordHistory:false);operation(editor);var desired=editor.Document;
        if(JsonNode.DeepEquals(source.Data,desired.Data))return source;
        var candidate=EncodeMeshStructure(source,desired);var model=Model(candidate);
        ValidateMeshPrecision(source,desired,model);validate?.Invoke(model);
        if(candidate.Header.Version!=Document.Header.Version)throw new InvalidDataException("Mesh edit changed SKEL version");
        // Only this checked splice path may change setup and Deform domains in one commit.
        Commit(candidate,meshStructure:true);return ReadModel();
    }
    static void ValidateMeshPrecision(Scene source,Scene desired,Scene actual)
    {
        Affine[]? desiredPose=null;Affine[]? actualPose=null;
        foreach(var (key,mesh) in desired.Meshes)
        {
            if(Scene.Text(mesh.Data["type"])!="mesh"||source.Meshes.TryGetValue(key,out var old)&&JsonNode.DeepEquals(old.Data,mesh.Data))continue;
            var stored=actual.Meshes[key];
            if(mesh.UV.Length!=stored.UV.Length||!mesh.Triangles.SequenceEqual(stored.Triangles))throw new InvalidDataException("SKEL mesh topology changed during encoding");
            void RequireWinding(Point[] before,Point[] after)
            {
                foreach(var t in mesh.Triangles.Chunk(3))
                {
                    var a=Point.Cross(before[t[1]]-before[t[0]],before[t[2]]-before[t[0]]);var b=Point.Cross(after[t[1]]-after[t[0]],after[t[2]]-after[t[0]]);
                    if(!double.IsFinite(a)||!double.IsFinite(b)||Point.Orientation(before[t[0]],before[t[1]],before[t[2]])!=Point.Orientation(after[t[0]],after[t[1]],after[t[2]]))throw new InvalidOperationException("SKEL float32 정밀도로 삼각형이 납작해지거나 뒤집힙니다. 기존 정점이나 변에서 조금 떨어진 위치를 선택하세요.");
                }
            }
            RequireWinding(mesh.UV,stored.UV);
            desiredPose??=desired.Pose();actualPose??=actual.Pose();
            RequireWinding(desired.Vertices(mesh,desiredPose).Select(v=>v.Position).ToArray(),actual.Vertices(stored,actualPose).Select(v=>v.Position).ToArray());
        }
    }
    BinaryDocument EncodeMeshStructure(Scene source,Scene desired)
    {
        var modern=setup4117Profile;var raw=Document.UnchangedBytes();var patches=new List<MeshPatch>();
        var metadata=modern?Document.ReadMetadata4117():Document.ReadPublicMetadata38();
        var setup41=modern?Document.ReadSetup4117():null;var setup38=modern?null:Body(Document).Setup;
        var groups=setup41?.DefaultSkin.SlotGroups??setup38!.DefaultSkin.SlotGroups;
        var slots=setup41?.DefaultSkin.PathSection.TransformSection.IkSection.SlotSection.Slots??setup38!.DefaultSkin.TransformSection.IkSection.SlotSection.Slots;
        var skinSpan=setup41?.DefaultSkin.Span??setup38!.DefaultSkin.Span;
        var strings=metadata.Strings.Select(s=>s.Value).ToList();var appended=new List<byte[]>();
        uint Reference(string? text)
        {
            if(text is null)return 0;var found=strings.IndexOf(text);if(found>=0)return(uint)found+1;
            var bytes=Encoding.UTF8.GetBytes(text);if(strings.Count>=65536||bytes.Length>1024*1024-metadata.EncodedTextBytes-appended.Sum(b=>b.Length))throw new InvalidDataException("SKEL mesh shared string budget");
            strings.Add(text);appended.Add(bytes);return(uint)strings.Count;
        }
        var changed=new HashSet<string>(StringComparer.Ordinal);var removed=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in source.Meshes)
        {
            if(!desired.Meshes.TryGetValue(item.Key,out var next))removed.Add(item.Key);
            else if(!JsonNode.DeepEquals(item.Value.Data,next.Data))changed.Add(item.Key);
        }
        foreach(var item in desired.Meshes)if(!source.Meshes.ContainsKey(item.Key))changed.Add(item.Key);
        byte[] MeshBytes(Mesh mesh,BinaryAttachment? original)
        {
            if(mesh.Skin!="default"||Scene.Text(mesh.Data["type"])!="mesh")throw new NotSupportedException("SKEL mesh structure default skin profile");
            if(mesh.UV.Length>65536)throw new InvalidDataException("SKEL vertex count exceeds uint16");
            using var output=new MemoryStream();UInt(output,original?.PlaceholderIndex??Reference(mesh.Name));UInt(output,original?.NameIndex??0);output.WriteByte(2);
            if(original?.Mesh is{} before){output.Write(raw.AsSpan(before.PathSpan.Offset,before.PathSpan.Length));output.Write(raw.AsSpan(before.ColorSpan.Offset,4));}
            else{UInt(output,Reference(Scene.Text(mesh.Data["path"],mesh.Name)));Span<byte> color=stackalloc byte[4];System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(color,uint.Parse(Scene.Text(mesh.Data["color"],"ffffffff"),System.Globalization.NumberStyles.HexNumber));output.Write(color);}
            UInt(output,(uint)mesh.UV.Length);foreach(var p in mesh.UV){Float(output,Float32(p.X));Float(output,Float32(p.Y));}
            UInt(output,(uint)mesh.Triangles.Length);foreach(var index in mesh.Triangles)Short(output,index);
            output.WriteByte(mesh.Weights.Length==0?(byte)0:(byte)1);
            if(mesh.Weights.Length==0)foreach(var p in mesh.Local){Float(output,Float32(p.X));Float(output,Float32(p.Y));}
            else foreach(var vertex in mesh.Weights){UInt(output,(uint)vertex.Length);foreach(var influence in vertex){UInt(output,(uint)influence.Bone);Float(output,Float32(influence.Local.X));Float(output,Float32(influence.Local.Y));Float(output,Float32(influence.Weight));}}
            UInt(output,(uint)Scene.Integer(mesh.Data["hull"]));if(modern)output.WriteByte(0);
            var edges=Scene.Values(mesh.Data["edges"]);UInt(output,(uint)edges.Length);foreach(var value in edges){if(value!=Math.Truncate(value))throw new InvalidDataException("SKEL edge index");Short(output,checked((int)value));}
            if(original?.Mesh is{} dimensions){foreach(var span in dimensions.DimensionSpans)output.Write(raw.AsSpan(span.Offset,span.Length));}
            else{Float(output,Float32(Scene.Number(mesh.Data["width"])));Float(output,Float32(Scene.Number(mesh.Data["height"])));}
            return output.ToArray();
        }
        var slotGroups=new List<byte[]>();
        for(var slotIndex=0;slotIndex<slots.Length;slotIndex++)
        {
            var slot=slots[slotIndex];var group=groups.SingleOrDefault(g=>g.SlotIndex==slotIndex);var attachments=new List<byte[]>();
            foreach(var attachment in group?.Attachments??[])
            {
                var key=Scene.Key("default",slot.Name,attachment.Placeholder);if(removed.Contains(key))continue;
                attachments.Add(changed.Contains(key)?MeshBytes(desired.Meshes[key],attachment):raw.AsSpan(attachment.Span.Offset,attachment.Span.Length).ToArray());
            }
            foreach(var item in desired.Meshes.Where(p=>p.Value.Slot==slot.Name&&!source.Meshes.ContainsKey(p.Key)))attachments.Add(MeshBytes(item.Value,null));
            if(attachments.Count>0){using var output=new MemoryStream();UInt(output,(uint)slotIndex);UInt(output,(uint)attachments.Count);foreach(var bytes in attachments)output.Write(bytes);slotGroups.Add(output.ToArray());}
            var desiredAttachment=desired.Slots[slotIndex].Attachment;if(desiredAttachment!=slot.Attachment)patches.Add(new(slot.AttachmentSpan,EncodedUInt(Reference(desiredAttachment))));
        }
        using(var skin=new MemoryStream()){UInt(skin,(uint)slotGroups.Count);foreach(var bytes in slotGroups)skin.Write(bytes);patches.Add(new(skinSpan,skin.ToArray()));}
        if(modern)
        {
            var animations=Document.ReadAnimations41();
            foreach(var info in animations.Items)
            {
                foreach(var reference in info.AttachmentKeys)if(reference.Name is{} name&&removed.Contains(Scene.Key("default",slots[reference.Slot].Name,name)))patches.Add(new(reference.NameSpan,EncodedUInt(0)));
                var tracks=new List<(int Slot,byte[] Bytes)>();var deleted=0;var edited=false;
                foreach(var track in info.Deforms)
                {
                    var key=Scene.Key("default",slots[track.Slot].Name,track.Name);if(removed.Contains(key)){deleted++;edited=true;continue;}
                    if(changed.Contains(key))
                    {
                        var keys=desired.Data["animations"]![info.Name]!["attachments"]!["default"]![slots[track.Slot].Name]![track.Name]!["deform"]!.AsArray();
                        tracks.Add((track.Slot,DeformStructure41(raw,track,keys)));edited=true;
                    }
                    else tracks.Add((track.Slot,raw.AsSpan(track.Span.Offset,track.Span.Length).ToArray()));
                }
                if(!edited)continue;
                using var section=new MemoryStream();var grouped=tracks.GroupBy(t=>t.Slot).ToArray();UInt(section,grouped.Length==0?0u:1u);
                if(grouped.Length>0){UInt(section,0);UInt(section,(uint)grouped.Length);foreach(var group in grouped){UInt(section,(uint)group.Key);UInt(section,(uint)group.Count());foreach(var track in group)section.Write(track.Bytes);}}
                patches.Add(new(info.AttachmentSpan,section.ToArray()));if(deleted>0)patches.Add(new(info.CountSpan,EncodedUInt((uint)(info.TimelineCount-deleted))));
            }
        }
        else
        {
            foreach(var animation in Body(Document).Animations)
            {
                foreach(var track in animation.SlotAttachments)foreach(var frame in track.Frames)if(frame.Name is{} name&&removed.Contains(Scene.Key("default",slots[track.SlotIndex].Name,name)))patches.Add(new(frame.NameSpan,EncodedUInt(0)));
                var edited=false;var encodedGroups=new List<byte[]>();
                foreach(var group in animation.Slots)
                {
                    var tracks=new List<byte[]>();
                    foreach(var track in group.Timelines)
                    {
                        var key=Scene.Key("default",slots[group.SlotIndex].Name,track.Name);if(removed.Contains(key)){edited=true;continue;}
                        if(!changed.Contains(key)){tracks.Add(raw.AsSpan(track.Span.Offset,track.Span.Length).ToArray());continue;}
                        var keys=desired.Data["animations"]![animation.Name]!["deform"]!["default"]![slots[group.SlotIndex].Name]![track.Name]!.AsArray();using var bytes=new MemoryStream();
                        bytes.Write(raw.AsSpan(track.NameSpan.Offset,track.NameSpan.Length));UInt(bytes,(uint)track.Frames.Length);
                        for(var i=0;i<track.Frames.Length;i++)
                        {
                            var frame=track.Frames[i];bytes.Write(raw.AsSpan(frame.TimeSpan.Offset,4));var sparse=SparseValues(Scene.Values(keys[i]!["vertices"]));DeformValues(bytes,sparse.Offset,sparse.Values);
                            if(frame.Curve is{} curve)bytes.Write(raw.AsSpan(curve.Span.Offset,curve.Span.Length));
                        }
                        tracks.Add(bytes.ToArray());edited=true;
                    }
                    if(tracks.Count==0)continue;using var scope=new MemoryStream();UInt(scope,(uint)group.SlotIndex);UInt(scope,(uint)tracks.Count);foreach(var track in tracks)scope.Write(track);encodedGroups.Add(scope.ToArray());
                }
                if(!edited)continue;using var section=new MemoryStream();UInt(section,encodedGroups.Count==0?0u:1u);if(encodedGroups.Count>0){UInt(section,0);UInt(section,(uint)encodedGroups.Count);foreach(var group in encodedGroups)section.Write(group);}
                patches.Add(new(new(animation.SkinCountSpan.Offset,animation.DrawOrderSpan.Offset-animation.SkinCountSpan.Offset),section.ToArray()));
            }
        }
        if(appended.Count>0)
        {
            var cursor=new BinaryCursor(raw);cursor.Skip(Document.Header.BodyOffset+16);if(cursor.Boolean()){cursor.Float();cursor.String();cursor.String();}var start=cursor.Offset;cursor.VarUInt();var oldStart=cursor.Offset;
            using var pool=new MemoryStream();UInt(pool,(uint)strings.Count);pool.Write(raw.AsSpan(oldStart,End(metadata.Span)-oldStart));foreach(var bytes in appended){UInt(pool,(uint)bytes.Length+1);pool.Write(bytes);}
            patches.Add(new(new(start,End(metadata.Span)-start),pool.ToArray()));
        }
        var ordered=patches.OrderBy(p=>p.Span.Offset).ToArray();long length=raw.Length;foreach(var patch in ordered)length+=patch.Bytes.Length-patch.Span.Length;if(length>Input.MaxBytes)throw new InvalidDataException("SKEL mesh structure size limit");
        using var result=new MemoryStream((int)length);var at=0;
        foreach(var patch in ordered){if(patch.Span.Offset<at||End(patch.Span)>raw.Length)throw new InvalidDataException("Overlapping SKEL mesh patches");result.Write(raw.AsSpan(at,patch.Span.Offset-at));result.Write(patch.Bytes);at=End(patch.Span);}result.Write(raw.AsSpan(at));
        var candidate=new BinaryDocument(result.ToArray());Validate(candidate);if(modern)candidate.ReadAnimations41();else Body(candidate);
        return candidate;
    }
    static byte[] DeformStructure41(byte[] raw,BinaryDeformTrack41 track,JsonArray keys)
    {
        var cursor=new BinaryCursor(raw);cursor.Skip(track.Span.Offset);cursor.VarUInt();cursor.Skip(1);var frames=cursor.VarUInt();var beziers=cursor.VarUInt();if(frames!=keys.Count)throw new InvalidDataException("SKEL mesh Deform frame count");
        using var output=new MemoryStream();UInt(output,track.NameIndex);output.WriteByte(0);UInt(output,frames);UInt(output,beziers);var timeAt=cursor.Offset;cursor.Float();output.Write(raw.AsSpan(timeAt,4));
        for(var i=0;i<keys.Count;i++)
        {
            var count=cursor.VarUInt();if(count>0){cursor.VarUInt();cursor.Skip(checked((int)count*4));}
            var sparse=SparseValues(Scene.Values(keys[i]!["vertices"]));DeformValues(output,sparse.Offset,sparse.Values);
            if(i+1==keys.Count)continue;var start=cursor.Offset;cursor.Float();var curve=cursor.Bytes(1)[0];if(curve==2)cursor.Skip(16);else if(curve>2)throw new InvalidDataException("SKEL Deform curve type");output.Write(raw.AsSpan(start,cursor.Offset-start));
        }
        if(cursor.Offset!=End(track.Span))throw new InvalidDataException("SKEL Deform track boundary");return output.ToArray();
    }
}
