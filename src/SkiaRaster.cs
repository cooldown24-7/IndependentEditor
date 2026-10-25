using SkiaSharp;
namespace IndependentDev;

internal static class SkiaRaster
{
    [ThreadStatic] static SKRuntimeEffect? samplingEffect;
    const string SamplingShader="""
        uniform shader color0; uniform shader alpha0; uniform shader color1; uniform shader alpha1;
        uniform float3 u; uniform float3 v; uniform float2 scale0; uniform float2 scale1;
        uniform float pma; uniform float nearest; uniform float mipMix;
        half4 main(float2 p) {
            float2 q=float2(dot(u,float3(p,1)),dot(v,float3(p,1)));
            float2 q0=q*scale0; if(nearest>0.5) q0=floor(q0)+float2(0.5);
            float3 rgb=color0.eval(q0).rgb; float a=alpha0.eval(q0).a;
            if(mipMix>0) {
                float2 q1=q*scale1; if(nearest>0.5) q1=floor(q1)+float2(0.5);
                rgb=mix(rgb,color1.eval(q1).rgb,mipMix); a=mix(a,alpha1.eval(q1).a,mipMix);
            }
            return half4(pma>0.5?min(rgb,float3(a)):rgb*a,a);
        }
        """;
    static float Scalar(double value)
    {var result=(float)value;if(!float.IsFinite(result))throw new InvalidDataException("Skia float coordinate range");return result;}
    internal static byte[] AlphaBytes(Texture source,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();source.Validate();var bytes=new byte[checked(source.Width*source.Height)];
        if(bytes.Length>=4096)
        {
            var input=default(System.Runtime.InteropServices.GCHandle);var output=default(System.Runtime.InteropServices.GCHandle);
            try{
                input=System.Runtime.InteropServices.GCHandle.Alloc(source.Rgba,System.Runtime.InteropServices.GCHandleType.Pinned);
                output=System.Runtime.InteropServices.GCHandle.Alloc(bytes,System.Runtime.InteropServices.GCHandleType.Pinned);
                using var pixels=new SKPixmap(new SKImageInfo(source.Width,source.Height,SKColorType.Rgba8888,SKAlphaType.Unpremul),input.AddrOfPinnedObject());
                if(pixels.ReadPixels(new SKImageInfo(source.Width,source.Height,SKColorType.Alpha8,SKAlphaType.Premul),output.AddrOfPinnedObject(),source.Width)){cancellation.ThrowIfCancellationRequested();return bytes;}
            }finally{if(output.IsAllocated)output.Free();if(input.IsAllocated)input.Free();}
        }
        for(var i=0;i<bytes.Length;i++){if((i&4095)==0)cancellation.ThrowIfCancellationRequested();bytes[i]=source.Rgba[i*4+3];}
        return bytes;
    }
    public static Texture Draw(int width,int height,IEnumerable<RenderPart> parts,IReadOnlyDictionary<string,Texture> pages,Affine worldToPixel,CancellationToken cancellation=default)
    {
        var input=TexturedRaster.ValidateDrawInput(width,height,parts,worldToPixel);var used=new HashSet<string>(StringComparer.Ordinal);long pixels=0;
        var parallel=input.Length>=1024&&height>=128&&Environment.ProcessorCount>=8;
        double VisibleArea(Point a,Point b,Point c)
        {
            var area=Point.Cross(b-a,c-a);
            return area==0||Math.Max(a.X,Math.Max(b.X,c.X))<.5||Math.Min(a.X,Math.Min(b.X,c.X))>width-.5||Math.Max(a.Y,Math.Max(b.Y,c.Y))<.5||Math.Min(a.Y,Math.Min(b.Y,c.Y))>height-.5?0:area;
        }
        static (Affine Mapping,double Footprint) Map(RenderPart part,Texture texture,Point a,Point b,Point c,double area)
        {
            var frame=new Affine(b.X-a.X,c.X-a.X,b.Y-a.Y,c.Y-a.Y,a.X,a.Y);
            var ua=new Point(part.Vertices[0].UV.X*texture.Width,part.Vertices[0].UV.Y*texture.Height);var ub=new Point(part.Vertices[1].UV.X*texture.Width,part.Vertices[1].UV.Y*texture.Height);var uc=new Point(part.Vertices[2].UV.X*texture.Width,part.Vertices[2].UV.Y*texture.Height);var mapping=new Affine(ub.X-ua.X,uc.X-ua.X,ub.Y-ua.Y,uc.Y-ua.Y,ua.X,ua.Y)*frame.Inverse();
            var footprint=texture.Mipmapped||texture.MinLinear!=texture.MagLinear?TexturedRaster.Footprint(part.Vertices[0] with{Position=a},part.Vertices[1] with{Position=b},part.Vertices[2] with{Position=c},texture.Width,texture.Height,area):0;
            return (mapping,footprint);
        }
        foreach(var part in input)
        {
            cancellation.ThrowIfCancellationRequested();
            if(!pages.TryGetValue(part.Page,out var texture))throw new InvalidDataException("Textured triangle/page");texture.Validate();
            if(used.Add(part.Page)){pixels+=(long)texture.Width*texture.Height;if(pixels>Texture.MaxPixels)throw new InvalidDataException("Skia page pixel budget");}
            part.Tint.Validate();part.DarkTint?.Validate();if(part.Blend is not("normal" or "additive" or "multiply" or "screen"))throw new InvalidDataException("Raster blend");
            var a=worldToPixel.Apply(part.Vertices[0].Position);var b=worldToPixel.Apply(part.Vertices[1].Position);var c=worldToPixel.Apply(part.Vertices[2].Position);
            Scalar(a.X);Scalar(a.Y);Scalar(b.X);Scalar(b.Y);Scalar(c.X);Scalar(c.Y);
            foreach(var vertex in part.Vertices){vertex.UV.Validate();Scalar(vertex.UV.X*texture.Width);Scalar(vertex.UV.Y*texture.Height);}
            parallel&=pixels<=2_000_000&&!texture.Mipmapped;
            // A pixel-centre row guarantees a band will validate; row gaps must validate here.
            if(parallel&&Math.Ceiling(Math.Max(.5,Math.Min(a.Y,Math.Min(b.Y,c.Y)))-.5)+.5>Math.Max(a.Y,Math.Max(b.Y,c.Y))){var area=VisibleArea(a,b,c);if(area!=0){var (mapping,_)=Map(part,texture,a,b,c,area);Scalar(mapping.A);Scalar(mapping.B);Scalar(mapping.X);Scalar(mapping.C);Scalar(mapping.D);Scalar(mapping.Y);}}
        }
        var sharedAlpha=parallel?used.ToDictionary(page=>page,page=>{var texture=pages[page];return new Lazy<byte[]>(()=>AlphaBytes(texture,cancellation));},StringComparer.Ordinal):null;
        cancellation.ThrowIfCancellationRequested();var output=new byte[checked(width*height*4)];
        void Band(int top,int rows)
        {
            using var surface=SKSurface.Create(new SKImageInfo(width,height,SKColorType.RgbaF32,SKAlphaType.Premul))??throw new InvalidOperationException("Skia surface allocation");surface.Canvas.Clear(SKColors.Transparent);
            // Native vertex texture mapping drops singular UV triangles; map screen coordinates forward instead.
            // The compiled code is constant; one effect per rendering thread avoids compilation on each redraw.
            var effect=samplingEffect??=SKRuntimeEffect.CreateShader(SamplingShader,out var errors)??throw new InvalidOperationException("Skia UV shader: "+errors);
            var images=new Dictionary<(string Page,int Level,bool Alpha),SKImage>();var mipmaps=new Dictionary<string,Texture[]>(StringComparer.Ordinal);long remainingMipPixels=Texture.MaxPixels;
            using var uniforms=new SKRuntimeEffectUniforms(effect);using var paint=new SKPaint{Color=SKColors.White,IsAntialias=false};
            SKShader?[] samplingShaders=new SKShader[4];SKRuntimeEffectChildren? children=null;(string Page,int First,int Second,bool Linear)? samplingKey=null;(Rgba Light,Rgba Dark)? tintKey=null;SKColorFilter? filter=null;
            var u=new float[3];var v=new float[3];string? blendKey=null;
            SKImage Image(string page,int level,bool alpha,Texture source)
            {
                var key=(page,level,alpha);if(images.TryGetValue(key,out var cached))return cached;
                var bytes=source.Rgba;
                // Share read-only alpha bytes within this draw; native images remain owned by each band.
                if(alpha)bytes=sharedAlpha is null?AlphaBytes(source,cancellation):sharedAlpha[page].Value;
                var image=SKImage.FromPixelCopy(new SKImageInfo(source.Width,source.Height,alpha?SKColorType.Alpha8:SKColorType.Rgb888x,alpha?SKAlphaType.Premul:SKAlphaType.Opaque),bytes)??throw new InvalidOperationException("Skia image allocation");images.Add(key,image);return image;
            }
            try
            {
                foreach(var part in input)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var texture=pages[part.Page];var a=worldToPixel.Apply(part.Vertices[0].Position);var b=worldToPixel.Apply(part.Vertices[1].Position);var c=worldToPixel.Apply(part.Vertices[2].Position);var area=VisibleArea(a,b,c);
                    if(area==0||rows!=height&&(Math.Max(a.Y,Math.Max(b.Y,c.Y))<top+.5||Math.Min(a.Y,Math.Min(b.Y,c.Y))>top+rows-.5))continue;
                    var (mapping,footprint)=Map(part,texture,a,b,c,area);
                    var linear=texture.MagLinear;Texture[]? levels=null;var first=0;var second=0;var mipMix=0d;
                    if(texture.Mipmapped||texture.MinLinear!=texture.MagLinear)
                    {
                        linear=footprint>1?texture.MinLinear:texture.MagLinear;
                        if(texture.Mipmapped&&footprint>1)
                        {
                            if(!mipmaps.TryGetValue(part.Page,out levels)){levels=TexturedRaster.Mipmaps(texture,ref remainingMipPixels);mipmaps.Add(part.Page,levels);}
                            var lod=Math.Clamp(Math.Log2(footprint),0,levels.Length-1);first=texture.MipLinear?(int)Math.Floor(lod):(int)Math.Ceiling(lod-.5);second=texture.MipLinear?Math.Min(first+1,levels.Length-1):first;mipMix=texture.MipLinear?lod-first:0;
                        }
                    }
                    u[0]=Scalar(mapping.A);u[1]=Scalar(mapping.B);u[2]=Scalar(mapping.X);v[0]=Scalar(mapping.C);v[1]=Scalar(mapping.D);v[2]=Scalar(mapping.Y);
                    var lower=levels?[first]??texture;var upper=levels?[second]??texture;
                    if(samplingKey!=(part.Page,first,second,linear))
                    {
                        foreach(var sampler in samplingShaders)sampler?.Dispose();Array.Clear(samplingShaders);
                        var tileX=texture.RepeatX?SKShaderTileMode.Repeat:SKShaderTileMode.Clamp;var tileY=texture.RepeatY?SKShaderTileMode.Repeat:SKShaderTileMode.Clamp;var sampling=new SKSamplingOptions(linear?SKFilterMode.Linear:SKFilterMode.Nearest);
                        samplingShaders[0]=Image(part.Page,first,false,lower).ToShader(tileX,tileY,sampling);samplingShaders[1]=Image(part.Page,first,true,lower).ToShader(tileX,tileY,sampling);samplingShaders[2]=Image(part.Page,second,false,upper).ToShader(tileX,tileY,sampling);samplingShaders[3]=Image(part.Page,second,true,upper).ToShader(tileX,tileY,sampling);
                        children=new SKRuntimeEffectChildren(effect){{"color0",samplingShaders[0]!},{"alpha0",samplingShaders[1]!},{"color1",samplingShaders[2]!},{"alpha1",samplingShaders[3]!}};
                        uniforms["scale0"]=new[]{Scalar((double)lower.Width/texture.Width),Scalar((double)lower.Height/texture.Height)};uniforms["scale1"]=new[]{Scalar((double)upper.Width/texture.Width),Scalar((double)upper.Height/texture.Height)};uniforms["pma"]=texture.Pma?1f:0f;uniforms["nearest"]=linear?0f:1f;samplingKey=(part.Page,first,second,linear);
                    }
                    uniforms["u"]=u;uniforms["v"]=v;uniforms["mipMix"]=Scalar(mipMix);using var shader=effect.ToShader(uniforms,children!);
                    var light=part.Tint;var dark=part.DarkTint??new Rgba();
                    if(tintKey!=(light,dark)){var next=SKColorFilter.CreateColorMatrix([Scalar(light.R-dark.R),0,0,0,Scalar(dark.R),0,Scalar(light.G-dark.G),0,0,Scalar(dark.G),0,0,Scalar(light.B-dark.B),0,Scalar(dark.B),0,0,0,Scalar(light.A),0]);paint.ColorFilter=next;filter?.Dispose();filter=next;tintKey=(light,dark);}
                    paint.Shader=shader;if(blendKey!=part.Blend){paint.BlendMode=part.Blend switch{"additive"=>SKBlendMode.Plus,"multiply"=>SKBlendMode.Multiply,"screen"=>SKBlendMode.Screen,_=>SKBlendMode.SrcOver};blendKey=part.Blend;}
                    SKPoint[] positions=[new(Scalar(a.X),Scalar(a.Y)),new(Scalar(b.X),Scalar(b.Y)),new(Scalar(c.X),Scalar(c.Y))];
                    var saveCount=0;SKRect? ownedCorner=null;
                    void ClipCorner(Point previous,Point corner,Point next)
                    {
                        var x=Math.Floor(corner.X);var y=Math.Floor(corner.Y);
                        if(corner.X!=x+.5||corner.Y!=y+.5||x<0||x>=width||y<0||y>=height)return;
                        // Resolve exact non-AA vertex samples independently of native endpoint rounding.
                        var cell=new SKRect((float)x,(float)y,(float)(x+1),(float)(y+1));
                        if(saveCount==0){saveCount=surface.Canvas.SaveCount;surface.Canvas.Save();}
                        surface.Canvas.ClipRect(cell,SKClipOperation.Difference,false);
                        // A triangle has at most one vertex owned by both incident edges.
                        if(TexturedRaster.OwnsEdge(previous,corner)&&TexturedRaster.OwnsEdge(corner,next))ownedCorner=cell;
                    }
                    using var vertices=SKVertices.CreateCopy(SKVertexMode.Triangles,positions,positions,null);
                    try
                    {
                        if(area>0){ClipCorner(c,a,b);ClipCorner(a,b,c);ClipCorner(b,c,a);}
                        else{ClipCorner(b,a,c);ClipCorner(a,c,b);ClipCorner(c,b,a);}
                        surface.Canvas.DrawVertices(vertices,SKBlendMode.Modulate,paint);
                    }
                    finally{if(saveCount!=0)surface.Canvas.RestoreToCount(saveCount);}
                    if(ownedCorner is SKRect owned)surface.Canvas.DrawRect(owned,paint);
                }
                // Quantizing premultiplied bytes first loses straight color at low alpha.
                using var pixelsView=surface.PeekPixels()??throw new InvalidOperationException("Skia pixel read");var row=new float[checked(width*4)];
                for(var y=top;y<top+rows;y++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    System.Runtime.InteropServices.Marshal.Copy(pixelsView.GetPixels()+checked(y*pixelsView.RowBytes),row,0,row.Length);
                    foreach(var value in row)if(!float.IsFinite(value))throw new InvalidDataException("Nonfinite Skia pixel");
                    for(var x=0;x<width;x++)
                    {
                        var alpha=(double)row[x*4+3];var offset=(y*width+x)*4;if(alpha<=0)continue;
                        for(var channel=0;channel<3;channel++)output[offset+channel]=(byte)Math.Round(Math.Clamp(row[x*4+channel]/alpha,0,1)*255);
                        output[offset+3]=(byte)Math.Round(Math.Clamp(alpha,0,1)*255);
                    }
                }
            }
            finally{paint.Shader=null;paint.ColorFilter=null;filter?.Dispose();foreach(var shader in samplingShaders)shader?.Dispose();foreach(var image in images.Values)image.Dispose();}
        }
        // shortcut: large/mipmapped pages stay serial; expand parallel support only after profiling those inputs.
        var bands=Environment.ProcessorCount>=16?4:2;
        try{if(parallel)Parallel.For(0,bands,new ParallelOptions{CancellationToken=cancellation,MaxDegreeOfParallelism=bands},i=>Band(height*i/bands,height*(i+1)/bands-height*i/bands));else Band(0,height);}
        catch(AggregateException error){var errors=error.Flatten().InnerExceptions;System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors.FirstOrDefault(item=>!EditorErrors.IsExpected(item)&&item is not OperationCanceledException)??errors[0]).Throw();}
        cancellation.ThrowIfCancellationRequested();return new(width,height,output);
    }
}
