namespace IndependentDev;

internal sealed record BinaryCurve3855(byte Kind,float[] Controls,ByteSpan Span,ByteSpan[] ControlSpans);
internal sealed record BinaryRotationFrame3855(float Time,float Angle,ByteSpan Span,ByteSpan TimeSpan,ByteSpan AngleSpan,BinaryCurve3855? Curve);
internal sealed record BinaryRotation3855(int Bone,BinaryRotationFrame3855[] Frames,ByteSpan Span,ByteSpan BoneSpan,ByteSpan CountSpan,ByteSpan TypeSpan,ByteSpan FrameCountSpan);
internal sealed record BinaryVectorFrame3855(float Time,float X,float Y,ByteSpan Span,ByteSpan TimeSpan,ByteSpan XSpan,ByteSpan YSpan,BinaryCurve3855? Curve);
internal sealed record BinaryVector3855(int Bone,BinaryVectorFrame3855[] Frames,ByteSpan Span,ByteSpan BoneSpan,ByteSpan CountSpan,ByteSpan TypeSpan,ByteSpan FrameCountSpan);
internal sealed record BinaryAttachmentFrame(float Time,string? Name,uint NameIndex,ByteSpan Span,ByteSpan TimeSpan,ByteSpan NameSpan);
internal sealed record BinarySlotAttachment(int SlotIndex,BinaryAttachmentFrame[] Frames,ByteSpan Span,ByteSpan SlotSpan,ByteSpan CountSpan,ByteSpan TypeSpan,ByteSpan FrameCountSpan);
internal sealed record BinaryColorFrame3855(float Time,uint Color,ByteSpan Span,ByteSpan TimeSpan,ByteSpan ColorSpan,BinaryCurve3855? Curve,uint? DarkColor=null,ByteSpan? DarkSpan=null);
internal sealed record BinarySlotColor3855(int SlotIndex,BinaryColorFrame3855[] Frames,ByteSpan Span,ByteSpan SlotSpan,ByteSpan CountSpan,ByteSpan TypeSpan,ByteSpan FrameCountSpan);
internal sealed record BinaryIkFrame3855(float Time,float Mix,bool BendPositive,ByteSpan Span,ByteSpan TimeSpan,ByteSpan MixSpan,ByteSpan SoftnessSpan,ByteSpan BendSpan,ByteSpan CompressSpan,ByteSpan StretchSpan);
internal sealed record BinaryIkTimeline3855(int Constraint,BinaryIkFrame3855[] Frames,ByteSpan Span,ByteSpan ConstraintSpan,ByteSpan FrameCountSpan);
internal sealed record BinaryTransformFrame3855(float Time,float RotateMix,ByteSpan Span,ByteSpan TimeSpan,ByteSpan RotateMixSpan,ByteSpan[] ZeroMixSpans);
internal sealed record BinaryTransformTimeline3855(int Constraint,BinaryTransformFrame3855[] Frames,ByteSpan Span,ByteSpan ConstraintSpan,ByteSpan FrameCountSpan);
internal sealed record BinaryDeformFrame3855(float Time,uint Offset,float[] Values,ByteSpan Span,ByteSpan TimeSpan,ByteSpan CountSpan,ByteSpan? OffsetSpan,ByteSpan[] ValueSpans,BinaryCurve3855? Curve);
internal sealed record BinaryDeformTimeline3855(string Name,uint NameIndex,int Domain,BinaryDeformFrame3855[] Frames,ByteSpan Span,ByteSpan NameSpan,ByteSpan CountSpan);
internal sealed record BinaryDeformSlot3855(int SlotIndex,BinaryDeformTimeline3855[] Timelines,ByteSpan Span,ByteSpan SlotSpan,ByteSpan CountSpan);
internal sealed record BinaryDeformAnimation3855(string Name,BinaryDeformSlot3855[] Slots,ByteSpan Span,ByteSpan NameSpan,ByteSpan[] OtherTrackCountSpans,ByteSpan SkinCountSpan,ByteSpan? SkinIndexSpan,ByteSpan? SlotCountSpan,ByteSpan DrawOrderSpan,ByteSpan EventCountSpan,BinaryRotation3855[] Rotations,BinaryVector3855[] Translations,BinaryVector3855[] Scales,BinaryVector3855[] Shears,BinarySlotAttachment[] SlotAttachments,BinarySlotColor3855[] SlotColors,BinaryIkTimeline3855[] Ik,BinaryTransformTimeline3855[] Transforms);
internal sealed record BinaryDeformBody3855(BinarySetup3855 Setup,BinaryDeformAnimation3855[] Animations,ByteSpan Span,int EncodedTextBytes,int FrameCount,int ValueCount);

internal sealed partial class BinaryDocument
{
    public BinaryDeformBody3855 ReadDeform3855()=>ReadAnimation3855(false);
    public BinaryDeformBody3855 ReadAnimation3855()=>ReadAnimation3855(true);
    BinaryDeformBody3855 ReadAnimation3855(bool allowAnimationTracks)
    {
        var setup=ReadSetup3855();var metadata=ReadPublicMetadata38();var start=setup.Span.Offset+setup.Span.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);
        if(setup.AnimationCount>(original.Length-start)/10)throw new InvalidDataException("Binary Deform animation count");
        var targets=new Dictionary<(int Slot,string Name),(BinaryAttachment Attachment,int Domain)>();
        foreach(var group in setup.DefaultSkin.SlotGroups)foreach(var attachment in group.Attachments){var mesh=attachment.Mesh;var domain=mesh is null?0:2*(mesh.Weights.Length==0?mesh.Vertices.Length:mesh.Weights.Sum(v=>v.Influences.Length));targets.Add((group.SlotIndex,attachment.Placeholder),(attachment,domain));}
        var animations=new BinaryDeformAnimation3855[(int)setup.AnimationCount];var names=new HashSet<string>(StringComparer.Ordinal);var textBytes=setup.EncodedTextBytes;var frameTotal=0;var valueTotal=0;var trackTotal=0;
        BinaryCurve3855? Curve(bool hasNext)
        {
            if(!hasNext)return null;var at=cursor.Offset;var kind=cursor.Bytes(1)[0];float[] controls=[];ByteSpan[] spans=[];
            if(kind==2){controls=new float[4];spans=new ByteSpan[4];for(var c=0;c<4;c++){var fieldAt=cursor.Offset;controls[c]=cursor.Float();spans[c]=new(fieldAt,4);}if(controls[0] is <0 or >1||controls[2] is <0 or >1)throw new InvalidDataException("Binary Deform curve time controls");}
            else if(kind is not(0 or 1))throw new NotSupportedException("3.8.55 binary Deform curve type pending");return new(kind,controls,new(at,cursor.Offset-at),spans);
        }
        for(var a=0;a<animations.Length;a++)
        {
            var at=cursor.Offset;var name=cursor.String(1024*1024-textBytes);if(string.IsNullOrEmpty(name)||name.Contains('\0')||!names.Add(name))throw new InvalidDataException("Binary animation name");textBytes+=System.Text.Encoding.UTF8.GetByteCount(name);var nameSpan=new ByteSpan(at,cursor.Offset-at);
            // shortcut: only pinned increasing bone-channel groups; unobserved mixed groups remain pending.
            var other=new ByteSpan[5];void Empty(int c){var countAt=cursor.Offset;if(cursor.VarUInt()!=0)throw new NotSupportedException("3.8.55 non-Deform binary animation tracks pending");other[c]=new(countAt,cursor.Offset-countAt);}var slotAt=cursor.Offset;var slotGroups=cursor.VarUInt();other[0]=new(slotAt,cursor.Offset-slotAt);if(slotGroups!=0&&!allowAnimationTracks)throw new NotSupportedException("3.8.55 non-Deform binary animation tracks pending");var slotLimit=setup.DefaultSkin.TransformSection.IkSection.SlotSection.Slots.Length;if(slotGroups>slotLimit||slotGroups>(original.Length-cursor.Offset)/9)throw new InvalidDataException("Binary attachment slot count");var slotAttachments=new List<BinarySlotAttachment>();var slotColors=new List<BinarySlotColor3855>();var seenAttachmentSlots=new HashSet<int>();
            for(var s=0;s<slotGroups;s++)
            {
                var groupAt=cursor.Offset;var slot=cursor.VarUInt();if(slot>=slotLimit||!seenAttachmentSlots.Add((int)slot))throw new InvalidDataException("Binary attachment slot reference");var slotSpan=new ByteSpan(groupAt,cursor.Offset-groupAt);var countAt=cursor.Offset;var timelines=cursor.VarUInt();if(timelines is not(1 or 2))throw new NotSupportedException("Binary slot timeline count pending");var countSpan=new ByteSpan(countAt,cursor.Offset-countAt);
                for(var t=0;t<timelines;t++)
                {
                    // shortcut: twoColor requires linear keys, expand curves only with paired export evidence.
                    var typeAt=cursor.Offset;var type=cursor.Bytes(1)[0];if(type is not(0 or 1 or 2)||timelines==2&&(t==0?type is not(1 or 2):type!=0))throw new NotSupportedException("Binary slot timeline type/order pending");var frameAt=cursor.Offset;var count=cursor.VarUInt();if(type==2&&setup.DefaultSkin.TransformSection.IkSection.SlotSection.Slots[(int)slot].DarkColor is null)throw new InvalidDataException("Binary twoColor requires setup dark");var width=type==0?1:type==2?7:4;if(count<1||count>100000-frameTotal||count>(1000000-valueTotal)/width||count>(original.Length-cursor.Offset)/(type==0?5:type==2?12:8))throw new InvalidDataException("Binary slot frame count");if(trackTotal>=65536)throw new InvalidDataException("Binary animation timeline count");trackTotal++;frameTotal+=(int)count;valueTotal+=(int)count*width;var frameCountSpan=new ByteSpan(frameAt,cursor.Offset-frameAt);float previous=-1;
                    if(type==0)
                    {
                        var frames=new BinaryAttachmentFrame[(int)count];for(var f=0;f<frames.Length;f++)
                        {
                            var atFrame=cursor.Offset;var time=cursor.Float();if(time<0||time<=previous)throw new InvalidDataException("Binary attachment time order");previous=time;var nameAt=cursor.Offset;var index=cursor.VarUInt();if(index>metadata.Strings.Length)throw new InvalidDataException("Binary attachment name reference");var attachment=index==0?null:metadata.Strings[(int)index-1].Value;if(index!=0)
                            {
                                if(string.IsNullOrEmpty(attachment)||attachment.Length>1024*1024-textBytes)throw new InvalidDataException("Binary attachment timeline text budget");var length=System.Text.Encoding.UTF8.GetByteCount(attachment);if(length>1024*1024-textBytes)throw new InvalidDataException("Binary attachment timeline text budget");textBytes+=length;if(!targets.ContainsKey(((int)slot,attachment)))throw new InvalidDataException("Binary attachment target reference");
                            }
                            frames[f]=new(time,attachment,index,new(atFrame,cursor.Offset-atFrame),new(atFrame,4),new(nameAt,cursor.Offset-nameAt));
                        }
                        slotAttachments.Add(new((int)slot,frames,new(groupAt,cursor.Offset-groupAt),slotSpan,countSpan,new(typeAt,1),frameCountSpan));
                    }
                    else
                    {
                        var frames=new BinaryColorFrame3855[(int)count];for(var f=0;f<frames.Length;f++){var atFrame=cursor.Offset;var time=cursor.Float();if(time<0||time<=previous)throw new InvalidDataException("Binary color time order");previous=time;var colorAt=cursor.Offset;var color=cursor.Int();uint? dark=null;ByteSpan? darkSpan=null;if(type==2){var darkAt=cursor.Offset;dark=cursor.Int();darkSpan=new(darkAt,4);if(dark>0xffffff)throw new NotSupportedException("Binary twoColor dark encoding pending");}var curve=Curve(f+1<frames.Length);if(type==2&&curve is{Kind:not 0})throw new NotSupportedException("Binary twoColor non-linear curve pending");frames[f]=new(time,color,new(atFrame,cursor.Offset-atFrame),new(atFrame,4),new(colorAt,4),curve,dark,darkSpan);}slotColors.Add(new((int)slot,frames,new(groupAt,cursor.Offset-groupAt),slotSpan,countSpan,new(typeAt,1),frameCountSpan));
                    }
                }
                if(timelines==2)slotColors[^1]=slotColors[^1] with{Span=new(groupAt,cursor.Offset-groupAt)};
            }
            var boneCountAt=cursor.Offset;var boneCount=cursor.VarUInt();other[1]=new(boneCountAt,cursor.Offset-boneCountAt);if(boneCount!=0&&!allowAnimationTracks)throw new NotSupportedException("3.8.55 non-Deform binary animation tracks pending");var boneLimit=setup.DefaultSkin.TransformSection.IkSection.SlotSection.BoneSection.Bones.Length;if(boneCount>boneLimit||boneCount>(original.Length-cursor.Offset)/12)throw new InvalidDataException("Binary rotation bone count");var rotations=new List<BinaryRotation3855>();var translations=new List<BinaryVector3855>();var scales=new List<BinaryVector3855>();var shears=new List<BinaryVector3855>();var seenBones=new HashSet<int>();
            for(var b=0;b<boneCount;b++)
            {
                var boneAt=cursor.Offset;var bone=cursor.VarUInt();if(bone>=boneLimit||!seenBones.Add((int)bone))throw new InvalidDataException("Binary rotation bone reference");var boneSpan=new ByteSpan(boneAt,cursor.Offset-boneAt);var countAt=cursor.Offset;var timelines=cursor.VarUInt();if(timelines is <1 or >4)throw new NotSupportedException("Binary bone timeline count pending");var countSpan=new ByteSpan(countAt,cursor.Offset-countAt);var mask=0;var previousType=-1;
                for(var t=0;t<timelines;t++)
                {
                var typeAt=cursor.Offset;var type=cursor.Bytes(1)[0];if(type>3||type<=previousType)throw new NotSupportedException("Binary bone timeline type/order pending");previousType=type;mask|=1<<type;var frameAt=cursor.Offset;var count=cursor.VarUInt();var width=type==0?1:2;if(count is <1||count>100000-frameTotal||count>(original.Length-cursor.Offset)/(4+4*width)||count>(1000000-valueTotal)/width)throw new InvalidDataException("Binary bone frame count");frameTotal+=(int)count;valueTotal+=(int)count*width;if(trackTotal>=65536)throw new InvalidDataException("Binary animation timeline count");trackTotal++;var frameSpan=new ByteSpan(frameAt,cursor.Offset-frameAt);float previous=-1;
                if(type==0)
                {
                    var frames=new BinaryRotationFrame3855[(int)count];for(var f=0;f<frames.Length;f++){var fieldAt=cursor.Offset;var time=cursor.Float();if(time<0||time<=previous)throw new InvalidDataException("Binary rotation time order");previous=time;var angleAt=cursor.Offset;var angle=cursor.Float();var curve=Curve(f+1<frames.Length);frames[f]=new(time,angle,new(fieldAt,cursor.Offset-fieldAt),new(fieldAt,4),new(angleAt,4),curve);}rotations.Add(new((int)bone,frames,new(boneAt,cursor.Offset-boneAt),boneSpan,countSpan,new(typeAt,1),frameSpan));
                }
                else
                {
                    var frames=new BinaryVectorFrame3855[(int)count];for(var f=0;f<frames.Length;f++){var fieldAt=cursor.Offset;var time=cursor.Float();if(time<0||time<=previous)throw new InvalidDataException("Binary vector time order");previous=time;var xAt=cursor.Offset;var x=cursor.Float();var yAt=cursor.Offset;var y=cursor.Float();var curve=Curve(f+1<frames.Length);if(type==3&&curve is not(null or{Kind:0}))throw new NotSupportedException("Binary shear curve evidence pending");frames[f]=new(time,x,y,new(fieldAt,cursor.Offset-fieldAt),new(fieldAt,4),new(xAt,4),new(yAt,4),curve);}var track=new BinaryVector3855((int)bone,frames,new(boneAt,cursor.Offset-boneAt),boneSpan,countSpan,new(typeAt,1),frameSpan);if(type==1)translations.Add(track);else if(type==2)scales.Add(track);else shears.Add(track);
                }
                }
                // Bits: rotate1, translate2, scale4, shear8. These four mixed masks have no pinned source evidence.
                if(mask is 9 or 10 or 12 or 13)throw new NotSupportedException("Binary bone timeline combination pending");
                var groupSpan=new ByteSpan(boneAt,cursor.Offset-boneAt);if((mask&1)!=0)rotations[^1]=rotations[^1] with{Span=groupSpan};if((mask&2)!=0)translations[^1]=translations[^1] with{Span=groupSpan};if((mask&4)!=0)scales[^1]=scales[^1] with{Span=groupSpan};if((mask&8)!=0)shears[^1]=shears[^1] with{Span=groupSpan};
            }
            var ikAt=cursor.Offset;var ikCount=cursor.VarUInt();other[2]=new(ikAt,cursor.Offset-ikAt);if(ikCount!=0&&!allowAnimationTracks)throw new NotSupportedException("3.8.55 non-Deform binary animation tracks pending");var ikLimit=setup.DefaultSkin.TransformSection.IkSection.Constraints.Length;if(ikCount>ikLimit||ikCount>(original.Length-cursor.Offset)/17)throw new InvalidDataException("Binary IK timeline count");var ikTracks=new BinaryIkTimeline3855[(int)ikCount];var seenIk=new HashSet<int>();
            for(var i=0;i<ikTracks.Length;i++)
            {
                var trackAt=cursor.Offset;var index=cursor.VarUInt();if(index>=ikLimit||!seenIk.Add((int)index))throw new InvalidDataException("Binary IK timeline reference");var indexSpan=new ByteSpan(trackAt,cursor.Offset-trackAt);var countAt=cursor.Offset;var count=cursor.VarUInt();if(count==0)throw new InvalidDataException("Binary IK frame count");
                // shortcut: pinned3.8.55 IK animation evidence contains only single keys; extend after multi-key evidence.
                if(count!=1)throw new NotSupportedException("Binary IK multi-key evidence pending");if(frameTotal>=100000||valueTotal>1000000-2||trackTotal>=65536)throw new InvalidDataException("Binary IK timeline budget");frameTotal++;valueTotal+=2;trackTotal++;var countSpan=new ByteSpan(countAt,cursor.Offset-countAt);
                var frameAt=cursor.Offset;var time=cursor.Float();if(time<0)throw new InvalidDataException("Binary IK time");var mixAt=cursor.Offset;var mix=cursor.Float();if(mix is <0 or >1)throw new InvalidDataException("Binary IK timeline mix");var softnessAt=cursor.Offset;var softness=cursor.Float();if(softness<0)throw new InvalidDataException("Binary IK timeline softness");if(softness!=0)throw new NotSupportedException("Binary IK timeline softness pending");var bendAt=cursor.Offset;var positive=cursor.Bytes(1)[0] switch{1=>true,255=>false,_=>throw new InvalidDataException("Binary IK timeline bend")};var compressAt=cursor.Offset;if(cursor.Boolean())throw new NotSupportedException("Binary IK timeline compress pending");var stretchAt=cursor.Offset;if(cursor.Boolean())throw new NotSupportedException("Binary IK timeline stretch pending");
                var frame=new BinaryIkFrame3855(time,mix,positive,new(frameAt,cursor.Offset-frameAt),new(frameAt,4),new(mixAt,4),new(softnessAt,4),new(bendAt,1),new(compressAt,1),new(stretchAt,1));ikTracks[i]=new((int)index,[frame],new(trackAt,cursor.Offset-trackAt),indexSpan,countSpan);
            }
            var transformAt=cursor.Offset;var transformCount=cursor.VarUInt();other[3]=new(transformAt,cursor.Offset-transformAt);if(transformCount!=0&&!allowAnimationTracks)throw new NotSupportedException("3.8.55 non-Deform binary animation tracks pending");var transformLimit=setup.DefaultSkin.TransformSection.Constraints.Length;if(transformCount>transformLimit||transformCount>(original.Length-cursor.Offset)/22||transformCount>65536-trackTotal||transformCount>100000-frameTotal||transformCount>(1000000-valueTotal)/4)throw new InvalidDataException("Binary Transform timeline count");var transformTracks=new BinaryTransformTimeline3855[(int)transformCount];var seenTransforms=new HashSet<int>();
            for(var i=0;i<transformTracks.Length;i++)
            {
                var trackAt=cursor.Offset;var index=cursor.VarUInt();if(index>=transformLimit||!seenTransforms.Add((int)index))throw new InvalidDataException("Binary Transform timeline reference");var indexSpan=new ByteSpan(trackAt,cursor.Offset-trackAt);var countAt=cursor.Offset;var count=cursor.VarUInt();if(count==0)throw new InvalidDataException("Binary Transform frame count");
                // shortcut: pinned3.8.55 Transform animation has one rotation-mix key and three zero mixes; expand after new evidence.
                if(count!=1)throw new NotSupportedException("Binary Transform multi-key evidence pending");frameTotal++;valueTotal+=4;trackTotal++;var countSpan=new ByteSpan(countAt,cursor.Offset-countAt);var frameAt=cursor.Offset;var time=cursor.Float();if(time<0)throw new InvalidDataException("Binary Transform time");var mixAt=cursor.Offset;var mix=cursor.Float();if(mix is <0 or >1)throw new InvalidDataException("Binary Transform rotation mix");var zeroSpans=new ByteSpan[3];for(var z=0;z<3;z++){var zeroAt=cursor.Offset;if(cursor.Float()!=0)throw new NotSupportedException("Binary Transform nonrotation mix evidence pending");zeroSpans[z]=new(zeroAt,4);}
                var frame=new BinaryTransformFrame3855(time,mix,new(frameAt,cursor.Offset-frameAt),new(frameAt,4),new(mixAt,4),zeroSpans);transformTracks[i]=new((int)index,[frame],new(trackAt,cursor.Offset-trackAt),indexSpan,countSpan);
            }
            Empty(4);
            var skinAt=cursor.Offset;var skins=cursor.VarUInt();if(skins>1)throw new NotSupportedException("3.8.55 additional binary Deform skins pending");var skinCountSpan=new ByteSpan(skinAt,cursor.Offset-skinAt);ByteSpan? skinIndexSpan=null;ByteSpan? slotCountSpan=null;BinaryDeformSlot3855[] groups=[];
            if(skins==1)
            {
                var skinIndexAt=cursor.Offset;if(cursor.VarUInt()!=0)throw new InvalidDataException("Binary Deform skin reference");skinIndexSpan=new(skinIndexAt,cursor.Offset-skinIndexAt);
                var slotsAt=cursor.Offset;var slots=cursor.VarUInt();if(slots>setup.DefaultSkin.TransformSection.IkSection.SlotSection.Slots.Length||slots>(original.Length-cursor.Offset)/2)throw new InvalidDataException("Binary Deform slot count");slotCountSpan=new(slotsAt,cursor.Offset-slotsAt);groups=new BinaryDeformSlot3855[(int)slots];var seenSlots=new HashSet<int>();
                for(var g=0;g<groups.Length;g++)
                {
                    var groupAt=cursor.Offset;var slot=cursor.VarUInt();if(slot>=setup.DefaultSkin.TransformSection.IkSection.SlotSection.Slots.Length||!seenSlots.Add((int)slot))throw new InvalidDataException("Binary Deform slot reference");var slotSpan=new ByteSpan(groupAt,cursor.Offset-groupAt);
                    var tracksAt=cursor.Offset;var tracks=cursor.VarUInt();if(tracks>65536-trackTotal||tracks>(original.Length-cursor.Offset)/7)throw new InvalidDataException("Binary Deform timeline count");trackTotal+=(int)tracks;var trackCountSpan=new ByteSpan(tracksAt,cursor.Offset-tracksAt);var timelines=new BinaryDeformTimeline3855[(int)tracks];var seenNames=new HashSet<string>(StringComparer.Ordinal);
                    for(var t=0;t<timelines.Length;t++)
                    {
                        var timelineAt=cursor.Offset;var nameIndex=cursor.VarUInt();if(nameIndex is <1||nameIndex>metadata.Strings.Length)throw new InvalidDataException("Binary Deform attachment reference");var key=metadata.Strings[(int)nameIndex-1].Value;if(string.IsNullOrEmpty(key)||!seenNames.Add(key)||!targets.TryGetValue(((int)slot,key),out var target))throw new InvalidDataException("Binary Deform mesh reference");
                        if(target.Attachment.Mesh is null)throw new NotSupportedException("3.8.55 non-mesh binary Deform pending");if(target.Attachment.Name is{} alias&&alias!=key)throw new NotSupportedException("3.8.55 aliased binary Deform pending");var referenceSpan=new ByteSpan(timelineAt,cursor.Offset-timelineAt);
                        var framesAt=cursor.Offset;var frameCount=cursor.VarUInt();if(frameCount is <1||frameCount>100000-frameTotal||frameCount>(original.Length-cursor.Offset)/5)throw new InvalidDataException("Binary Deform frame count");frameTotal+=(int)frameCount;var frameCountSpan=new ByteSpan(framesAt,cursor.Offset-framesAt);var frames=new BinaryDeformFrame3855[(int)frameCount];float previous=-1;
                        for(var f=0;f<frames.Length;f++)
                        {
                            var frameAt=cursor.Offset;var timeSpan=new ByteSpan(frameAt,4);var time=cursor.Float();if(time<0||time<=previous)throw new InvalidDataException("Binary Deform time order");previous=time;
                            var valuesAt=cursor.Offset;var values=cursor.VarUInt();if(values>target.Domain||values>1000000-valueTotal)throw new InvalidDataException("Binary Deform value count");if(values%2!=0)throw new NotSupportedException("3.8.55 odd binary Deform components pending");var valuesSpan=new ByteSpan(valuesAt,cursor.Offset-valuesAt);uint offset=0;ByteSpan? offsetSpan=null;
                            if(values>0){var offsetAt=cursor.Offset;offset=cursor.VarUInt();if(offset>target.Domain||values>target.Domain-offset)throw new InvalidDataException("Binary Deform component range");if(offset%2!=0)throw new NotSupportedException("3.8.55 odd binary Deform offset pending");offsetSpan=new(offsetAt,cursor.Offset-offsetAt);}
                            var tail=f+1<frames.Length?1:0;if(original.Length-cursor.Offset<tail||values>(original.Length-cursor.Offset-tail)/4)throw new InvalidDataException("Binary Deform value bytes");valueTotal+=(int)values;var data=new float[(int)values];var spans=new ByteSpan[data.Length];for(var v=0;v<data.Length;v++){var valueAt=cursor.Offset;data[v]=cursor.Float();spans[v]=new(valueAt,4);}var curve=Curve(f+1<frames.Length);frames[f]=new(time,offset,data,new(frameAt,cursor.Offset-frameAt),timeSpan,valuesSpan,offsetSpan,spans,curve);
                        }
                        timelines[t]=new(key,nameIndex,target.Domain,frames,new(timelineAt,cursor.Offset-timelineAt),referenceSpan,frameCountSpan);
                    }
                    groups[g]=new((int)slot,timelines,new(groupAt,cursor.Offset-groupAt),slotSpan,trackCountSpan);
                }
            }
            var drawAt=cursor.Offset;if(cursor.VarUInt()!=0)throw new NotSupportedException("3.8.55 binary draw-order tracks pending");var drawSpan=new ByteSpan(drawAt,cursor.Offset-drawAt);var eventsAt=cursor.Offset;if(cursor.VarUInt()!=0)throw new NotSupportedException("3.8.55 binary event tracks pending");var eventSpan=new ByteSpan(eventsAt,cursor.Offset-eventsAt);animations[a]=new(name,groups,new(at,cursor.Offset-at),nameSpan,other,skinCountSpan,skinIndexSpan,slotCountSpan,drawSpan,eventSpan,rotations.ToArray(),translations.ToArray(),scales.ToArray(),shears.ToArray(),slotAttachments.ToArray(),slotColors.ToArray(),ikTracks,transformTracks);
        }
        if(cursor.Offset!=original.Length)throw new InvalidDataException("Binary Deform trailing bytes");return new(setup,animations,new(start,cursor.Offset-start),textBytes,frameTotal,valueTotal);
    }
}





