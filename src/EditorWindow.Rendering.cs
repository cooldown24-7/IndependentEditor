using System.Windows;
using System.Collections.Immutable;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace IndependentDev;

internal sealed partial class EditorWindow
{
    sealed partial class EditorSurface
    {
        Scene? frameScene;TextureAtlas? frameAtlas;string? frameSkin,frameAnimation;double frameTime;
        ImmutableHashSet<string>? frameHidden;List<RenderPart>? frameParts;Affine[] framePose=[];
        Lazy<VertexGeometry[]>? frameHandleVertices;
        BitmapSource? frameBitmap;List<RenderPart>? bitmapParts;TextureAtlas? bitmapAtlas;Affine bitmapBasis,bitmapMatrix;Point bitmapStep,bitmapOffset;Rect bitmapDestination;Size bitmapViewport;bool bitmapFull,bitmapCrisp;double bitmapCameraScale;
        DrawingGroup? overlay;BitmapSource? overlayBitmap;Rect overlayDestination,overlayBounds;bool overlayRasterFull;DpiScale overlayDpi;Affine[] overlayPose=[];VertexHandle[] overlayHandles=[];List<RenderPart>? overlayParts;Point overlayOffset;double overlayScale;
        (bool Bones,bool Meshes,string? Key,ImmutableHashSet<int> Vertices,int? Bone,bool Handles,ImmutableHashSet<string> Wireframes,ItemViewState View) overlayState;
        internal int PoseBuilds {get;private set;}
        internal int RasterBuilds {get;private set;}
        internal int RasterPatches {get;private set;}
        internal int OverlayPatches {get;private set;}
        internal int ZoomPreviewFrames {get;private set;}
        DispatcherTimer? zoomTimer;
        internal void NavigateZoom(Point anchor,double delta)
        {
            var before=owner.session.Camera.Scale;owner.session.ZoomAt(anchor,delta);if(before==owner.session.Camera.Scale)return;asyncZoom=IsVisible&&owner.nativeRenderer;ZoomWorkerError=null;
            if(zoomTimer is null){zoomTimer=new DispatcherTimer(DispatcherPriority.Background,Dispatcher){Interval=TimeSpan.FromMilliseconds(120)};zoomTimer.Tick+=(_,_)=>FinishZoom();}
            zoomTimer.Stop();zoomTimer.Start();InvalidateVisual();
        }
        internal void FinishZoom(){zoomTimer?.Stop();if(queuedZoom is not null&&queuedZoom.View!=runningZoom?.View)zoomCancellation?.Cancel();InvalidateVisual();}
        (bool Bones,bool Meshes,string? Key,ImmutableHashSet<int> Vertices,int? Bone,bool Handles,ImmutableHashSet<string> Wireframes,ItemViewState View) OverlayState()
            =>(owner.showBones.IsChecked==true,owner.showMeshes.IsChecked==true,owner.session.SelectedKey,owner.session.SelectedVertices,(owner.hierarchy.SelectedItem as InspectorItem)?.Node.BoneIndex,!owner.session.PoseReadOnly&&!owner.playbackTimer.IsEnabled,owner.wireframes,owner.itemView);
        MatrixTransform ZoomTransform(double scale,Point offset)
        {
            var camera=owner.session.Camera;var ratio=camera.Scale/scale;return new(ratio,0,0,ratio,camera.Offset.X-offset.X*ratio,camera.Offset.Y-offset.Y*ratio);
        }
        bool CanPreviewZoom(List<RenderPart> parts)
        {
            if(zoomTimer?.IsEnabled!=true||frameBitmap is null||overlay is null||!ReferenceEquals(bitmapParts,parts)||!ReferenceEquals(bitmapAtlas,owner.atlas)||bitmapCrisp!=(owner.crispPixels.IsChecked==true)||!bitmapFull&&bitmapViewport!=RenderSize||!ReferenceEquals(overlayParts,parts)||overlayState!=OverlayState()||!overlayDpi.Equals(VisualTreeHelper.GetDpi(this)))return false;
            var ratio=owner.session.Camera.Scale/overlayScale;if(ratio<.75||ratio>1.5)return false;
            var viewport=new Rect(RenderSize);
            if(!bitmapFull&&!ZoomTransform(bitmapCameraScale,bitmapOffset).TransformBounds(bitmapDestination).Contains(viewport))return false;
            if(overlayBitmap is not null&&!overlayRasterFull)
            {
                var transform=ZoomTransform(overlayScale,overlayOffset);var visible=transform.TransformBounds(overlayBounds);visible.Intersect(viewport);
                if(!visible.IsEmpty&&!transform.TransformBounds(overlayDestination).Contains(visible))return false;
            }
            return true;
        }
        void DrawZoomFrame(DrawingContext drawing)
        {
            // Reuse pixels during wheel/drag input; the idle timer restores exact sampling and constant-size handles.
            var target=ZoomTransform(bitmapCameraScale,bitmapOffset).TransformBounds(bitmapDestination);
            PixelDisplay.Image(drawing,frameBitmap!,target,bitmapCrisp?BitmapScalingMode.NearestNeighbor:BitmapScalingMode.Linear);ZoomPreviewFrames++;
        }
        void DrawZoomOverlay(DrawingContext drawing)
        {
            drawing.PushTransform(ZoomTransform(overlayScale,overlayOffset));
            if(overlayBitmap is not null)PixelDisplay.Image(drawing,overlayBitmap,overlayDestination,BitmapScalingMode.NearestNeighbor);else drawing.DrawDrawing(overlay);drawing.Pop();
        }
        static bool ChangedBounds(List<RenderPart> before,List<RenderPart> after,Affine matrix,out Rect bounds)
        {
            bounds=Rect.Empty;
            if(before.Count!=after.Count)
            {
                var a=before.GroupBy(part=>(part.Slot,part.Attachment)).ToArray();var b=after.GroupBy(part=>(part.Slot,part.Attachment)).ToArray();if(!a.Select(group=>group.Key).SequenceEqual(b.Select(group=>group.Key)))return false;
                for(var i=0;i<a.Length;i++)
                {
                    if(a[i].Count()!=b[i].Count())foreach(var vertex in a[i].Concat(b[i]).SelectMany(part=>part.Vertices)){var p=matrix.Apply(vertex.Position);bounds.Union(new System.Windows.Point(p.X,p.Y));}
                    else{ChangedBounds(a[i].ToList(),b[i].ToList(),matrix,out var changed);bounds.Union(changed);}
                }
                return true;
            }
            for(var i=0;i<before.Count;i++)
            {
                var a=before[i];var b=after[i];if(a.Page==b.Page&&a.Tint==b.Tint&&a.DarkTint==b.DarkTint&&a.Blend==b.Blend&&a.Vertices.AsSpan().SequenceEqual(b.Vertices))continue;
                foreach(var vertex in a.Vertices.Concat(b.Vertices)){var p=matrix.Apply(vertex.Position);bounds.Union(new System.Windows.Point(p.X,p.Y));}
            }
            return true;
        }
        static bool PatchRegion(Rect bounds,int width,int height,out Int32Rect region)
        {
            region=Int32Rect.Empty;if(bounds.IsEmpty)return true;var left=Math.Floor(bounds.Left);var top=Math.Floor(bounds.Top);var right=Math.Ceiling(bounds.Right);var bottom=Math.Ceiling(bounds.Bottom);
            if(left<0||top<0||right>width||bottom>height||right<=left||bottom<=top||(right-left)*(bottom-top)>width*(double)height*.5)return false;
            region=new((int)left,(int)top,(int)(right-left),(int)(bottom-top));return true;
        }
        static BitmapSource ReplacePixels(BitmapSource original,BitmapSource patch,Int32Rect region)
        {
            if(original.Format!=patch.Format||patch.PixelWidth!=region.Width||patch.PixelHeight!=region.Height)throw new InvalidDataException("Raster patch format");
            var stride=original.PixelWidth*4;var bytes=new byte[stride*original.PixelHeight];original.CopyPixels(bytes,stride,0);patch.CopyPixels(bytes,stride,region.Y*stride+region.X*4);var bitmap=BitmapSource.Create(original.PixelWidth,original.PixelHeight,original.DpiX,original.DpiY,original.Format,null,bytes,stride);bitmap.Freeze();return bitmap;
        }
        BitmapSource RasterBitmap(List<RenderPart> parts,int width,int height,Affine matrix)
        {
            if(owner.nativeRenderer)return RasterNativeFrame(parts,owner.atlas.Pages,width,height,matrix);var bytes=TexturedRaster.Draw(width,height,parts,owner.atlas.Pages,matrix).Rgba;
            for(var i=0;i<bytes.Length;i+=4)(bytes[i],bytes[i+2])=(bytes[i+2],bytes[i]);var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,bytes,width*4);bitmap.Freeze();return bitmap;
        }
        bool PatchFrame(List<RenderPart> parts)
        {
            if(frameBitmap is null||bitmapParts is null||!bitmapFull||!ChangedBounds(bitmapParts,parts,bitmapMatrix,out var dirty))return false;
            if(!dirty.IsEmpty)dirty.Inflate(1,1);if(!PatchRegion(dirty,frameBitmap.PixelWidth,frameBitmap.PixelHeight,out var region))return false;
            if(!region.IsEmpty){var patch=RasterBitmap(parts,region.Width,region.Height,bitmapMatrix with{X=bitmapMatrix.X-region.X,Y=bitmapMatrix.Y-region.Y});frameBitmap=ReplacePixels(frameBitmap,patch,region);RasterBuilds++;RasterPatches++;}bitmapParts=parts;return true;
        }
        List<RenderPart> FrameParts(out Affine[] pose)
        {
            var session=owner.session;
            if(frameParts is null||!ReferenceEquals(frameHidden,owner.itemView.Hidden)||!ReferenceEquals(frameScene,session.Document)||!ReferenceEquals(frameAtlas,owner.atlas)||frameSkin!=session.Skin||frameAnimation!=session.Animation||frameTime!=session.Time)
            {
                var next=Playback.Parts(session.Document,session.Skin,out var nextPose,session.Animation,session.Time,owner.atlas);
                if(owner.itemView.Hidden.Count>0)next=next.Where(part=>owner.itemView.Visible(session.Document,session.Skin,part.Slot,part.Attachment)).ToList();frameHidden=owner.itemView.Hidden;var document=session.Document;var skin=session.Skin;var animation=session.Animation;var time=session.Time;
                frameHandleVertices=new(()=>EditorHandles.VisibleVertices(document,skin,next,nextPose,animation,time).ToArray());
                frameScene=session.Document;frameAtlas=owner.atlas;frameSkin=session.Skin;frameAnimation=session.Animation;frameTime=session.Time;frameParts=next;framePose=nextPose;PoseBuilds++;
            }
            pose=framePose;return frameParts;
        }
        void DrawFrame(DrawingContext drawing,List<RenderPart> parts,int width,int height,Affine matrix,Rect destination)
        {
            var camera=owner.session.Camera;var crisp=owner.crispPixels.IsChecked==true;var basis=matrix with{X=0,Y=0};var step=new Point(destination.Width/width,destination.Height/height);
            // A complete world-pixel raster has the same samples at every nearest-neighbour magnification.
            if(frameBitmap is not null&&bitmapFull&&crisp&&bitmapCrisp&&camera.Scale>1&&bitmapCameraScale>1&&basis==bitmapBasis&&ReferenceEquals(bitmapParts,parts)&&ReferenceEquals(bitmapAtlas,owner.atlas))
            {PixelDisplay.Image(drawing,frameBitmap,ZoomTransform(bitmapCameraScale,bitmapOffset).TransformBounds(bitmapDestination),BitmapScalingMode.NearestNeighbor);return;}
            var compatible=frameBitmap is not null&&ReferenceEquals(bitmapAtlas,owner.atlas)&&bitmapBasis==basis&&bitmapStep==step&&bitmapCrisp==crisp&&bitmapViewport==RenderSize;
            if((!compatible||!ReferenceEquals(bitmapParts,parts)||!bitmapFull&&bitmapOffset!=camera.Offset)&&!(compatible&&!ReferenceEquals(bitmapParts,parts)&&PatchFrame(parts)))
            {
                var plan=PlanFrame(parts,width,height,matrix,destination);InstallFrame(RasterBitmap(parts,plan.Width,plan.Height,plan.Matrix),plan,parts,owner.atlas);RasterBuilds++;
            }
            var delta=camera.Offset-bitmapOffset;var target=bitmapDestination;target.Offset(delta.X,delta.Y);RenderOptions.SetBitmapScalingMode(this,crisp?BitmapScalingMode.NearestNeighbor:BitmapScalingMode.Linear);PixelDisplay.Image(drawing,frameBitmap!,target,RenderOptions.GetBitmapScalingMode(this));
        }
        Rect MeasureOverlay(List<RenderPart> parts,Affine[] pose,VertexHandle[] handles)
        {
            var bounds=Rect.Empty;void Include(Point point,double radius)=>bounds.Union(new Rect(point.X-radius,point.Y-radius,radius*2,radius*2));
            PaintOverlay(parts,pose,handles,(a,b,_)=>{Include(a,1);Include(b,1);},(p,r,_,pen)=>Include(p,r+(pen is null?0:1)));return bounds;
        }
        BitmapSource RasterCurrentOverlay(List<RenderPart> parts,Affine[] pose,VertexHandle[] handles,Rect destination,DpiScale dpi)
        {
            BitmapSource Wpf(){var source=new DrawingGroup();using(var dc=source.Open())BuildOverlay(dc,parts,pose,handles,destination);source.Freeze();return RasterOverlay(source,destination,dpi);}
            if(!owner.nativeRenderer||dpi.DpiScaleX!=dpi.DpiScaleY)return Wpf();
            var snapshot=CaptureOverlay(parts,pose,handles);return NativeOverlaySafe(snapshot,destination,dpi)?RasterOverlaySnapshot(snapshot,destination,dpi):Wpf();
        }
        static BitmapSource RasterOverlay(DrawingGroup source,Rect destination,DpiScale dpi)
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.PushTransform(new TranslateTransform(-destination.X,-destination.Y));dc.DrawDrawing(source);dc.Pop();}
            var bitmap=new RenderTargetBitmap((int)Math.Round(destination.Width*dpi.DpiScaleX),(int)Math.Round(destination.Height*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
        }
        void DrawOverlay(DrawingContext drawing,List<RenderPart> parts,Affine[] pose)
        {
            var session=owner.session;var state=OverlayState();
            var dpi=VisualTreeHelper.GetDpi(this);
            var movement=session.Camera.Offset-overlayOffset;var coverage=overlayDestination;coverage.Offset(movement.X,movement.Y);var visible=overlay is null?Rect.Empty:overlayBounds;if(!visible.IsEmpty){visible.Offset(movement.X,movement.Y);visible.Intersect(new Rect(RenderSize));}
            var uncovered=overlayBitmap is not null&&!overlayRasterFull&&!visible.IsEmpty&&!coverage.Contains(visible);var newlyVisible=overlayBitmap is null&&parts.Count>256&&!visible.IsEmpty&&visible.Width*visible.Height*dpi.DpiScaleX*dpi.DpiScaleY<=1_000_000;
            if(overlay is null||!ReferenceEquals(overlayParts,parts)||overlayScale!=session.Camera.Scale||overlayState!=state||!overlayDpi.Equals(dpi)||uncovered||newlyVisible)
            {
                var oldHandles=overlayHandles;var shift=session.Camera.Offset-overlayOffset;var previous=overlayDestination;previous.Offset(shift.X,shift.Y);var patchable=!uncovered&&overlayBitmap is not null&&overlayParts is not null&&overlayState==state&&overlayScale==session.Camera.Scale&&overlayDpi.Equals(dpi)&&overlayPose.AsSpan().SequenceEqual(pose);
                var handles=state.Item6?EditorHandles.Visible(session,parts,pose):[];var next=new DrawingGroup();var nextBounds=MeasureOverlay(parts,pose,handles);
                var patched=false;
                if(patchable&&oldHandles.Length==handles.Length&&ChangedBounds(overlayParts!,parts,session.Camera.Matrix,out var dirty))
                {
                    for(var i=0;i<handles.Length;i++){var a=oldHandles[i];var b=handles[i];if(a.Key!=b.Key||a.Index!=b.Index){patchable=false;break;}var p=a.Screen+shift;if(p!=b.Screen){dirty.Union(new System.Windows.Point(p.X,p.Y));dirty.Union(new System.Windows.Point(b.Screen.X,b.Screen.Y));}}
                    if(patchable)
                    {
                        if(!dirty.IsEmpty)dirty.Inflate(6,6);var pixels=dirty.IsEmpty?Rect.Empty:new Rect((dirty.X-previous.X)*dpi.DpiScaleX,(dirty.Y-previous.Y)*dpi.DpiScaleY,dirty.Width*dpi.DpiScaleX,dirty.Height*dpi.DpiScaleY);
                        if(PatchRegion(pixels,overlayBitmap!.PixelWidth,overlayBitmap.PixelHeight,out var region))
                        {if(!region.IsEmpty){var target=new Rect(previous.X+region.X/dpi.DpiScaleX,previous.Y+region.Y/dpi.DpiScaleY,region.Width/dpi.DpiScaleX,region.Height/dpi.DpiScaleY);overlayBitmap=ReplacePixels(overlayBitmap,RasterCurrentOverlay(parts,pose,handles,target,dpi),region);OverlayPatches++;}overlayDestination=previous;patched=true;}
                    }
                }
                if(!patched)
                {
                    // shortcut: viewports exceeding this pixel budget stay vector-based; tile them if large-window profiling requires it.
                    overlayBitmap=null;overlayRasterFull=false;var bounds=nextBounds;
                    if(parts.Count>256&&!bounds.IsEmpty)
                    {
                        bounds.Inflate(8,8);overlayRasterFull=bounds.Width*bounds.Height*dpi.DpiScaleX*dpi.DpiScaleY<=1_000_000;
                        if(!overlayRasterFull){var factor=Math.Max(1,Math.Min(1.4,Math.Sqrt(980_000/(ActualWidth*ActualHeight*dpi.DpiScaleX*dpi.DpiScaleY))));bounds.Intersect(new Rect(ActualWidth*(1-factor)/2,ActualHeight*(1-factor)/2,ActualWidth*factor,ActualHeight*factor));}
                        if(!bounds.IsEmpty)
                        {
                            var left=Math.Floor(bounds.Left*dpi.DpiScaleX);var top=Math.Floor(bounds.Top*dpi.DpiScaleY);var w=Math.Ceiling(bounds.Right*dpi.DpiScaleX)-left;var h=Math.Ceiling(bounds.Bottom*dpi.DpiScaleY)-top;
                            if(w>0&&h>0&&w<=8192&&h<=8192&&w*h<=1_000_000){overlayDestination=new(left/dpi.DpiScaleX,top/dpi.DpiScaleY,w/dpi.DpiScaleX,h/dpi.DpiScaleY);overlayBitmap=RasterCurrentOverlay(parts,pose,handles,overlayDestination,dpi);}
                        }
                    }
                }
                if(overlayBitmap is null)using(var dc=next.Open())BuildOverlay(dc,parts,pose,handles);next.Freeze();overlay=next;overlayBounds=nextBounds;overlayParts=parts;overlayOffset=session.Camera.Offset;overlayScale=session.Camera.Scale;overlayState=state;overlayPose=pose;overlayHandles=handles;overlayDpi=dpi;
            }
            var delta=session.Camera.Offset-overlayOffset;drawing.PushTransform(new TranslateTransform(delta.X,delta.Y));if(overlayBitmap is not null)PixelDisplay.Image(drawing,overlayBitmap,overlayDestination,BitmapScalingMode.NearestNeighbor);else drawing.DrawDrawing(overlay);drawing.Pop();
        }
    }
}
