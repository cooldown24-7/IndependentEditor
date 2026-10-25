namespace IndependentDev;

internal sealed record BinaryEvent(uint NameIndex,string Name,int Int,float Float,string? Text,ByteSpan Span,ByteSpan NameSpan,ByteSpan IntSpan,ByteSpan FloatSpan,ByteSpan TextSpan,ByteSpan AudioSpan);
internal sealed record BinarySetup3855(BinaryDefaultSkin3855 DefaultSkin,BinaryEvent[] Events,uint AnimationCount,bool Complete,ByteSpan ExtraSkinsSpan,ByteSpan EventCountSpan,ByteSpan AnimationCountSpan,ByteSpan Span,int EncodedTextBytes);

internal sealed record BinarySetup4117(BinaryDefaultSkin4117 DefaultSkin,BinaryEvent[] Events,uint AnimationCount,bool Complete,ByteSpan ExtraSkinsSpan,ByteSpan EventCountSpan,ByteSpan AnimationCountSpan,ByteSpan Span,int EncodedTextBytes);
internal sealed record BinarySetupTail(BinaryEvent[] Events,uint AnimationCount,bool Complete,ByteSpan ExtraSkinsSpan,ByteSpan EventCountSpan,ByteSpan AnimationCountSpan,ByteSpan Span,int EncodedTextBytes);
internal sealed record BinaryAnimationSlots4117(string Name,uint AnimationCount,uint DeclaredTimelineCount,BinarySlotAttachment[] SlotAttachments,uint FollowingBoneGroupCount,ByteSpan Span,ByteSpan NameSpan,ByteSpan TimelineCountSpan,ByteSpan SlotGroupCountSpan,ByteSpan BoneGroupCountSpan,int FrameCount,int EncodedTextBytes);
internal sealed partial class BinaryDocument
{
    public BinarySetup3855 ReadSetup3855()
    {
        var skin=ReadDefaultSkin3855();var data=ReadSetupTail(ReadPublicMetadata38(),skin.Span.Offset+skin.Span.Length,skin.EncodedTextBytes,false);
        return new(skin,data.Events,data.AnimationCount,data.Complete,data.ExtraSkinsSpan,data.EventCountSpan,data.AnimationCountSpan,data.Span,data.EncodedTextBytes);
    }
    public BinarySetup4117 ReadSetup4117()
    {
        var skin=ReadDefaultSkin4117();var data=ReadSetupTail(ReadMetadata4117(),skin.Span.Offset+skin.Span.Length,skin.EncodedTextBytes,true);
        return new(skin,data.Events,data.AnimationCount,data.Complete,data.ExtraSkinsSpan,data.EventCountSpan,data.AnimationCountSpan,data.Span,data.EncodedTextBytes);
    }
    public BinaryAnimationSlots4117 ReadAnimationSlots4117()
    {
        if(Header.Version is not ("4.1.11" or "4.1.17"))throw new NotSupportedException("Animation slots profile requires exact 4.1.11/4.1.17");
        var setup=ReadSetup4117();if(setup.AnimationCount==0)throw new NotSupportedException("4.1.17 animation slots require positive animation count");var metadata=ReadMetadata4117();var slots=setup.DefaultSkin.PathSection.TransformSection.IkSection.SlotSection;var targets=setup.DefaultSkin.SlotGroups.SelectMany(group=>group.Attachments.Select(attachment=>(group.SlotIndex,attachment.Placeholder))).ToHashSet();var start=setup.AnimationCountSpan.Offset+setup.AnimationCountSpan.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);var textBytes=setup.EncodedTextBytes;
        var name=cursor.String(1024*1024-textBytes);if(string.IsNullOrEmpty(name)||name.Contains('\0'))throw new InvalidDataException("4.1.17 animation name");textBytes+=System.Text.Encoding.UTF8.GetByteCount(name);var nameSpan=new ByteSpan(start,cursor.Offset-start);var countAt=cursor.Offset;var declared=cursor.VarUInt();if(declared>65536||declared>original.Length-cursor.Offset)throw new InvalidDataException("4.1.17 animation timeline count");var countSpan=new ByteSpan(countAt,cursor.Offset-countAt);
        // shortcut: first4.1.11/4.1.17 animation attachment slots only; bone/remaining sections stay unparsed.
        var slotsAt=cursor.Offset;var groups=cursor.VarUInt();if(groups>slots.Slots.Length||groups>declared||groups>(original.Length-cursor.Offset)/9)throw new InvalidDataException("4.1.17 animation attachment slot count");var slotCountSpan=new ByteSpan(slotsAt,cursor.Offset-slotsAt);var timelines=new BinarySlotAttachment[(int)groups];var seen=new HashSet<int>();var frameTotal=0;
        for(var g=0;g<timelines.Length;g++)
        {
            var groupAt=cursor.Offset;var slot=cursor.VarUInt();if(slot>=slots.Slots.Length||!seen.Add((int)slot))throw new InvalidDataException("4.1.17 animation attachment slot reference");var slotSpan=new ByteSpan(groupAt,cursor.Offset-groupAt);var tracksAt=cursor.Offset;if(cursor.VarUInt()!=1)throw new NotSupportedException("4.1.17 non-attachment slot timelines pending");var tracksSpan=new ByteSpan(tracksAt,cursor.Offset-tracksAt);var typeAt=cursor.Offset;if(cursor.Bytes(1)[0]!=0)throw new NotSupportedException("4.1.17 slot timeline type pending");var typeSpan=new ByteSpan(typeAt,1);var framesAt=cursor.Offset;var count=cursor.VarUInt();if(count<1||count>100000-frameTotal||count>(original.Length-cursor.Offset)/5)throw new InvalidDataException("4.1.17 attachment frame count");frameTotal+=(int)count;var framesSpan=new ByteSpan(framesAt,cursor.Offset-framesAt);var frames=new BinaryAttachmentFrame[(int)count];float previous=-1;
            for(var f=0;f<frames.Length;f++)
            {
                var frameAt=cursor.Offset;var time=cursor.Float();if(time<0||time<=previous)throw new InvalidDataException("4.1.17 attachment time order");previous=time;var refAt=cursor.Offset;var index=cursor.VarUInt();if(index>metadata.Strings.Length)throw new InvalidDataException("4.1.17 attachment name reference");var attachment=index==0?null:metadata.Strings[(int)index-1].Value;
                if(index!=0)
                {
                    if(string.IsNullOrEmpty(attachment)||attachment.Contains('\0')||!targets.Contains(((int)slot,attachment)))throw new InvalidDataException("4.1.17 attachment timeline target");if(attachment.Length>1024*1024-textBytes)throw new InvalidDataException("4.1.17 attachment timeline text budget");var size=System.Text.Encoding.UTF8.GetByteCount(attachment);if(size>1024*1024-textBytes)throw new InvalidDataException("4.1.17 attachment timeline text budget");textBytes+=size;
                }
                frames[f]=new(time,attachment,index,new(frameAt,cursor.Offset-frameAt),new(frameAt,4),new(refAt,cursor.Offset-refAt));
            }
            timelines[g]=new((int)slot,frames,new(groupAt,cursor.Offset-groupAt),slotSpan,tracksSpan,typeSpan,framesSpan);
        }
        var boneAt=cursor.Offset;var bones=cursor.VarUInt();if(bones>slots.BoneSection.Bones.Length||bones>declared-groups)throw new InvalidDataException("4.1.17 animation bone group count");return new(name,setup.AnimationCount,declared,timelines,bones,new(start,cursor.Offset-start),nameSpan,countSpan,slotCountSpan,new(boneAt,cursor.Offset-boneAt),frameTotal,textBytes);
    }
    BinarySetupTail ReadSetupTail(BinaryMetadata metadata,int start,int encodedTextBytes,bool modern)
    {
        var version=modern?"4.1.17":"3.8.55";var cursor=new BinaryCursor(original);cursor.Skip(start);
        // shortcut: pinned4.1.17 setup covers default events0/0/empty and no extra skins/audio; only animation0 plus EOF fully consumes.
        if(cursor.VarUInt()!=0)throw new NotSupportedException(version+" additional binary skins pending");var extraSpan=new ByteSpan(start,cursor.Offset-start);
        var countAt=cursor.Offset;var count=cursor.VarUInt();if(count>8192||original.Length-cursor.Offset<1||count>(original.Length-cursor.Offset-1)/8)throw new InvalidDataException("Binary event count");var countSpan=new ByteSpan(countAt,cursor.Offset-countAt);
        var events=new BinaryEvent[(int)count];var names=new HashSet<string>(StringComparer.Ordinal);var textBytes=encodedTextBytes;
        string? Text(){var value=cursor.String(1024*1024-textBytes);textBytes+=System.Text.Encoding.UTF8.GetByteCount(value??"");return value;}
        for(var i=0;i<events.Length;i++)
        {
            var at=cursor.Offset;var nameIndex=cursor.VarUInt();if(nameIndex is <1||nameIndex>metadata.Strings.Length)throw new InvalidDataException("Binary event name reference");var name=metadata.Strings[(int)nameIndex-1].Value;if(string.IsNullOrEmpty(name)||name.Contains('\0')||!names.Add(name))throw new InvalidDataException("Binary event name");var nameSpan=new ByteSpan(at,cursor.Offset-at);
            var intAt=cursor.Offset;var integer=cursor.VarInt();var intSpan=new ByteSpan(intAt,cursor.Offset-intAt);var floatSpan=new ByteSpan(cursor.Offset,4);var number=cursor.Float();var textAt=cursor.Offset;var text=Text();var textSpan=new ByteSpan(textAt,cursor.Offset-textAt);var audioAt=cursor.Offset;if(Text() is not null)throw new NotSupportedException(version+" binary event audio pending");var audioSpan=new ByteSpan(audioAt,cursor.Offset-audioAt);
            if(modern&&(integer!=0||number!=0||text!=""))throw new NotSupportedException("4.1.17 nondefault event settings pending");
            events[i]=new(nameIndex,name,integer,number,text,new(at,cursor.Offset-at),nameSpan,intSpan,floatSpan,textSpan,audioSpan);
        }
        var animationAt=cursor.Offset;var animations=cursor.VarUInt();if(animations>8192||animations>original.Length-cursor.Offset)throw new InvalidDataException("Binary animation count");var animationSpan=new ByteSpan(animationAt,cursor.Offset-animationAt);
        if(animations==0&&cursor.Offset!=original.Length)throw new InvalidDataException("Binary setup trailing bytes");
        return new(events,animations,animations==0,extraSpan,countSpan,animationSpan,new(start,cursor.Offset-start),textBytes);
    }
}
