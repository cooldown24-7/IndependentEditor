using System.Security.Cryptography;
using System.Text.Json;
namespace IndependentDev;

internal sealed record ByteSpan(int Offset,int Length);
internal sealed record SkeletonHeader(string Version,string Hash,ByteSpan HashSpan,ByteSpan VersionSpan,int BodyOffset,string LayoutEvidence);
internal sealed record BinarySharedString(string? Value,ByteSpan Span);
internal sealed record BinaryMetadata(float X,float Y,float Width,float Height,bool Nonessential,float? Fps,string? Images,string? Audio,BinarySharedString[] Strings,ByteSpan Span,int EncodedTextBytes);
internal sealed record BinaryRootPrefix(int BoneCount,ByteSpan CountSpan,string Name,ByteSpan NameSpan,float Rotation,float X,float Y,float ScaleX,float ScaleY,float ShearX,float ShearY,float Length,ByteSpan[] FloatSpans,ByteSpan Span);
internal sealed record BinaryBone(string Name,int ParentIndex,string TransformMode,float Rotation,float X,float Y,float ScaleX,float ScaleY,float ShearX,float ShearY,float Length,uint Color,ByteSpan Span,ByteSpan NameSpan,ByteSpan? ParentSpan,ByteSpan[] FloatSpans,ByteSpan TransformSpan,ByteSpan SkinSpan,ByteSpan ColorSpan);
internal sealed record BinaryBoneSection(BinaryBone[] Bones,ByteSpan CountSpan,ByteSpan Span,int EncodedTextBytes);
internal sealed record BinarySlot(string Name,int BoneIndex,uint Color,uint AttachmentIndex,string? Attachment,string Blend,ByteSpan Span,ByteSpan NameSpan,ByteSpan BoneSpan,ByteSpan ColorSpan,ByteSpan DarkSpan,ByteSpan AttachmentSpan,ByteSpan BlendSpan,uint? DarkColor);
internal sealed record BinarySlotSection(BinaryBoneSection BoneSection,BinarySlot[] Slots,ByteSpan CountSpan,ByteSpan Span,int EncodedTextBytes);
internal sealed record BinaryIk(string Name,uint Order,int[] Bones,int Target,float Mix,bool BendPositive,ByteSpan Span,ByteSpan NameSpan,ByteSpan OrderSpan,ByteSpan SkinSpan,ByteSpan BoneCountSpan,ByteSpan[] BoneSpans,ByteSpan TargetSpan,ByteSpan MixSpan,ByteSpan SoftnessSpan,ByteSpan BendSpan,ByteSpan CompressSpan,ByteSpan StretchSpan,ByteSpan UniformSpan,bool Compress,bool Stretch,bool Uniform,float Softness);
internal sealed record BinaryIkSection(BinarySlotSection SlotSection,BinaryIk[] Constraints,ByteSpan CountSpan,ByteSpan Span,int EncodedTextBytes);
internal sealed record BinaryTransform3855(string Name,uint Order,int[] Bones,int Target,float Rotation,float X,float Y,float ScaleX,float ScaleY,float ShearY,float RotateMix,float TranslateMix,float ScaleMix,float ShearMix,ByteSpan Span,ByteSpan NameSpan,ByteSpan OrderSpan,ByteSpan SkinSpan,ByteSpan BoneCountSpan,ByteSpan[] BoneSpans,ByteSpan TargetSpan,ByteSpan LocalSpan,ByteSpan RelativeSpan,ByteSpan[] FloatSpans,bool SkinRequired);
internal sealed record BinaryTransformSection3855(BinaryIkSection IkSection,BinaryTransform3855[] Constraints,ByteSpan CountSpan,ByteSpan Span,int EncodedTextBytes);
internal sealed record BinaryTransform4117(string Name,uint Order,bool SkinRequired,int Bone,int Target,float[] Values,ByteSpan Span,ByteSpan NameSpan,ByteSpan OrderSpan,ByteSpan SkinSpan,ByteSpan BoneCountSpan,ByteSpan BoneSpan,ByteSpan TargetSpan,ByteSpan LocalSpan,ByteSpan RelativeSpan,ByteSpan[] FloatSpans,bool Local,bool Relative);
internal sealed record BinaryTransformSection4117(BinaryIkSection IkSection,BinaryTransform4117[] Constraints,ByteSpan CountSpan,ByteSpan Span,int EncodedTextBytes);
internal sealed record BinaryPath4117(string Name,uint Order,int[] Bones,int Target,byte PositionMode,byte SpacingMode,byte RotateMode,float[] Values,ByteSpan Span,ByteSpan NameSpan,ByteSpan OrderSpan,ByteSpan SkinSpan,ByteSpan BoneCountSpan,ByteSpan[] BoneSpans,ByteSpan TargetSpan,ByteSpan[] ModeSpans,ByteSpan[] FloatSpans);
internal sealed record BinaryPathSection4117(BinaryTransformSection4117 TransformSection,BinaryPath4117[] Constraints,ByteSpan CountSpan,ByteSpan Span,int EncodedTextBytes);
internal enum BinaryInspection { Header,Metadata38,Root38,Bones3855,Slots3855,Ik3855,Transform3855,DefaultSkin3855,Setup3855,Deform3855,Animation3855,Bones4117,Slots4117,Ik4117,Transform4117,Path4117,DefaultSkin4117,Setup4117,AnimationSlots4117 }
internal sealed partial class BinaryDocument
{
    readonly byte[] original;
    public SkeletonHeader Header { get; }
    public string Digest { get; }
    public int Length=>original.Length;
    public byte[] UnchangedBytes()=>original.ToArray();
    public BinaryDocument(byte[] bytes)
    {
        if(bytes.Length>16*1024*1024)throw new InvalidDataException("Binary size");
        original=bytes.ToArray();Digest=Convert.ToHexString(SHA256.HashData(original));
        var matches=new List<SkeletonHeader>();
        foreach(var profile in new[]{"3.8.","4.1."})
        {
            try
            {
                var cursor=new BinaryCursor(original);var hash=profile=="3.8."?cursor.String()??"":Convert.ToHexString(cursor.Bytes(8));
                var versionStart=cursor.Offset;var version=cursor.String(32);
                if(version is null||!version.StartsWith(profile,StringComparison.Ordinal)||!version[(profile.Length)..].All(char.IsAsciiDigit)||version.Length==profile.Length)continue;
                matches.Add(new(version,hash,new(0,versionStart),new(versionStart,cursor.Offset-versionStart),cursor.Offset,profile=="3.8."?"public binary-format header":version=="4.1.17"?"pinned4.1.17 header boundary; body support is profile-specific":"owned pre-existing header boundary; body schema unverified"));
            }
            catch(Exception e) when(e is InvalidDataException or IOException or ArgumentException or System.Text.DecoderFallbackException){ }
        }
        Header=matches.Count switch{1=>matches[0],0=>throw new InvalidDataException("Unrecognized or truncated SKEL header"),_=>throw new InvalidDataException("Ambiguous SKEL header")};
    }
    public static BinaryDocument Load(string path)
    {using var stream=File.OpenRead(path);return new(Input.ReadBytes(stream));}
    // shortcut: unversioned public metadata layout only; require version-pinned evidence before using these positions for editing.
    public BinaryMetadata ReadPublicMetadata38()
    {
        if(!Header.Version.StartsWith("3.8.",StringComparison.Ordinal))throw new NotSupportedException("Public metadata profile is limited to experimental 3.8; 4.1 layout unverified");
        return ReadMetadataPrefix();
    }
    public BinaryMetadata ReadMetadata4117()
    {
        // The authorized4.1.11 sample has the same checked setup layout; keep all field/reference guards.
        if(Header.Version is not ("4.1.11" or "4.1.17"))throw new NotSupportedException("Metadata profile requires exact 4.1.17 or 4.1.11");
        var metadata=ReadMetadataPrefix();
        if(!metadata.Nonessential)throw new NotSupportedException("4.1.17 bone profile requires nonessential data");
        return metadata;
    }
    BinaryMetadata ReadMetadataPrefix()
    {
        var cursor=new BinaryCursor(original);cursor.Skip(Header.BodyOffset);
        var x=cursor.Float();var y=cursor.Float();var width=cursor.Float();var height=cursor.Float();
        if(width<0||height<0)throw new InvalidDataException("Binary metadata dimensions");
        var nonessential=cursor.Boolean();float? fps=null;string? images=null;string? audio=null;
        var textBytes=0;
        string? Text()
        {
            var value=cursor.String(1024*1024-textBytes);
            textBytes+=System.Text.Encoding.UTF8.GetByteCount(value??"");return value;
        }
        if(nonessential){fps=cursor.Float();if(fps<=0)throw new InvalidDataException("Binary metadata fps");images=Text();audio=Text();}
        var count=cursor.VarUInt();
        if(count>65536||count>original.Length-cursor.Offset)throw new InvalidDataException("Binary shared string count");
        var strings=new BinarySharedString[(int)count];
        for(var i=0;i<strings.Length;i++){var start=cursor.Offset;var value=Text();strings[i]=new(value,new(start,cursor.Offset-start));}
        return new(x,y,width,height,nonessential,fps,images,audio,strings,new(Header.BodyOffset,cursor.Offset-Header.BodyOffset),textBytes);
    }
    public BinaryRootPrefix ReadPublicRoot38()=>ReadPublicRoot38(ReadPublicMetadata38());
    BinaryRootPrefix ReadPublicRoot38(BinaryMetadata metadata)
    {
        var start=metadata.Span.Offset+metadata.Span.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);
        var count=cursor.VarUInt();if(count is <1 or >4096)throw new InvalidDataException("Binary bone count");var countSpan=new ByteSpan(start,cursor.Offset-start);
        var nameStart=cursor.Offset;var name=cursor.String(1024*1024-metadata.EncodedTextBytes);
        if(string.IsNullOrEmpty(name)||name.Contains('\0'))throw new InvalidDataException("Binary root name");var nameSpan=new ByteSpan(nameStart,cursor.Offset-nameStart);
        var values=new float[8];var spans=new ByteSpan[8];for(var i=0;i<8;i++){var offset=cursor.Offset;values[i]=cursor.Float();spans[i]=new(offset,4);}
        if(values[7]<0)throw new InvalidDataException("Binary bone length");
        // shortcut: stop before transform mode; its version-specific encoding and following fields remain unverified.
        return new((int)count,countSpan,name,nameSpan,values[0],values[1],values[2],values[3],values[4],values[5],values[6],values[7],spans,new(start,cursor.Offset-start));
    }
    public BinaryBoneSection ReadBones3855()
    {
        if(Header.Version!="3.8.55")throw new NotSupportedException("Bone section profile requires exact 3.8.55");
        return ReadBoneSection(ReadPublicMetadata38(),false);
    }
    public BinaryBoneSection ReadBones4117()=>ReadBoneSection(ReadMetadata4117(),true);
    BinaryBoneSection ReadBoneSection(BinaryMetadata metadata,bool modern)
    {
        // shortcut: pinned4.1.17 Raptor extends nonessential1/modes0,1,2,3/skin0; extend only after new structural evidence.
        var version=modern?"4.1.17":"3.8.55";
        if(!metadata.Nonessential)throw new NotSupportedException(version+" bone profile requires nonessential data");
        var start=metadata.Span.Offset+metadata.Span.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);
        var count=cursor.VarUInt();if(count is <1 or >4096||count>(original.Length-cursor.Offset)/40)throw new InvalidDataException("Binary bone count");var countSpan=new ByteSpan(start,cursor.Offset-start);
        var bones=new BinaryBone[(int)count];var names=new HashSet<string>(StringComparer.Ordinal);var textBytes=metadata.EncodedTextBytes;
        for(var i=0;i<bones.Length;i++)
        {
            var boneStart=cursor.Offset;var name=cursor.String(1024*1024-textBytes);var nameSpan=new ByteSpan(boneStart,cursor.Offset-boneStart);
            if(string.IsNullOrEmpty(name)||name.Contains('\0')||!names.Add(name))throw new InvalidDataException("Binary bone name");textBytes+=System.Text.Encoding.UTF8.GetByteCount(name);
            var parent=-1;ByteSpan? parentSpan=null;
            if(i>0){var at=cursor.Offset;var raw=cursor.VarUInt();if(raw>=i)throw new InvalidDataException("Binary bone parent must precede child");parent=(int)raw;parentSpan=new(at,cursor.Offset-at);}
            var values=new float[8];var spans=new ByteSpan[8];for(var j=0;j<8;j++){var at=cursor.Offset;values[j]=cursor.Float();spans[j]=new(at,4);}
            if(values[7]<0)throw new InvalidDataException("Binary bone length");
            var modeSpan=new ByteSpan(cursor.Offset,1);var mode=cursor.Bytes(1)[0] switch{0=>"normal",1=>"onlyTranslation",2=>"noRotationOrReflection",3=>"noScale",_=>throw new NotSupportedException(version+" unverified transform mode")};
            var skinSpan=new ByteSpan(cursor.Offset,1);if(cursor.Boolean())throw new NotSupportedException(version+" skin-required bone pending");
            var colorSpan=new ByteSpan(cursor.Offset,4);var color=cursor.Int();
            bones[i]=new(name,parent,mode,values[0],values[1],values[2],values[3],values[4],values[5],values[6],values[7],color,new(boneStart,cursor.Offset-boneStart),nameSpan,parentSpan,spans,modeSpan,skinSpan,colorSpan);
        }
        return new(bones,countSpan,new(start,cursor.Offset-start),textBytes);
    }
    public BinarySlotSection ReadSlots3855()
    {
        return ReadSlotSection(ReadBones3855(),ReadPublicMetadata38(),false);
    }
    public BinarySlotSection ReadSlots4117()=>ReadSlotSection(ReadBones4117(),ReadMetadata4117(),true);
    BinarySlotSection ReadSlotSection(BinaryBoneSection bones,BinaryMetadata metadata,bool modern)
    {
        var start=bones.Span.Offset+bones.Span.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);
        var count=cursor.VarUInt();if(count>8192||count>(original.Length-cursor.Offset)/13)throw new InvalidDataException("Binary slot count");var countSpan=new ByteSpan(start,cursor.Offset-start);
        var slots=new BinarySlot[(int)count];var names=new HashSet<string>(StringComparer.Ordinal);var textBytes=bones.EncodedTextBytes;
        for(var i=0;i<slots.Length;i++)
        {
            var at=cursor.Offset;var name=cursor.String(1024*1024-textBytes);var nameSpan=new ByteSpan(at,cursor.Offset-at);
            if(string.IsNullOrEmpty(name)||name.Contains('\0')||!names.Add(name))throw new InvalidDataException("Binary slot name");textBytes+=System.Text.Encoding.UTF8.GetByteCount(name);
            var boneAt=cursor.Offset;var bone=cursor.VarUInt();if(bone>=bones.Bones.Length)throw new InvalidDataException("Binary slot bone reference");var boneSpan=new ByteSpan(boneAt,cursor.Offset-boneAt);
            var colorSpan=new ByteSpan(cursor.Offset,4);var color=cursor.Int();var darkSpan=new ByteSpan(cursor.Offset,4);
            // shortcut: pinned4.1.17 slots cover absent dark/blend0 only; extend after new structural evidence.
            var dark=cursor.Int();if(modern&&dark!=uint.MaxValue)throw new NotSupportedException("4.1.17 slot dark pending");if(dark!=uint.MaxValue&&dark>0xffffff)throw new NotSupportedException("3.8.55 unverified slot dark encoding");
            var attachmentAt=cursor.Offset;var attachmentIndex=cursor.VarUInt();if(attachmentIndex>metadata.Strings.Length)throw new InvalidDataException("Binary slot shared attachment reference");var attachmentSpan=new ByteSpan(attachmentAt,cursor.Offset-attachmentAt);var attachment=attachmentIndex==0?null:metadata.Strings[(int)attachmentIndex-1].Value;
            if(modern&&attachmentIndex!=0&&(string.IsNullOrEmpty(attachment)||attachment.Contains('\0')))throw new InvalidDataException("4.1.17 slot attachment name");
            var blendSpan=new ByteSpan(cursor.Offset,1);var blend=cursor.Bytes(1)[0] switch{0=>"normal",1 when !modern=>"additive",2 when !modern=>"multiply",_=>throw new NotSupportedException(modern?"4.1.17 slot blend pending":"3.8.55 unverified slot blend")};
            slots[i]=new(name,(int)bone,color,attachmentIndex,attachment,blend,new(at,cursor.Offset-at),nameSpan,boneSpan,colorSpan,darkSpan,attachmentSpan,blendSpan,dark==uint.MaxValue?null:dark);
        }
        return new(bones,slots,countSpan,new(start,cursor.Offset-start),textBytes);
    }
    public BinaryIkSection ReadIk3855()
    {
        return ReadIkSection(ReadSlots3855(),false);
    }
    public BinaryIkSection ReadIk4117()=>ReadIkSection(ReadSlots4117(),true);
    BinaryIkSection ReadIkSection(BinarySlotSection slots,bool modern)
    {
        var version=modern?"4.1.17":"3.8.55";
        var start=slots.Span.Offset+slots.Span.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);
        var count=cursor.VarUInt();if(count>8192||count>(original.Length-cursor.Offset)/19)throw new InvalidDataException("Binary IK count");var countSpan=new ByteSpan(start,cursor.Offset-start);
        var constraints=new BinaryIk[(int)count];var names=new HashSet<string>(StringComparer.Ordinal);var textBytes=slots.EncodedTextBytes;
        for(var i=0;i<constraints.Length;i++)
        {
            var at=cursor.Offset;var name=cursor.String(1024*1024-textBytes);var nameSpan=new ByteSpan(at,cursor.Offset-at);
            if(string.IsNullOrEmpty(name)||name.Contains('\0')||!names.Add(name))throw new InvalidDataException("Binary IK name");textBytes+=System.Text.Encoding.UTF8.GetByteCount(name);
            var orderAt=cursor.Offset;var order=cursor.VarUInt();var orderSpan=new ByteSpan(orderAt,cursor.Offset-orderAt);var skinSpan=new ByteSpan(cursor.Offset,1);
            // shortcut: binary IK still requires skin0 (3.8 also softness0/no scaling); extend after new structural evidence.
            if(cursor.Boolean())throw new NotSupportedException(version+" skin-required IK pending");
            var countAt=cursor.Offset;var boneCount=cursor.VarUInt();if(boneCount is <1 or >2)throw new InvalidDataException("Binary IK bone count");var boneCountSpan=new ByteSpan(countAt,cursor.Offset-countAt);
            var bones=new int[(int)boneCount];var boneSpans=new ByteSpan[bones.Length];
            for(var b=0;b<bones.Length;b++){var boneAt=cursor.Offset;var index=cursor.VarUInt();if(index>=slots.BoneSection.Bones.Length)throw new InvalidDataException("Binary IK bone reference");bones[b]=(int)index;boneSpans[b]=new(boneAt,cursor.Offset-boneAt);}
            if(bones.Length==2&&bones[0]==bones[1])throw new InvalidDataException("Binary IK duplicate bone");
            var targetAt=cursor.Offset;var target=cursor.VarUInt();if(target>=slots.BoneSection.Bones.Length)throw new InvalidDataException("Binary IK target reference");var targetSpan=new ByteSpan(targetAt,cursor.Offset-targetAt);
            var mixSpan=new ByteSpan(cursor.Offset,4);var mix=cursor.Float();if(mix is <0 or >1)throw new InvalidDataException("Binary IK mix");var softnessSpan=new ByteSpan(cursor.Offset,4);var softness=cursor.Float();if(softness<0)throw new InvalidDataException("Binary IK softness");if(softness!=0&&!modern)throw new NotSupportedException(version+" binary IK softness pending");
            var bendSpan=new ByteSpan(cursor.Offset,1);var bend=cursor.Bytes(1)[0] switch{1=>true,255=>false,_=>throw new InvalidDataException("Binary IK bend direction")};
            var compressSpan=new ByteSpan(cursor.Offset,1);var compress=cursor.Boolean();if(compress&&!modern)throw new NotSupportedException(version+" binary IK compress pending");var stretchSpan=new ByteSpan(cursor.Offset,1);var stretch=cursor.Boolean();if(stretch&&!modern)throw new NotSupportedException(version+" binary IK stretch pending");var uniformSpan=new ByteSpan(cursor.Offset,1);var uniform=cursor.Boolean();if(uniform&&!modern)throw new NotSupportedException(version+" binary IK uniform pending");
            constraints[i]=new(name,order,bones,(int)target,mix,bend,new(at,cursor.Offset-at),nameSpan,orderSpan,skinSpan,boneCountSpan,boneSpans,targetSpan,mixSpan,softnessSpan,bendSpan,compressSpan,stretchSpan,uniformSpan,compress,stretch,uniform,softness);
        }
        return new(slots,constraints,countSpan,new(start,cursor.Offset-start),textBytes);
    }
    public BinaryTransformSection3855 ReadTransform3855()
    {
        var ik=ReadIk3855();var start=ik.Span.Offset+ik.Span.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);
        var count=cursor.VarUInt();if(count>8192||count>(original.Length-cursor.Offset)/49)throw new InvalidDataException("Binary transform count");var countSpan=new ByteSpan(start,cursor.Offset-start);
        var constraints=new BinaryTransform3855[(int)count];var names=new HashSet<string>(StringComparer.Ordinal);var textBytes=ik.EncodedTextBytes;
        for(var i=0;i<constraints.Length;i++)
        {
            var at=cursor.Offset;var name=cursor.String(1024*1024-textBytes);var nameSpan=new ByteSpan(at,cursor.Offset-at);
            if(string.IsNullOrEmpty(name)||name.Contains('\0')||!names.Add(name))throw new InvalidDataException("Binary transform name");textBytes+=System.Text.Encoding.UTF8.GetByteCount(name);
            var orderAt=cursor.Offset;var order=cursor.VarUInt();var orderSpan=new ByteSpan(orderAt,cursor.Offset-orderAt);var skinSpan=new ByteSpan(cursor.Offset,1);
            // shortcut: pinned3.8.55 Transform profile covers local/relative0 and chain1/2 only; extend after new format evidence.
            var skinRequired=cursor.Boolean();
            var countAt=cursor.Offset;var boneCount=cursor.VarUInt();if(boneCount is <1 or >2)throw new NotSupportedException("3.8.55 Transform chain count pending");var boneCountSpan=new ByteSpan(countAt,cursor.Offset-countAt);
            var bones=new int[(int)boneCount];var boneSpans=new ByteSpan[bones.Length];
            for(var b=0;b<bones.Length;b++){var boneAt=cursor.Offset;var index=cursor.VarUInt();if(index>=ik.SlotSection.BoneSection.Bones.Length)throw new InvalidDataException("Binary transform bone reference");bones[b]=(int)index;boneSpans[b]=new(boneAt,cursor.Offset-boneAt);}
            if(bones.Length==2&&bones[0]==bones[1])throw new InvalidDataException("Binary transform duplicate bone");
            var targetAt=cursor.Offset;var target=cursor.VarUInt();if(target>=ik.SlotSection.BoneSection.Bones.Length)throw new InvalidDataException("Binary transform target reference");var targetSpan=new ByteSpan(targetAt,cursor.Offset-targetAt);
            var localSpan=new ByteSpan(cursor.Offset,1);if(cursor.Boolean())throw new NotSupportedException("3.8.55 binary local Transform pending");var relativeSpan=new ByteSpan(cursor.Offset,1);if(cursor.Boolean())throw new NotSupportedException("3.8.55 binary relative Transform pending");
            var values=new float[10];var spans=new ByteSpan[10];for(var f=0;f<values.Length;f++){var fieldAt=cursor.Offset;values[f]=cursor.Float();if(f>=6&&(values[f]<(f==7?-1:0)||values[f]>1))throw new InvalidDataException("Binary transform mix");spans[f]=new(fieldAt,4);}
            constraints[i]=new(name,order,bones,(int)target,values[0],values[1],values[2],values[3],values[4],values[5],values[6],values[7],values[8],values[9],new(at,cursor.Offset-at),nameSpan,orderSpan,skinSpan,boneCountSpan,boneSpans,targetSpan,localSpan,relativeSpan,spans,skinRequired);
        }
        return new(ik,constraints,countSpan,new(start,cursor.Offset-start),textBytes);
    }
    public BinaryTransformSection4117 ReadTransform4117()
    {
        var ik=ReadIk4117();var start=ik.Span.Offset+ik.Span.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);
        var count=cursor.VarUInt();if(count>8192||count>(original.Length-cursor.Offset)/57)throw new InvalidDataException("Binary 4.1.17 Transform count");var countSpan=new ByteSpan(start,cursor.Offset-start);
        var constraints=new BinaryTransform4117[(int)count];var names=new HashSet<string>(StringComparer.Ordinal);var textBytes=ik.EncodedTextBytes;
        for(var i=0;i<constraints.Length;i++)
        {
            var at=cursor.Offset;var name=cursor.String(1024*1024-textBytes);var nameSpan=new ByteSpan(at,cursor.Offset-at);
            if(string.IsNullOrEmpty(name)||name.Contains('\0')||!names.Add(name))throw new InvalidDataException("Binary Transform name");textBytes+=System.Text.Encoding.UTF8.GetByteCount(name);
            var orderAt=cursor.Offset;var order=cursor.VarUInt();var orderSpan=new ByteSpan(orderAt,cursor.Offset-orderAt);var skinSpan=new ByteSpan(cursor.Offset,1);var skin=cursor.Boolean();
            // shortcut: pinned4.1.17 Transform covers single-bone and12 raw floats; local/relative flags are preserved; JSON omitted Y defaults remain unverified.
            var countAt=cursor.Offset;if(cursor.VarUInt()!=1)throw new NotSupportedException("4.1.17 Transform requires observed single bone");var boneCountSpan=new ByteSpan(countAt,cursor.Offset-countAt);
            var boneAt=cursor.Offset;var bone=cursor.VarUInt();if(bone>=ik.SlotSection.BoneSection.Bones.Length)throw new InvalidDataException("Binary Transform bone reference");var boneSpan=new ByteSpan(boneAt,cursor.Offset-boneAt);
            var targetAt=cursor.Offset;var target=cursor.VarUInt();if(target>=ik.SlotSection.BoneSection.Bones.Length)throw new InvalidDataException("Binary Transform target reference");var targetSpan=new ByteSpan(targetAt,cursor.Offset-targetAt);
            var localSpan=new ByteSpan(cursor.Offset,1);var local=cursor.Boolean();var relativeSpan=new ByteSpan(cursor.Offset,1);var relative=cursor.Boolean();
            var values=new float[12];var spans=new ByteSpan[12];for(var f=0;f<12;f++){var fieldAt=cursor.Offset;values[f]=cursor.Float();if(f>=6&&(values[f]<-1||values[f]>1))throw new InvalidDataException("4.1.17 Transform raw mix range");spans[f]=new(fieldAt,4);}
            constraints[i]=new(name,order,skin,(int)bone,(int)target,values,new(at,cursor.Offset-at),nameSpan,orderSpan,skinSpan,boneCountSpan,boneSpan,targetSpan,localSpan,relativeSpan,spans,local,relative);
        }
        return new(ik,constraints,countSpan,new(start,cursor.Offset-start),textBytes);
    }
    public BinaryPathSection4117 ReadPath4117()
    {
        var transform=ReadTransform4117();var slots=transform.IkSection.SlotSection;var start=transform.Span.Offset+transform.Span.Length;var cursor=new BinaryCursor(original);cursor.Skip(start);
        var count=cursor.VarUInt();if(count>8192||count>(original.Length-cursor.Offset)/37)throw new InvalidDataException("Binary 4.1.17 Path count");var countSpan=new ByteSpan(start,cursor.Offset-start);
        var constraints=new BinaryPath4117[(int)count];var names=new HashSet<string>(StringComparer.Ordinal);var textBytes=transform.EncodedTextBytes;
        for(var i=0;i<constraints.Length;i++)
        {
            var at=cursor.Offset;var name=cursor.String(1024*1024-textBytes);var nameSpan=new ByteSpan(at,cursor.Offset-at);
            if(string.IsNullOrEmpty(name)||name.Contains('\0')||!names.Add(name))throw new InvalidDataException("Binary Path name");textBytes+=System.Text.Encoding.UTF8.GetByteCount(name);
            var orderAt=cursor.Offset;var order=cursor.VarUInt();var orderSpan=new ByteSpan(orderAt,cursor.Offset-orderAt);var skinSpan=new ByteSpan(cursor.Offset,1);
            // shortcut: pinned4.1.17 Path covers skin0/chains4,6,8/raw modes1,2,2; extend after new structural evidence.
            if(cursor.Boolean())throw new NotSupportedException("4.1.17 skin-required Path pending");
            var countAt=cursor.Offset;var boneCount=cursor.VarUInt();if(boneCount is not (4 or 6 or 8))throw new NotSupportedException("4.1.17 Path chain count pending");var boneCountSpan=new ByteSpan(countAt,cursor.Offset-countAt);
            var bones=new int[(int)boneCount];var boneSpans=new ByteSpan[bones.Length];var seen=new HashSet<int>();
            for(var b=0;b<bones.Length;b++){var boneAt=cursor.Offset;var bone=cursor.VarUInt();if(bone>=slots.BoneSection.Bones.Length||!seen.Add((int)bone))throw new InvalidDataException("Binary Path bone reference");bones[b]=(int)bone;boneSpans[b]=new(boneAt,cursor.Offset-boneAt);}
            var targetAt=cursor.Offset;var target=cursor.VarUInt();if(target>=slots.Slots.Length)throw new InvalidDataException("Binary Path target slot reference");var targetSpan=new ByteSpan(targetAt,cursor.Offset-targetAt);
            var modes=new byte[3];var modeSpans=new ByteSpan[3];for(var m=0;m<3;m++){modeSpans[m]=new(cursor.Offset,1);modes[m]=cursor.Bytes(1)[0];if(modes[m]!=(m==0?1:2))throw new NotSupportedException("4.1.17 Path mode pending");}
            var values=new float[6];var spans=new ByteSpan[6];for(var f=0;f<6;f++){spans[f]=new(cursor.Offset,4);values[f]=cursor.Float();if(f>=3&&(values[f]<0||values[f]>1))throw new InvalidDataException("4.1.17 Path raw mix range");}
            constraints[i]=new(name,order,bones,(int)target,modes[0],modes[1],modes[2],values,new(at,cursor.Offset-at),nameSpan,orderSpan,skinSpan,boneCountSpan,boneSpans,targetSpan,modeSpans,spans);
        }
        return new(transform,constraints,countSpan,new(start,cursor.Offset-start),textBytes);
    }
    public void ExportUnchanged(string directory,BinaryInspection inspection=BinaryInspection.Header)
    {
        if(!Enum.IsDefined(inspection))throw new ArgumentOutOfRangeException(nameof(inspection));
        var modern=inspection is BinaryInspection.Bones4117 or BinaryInspection.Slots4117 or BinaryInspection.Ik4117 or BinaryInspection.Transform4117 or BinaryInspection.Path4117 or BinaryInspection.DefaultSkin4117 or BinaryInspection.Setup4117 or BinaryInspection.AnimationSlots4117;var animationSlots41=inspection==BinaryInspection.AnimationSlots4117?ReadAnimationSlots4117():null;var setup41=inspection is BinaryInspection.Setup4117 or BinaryInspection.AnimationSlots4117?ReadSetup4117():null;var skin41=setup41?.DefaultSkin??(inspection==BinaryInspection.DefaultSkin4117?ReadDefaultSkin4117():null);var path41=skin41?.PathSection??(inspection==BinaryInspection.Path4117?ReadPath4117():null);var transform41=path41?.TransformSection??(inspection==BinaryInspection.Transform4117?ReadTransform4117():null);var ik41=transform41?.IkSection??(inspection==BinaryInspection.Ik4117?ReadIk4117():null);var slots41=ik41?.SlotSection??(inspection==BinaryInspection.Slots4117?ReadSlots4117():null);var prefix=inspection==BinaryInspection.Header?null:modern?ReadMetadata4117():ReadPublicMetadata38();var bones41=slots41?.BoneSection??(modern?ReadBones4117():null);var root=inspection==BinaryInspection.Root38?ReadPublicRoot38(prefix!):null;var deform=inspection==BinaryInspection.Animation3855?ReadAnimation3855():inspection==BinaryInspection.Deform3855?ReadDeform3855():null;var setup=deform?.Setup??(inspection==BinaryInspection.Setup3855?ReadSetup3855():null);var skin=setup?.DefaultSkin??(inspection==BinaryInspection.DefaultSkin3855?ReadDefaultSkin3855():null);var transform=skin?.TransformSection??(inspection==BinaryInspection.Transform3855?ReadTransform3855():null);var ik=transform?.IkSection??(inspection==BinaryInspection.Ik3855?ReadIk3855():null);var slots=ik?.SlotSection??(inspection==BinaryInspection.Slots3855?ReadSlots3855():null);var bones=slots?.BoneSection??(inspection==BinaryInspection.Bones3855?ReadBones3855():null);
        Experiment.CheckOutput(directory);var sourcePath=Path.Combine(directory,"source.skel");var metadataPath=Path.Combine(directory,"binary-provenance.json");
        if(File.Exists(sourcePath)||Directory.Exists(sourcePath)||File.Exists(metadataPath)||Directory.Exists(metadataPath))throw new IOException("Binary export destination exists");
        var report=new{Header,Length,Digest,BodyParsed=deform is not null||setup?.Complete==true||setup41?.Complete==true,Editable=false,FullConsumption=deform is not null||setup?.Complete==true||setup41?.Complete==true,PublicMetadata38=modern?null:prefix,Metadata4117=modern?prefix:null,Bones4117=bones41,Slots4117=slots41 is null?null:new{slots41.Slots,slots41.CountSpan,slots41.Span,slots41.EncodedTextBytes},Ik4117=ik41 is null?null:new{ik41.Constraints,ik41.CountSpan,ik41.Span,ik41.EncodedTextBytes},Transform4117=transform41 is null?null:new{transform41.Constraints,transform41.CountSpan,transform41.Span,transform41.EncodedTextBytes},Path4117=path41 is null?null:new{path41.Constraints,path41.CountSpan,path41.Span,path41.EncodedTextBytes},DefaultSkin4117=skin41 is null?null:new{skin41.SlotGroups,skin41.CountSpan,skin41.Span,skin41.AttachmentCount,skin41.VertexCount,skin41.EncodedTextBytes},Setup4117=setup41 is null?null:new{setup41.Events,setup41.AnimationCount,setup41.Complete,setup41.ExtraSkinsSpan,setup41.EventCountSpan,setup41.AnimationCountSpan,setup41.Span,setup41.EncodedTextBytes},AnimationSlots4117=animationSlots41,AnimationSlotsEvidence4117=animationSlots41 is null?null:"output/format-evidence-20261018/animationslots4117-raptor-metadata.json; first animation attachment slots only; bone/remaining body unparsed",SetupEvidence4117=setup41 is null?null:"output/format-evidence-20261018/setup4117-raptor-metadata.json;animation0 exact EOF complete;positive animations partial",DefaultSkinEvidence4117=skin41 is null?null:"output/format-evidence-20261018/default4117-raptor-metadata.json;region/mesh, sequence absent;partial only",PathEvidence4117=path41 is null?null:"output/format-evidence-20261018/path4117-stretchyman-metadata.json; mix-and-match/raptor companions;raw setup only",TransformEvidence4117=transform41 is null?null:"output/format-evidence-20261018/transform4117-stretchyman-metadata.json; mix-and-match/raptor companions;12 raw floats and local/relative flags, JSON omitted Y defaults pending",IkEvidence4117=ik41 is null?null:"output/format-evidence-20261018/ik4117-stretchyman-metadata.json; mix-and-match/raptor companions; one/two-bone/skin0; softness/scaling flags retained",SlotEvidence4117=slots41 is null?null:"output/format-evidence-20261018/slots4117-stretchyman-metadata.json; mix-and-match/raptor companion metadata; absent dark/blend0 only",PublicRootPrefix38=root,Bones3855=bones,Slots3855=slots is null?null:new{slots.Slots,slots.CountSpan,slots.Span,slots.EncodedTextBytes},Ik3855=ik is null?null:new{ik.Constraints,ik.CountSpan,ik.Span,ik.EncodedTextBytes},Transform3855=transform is null?null:new{transform.Constraints,transform.CountSpan,transform.Span,transform.EncodedTextBytes},DefaultSkin3855=skin is null?null:new{skin.SlotGroups,skin.PathCountSpan,skin.CountSpan,skin.Span,skin.AttachmentCount,skin.VertexCount,skin.EncodedTextBytes},Setup3855=setup is null?null:new{setup.Events,setup.AnimationCount,setup.Complete,setup.ExtraSkinsSpan,setup.EventCountSpan,setup.AnimationCountSpan,setup.Span,setup.EncodedTextBytes},Deform3855=deform is null?null:new{deform.Animations,deform.Span,deform.EncodedTextBytes,deform.FrameCount,deform.ValueCount},RootRemainderParsed=deform is not null||setup?.Complete==true||setup41?.Complete==true,MetadataEvidence=prefix is null?null:modern?"output/format-evidence-20261018/bones4117-metadata.json; exact4.1.17 only":"https://esotericsoftware.com/spine-binary-format (unversioned; experimental profile)",BoneEvidence=bones is null?null:"output/resume-20261019-unit73/inheritance-evidence.json; exact3.8.55, nonessential1, modes0/1/2/3, skin0 only",SlotEvidence=slots is null?null:"output/resume-20261019-unit76/dark-evidence.json; retained unit28 and unit74 blend0/1/2; dark none/RGB24",IkEvidence=ik is null?null:"output/heartbeat-20261018/unit29-contract.md; limited setup only",TransformEvidence=transform is null?null:"output/heartbeat-20261018/unit30-contract.md; limited setup only",SkinEvidence=skin is null?null:"output/heartbeat-20261018/unit33-contract.md; region/unweighted polygon/mesh with weights",SetupEvidence=setup is null?null:"output/heartbeat-20261018/unit34-contract.md; limited animation0 complete or positive animation count partial",DeformEvidence=deform is null?null:inspection==BinaryInspection.Animation3855?"output/heartbeat-20261018/unit48-contract.md; read-only rotation linear/step plus Deform":"output/heartbeat-20261018/unit36-contract.md; Deform-only full body",UnparsedOffset=animationSlots41 is not null?animationSlots41.Span.Offset+animationSlots41.Span.Length:setup41 is not null?setup41.Span.Offset+setup41.Span.Length:skin41 is not null?skin41.Span.Offset+skin41.Span.Length:path41 is not null?path41.Span.Offset+path41.Span.Length:transform41 is not null?transform41.Span.Offset+transform41.Span.Length:ik41 is not null?ik41.Span.Offset+ik41.Span.Length:slots41 is not null?slots41.Span.Offset+slots41.Span.Length:bones41 is not null?bones41.Span.Offset+bones41.Span.Length:deform is not null?deform.Span.Offset+deform.Span.Length:setup is not null?setup.Span.Offset+setup.Span.Length:skin is not null?skin.Span.Offset+skin.Span.Length:transform is not null?transform.Span.Offset+transform.Span.Length:ik is not null?ik.Span.Offset+ik.Span.Length:slots is not null?slots.Span.Offset+slots.Span.Length:bones is not null?bones.Span.Offset+bones.Span.Length:root is not null?root.Span.Offset+root.Span.Length:prefix is null?Header.BodyOffset:prefix.Span.Offset+prefix.Span.Length};
        var bytes=JsonSerializer.SerializeToUtf8Bytes(report,new JsonSerializerOptions{WriteIndented=true});Directory.CreateDirectory(directory);
        // shortcut: individual installs are atomic; a later metadata install failure may leave the complete source copy.
        BundleSave.InstallNew(sourcePath,original);BundleSave.InstallNew(metadataPath,bytes);
    }
}
