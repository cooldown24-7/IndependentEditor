namespace IndependentDev;

internal sealed record BinaryRegion(uint PathIndex,string? Path,float Rotation,float X,float Y,float ScaleX,float ScaleY,float Width,float Height,uint Color,ByteSpan PathSpan,ByteSpan[] FloatSpans,ByteSpan ColorSpan,ByteSpan? SequenceSpan=null);
internal sealed record BinaryPolygon3855(Point[] Vertices,uint Color,ByteSpan CountSpan,ByteSpan WeightedSpan,ByteSpan[] CoordinateSpans,ByteSpan ColorSpan);
internal sealed record BinaryInfluence(int Bone,float X,float Y,float Weight,ByteSpan Span,ByteSpan BoneSpan,ByteSpan XSpan,ByteSpan YSpan,ByteSpan WeightSpan);
internal sealed record BinaryWeightedVertex(BinaryInfluence[] Influences,ByteSpan CountSpan,ByteSpan Span);
internal sealed record BinaryMesh(uint PathIndex,string? Path,uint Color,Point[] Uvs,ushort[] Triangles,Point[] Vertices,BinaryWeightedVertex[] Weights,uint Hull,ushort[] Edges,float Width,float Height,ByteSpan PathSpan,ByteSpan ColorSpan,ByteSpan CountSpan,ByteSpan[] UvSpans,ByteSpan TriangleCountSpan,ByteSpan[] TriangleSpans,ByteSpan WeightedSpan,ByteSpan[] VertexSpans,ByteSpan HullSpan,ByteSpan EdgeCountSpan,ByteSpan[] EdgeSpans,ByteSpan[] DimensionSpans,ByteSpan? SequenceSpan=null);
internal sealed record BinaryClip3855(int EndSlot,BinaryPolygon3855 Polygon,ByteSpan EndSpan);
internal sealed record BinaryPathAttachment(bool Closed,bool ConstantSpeed,Point[] Vertices,BinaryWeightedVertex[] Weights,float[] Lengths,uint Color);
internal sealed record BinaryAttachment(uint PlaceholderIndex,string Placeholder,uint NameIndex,string? Name,string Type,BinaryRegion? Region,BinaryPolygon3855? Box,BinaryMesh? Mesh,BinaryClip3855? Clip,ByteSpan Span,ByteSpan PlaceholderSpan,ByteSpan NameSpan,ByteSpan TypeSpan,BinaryPathAttachment? Path=null);
internal sealed record BinarySkinSlot(int SlotIndex,BinaryAttachment[] Attachments,ByteSpan Span,ByteSpan SlotSpan,ByteSpan CountSpan);
internal sealed record BinaryDefaultSkin3855(BinaryTransformSection3855 TransformSection,BinarySkinSlot[] SlotGroups,ByteSpan PathCountSpan,ByteSpan CountSpan,ByteSpan Span,int AttachmentCount,int VertexCount,int EncodedTextBytes);

internal sealed record BinarySkinAttachments(BinarySkinSlot[] SlotGroups,ByteSpan CountSpan,ByteSpan Span,int AttachmentCount,int VertexCount,int EncodedTextBytes);
internal sealed record BinaryDefaultSkin4117(BinaryPathSection4117 PathSection,BinarySkinSlot[] SlotGroups,ByteSpan CountSpan,ByteSpan Span,int AttachmentCount,int VertexCount,int EncodedTextBytes);
internal sealed partial class BinaryDocument
{
    public BinaryDefaultSkin3855 ReadDefaultSkin3855()
    {
        var transform=ReadTransform3855();var metadata=ReadPublicMetadata38();var cursor=new BinaryCursor(original);cursor.Skip(transform.Span.Offset+transform.Span.Length);
        // shortcut: pinned3.8.55 default skin requires Path0; weighted polygons and unobserved types need additional implementation/evidence.
        var pathAt=cursor.Offset;if(cursor.VarUInt()!=0)throw new NotSupportedException("3.8.55 binary Path section pending");var pathSpan=new ByteSpan(pathAt,cursor.Offset-pathAt);
        var data=ReadSkinAttachments(transform.IkSection.SlotSection,metadata,cursor.Offset,transform.EncodedTextBytes,false);
        return new(transform,data.SlotGroups,pathSpan,data.CountSpan,data.Span,data.AttachmentCount,data.VertexCount,data.EncodedTextBytes);
    }
    public BinaryDefaultSkin4117 ReadDefaultSkin4117()
    {
        var path=ReadPath4117();var data=ReadSkinAttachments(path.TransformSection.IkSection.SlotSection,ReadMetadata4117(),path.Span.Offset+path.Span.Length,path.EncodedTextBytes,true);
        return new(path,data.SlotGroups,data.CountSpan,data.Span,data.AttachmentCount,data.VertexCount,data.EncodedTextBytes);
    }
    BinarySkinAttachments ReadSkinAttachments(BinarySlotSection slots,BinaryMetadata metadata,int start,int encodedTextBytes,bool modern)
    {
        var cursor=new BinaryCursor(original);cursor.Skip(start);
        var count=cursor.VarUInt();if(count>slots.Slots.Length||count>(original.Length-cursor.Offset)/2)throw new InvalidDataException("Binary skin slot count");var countSpan=new ByteSpan(start,cursor.Offset-start);
        var groups=new BinarySkinSlot[(int)count];var seenSlots=new HashSet<int>();var attachmentTotal=0;var vertexTotal=0;var triangleTotal=0;var edgeTotal=0;var influenceTotal=0;
        (uint Index,string? Value,ByteSpan Span) Reference()
        {
            var at=cursor.Offset;var index=cursor.VarUInt();if(index>metadata.Strings.Length)throw new InvalidDataException("Binary skin shared string reference");var value=index==0?null:metadata.Strings[(int)index-1].Value;if(value?.Contains('\0')==true)throw new InvalidDataException("Binary skin string contains NUL");if(modern&&index!=0&&string.IsNullOrEmpty(value))throw new InvalidDataException("4.1.17 skin shared name");return(index,value,new(at,cursor.Offset-at));
        }
        ByteSpan? Sequence()
        {
            if(!modern)return null;
            // shortcut: pinned4.1.17 region/mesh evidence covers sequence absent only; add payloads after new structural evidence.
            var span=new ByteSpan(cursor.Offset,1);if(cursor.Boolean())throw new NotSupportedException("4.1.17 attachment sequence pending");return span;
        }
        void VertexCount(uint vertices){if(vertices is <1 or >65536||vertices>500000-vertexTotal)throw new InvalidDataException("Binary attachment vertex count");}
        (Point[] Points,ByteSpan[] Spans) Coordinates(uint count,int trailing)
        {
            if(original.Length-cursor.Offset<trailing||count>(original.Length-cursor.Offset-trailing)/8)throw new InvalidDataException("Binary vertex bytes");var points=new Point[(int)count];var spans=new ByteSpan[points.Length*2];
            for(var v=0;v<points.Length;v++){var at=cursor.Offset;var x=cursor.Float();var y=cursor.Float();points[v]=new(x,y);spans[2*v]=new(at,4);spans[2*v+1]=new(at+4,4);}return(points,spans);
        }
        (Point[] Points,BinaryWeightedVertex[] Weights,ByteSpan Weighted,ByteSpan[] Spans) Vertices(uint count,int trailing,string kind)
        {
            VertexCount(count);var weighted=new ByteSpan(cursor.Offset,1);var flag=cursor.Boolean();vertexTotal+=(int)count;
            if(!flag){var coordinates=Coordinates(count,trailing);return(coordinates.Points,[],weighted,coordinates.Spans);}
            if(kind!="mesh"&&!(modern&&kind=="path"))throw new NotSupportedException("3.8.55 weighted binary "+kind+" pending");
            if(original.Length-cursor.Offset<trailing||count>(original.Length-cursor.Offset-trailing)/14)throw new InvalidDataException("Binary weighted vertex bytes");
            var result=new BinaryWeightedVertex[(int)count];var boneCount=slots.BoneSection.Bones.Length;
            for(var v=0;v<result.Length;v++)
            {
                var start=cursor.Offset;var amount=cursor.VarUInt();if(amount is <1||amount>boneCount||amount>250000-influenceTotal||amount>(original.Length-cursor.Offset)/13)throw new InvalidDataException("Binary weighted influence count");influenceTotal+=(int)amount;var countSpan=new ByteSpan(start,cursor.Offset-start);var influences=new BinaryInfluence[(int)amount];var positive=false;
                for(var i=0;i<influences.Length;i++)
                {
                    var boneAt=cursor.Offset;var bone=cursor.VarUInt();if(bone>=boneCount)throw new InvalidDataException("Binary weighted bone reference");var boneSpan=new ByteSpan(boneAt,cursor.Offset-boneAt);var xAt=cursor.Offset;var x=cursor.Float();var y=cursor.Float();var weight=cursor.Float();if(weight<0)throw new InvalidDataException("Negative binary weight");positive|=weight>0;influences[i]=new((int)bone,x,y,weight,new(boneAt,cursor.Offset-boneAt),boneSpan,new(xAt,4),new(xAt+4,4),new(xAt+8,4));
                }
                if(!positive)throw new InvalidDataException("Zero total binary weight");result[v]=new(influences,countSpan,new(start,cursor.Offset-start));
            }
            return([],result,weighted,[]);
        }
        for(var g=0;g<groups.Length;g++)
        {
            var groupAt=cursor.Offset;var slot=cursor.VarUInt();if(slot>=slots.Slots.Length||!seenSlots.Add((int)slot))throw new InvalidDataException("Binary skin slot reference");var slotSpan=new ByteSpan(groupAt,cursor.Offset-groupAt);
            var countAt=cursor.Offset;var attachments=cursor.VarUInt();if(attachments>65536-attachmentTotal||attachments>(original.Length-cursor.Offset)/(modern?37:17))throw new InvalidDataException("Binary skin attachment count");attachmentTotal+=(int)attachments;var attachmentCountSpan=new ByteSpan(countAt,cursor.Offset-countAt);
            var items=new BinaryAttachment[(int)attachments];var names=new HashSet<string>(StringComparer.Ordinal);
            for(var a=0;a<items.Length;a++)
            {
                var at=cursor.Offset;var placeholder=Reference();if(string.IsNullOrEmpty(placeholder.Value)||!names.Add(placeholder.Value))throw new InvalidDataException("Binary skin placeholder name");var name=Reference();var typeSpan=new ByteSpan(cursor.Offset,1);var type=cursor.Bytes(1)[0];if(modern&&type is not (0 or 2 or 4))throw new NotSupportedException("4.1.17 binary attachment type pending");BinaryRegion? region=null;BinaryPolygon3855? box=null;BinaryMesh? mesh=null;BinaryClip3855? clip=null;BinaryPathAttachment? pathAttachment=null;string kind;
                if(type==0)
                {
                    kind="region";var path=Reference();var values=new float[7];var spans=new ByteSpan[7];for(var f=0;f<7;f++){var fieldAt=cursor.Offset;values[f]=cursor.Float();spans[f]=new(fieldAt,4);}if(values[5]<0||values[6]<0)throw new InvalidDataException("Binary region dimensions");var colorSpan=new ByteSpan(cursor.Offset,4);var color=cursor.Int();
                    var sequence=Sequence();region=new(path.Index,path.Value,values[0],values[1],values[2],values[3],values[4],values[5],values[6],color,path.Span,spans,colorSpan,sequence);
                }
                else if(type is 1 or 6)
                {
                    kind=type==1?"boundingbox":"clipping";var endAt=cursor.Offset;var end=type==6?cursor.VarUInt():0;if(type==6&&end>=slots.Slots.Length)throw new InvalidDataException("Binary clipping end slot reference");var endSpan=new ByteSpan(endAt,cursor.Offset-endAt);
                    var verticesAt=cursor.Offset;var vertices=cursor.VarUInt();var verticesSpan=new ByteSpan(verticesAt,cursor.Offset-verticesAt);var decoded=Vertices(vertices,4,type==1?"box":"clip");var colorSpan=new ByteSpan(cursor.Offset,4);var color=cursor.Int();var polygon=new BinaryPolygon3855(decoded.Points,color,verticesSpan,decoded.Weighted,decoded.Spans,colorSpan);if(type==1)box=polygon;else clip=new((int)end,polygon,endSpan);
                }
                else if(type==2)
                {
                    kind="mesh";var path=Reference();var colorSpan=new ByteSpan(cursor.Offset,4);var color=cursor.Int();var verticesAt=cursor.Offset;var vertices=cursor.VarUInt();VertexCount(vertices);var verticesSpan=new ByteSpan(verticesAt,cursor.Offset-verticesAt);var uv=Coordinates(vertices,1);
                    var triangleAt=cursor.Offset;var triangleCount=cursor.VarUInt();if(triangleCount%3!=0||triangleCount>1000000-triangleTotal||triangleCount>(original.Length-cursor.Offset)/2)throw new InvalidDataException("Binary mesh triangle count");triangleTotal+=(int)triangleCount;var triangleCountSpan=new ByteSpan(triangleAt,cursor.Offset-triangleAt);var triangles=new ushort[(int)triangleCount];var triangleSpans=new ByteSpan[triangles.Length];
                    for(var t=0;t<triangles.Length;t++){var indexAt=cursor.Offset;triangles[t]=cursor.Short();if(triangles[t]>=vertices)throw new InvalidDataException("Binary mesh triangle reference");triangleSpans[t]=new(indexAt,2);}
                    var decoded=Vertices(vertices,modern?11:10,"mesh");var hullAt=cursor.Offset;var hull=cursor.VarUInt();if(hull>vertices)throw new InvalidDataException("Binary mesh hull count");var hullSpan=new ByteSpan(hullAt,cursor.Offset-hullAt);var sequence=Sequence();
                    var edgeAt=cursor.Offset;var edgeCount=cursor.VarUInt();if(edgeCount%2!=0||edgeCount>1000000-edgeTotal||original.Length-cursor.Offset<8||edgeCount>(original.Length-cursor.Offset-8)/2)throw new InvalidDataException("Binary mesh edge count");edgeTotal+=(int)edgeCount;var edgeCountSpan=new ByteSpan(edgeAt,cursor.Offset-edgeAt);var edges=new ushort[(int)edgeCount];var edgeSpans=new ByteSpan[edges.Length];
                    for(var e=0;e<edges.Length;e++){var indexAt=cursor.Offset;edges[e]=cursor.Short();if(edges[e]%2!=0)throw new NotSupportedException(modern?"4.1.17 odd binary edge offset pending":"3.8.55 odd binary edge offset pending");if(edges[e]>=2*vertices)throw new InvalidDataException("Binary mesh edge reference");edgeSpans[e]=new(indexAt,2);}
                    var widthSpan=new ByteSpan(cursor.Offset,4);var width=cursor.Float();var heightSpan=new ByteSpan(cursor.Offset,4);var height=cursor.Float();if(width<0||height<0)throw new InvalidDataException("Binary mesh dimensions");mesh=new(path.Index,path.Value,color,uv.Points,triangles,decoded.Points,decoded.Weights,hull,edges,width,height,path.Span,colorSpan,verticesSpan,uv.Spans,triangleCountSpan,triangleSpans,decoded.Weighted,decoded.Spans,hullSpan,edgeCountSpan,edgeSpans,[widthSpan,heightSpan],sequence);
                }
                else if(modern&&type==4)
                {
                    kind="path";var closed=cursor.Boolean();var constantSpeed=cursor.Boolean();var countVertices=cursor.VarUInt();VertexCount(countVertices);BezierPath.CurveCount((int)countVertices,closed);var decoded=Vertices(countVertices,4*(int)(countVertices/3)+4,"path");var lengths=new float[countVertices/3];for(var l=0;l<lengths.Length;l++)lengths[l]=cursor.Float();var color=cursor.Int();pathAttachment=new(closed,constantSpeed,decoded.Points,decoded.Weights,lengths,color);
                }
                else throw new NotSupportedException("3.8.55 binary attachment type pending");
                items[a]=new(placeholder.Index,placeholder.Value,name.Index,name.Value,kind,region,box,mesh,clip,new(at,cursor.Offset-at),placeholder.Span,name.Span,typeSpan,pathAttachment);
            }
            groups[g]=new((int)slot,items,new(groupAt,cursor.Offset-groupAt),slotSpan,attachmentCountSpan);
        }
        return new(groups,countSpan,new(start,cursor.Offset-start),attachmentTotal,vertexTotal,encodedTextBytes);
    }
}
