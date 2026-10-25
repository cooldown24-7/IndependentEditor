using System.Text.Json.Nodes;
namespace IndependentDev;

internal sealed record BinaryDeformTrack41(int Slot,string Name,uint NameIndex,ByteSpan Span);
internal sealed record BinaryAttachmentKey41(int Slot,string? Name,ByteSpan NameSpan);
internal sealed record BinaryAnimationInfo41(string Name,int TimelineCount,int FrameCount,double Duration,ByteSpan Span,string[] EvaluationIssues,ByteSpan CountSpan,ByteSpan AttachmentSpan,BinaryDeformTrack41[] Deforms,BinaryAttachmentKey41[] AttachmentKeys);
internal sealed record BinaryAnimationBody41(JsonObject Animations,BinaryAnimationInfo41[] Items,ByteSpan Span,int FrameCount,int ValueCount);
internal sealed partial class BinaryDocument
{
    public BinaryAnimationBody41 ReadAnimations41()
    {
        var setup=ReadSetup4117();var metadata=ReadMetadata4117();var slots=setup.DefaultSkin.PathSection.TransformSection.IkSection.SlotSection;var bones=slots.BoneSection.Bones;
        var start=setup.AnimationCountSpan.Offset+setup.AnimationCountSpan.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);
        var animations=new JsonObject();var infos=new List<BinaryAnimationInfo41>();var issues=new List<string>();var frameTotal=0;var valueTotal=0;var trackTotal=0;var textBytes=setup.EncodedTextBytes;
        var targets=setup.DefaultSkin.SlotGroups.SelectMany(g=>g.Attachments.Select(a=>(g.SlotIndex,a.Placeholder))).ToHashSet();
        var meshes=setup.DefaultSkin.SlotGroups.SelectMany(g=>g.Attachments.Where(a=>a.Mesh is not null||a.Path is not null).Select(a=>(Key:(g.SlotIndex,a.Placeholder),Domain:2*(a.Mesh is{} mesh?(mesh.Weights.Length==0?mesh.Vertices.Length:mesh.Weights.Sum(v=>v.Influences.Length)):(a.Path!.Weights.Length==0?a.Path.Vertices.Length:a.Path.Weights.Sum(v=>v.Influences.Length)))))).ToDictionary(p=>p.Key,p=>p.Domain);
        void Frames(uint count,int width)
        {
            if(count<1||count>100000-frameTotal||count>(original.Length-cursor.Offset)/5||count>(1000000-valueTotal)/width)throw new InvalidDataException("4.1 animation frame/value budget");
            frameTotal+=(int)count;valueTotal+=(int)count*width;
        }
        void Track(){if(++trackTotal>65536)throw new InvalidDataException("4.1 animation track budget");}
        double Time(){var value=cursor.Float();if(value<0)throw new InvalidDataException("4.1 animation time");return value;}
        JsonArray Numbers(IEnumerable<double> values)=>new(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray());
        string Shared(out uint index)
        {
            index=cursor.VarUInt();if(index<1||index>metadata.Strings.Length)throw new InvalidDataException("4.1 animation shared reference");var value=metadata.Strings[(int)index-1].Value;if(string.IsNullOrEmpty(value)||value.Contains('\0'))throw new InvalidDataException("4.1 animation shared name");return value;
        }
        double[] Controls(double time,double next,int width,ref int seen,bool forwardTime)
        {
            var values=new double[4*width];for(var i=0;i<values.Length;i++)values[i]=cursor.Float();seen+=width;
            for(var i=0;i<width;i++)if(!forwardTime&&(values[4*i]<time||values[4*i]>next||values[4*i+2]<time||values[4*i+2]>next))issues.Add($"Bezier time handles outside key interval [{time:R},{next:R}], controls at byte {cursor.Offset-values.Length*4+i*4*4}");return values;
        }
        void Curve(JsonObject frame,double time,double next,int width,ref int seen,bool normalized=false,bool forwardTime=false)
        {
            var kind=cursor.Bytes(1)[0];if(kind==1)frame["curve"]="stepped";
            else if(kind==2)
            {
                if(valueTotal>1000000-4*width)throw new InvalidDataException("4.1 animation curve value budget");valueTotal+=4*width;
                var values=Controls(time,next,width,ref seen,forwardTime);if(normalized){values[0]=(values[0]-time)/(next-time);values[2]=(values[2]-time)/(next-time);}frame["curve"]=Numbers(values);
            }
            else if(kind!=0)throw new NotSupportedException("4.1 animation curve type pending");
        }
        JsonArray Keys(string[] fields,bool rgba=false,bool forwardTime=false,Action<JsonObject>? discrete=null)
        {
            // The exported Bezier count is capacity; linear/stepped segments can leave entries unused.
            var width=rgba?4:fields.Length;var count=cursor.VarUInt();Frames(count,width);var beziers=cursor.VarUInt();if(beziers>count*width)throw new InvalidDataException($"4.1 animation Bezier count at {cursor.Offset}: frames={count}, width={width}, beziers={beziers}");var seen=0;
            JsonObject Values(double time)
            {
                var node=new JsonObject{["time"]=time};if(rgba)node["color"]=cursor.Int().ToString("x8",System.Globalization.CultureInfo.InvariantCulture);else foreach(var field in fields)node[field]=(double)cursor.Float();discrete?.Invoke(node);return node;
            }
            var time=Time();var current=Values(time);var keys=new JsonArray();
            for(var f=0;f<count;f++)
            {
                JsonObject? next=null;double nextTime=0;
                if(f+1<count){nextTime=Time();if(nextTime<=time)throw new InvalidDataException("4.1 animation time order");next=Values(nextTime);Curve(current,time,nextTime,width,ref seen,forwardTime:forwardTime);}
                keys.Add(current);if(next is not null){current=next;time=nextTime;}
            }
            if(seen>beziers)throw new InvalidDataException("4.1 animation Bezier count mismatch");return keys;
        }
        for(var a=0;a<setup.AnimationCount;a++)
        {
            issues.Clear();var at=cursor.Offset;var firstFrame=frameTotal;var firstTrack=trackTotal;var name=cursor.String(1024*1024-textBytes);
            if(string.IsNullOrEmpty(name)||name.Contains('\0')||animations.ContainsKey(name))throw new InvalidDataException("4.1 animation name");textBytes+=System.Text.Encoding.UTF8.GetByteCount(name);
            var countAt=cursor.Offset;var declared=cursor.VarUInt();var countSpan=new ByteSpan(countAt,cursor.Offset-countAt);if(declared>65536-trackTotal)throw new InvalidDataException("4.1 animation declared track count");var animation=new JsonObject();var duration=0d;var deformTracks=new List<BinaryDeformTrack41>();var attachmentKeys=new List<BinaryAttachmentKey41>();
            void Add(JsonObject channels,string channel,JsonArray keys){Track();channels.Add(channel,keys);if(keys.Count>0)duration=Math.Max(duration,Scene.Number(keys[^1]!["time"]));}
            var slotGroups=cursor.VarUInt();if(slotGroups>slots.Slots.Length)throw new InvalidDataException("4.1 animation slot count");var slotTracks=new JsonObject();
            for(var s=0;s<slotGroups;s++)
            {
                var index=cursor.VarUInt();if(index>=slots.Slots.Length)throw new InvalidDataException("4.1 animation slot reference");var channels=new JsonObject();slotTracks.Add(slots.Slots[(int)index].Name,channels);var count=cursor.VarUInt();if(count is <1 or >2)throw new NotSupportedException("4.1 animation slot track count pending");
                for(var t=0;t<count;t++)
                {
                    var type=cursor.Bytes(1)[0];if(type==0)
                    {
                        var frames=cursor.VarUInt();Frames(frames,1);var keys=new JsonArray();var previous=-1d;
                        for(var f=0;f<frames;f++)
                        {
                            var time=Time();if(time<=previous)throw new InvalidDataException("4.1 attachment time order");previous=time;var referenceAt=cursor.Offset;var reference=cursor.VarUInt();if(reference>metadata.Strings.Length)throw new InvalidDataException("4.1 attachment reference");var target=reference==0?null:metadata.Strings[(int)reference-1].Value;
                            attachmentKeys.Add(new((int)index,target,new(referenceAt,cursor.Offset-referenceAt)));
                            if(target is not null){if(!targets.Contains(((int)index,target)))throw new InvalidDataException("4.1 attachment binding");var size=System.Text.Encoding.UTF8.GetByteCount(target);if(size>1024*1024-textBytes)throw new InvalidDataException("4.1 animation text budget");textBytes+=size;}
                            keys.Add(new JsonObject{["time"]=time,["name"]=target});
                        }
                        Add(channels,"attachment",keys);
                    }
                    else if(type==1)Add(channels,"rgba",Keys([],rgba:true));else throw new NotSupportedException("4.1 slot animation type pending at "+cursor.Offset);
                }
            }
            if(slotTracks.Count>0)animation["slots"]=slotTracks;
            var boneGroups=cursor.VarUInt();if(boneGroups>bones.Length)throw new InvalidDataException("4.1 animation bone count");var boneTracks=new JsonObject();
            for(var b=0;b<boneGroups;b++)
            {
                var index=cursor.VarUInt();if(index>=bones.Length)throw new InvalidDataException("4.1 animation bone reference");var channels=new JsonObject();boneTracks.Add(bones[(int)index].Name,channels);var count=cursor.VarUInt();if(count is <1 or >4)throw new NotSupportedException("4.1 animation bone track count pending");
                for(var t=0;t<count;t++)
                {
                    var type=cursor.Bytes(1)[0];var channel=type switch{0=>"rotate",1=>"translate",4=>"scale",7=>"shear",_=>throw new NotSupportedException("4.1 split-axis bone track pending at "+cursor.Offset)};
                    Add(channels,channel,Keys(type==0?["value"]:["x","y"],forwardTime:Header.Version=="4.1.11"));
                }
            }
            if(boneTracks.Count>0)animation["bones"]=boneTracks;
            var ikConstraints=setup.DefaultSkin.PathSection.TransformSection.IkSection.Constraints;var ikCount=cursor.VarUInt();if(ikCount>ikConstraints.Length)throw new InvalidDataException("4.1 IK timeline count");var ikTracks=new JsonObject();
            for(var t=0;t<ikCount;t++)
            {
                var index=cursor.VarUInt();if(index>=ikConstraints.Length)throw new InvalidDataException("4.1 IK timeline reference");var keys=Keys(["mix","softness"],discrete:frame=>{frame["bendPositive"]=cursor.Bytes(1)[0] switch{1=>true,255=>false,_=>throw new InvalidDataException("4.1 IK bend direction")};frame["compress"]=cursor.Boolean();frame["stretch"]=cursor.Boolean();});Add(ikTracks,ikConstraints[(int)index].Name,keys);
            }
            if(ikTracks.Count>0)animation["ik"]=ikTracks;
            var transforms=setup.DefaultSkin.PathSection.TransformSection.Constraints;var transformCount=cursor.VarUInt();if(transformCount>transforms.Length)throw new InvalidDataException("4.1 Transform timeline count");var transformTracks=new JsonObject();
            for(var t=0;t<transformCount;t++)
            {
                var index=cursor.VarUInt();if(index>=transforms.Length)throw new InvalidDataException("4.1 Transform timeline reference");string[] fields=["mixRotate","mixX","mixY","mixScaleX","mixScaleY","mixShearY"];var keys=Keys(fields);
                foreach(var frame in keys)
                {
                    var values=fields.Select(field=>Scene.Number(frame![field])).ToArray();if(values.Where((value,component)=>value<-1||value>1).Any())throw new InvalidDataException("4.1 Transform key mix range");
                }
                Add(transformTracks,transforms[(int)index].Name,keys);
            }
            if(transformTracks.Count>0)animation["transform"]=transformTracks;
            var paths=setup.DefaultSkin.PathSection.Constraints;var pathCount=cursor.VarUInt();if(pathCount>paths.Length)throw new InvalidDataException("4.1 Path timeline count");var pathTracks=new JsonObject();
            for(var t=0;t<pathCount;t++)
            {
                var index=cursor.VarUInt();if(index>=paths.Length)throw new InvalidDataException("4.1 Path timeline reference");var count=cursor.VarUInt();if(count is <1 or >3)throw new InvalidDataException("4.1 Path channel count");var channels=new JsonObject();pathTracks.Add(paths[(int)index].Name,channels);
                for(var p=0;p<count;p++){var type=cursor.Bytes(1)[0];var channel=type switch{0=>"position",1=>"spacing",2=>"mix",_=>throw new InvalidDataException("4.1 Path channel type")};Add(channels,channel,Keys(type==2?["mixRotate","mixX","mixY"]:[channel]));}
            }
            if(pathTracks.Count>0)animation["path"]=pathTracks;
            var attachmentAt=cursor.Offset;var skinCount=cursor.VarUInt();if(skinCount>1)throw new NotSupportedException("4.1 animation extra skins pending");var attachments=new JsonObject();
            for(var skin=0;skin<skinCount;skin++)
            {
                if(cursor.VarUInt()!=0)throw new NotSupportedException("4.1 animation extra skin reference pending");var groups=cursor.VarUInt();if(groups>slots.Slots.Length)throw new InvalidDataException("4.1 Deform slot count");var deformSlots=new JsonObject();
                for(var s=0;s<groups;s++)
                {
                    var slot=cursor.VarUInt();if(slot>=slots.Slots.Length)throw new InvalidDataException("4.1 Deform slot reference");var items=new JsonObject();deformSlots.Add(slots.Slots[(int)slot].Name,items);var count=cursor.VarUInt();if(count>meshes.Count)throw new InvalidDataException("4.1 Deform attachment count");
                    for(var t=0;t<count;t++)
                    {
                        var trackAt=cursor.Offset;var target=Shared(out var nameIndex);if(!meshes.TryGetValue(((int)slot,target),out var domain))throw new InvalidDataException("4.1 Deform attachment binding");if(cursor.Bytes(1)[0]!=0)throw new NotSupportedException("4.1 attachment sequence timelines pending");
                        var frames=cursor.VarUInt();Frames(frames,1);var beziers=cursor.VarUInt();if(beziers>frames)throw new InvalidDataException("4.1 Deform Bezier count");var seen=0;var keys=new JsonArray();var time=Time();
                        for(var f=0;f<frames;f++)
                        {
                            var length=cursor.VarUInt();if(length>1000000-valueTotal||length>domain||length>(original.Length-cursor.Offset)/4)throw new InvalidDataException("4.1 Deform value budget");valueTotal+=(int)length;var key=new JsonObject{["time"]=time};
                            if(length>0){var offset=cursor.VarUInt();if(offset>domain-length)throw new InvalidDataException("4.1 Deform value range");var values=new double[(int)length];for(var c=0;c<values.Length;c++)values[c]=cursor.Float();key["offset"]=(int)offset;key["vertices"]=Numbers(values);}
                            var next=0d;if(f+1<frames){next=Time();if(next<=time)throw new InvalidDataException("4.1 Deform time order");Curve(key,time,next,1,ref seen,normalized:true);}keys.Add(key);time=next;
                        }
                        if(seen>beziers)throw new InvalidDataException("4.1 Deform Bezier count mismatch");var channel=new JsonObject();Add(channel,"deform",keys);items.Add(target,channel);
                        deformTracks.Add(new((int)slot,target,nameIndex,new(trackAt,cursor.Offset-trackAt)));
                    }
                }
                attachments.Add("default",deformSlots);
            }
            if(attachments.Count>0)animation["attachments"]=attachments;var attachmentSpan=new ByteSpan(attachmentAt,cursor.Offset-attachmentAt);
            var drawFrames=cursor.VarUInt();if(drawFrames>0)
            {
                Frames(drawFrames,1);var keys=new JsonArray();var previous=-1d;
                for(var f=0;f<drawFrames;f++)
                {
                    var time=Time();if(time<=previous)throw new InvalidDataException("4.1 draw order time order");previous=time;var count=cursor.VarUInt();if(count>slots.Slots.Length||count>(1000000-valueTotal)/2||count>(original.Length-cursor.Offset)/2)throw new InvalidDataException("4.1 draw order offset count/budget");valueTotal+=(int)count*2;var offsets=new JsonArray();var seenSlots=new HashSet<int>();var destinations=new HashSet<int>();var last=-1;
                    for(var o=0;o<count;o++){var index=cursor.VarUInt();var delta=cursor.VarUInt();if(index>=slots.Slots.Length||index<=last||delta>=slots.Slots.Length-index||!seenSlots.Add((int)index)||!destinations.Add((int)(index+delta)))throw new InvalidDataException("4.1 draw order offset range/order");last=(int)index;offsets.Add(new JsonObject{["slot"]=slots.Slots[(int)index].Name,["offset"]=(int)delta});}
                    keys.Add(new JsonObject{["time"]=time,["offsets"]=offsets});
                }
                Add(animation,"drawOrder",keys);
            }
            if(cursor.VarUInt()!=0)throw new NotSupportedException("4.1 binary event timelines pending");
            // 4.1.11 exports may leave the timeline count at zero; section counts still bound every track.
            if(!(Header.Version=="4.1.11"&&declared==0)&&trackTotal-firstTrack!=declared)throw new InvalidDataException("4.1 animation declared track count mismatch");animations.Add(name,animation);infos.Add(new(name,trackTotal-firstTrack,frameTotal-firstFrame,duration,new(at,cursor.Offset-at),issues.ToArray(),countSpan,attachmentSpan,deformTracks.ToArray(),attachmentKeys.ToArray()));
        }
        if(cursor.Offset!=original.Length)throw new InvalidDataException("4.1 animation trailing bytes");return new(animations,infos.ToArray(),new(start,cursor.Offset-start),frameTotal,valueTotal);
    }
}
