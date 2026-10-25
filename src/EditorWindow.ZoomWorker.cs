using System.Windows;
using System.Collections.Immutable;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;
using System.Runtime.CompilerServices;
namespace IndependentDev;

internal sealed partial class EditorWindow
{
    sealed partial class EditorSurface
    {
        enum OverlayInk { Wire,Selected,Bone,Vertex,SelectedVertex,Hull,Interior,SelectedInterior }
        readonly record struct OverlayMark(Point A,Point B,double Radius,OverlayInk Ink,bool Outline=false);
        sealed record OverlayContext(Scene Scene,string Skin,string? Animation,double Time,List<RenderPart> Parts,Affine[] Pose,Affine Camera,(bool Bones,bool Meshes,string? Key,ImmutableHashSet<int> Vertices,int? Bone,bool Handles,ImmutableHashSet<string> Wireframes,ItemViewState View) State,VertexHandle[]? Handles)
        {public Lazy<VertexGeometry[]>? Geometry {get;init;}}
        sealed record OverlaySnapshot(OverlayMark[] Marks,Rect Bounds,VertexHandle[] Handles);
        OverlaySnapshot CaptureOverlay(List<RenderPart> parts,Affine[] pose,VertexHandle[] handles)
            =>CaptureOverlayContext(new(owner.session.Document,owner.session.Skin,owner.session.Animation,owner.session.Time,parts,pose,owner.session.Camera.Matrix,OverlayState(),handles));
        static OverlaySnapshot CaptureOverlayContext(OverlayContext context,CancellationToken cancellation=default)
        {
            cancellation.ThrowIfCancellationRequested();context=context with{Handles=context.State.Handles?context.Handles??(context.Geometry is null?EditorHandles.Visible(context.Scene,context.Skin,context.Parts,context.Pose,context.Camera,context.Animation,context.Time):EditorHandles.ProjectVertices(context.Geometry.Value,context.Camera)):[]};
            var marks=new List<OverlayMark>(Math.Min(32768,checked((context.State.Meshes?context.Parts.Count*3:0)+(context.State.Bones?context.Pose.Length*2:0)+context.Handles!.Length)));var bounds=Rect.Empty;
            // These callbacks run tens of thousands of times per frame, before tiered JIT normally optimizes them.
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            void Include(Point p,double r)=>bounds.Union(new Rect(p.X-r,p.Y-r,r*2,r*2));
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            void Segment(Point a,Point b,Pen pen){cancellation.ThrowIfCancellationRequested();marks.Add(new(a,b,0,ReferenceEquals(pen,WirePen)?OverlayInk.Wire:ReferenceEquals(pen,HullPen)?OverlayInk.Hull:ReferenceEquals(pen,InteriorPen)?OverlayInk.Interior:ReferenceEquals(pen,SelectedInteriorPen)?OverlayInk.SelectedInterior:ReferenceEquals(pen,SelectedPen)?OverlayInk.Selected:OverlayInk.Bone));Include(a,1);Include(b,1);}
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            void Circle(Point p,double r,Brush brush,Pen? pen){cancellation.ThrowIfCancellationRequested();var color=((SolidColorBrush)brush).Color;marks.Add(new(p,default,r,color==Colors.White?OverlayInk.Vertex:color==Colors.Yellow?OverlayInk.SelectedVertex:color==Colors.Orange?OverlayInk.Selected:OverlayInk.Bone,pen is not null));Include(p,r+(pen is null?0:1));}
            PaintOverlay(context,Segment,Circle);
            return new(marks.ToArray(),bounds,context.Handles!);
        }
        static Rect MarkBounds(OverlayMark mark)
        {
            var bounds=mark.Radius==0?new Rect(new System.Windows.Point(mark.A.X,mark.A.Y),new System.Windows.Point(mark.B.X,mark.B.Y)):new Rect(mark.A.X-mark.Radius,mark.A.Y-mark.Radius,mark.Radius*2,mark.Radius*2);bounds.Inflate(mark.Ink==OverlayInk.Bone?4:2,mark.Ink==OverlayInk.Bone?4:2);return bounds;
        }
        static bool MarkVisible(OverlayMark mark,Rect target)=>MarkBounds(mark).IntersectsWith(target);
        static bool NativeOverlaySafe(OverlaySnapshot snapshot,Rect target,DpiScale dpi)
        {
            if(dpi.DpiScaleX!=dpi.DpiScaleY)return false;
            bool Safe(Point p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&Math.Abs((p.X-target.X)*dpi.DpiScaleX)<=1_000_000&&Math.Abs((p.Y-target.Y)*dpi.DpiScaleY)<=1_000_000;
            return snapshot.Marks.Where(mark=>MarkVisible(mark,target)).All(mark=>Safe(mark.A)&&(mark.Radius!=0||Safe(mark.B)));
        }
        static BitmapSource RasterOverlaySnapshot(OverlaySnapshot snapshot,Rect target,DpiScale dpi,CancellationToken cancellation=default)
        {
            cancellation.ThrowIfCancellationRequested();
            var width=(int)Math.Round(target.Width*dpi.DpiScaleX);var height=(int)Math.Round(target.Height*dpi.DpiScaleY);var bytes=new byte[checked(width*height*4)];
            // Every band uses the same mark bounds; compute them once instead of rebuilding Rects per band.
            var bands=dpi.DpiScaleX==dpi.DpiScaleY&&dpi.DpiScaleY>=1&&snapshot.Marks.Length>=1024&&height>=128?Math.Min(Environment.ProcessorCount>=16&&target.Contains(snapshot.Bounds)?8:4,Math.Max(1,Environment.ProcessorCount-1)):1;
            var bounds=bands>1?new Rect[snapshot.Marks.Length]:null;
            if(bounds is not null)for(var i=0;i<bounds.Length;i++){cancellation.ThrowIfCancellationRequested();bounds[i]=MarkBounds(snapshot.Marks[i]);}
            void Band(int top,int rows)
            {
                // Keep the full origin and clip: clipping at a band edge changes native antialiasing pixels.
                using var surface=SKSurface.Create(new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Premul))??throw new InvalidOperationException("Overlay surface");var canvas=surface.Canvas;canvas.Clear(SKColors.Transparent);
                var tileTarget=new Rect(target.X,target.Y+top/dpi.DpiScaleY,target.Width,rows/dpi.DpiScaleY);
                using var stroke=new SKPaint{IsAntialias=true,Style=SKPaintStyle.Stroke};using var fill=new SKPaint{IsAntialias=true};using var outline=new SKPaint{IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=(float)dpi.DpiScaleX,Color=SKColors.Black};
                OverlayInk? strokeInk=null,fillInk=null;
                var lines=new List<SKPoint>(2048);
                void FlushLines(){if(lines.Count==0)return;canvas.DrawPoints(SKPointMode.Lines,lines.ToArray(),stroke);lines.Clear();}
                SKPoint Pixel(Point p)=>new((float)((p.X-target.X)*dpi.DpiScaleX),(float)((p.Y-target.Y)*dpi.DpiScaleY));
                for(var i=0;i<snapshot.Marks.Length;i++)
                {
                    cancellation.ThrowIfCancellationRequested();var mark=snapshot.Marks[i];var box=bounds is null?MarkBounds(mark):bounds[i];if(!box.IntersectsWith(target)||rows!=height&&!box.IntersectsWith(tileTarget))continue;var p=Pixel(mark.A);var color=mark.Ink switch{OverlayInk.Wire=>new SKColor(0,0xdc,0xca,0x99),OverlayInk.Hull=>SKColors.Turquoise,OverlayInk.Interior or OverlayInk.SelectedInterior=>SKColors.Gray,OverlayInk.Selected=>SKColors.Orange,OverlayInk.Bone=>SKColors.LightSkyBlue,OverlayInk.Vertex=>SKColors.White,_=>SKColors.Yellow};
                    if(mark.Radius==0){if(strokeInk!=mark.Ink){FlushLines();stroke.Color=color;stroke.StrokeWidth=(float)((mark.Ink is OverlayInk.Selected or OverlayInk.Hull or OverlayInk.SelectedInterior?2:1)*dpi.DpiScaleX);strokeInk=mark.Ink;}lines.Add(p);lines.Add(Pixel(mark.B));if(lines.Count==2048)FlushLines();}
                    else{FlushLines();if(fillInk!=mark.Ink){fill.Color=color;fillInk=mark.Ink;}var rx=(float)(mark.Radius*dpi.DpiScaleX);var ry=(float)(mark.Radius*dpi.DpiScaleY);canvas.DrawOval(p.X,p.Y,rx,ry,fill);if(mark.Outline)canvas.DrawOval(p.X,p.Y,rx,ry,outline);}
                }
                FlushLines();
                using var pixels=surface.PeekPixels()??throw new InvalidOperationException("Overlay pixels");for(var y=top;y<top+rows;y++){cancellation.ThrowIfCancellationRequested();System.Runtime.InteropServices.Marshal.Copy(pixels.GetPixels()+y*pixels.RowBytes,bytes,y*width*4,width*4);}
            }
            try{if(bands>1)Parallel.For(0,bands,new ParallelOptions{CancellationToken=cancellation,MaxDegreeOfParallelism=bands},i=>Band(height*i/bands,height*(i+1)/bands-height*i/bands));else Band(0,height);}
            catch(AggregateException error){var errors=error.Flatten().InnerExceptions;System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors.FirstOrDefault(item=>!EditorErrors.IsExpected(item)&&item is not OperationCanceledException)??errors[0]).Throw();}
            cancellation.ThrowIfCancellationRequested();
            var bitmap=BitmapSource.Create(width,height,96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32,null,bytes,width*4);bitmap.Freeze();return bitmap;
        }
        sealed record FramePlan(int Width,int Height,Affine Matrix,Rect Destination,Affine Basis,Point Step,Point Offset,double Scale,Size Viewport,bool Full,bool Crisp);
        FramePlan PlanFrame(List<RenderPart> parts,int width,int height,Affine matrix,Rect destination)
            =>PlanFrame(parts,width,height,matrix,destination,owner.session.Camera.Offset,owner.session.Camera.Scale,RenderSize,owner.crispPixels.IsChecked==true);
        static FramePlan PlanFrame(List<RenderPart> parts,int width,int height,Affine matrix,Rect destination,Point offset,double scale,Size viewport,bool crisp)
        {
            var basis=matrix with{X=0,Y=0};var step=new Point(destination.Width/width,destination.Height/height);var bounds=Rect.Empty;foreach(var vertex in parts.SelectMany(part=>part.Vertices)){var p=matrix.Apply(vertex.Position);bounds.Union(new System.Windows.Point(p.X,p.Y));}var left=Math.Floor(bounds.Left)-2;var top=Math.Floor(bounds.Top)-2;var w=Math.Ceiling(bounds.Right)+2-left;var h=Math.Ceiling(bounds.Bottom)+2-top;var full=!bounds.IsEmpty&&w>0&&h>0&&w*h<=1_000_000;
            if(full){matrix=matrix with{X=matrix.X-left,Y=matrix.Y-top};width=(int)w;height=(int)h;destination=new(destination.Left+left*step.X,destination.Top+top*step.Y,width*step.X,height*step.Y);}
            return new(width,height,matrix,destination,basis,step,offset,scale,viewport,full,crisp);
        }
        void InstallFrame(BitmapSource bitmap,FramePlan plan,List<RenderPart> parts,TextureAtlas atlas)
        {
            frameBitmap=bitmap;bitmapParts=parts;bitmapAtlas=atlas;bitmapMatrix=plan.Matrix;bitmapBasis=plan.Basis;bitmapStep=plan.Step;bitmapOffset=plan.Offset;bitmapDestination=plan.Destination;bitmapFull=plan.Full;bitmapCrisp=plan.Crisp;bitmapViewport=plan.Viewport;bitmapCameraScale=plan.Scale;
        }
        static BitmapSource RasterNativeFrame(List<RenderPart> parts,IReadOnlyDictionary<string,Texture> pages,int width,int height,Affine matrix,CancellationToken cancellation=default)
        {
            var bytes=SkiaRaster.Draw(width,height,parts,pages,matrix,cancellation).Rgba;for(var i=0;i<bytes.Length;i+=4)(bytes[i],bytes[i+2])=(bytes[i+2],bytes[i]);cancellation.ThrowIfCancellationRequested();var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,bytes,width*4);bitmap.Freeze();return bitmap;
        }
        sealed record ZoomView(Scene Scene,TextureAtlas Atlas,List<RenderPart> Parts,string Skin,string? Animation,double Time,bool Crisp,(bool Bones,bool Meshes,string? Key,ImmutableHashSet<int> Vertices,int? Bone,bool Handles,ImmutableHashSet<string> Wireframes,ItemViewState View) State,DpiScale Dpi,Size Viewport,double Scale,Point Offset);
        sealed record ZoomInput(ZoomView View,Affine[] Pose,int Width,int Height,Affine Matrix,Rect Destination);
        sealed record ZoomStages(double PrepareMs,double TextureMs,double OverlayMs);
        sealed record ZoomResult(FramePlan Frame,BitmapSource Texture,Rect OverlayTarget,bool OverlayFull,OverlaySnapshot Snapshot,BitmapSource Overlay,ZoomStages Stages);
        sealed class ZoomFallback:Exception;
        bool asyncZoom;Task<ZoomResult>? zoomWork;CancellationTokenSource? zoomCancellation;ZoomInput? runningZoom,queuedZoom;ZoomView? installedZoom;
        internal bool ZoomWorkPending=>zoomWork is not null||queuedZoom is not null;
        internal int ZoomJobsStarted {get;private set;}
        internal int ZoomJobsApplied {get;private set;}
        internal int ZoomJobsDiscarded {get;private set;}
        internal string? ZoomWorkerError {get;private set;}
        internal object? ZoomStageTimes {get;private set;}
        ZoomView ZoomState(List<RenderPart> parts)=>new(owner.session.Document,owner.atlas,parts,owner.session.Skin,owner.session.Animation,owner.session.Time,owner.crispPixels.IsChecked==true,OverlayState(),VisualTreeHelper.GetDpi(this),RenderSize,owner.session.Camera.Scale,owner.session.Camera.Offset);
        bool CurrentSource(ZoomView view)=>IsVisible&&ReferenceEquals(view.Scene,owner.session.Document)&&ReferenceEquals(view.Atlas,owner.atlas)&&view.Skin==owner.session.Skin&&view.Animation==owner.session.Animation&&view.Time==owner.session.Time&&view.Crisp==(owner.crispPixels.IsChecked==true)&&view.State==OverlayState()&&view.Dpi.Equals(VisualTreeHelper.GetDpi(this))&&view.Viewport==RenderSize;
        internal void AbortZoom(){asyncZoom=false;queuedZoom=null;installedZoom=null;zoomTimer?.Stop();try{zoomCancellation?.Cancel();}catch(ObjectDisposedException)when(Dispatcher.HasShutdownStarted){}}
        bool PrepareAsyncZoom(List<RenderPart> parts,Affine[] pose,int width,int height,Affine matrix,Rect destination)
        {
            if(!asyncZoom)return false;
            if(!IsVisible||!owner.nativeRenderer||frameBitmap is null||overlay is null||!ReferenceEquals(bitmapParts,parts)||!ReferenceEquals(bitmapAtlas,owner.atlas)||overlayState!=OverlayState()||!overlayDpi.Equals(VisualTreeHelper.GetDpi(this))){AbortZoom();return false;}
            var view=ZoomState(parts);if(installedZoom==view&&zoomTimer?.IsEnabled!=true&&!ZoomWorkPending){asyncZoom=false;return false;}
            var refresh=zoomTimer?.IsEnabled!=true||!CanPreviewZoom(parts);
            if(zoomWork is not null){queuedZoom=view==runningZoom?.View?null:new(view,pose,width,height,matrix,destination);}
            else if(refresh&&installedZoom!=view&&!StartZoom(new(view,pose,width,height,matrix,destination))){AbortZoom();return false;}
            return true;
        }
        bool StartZoom(ZoomInput input)
        {
            if(zoomWork is not null)throw new InvalidOperationException("Only one zoom render may run at a time");
            if(!CurrentSource(input.View))return false;
            var view=input.View;if(view.Dpi.DpiScaleX!=view.Dpi.DpiScaleY||view.Viewport.Width*view.Viewport.Height*view.Dpi.DpiScaleX*view.Dpi.DpiScaleY>980_000)return false;
            var basis=input.Matrix with{X=0,Y=0};var reuse=frameBitmap is not null&&bitmapFull&&view.Crisp&&bitmapCrisp&&view.Scale>1&&bitmapCameraScale>1&&basis==bitmapBasis&&ReferenceEquals(bitmapParts,view.Parts)&&ReferenceEquals(bitmapAtlas,view.Atlas);
            var reusedPlan=reuse?new FramePlan(frameBitmap!.PixelWidth,frameBitmap.PixelHeight,bitmapMatrix,bitmapDestination,bitmapBasis,bitmapStep,bitmapOffset,bitmapCameraScale,bitmapViewport,bitmapFull,bitmapCrisp):null;var cached=reuse?frameBitmap:null;var cancellation=new CancellationTokenSource();zoomCancellation=cancellation;runningZoom=input;ZoomJobsStarted++;
            var geometry=ReferenceEquals(view.Parts,frameParts)?frameHandleVertices:null;
            zoomWork=Task.Run(async ()=>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                // Both rasters read the same immutable view; publish only after both have finished.
                var plan=reusedPlan??PlanFrame(view.Parts,input.Width,input.Height,input.Matrix,input.Destination,view.Offset,view.Scale,view.Viewport,view.Crisp);
                double textureMs=0;var textureWork=Task.Run(()=>{var clock=System.Diagnostics.Stopwatch.StartNew();var bitmap=cached??RasterNativeFrame(view.Parts,view.Atlas.Pages,plan.Width,plan.Height,plan.Matrix,cancellation.Token);textureMs=clock.Elapsed.TotalMilliseconds;return bitmap;},cancellation.Token);
                var overlayWork=Task.Run(()=>
                {
                var watch=System.Diagnostics.Stopwatch.StartNew();cancellation.Token.ThrowIfCancellationRequested();var camera=new Affine(view.Scale,0,0,-view.Scale,view.Offset.X,view.Offset.Y);var snapshot=CaptureOverlayContext(new(view.Scene,view.Skin,view.Animation,view.Time,view.Parts,input.Pose,camera,view.State,null){Geometry=geometry},cancellation.Token);var target=snapshot.Bounds;if(target.IsEmpty)target=new Rect(0,0,1/view.Dpi.DpiScaleX,1/view.Dpi.DpiScaleY);target.Inflate(8,8);var full=target.Width*target.Height*view.Dpi.DpiScaleX*view.Dpi.DpiScaleY<=1_000_000;
                if(!full){var factor=Math.Max(1,Math.Min(1.4,Math.Sqrt(980_000/(view.Viewport.Width*view.Viewport.Height*view.Dpi.DpiScaleX*view.Dpi.DpiScaleY))));target.Intersect(new Rect(view.Viewport.Width*(1-factor)/2,view.Viewport.Height*(1-factor)/2,view.Viewport.Width*factor,view.Viewport.Height*factor));}
                if(target.IsEmpty)target=new Rect(0,0,1/view.Dpi.DpiScaleX,1/view.Dpi.DpiScaleY);var left=Math.Floor(target.Left*view.Dpi.DpiScaleX);var top=Math.Floor(target.Top*view.Dpi.DpiScaleY);var w=Math.Ceiling(target.Right*view.Dpi.DpiScaleX)-left;var h=Math.Ceiling(target.Bottom*view.Dpi.DpiScaleY)-top;if(w<=0||h<=0||w>8192||h>8192||w*h>1_000_000)throw new ZoomFallback();target=new(left/view.Dpi.DpiScaleX,top/view.Dpi.DpiScaleY,w/view.Dpi.DpiScaleX,h/view.Dpi.DpiScaleY);if(!NativeOverlaySafe(snapshot,target,view.Dpi))throw new ZoomFallback();
                var prepared=watch.Elapsed.TotalMilliseconds;var overlay=RasterOverlaySnapshot(snapshot,target,view.Dpi,cancellation.Token);cancellation.Token.ThrowIfCancellationRequested();return (target,full,snapshot,overlay,prepared,raster:watch.Elapsed.TotalMilliseconds-prepared);
                },cancellation.Token);
                await Task.WhenAll(textureWork,overlayWork).ConfigureAwait(false);var result=overlayWork.Result;
                return new ZoomResult(plan,textureWork.Result,result.target,result.full,result.snapshot,result.overlay,new(result.prepared,textureMs,result.raster));
            },cancellation.Token);
            _=zoomWork.ContinueWith(task=>{if(Dispatcher.HasShutdownStarted){_=task.Exception;cancellation.Dispose();return;}Dispatcher.BeginInvoke(DispatcherPriority.Render,(Action)(()=>CompleteZoom(input,task,cancellation)));},CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);return true;
        }
        void CompleteZoom(ZoomInput input,Task<ZoomResult> task,CancellationTokenSource cancellation)
        {
            zoomWork=null;zoomCancellation=null;runningZoom=null;cancellation.Dispose();
            var sourceCurrent=CurrentSource(input.View);var alreadyExact=sourceCurrent&&installedZoom==ZoomState(input.View.Parts)&&installedZoom!=input.View;
            if(task.IsCompletedSuccessfully&&asyncZoom&&sourceCurrent&&!alreadyExact)
            {
                var result=task.Result;ZoomStageTimes=result.Stages;var view=input.View;if(!ReferenceEquals(frameBitmap,result.Texture))RasterBuilds++;InstallFrame(result.Texture,result.Frame,view.Parts,view.Atlas);overlayBitmap=result.Overlay;overlayDestination=result.OverlayTarget;overlayRasterFull=result.OverlayFull;overlayBounds=result.Snapshot.Bounds;overlay=new DrawingGroup();overlay.Freeze();overlayParts=view.Parts;overlayOffset=view.Offset;overlayScale=view.Scale;overlayState=view.State;overlayDpi=view.Dpi;overlayPose=input.Pose;overlayHandles=result.Snapshot.Handles;installedZoom=view;ZoomJobsApplied++;
            }
            else{ZoomJobsDiscarded++;if(task.IsFaulted){var error=task.Exception!.GetBaseException();if(error is ZoomFallback)AbortZoom();else{if(!EditorErrors.IsExpected(error)&&error is not OperationCanceledException)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();ZoomWorkerError=error.Message;owner.status.Text=error.Message;AbortZoom();}}}
            var next=queuedZoom;queuedZoom=null;if(asyncZoom&&next is not null&&CurrentSource(next.View)&&next.View==ZoomState(next.View.Parts)&&next.View!=installedZoom)StartZoom(next);InvalidateVisual();
        }
    }
}


