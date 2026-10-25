using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace IndependentDev;

internal sealed class TexturePreview:Grid
{
    sealed record Choice(string Label,string Page,AtlasRegion? Region=null);
    readonly ListBox choices=new(){DisplayMemberPath=nameof(Choice.Label)};
    readonly PixelPreview image=new(){Stretch=Stretch.Uniform,Margin=new Thickness(20)};
    readonly CheckBox crisp=new(){Content="픽셀 선명하게",IsChecked=true,Margin=new Thickness(8)};
    readonly CheckBox partOnly=new(){Content="선택 파츠만",IsChecked=true,Margin=new Thickness(8)};
    readonly CheckBox grid=new(){Content="픽셀 격자",IsChecked=true,Margin=new Thickness(8)};
    readonly TextBlock details=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12)};
    TextureAtlas atlas;
    string? cachedPage;
    BitmapSource? bitmap;
    Scene? scene;
    string skin="default";
    public TexturePreview(TextureAtlas atlas,Scene? scene=null,string skin="default")
    {
        this.atlas=atlas;
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var tools=new WrapPanel();tools.Children.Add(crisp);tools.Children.Add(partOnly);tools.Children.Add(grid);SetColumnSpan(tools,5);Children.Add(tools);
        void Button(string label,Action action){var button=new Button{Content=label,Margin=new Thickness(3),Padding=new Thickness(8,4,8,4)};button.Click+=(_,_)=>action();tools.Children.Add(button);}
        Button("맞춤",image.Fit);Button("100%",image.ActualSize);Button("−",()=>image.ZoomAt(new(image.ActualWidth/2,image.ActualHeight/2),-1));Button("+",()=>image.ZoomAt(new(image.ActualWidth/2,image.ActualHeight/2),1));
        var zoom=new TextBlock{Margin=new Thickness(8),VerticalAlignment=VerticalAlignment.Center};tools.Children.Add(zoom);AutomationProperties.SetName(zoom,"텍스처 확대 배율");image.ViewChanged+=()=>zoom.Text=$"{image.Camera.Scale*100:0.##}%";
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        crisp.Click+=(_,_)=>{RenderOptions.SetBitmapScalingMode(image,crisp.IsChecked==true?BitmapScalingMode.NearestNeighbor:BitmapScalingMode.Linear);image.InvalidateVisual();};
        grid.Click+=(_,_)=>{image.PixelGrid=grid.IsChecked==true;image.InvalidateVisual();};partOnly.Click+=(_,_)=>ShowSelection();
        ColumnDefinitions.Add(new(){Width=new GridLength(200),MinWidth=80});ColumnDefinitions.Add(new(){Width=new GridLength(5)});ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=80});ColumnDefinitions.Add(new(){Width=new GridLength(5)});ColumnDefinitions.Add(new(){Width=new GridLength(250),MinWidth=80});
        AutomationProperties.SetName(choices,"텍스처 영역 목록");AutomationProperties.SetName(image,"Atlas PNG 미리보기");AutomationProperties.SetName(details,"텍스처 영역 정보");
        choices.Background=new SolidColorBrush(Color.FromRgb(0x30,0x32,0x36));Children.Add(choices);
        VirtualizingStackPanel.SetIsVirtualizing(choices,true);VirtualizingStackPanel.SetVirtualizationMode(choices,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(choices,true);
        var view=new Border{Background=new SolidColorBrush(Color.FromRgb(0x25,0x27,0x2b)),Child=image,ClipToBounds=true};SetColumn(view,2);Children.Add(view);
        var info=new ScrollViewer{Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};SetColumn(info,4);Children.Add(info);
        foreach(var column in new[]{1,3}){var split=new GridSplitter{Width=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=Brushes.Transparent,ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Focusable=true};SetColumn(split,column);AutomationProperties.SetName(split,column==1?"텍스처 목록 너비 조절":"텍스처 정보 너비 조절");Children.Add(split);}
        foreach(UIElement child in Children)if(!ReferenceEquals(child,tools))SetRow(child,1);
        choices.SelectionChanged+=(_,_)=>ShowSelection(true);SetAtlas(atlas,scene,skin);
    }
    public void SetAtlas(TextureAtlas next,Scene? scene=null,string skin="default")
    {
        var previous=choices.SelectedItem as Choice;atlas=next;this.scene=scene;this.skin=skin;cachedPage=null;bitmap=null;image.Source=null;
        choices.ItemsSource=atlas.Pages.Keys.Select(page=>new Choice("PNG · "+page,page)).Concat(atlas.AllRegions.Select(region=>new Choice(region.Index<0?region.Name:$"{region.Name} [{region.Index}]",region.Page,region))).ToArray();
        choices.SelectedItem=choices.Items.Cast<Choice>().FirstOrDefault(choice=>previous?.Region is{} old?choice.Region is{} region&&region.Name==old.Name&&region.Index==old.Index:choice.Region is null&&choice.Page==previous?.Page);if(choices.SelectedIndex<0&&choices.Items.Count>0)choices.SelectedIndex=0;
        ShowSelection(true);
        if(choices.Items.Count==0)details.Text="표시할 텍스처가 없습니다.";
    }
    public void UpdateScene(Scene next,string nextSkin)
    {if(scene is not null&&(!ReferenceEquals(scene,next)||skin!=nextSkin)){scene=next;skin=nextSkin;ShowSelection();}}
    void ShowSelection(bool resetView=false)
    {
        if(choices.SelectedItem is not Choice choice)return;
        try
        {
            var texture=atlas.Pages[choice.Page];var region=choice.Region;
            if(region is not null&&(long)region.OriginalWidth*region.OriginalHeight>Texture.MaxPixels)throw new InvalidDataException("Preview dimensions");
            if(cachedPage!=choice.Page)
            {
                var pixels=texture.Rgba.ToArray();for(var i=0;i<pixels.Length;i+=4)(pixels[i],pixels[i+2])=(pixels[i+2],pixels[i]);
                bitmap=BitmapSource.Create(texture.Width,texture.Height,96,96,texture.Pma?PixelFormats.Pbgra32:PixelFormats.Bgra32,null,pixels,texture.Width*4);bitmap.Freeze();cachedPage=choice.Page;
            }
            var mask=region is not null&&partOnly.IsChecked==true?PartMask(region):null;
            image.Source=region is null?bitmap:Restore(bitmap!,region,mask);
            if(resetView)image.Fit();
            partOnly.IsEnabled=region is not null;
            details.Text=region is null?$"{choice.Page}\nPNG {texture.Width} × {texture.Height}":$"{region.Name}\n인덱스 {region.Index}\n페이지 {region.Page}\n위치 {region.X}, {region.Y}\n영역 {region.Width} × {region.Height}\n원본 {region.OriginalWidth} × {region.OriginalHeight}\n오프셋 {region.OffsetX}, {region.OffsetY}\n회전 {region.Rotation}°";
            if(region is not null)details.Text+=mask is not null?"\n표시: 선택 파츠 UV 윤곽":partOnly.IsChecked==true?"\n표시: 사각 영역 (연결된 파츠 윤곽 없음)":"\n표시: Atlas 사각 영역";
            details.Text+="\n좌상단 (0,0) · X→ Y↓\n격자 1 px · 배경 체크 8 px";
        }
        catch(Exception error)when(EditorErrors.IsExpected(error)){image.Source=null;details.Text=error.Message;}
    }
    Geometry? PartMask(AtlasRegion region)
    {
        if(scene is null||region.Index>=0)return null;
        var meshes=scene.Meshes.Values.Where(mesh=>scene.TryMesh(skin,mesh.Slot,mesh.Name,out var active)&&ReferenceEquals(active,mesh)&&Scene.Text(mesh.Data["path"],mesh.Name)==region.Name&&Scene.Text(mesh.Data["type"],"region") is "region" or "mesh").ToArray();
        if(meshes.Length==0||meshes.Any(mesh=>Scene.Text(mesh.Data["type"],"region")=="region"))return null;
        var geometry=new StreamGeometry{FillRule=FillRule.Nonzero};using(var drawing=geometry.Open())
        foreach(var mesh in meshes)for(var i=0;i<mesh.Triangles.Length;i+=3)
        {
            var a=mesh.UV[mesh.Triangles[i]];var b=mesh.UV[mesh.Triangles[i+1]];var c=mesh.UV[mesh.Triangles[i+2]];
            if((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X)<0)(b,c)=(c,b);
            drawing.BeginFigure(new(a.X*region.OriginalWidth,a.Y*region.OriginalHeight),true,true);drawing.LineTo(new(b.X*region.OriginalWidth,b.Y*region.OriginalHeight),true,false);drawing.LineTo(new(c.X*region.OriginalWidth,c.Y*region.OriginalHeight),true,false);
        }
        geometry.Freeze();return geometry;
    }
    internal static ImageSource Restore(BitmapSource page,AtlasRegion region,Geometry? mask=null)
    {
        if(region.OriginalWidth<=0||region.OriginalHeight<=0||(long)region.OriginalWidth*region.OriginalHeight>Texture.MaxPixels)throw new InvalidDataException("Preview dimensions");
        var group=new DrawingGroup();using(var drawing=group.Open())
        {
            drawing.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,region.OriginalWidth,region.OriginalHeight));
            if(mask is not null)drawing.PushClip(mask);
            if(region.Width>0&&region.Height>0)
            {
                var quarter=region.Rotation is 90 or 270;var crop=new CroppedBitmap(page,new Int32Rect(region.X,region.Y,quarter?region.Height:region.Width,quarter?region.Width:region.Height));
                BitmapSource restored=region.Rotation==0?crop:new TransformedBitmap(crop,new RotateTransform(region.Rotation));drawing.DrawImage(restored,new Rect(region.OffsetX,region.OriginalHeight-region.OffsetY-region.Height,region.Width,region.Height));
            }
            if(mask is not null)drawing.Pop();
        }
        var result=new DrawingImage(group);result.Freeze();return result;
    }

    sealed class PixelPreview:Image
    {
        public bool PixelGrid=true;
        internal CanvasCamera Camera {get;}=new();
        internal event Action? ViewChanged;
        bool fit=true,ready;
        System.Windows.Point? pan;
        public PixelPreview()
        {
            ClipToBounds=true;Focusable=true;ToolTip="휠: 확대/축소 · 우클릭/가운데 버튼 또는 Space+왼쪽 드래그: 이동";
            MouseWheel+=(_,e)=>{ZoomAt(e.GetPosition(this),e.Delta/120d);e.Handled=true;};
            MouseDown+=(_,e)=>{if(e.ChangedButton is MouseButton.Right or MouseButton.Middle||e.ChangedButton==MouseButton.Left&&Keyboard.IsKeyDown(Key.Space)){Focus();pan=e.GetPosition(this);CaptureMouse();e.Handled=true;}};
            MouseMove+=(_,e)=>{if(pan is not{} previous||!IsMouseCaptured||!EnsureView())return;var current=e.GetPosition(this);Camera.Pan(new(current.X-previous.X,current.Y-previous.Y));pan=current;fit=false;InvalidateVisual();};
            MouseUp+=(_,e)=>{if(pan is null)return;pan=null;ReleaseMouseCapture();e.Handled=true;};LostMouseCapture+=(_,_)=>pan=null;
        }
        protected override Size MeasureOverride(Size available)=>new(double.IsFinite(available.Width)?available.Width:Source?.Width??0,double.IsFinite(available.Height)?available.Height:Source?.Height??0);
        protected override Size ArrangeOverride(Size finalSize)=>finalSize;
        bool EnsureView()
        {
            if(Source is null||Source.Width<=0||Source.Height<=0||ActualWidth<=0||ActualHeight<=0)return false;
            var width=Source is BitmapSource bitmap?bitmap.PixelWidth:Source.Width;var height=Source is BitmapSource pixels?pixels.PixelHeight:Source.Height;var viewport=new Point(ActualWidth,ActualHeight);
            if(!ready||Camera.Viewport!=viewport)
            {
                // CanvasCamera reserves a 30-DIP border; the texture panel already has its own margin.
                if(fit){Camera.Fit([new(0,0),new(width,-height)],viewport+new Point(60,60));Camera.Resize(viewport);}else Camera.Resize(viewport);
                ready=true;ViewChanged?.Invoke();
            }
            return true;
        }
        internal void Fit(){fit=true;ready=false;pan=null;if(IsMouseCaptured)ReleaseMouseCapture();EnsureView();InvalidateVisual();}
        internal void ActualSize(){if(!EnsureView())return;Camera.ActualSize();fit=false;ViewChanged?.Invoke();InvalidateVisual();}
        internal void ZoomAt(System.Windows.Point anchor,double wheel){if(!EnsureView())return;Camera.ZoomAt(new(anchor.X,anchor.Y),wheel);fit=false;ViewChanged?.Invoke();InvalidateVisual();}
        protected override void OnRender(DrawingContext drawing)
        {
            drawing.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));if(!EnsureView()||Source is not{} source)return;
            var width=source is BitmapSource bitmap?bitmap.PixelWidth:source.Width;var height=source is BitmapSource pixels?pixels.PixelHeight:source.Height;
            var scale=Camera.Scale;var rect=new Rect(Camera.Offset.X,Camera.Offset.Y,width*scale,height*scale);var bounds=Rect.Intersect(rect,new Rect(RenderSize));
            PixelDisplay.Background(drawing,bounds,Camera.Offset,scale);
            PixelDisplay.Image(drawing,source,rect,RenderOptions.GetBitmapScalingMode(this));
            if(PixelGrid)PixelDisplay.Grid(drawing,bounds,Camera.Offset,scale);
        }
    }
}
