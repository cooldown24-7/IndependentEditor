using System.Text.Json;
namespace IndependentDev;
internal static partial class Program
{
    static double BinaryCoordinate(string text)
    {
        if(!double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value))throw new InvalidDataException("Binary coordinate syntax");
        if(!double.IsFinite(value))throw new InvalidDataException("Binary coordinate numeric range");
        if(value==0&&text.TakeWhile(c=>c is not('e' or 'E')).Any(c=>c is >= '1' and <= '9'))throw new InvalidDataException("Binary coordinate numeric underflow");return value;
    }
    static float[] BinaryValues(string text)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(text)>Input.MaxBytes)throw new InvalidDataException("Binary Deform JSON size");using var json=JsonDocument.Parse(text);if(json.RootElement.ValueKind!=JsonValueKind.Array||json.RootElement.GetArrayLength()>1000000)throw new InvalidDataException("Binary Deform JSON values");
        return json.RootElement.EnumerateArray().Select(value=>value.ValueKind==JsonValueKind.Number?BinaryEditor.Float32(BinaryCoordinate(value.GetRawText())):throw new InvalidDataException("Binary Deform numeric value")).ToArray();
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if(args.Length==0)
            {
                var root=Experiment.Root();
                return new System.Windows.Application().Run(new EditorWindow(Path.Combine(root,"fixtures","preview","scene.json"),Path.Combine(root,"fixtures","preview","scene.atlas"),skia:true));
            }
            const string usage="--inspect-skel-animations41 input.skel NEW-output-directory (complete supported-track decode; per-animation evaluation limits reported)\n--render-skel-animations41 input.skel atlas NEW-output-directory animation time (own evaluator, rejects unsupported curves)\n--render-skel-attachments41 input.skel atlas NEW-output-directory time (PARTIAL first animation attachment keys only; setup bones, other tracks unparsed)\n--render-skel-animation-skia3855 input.skel atlas output-directory skin [animation [time]] (explicit animated binary native render)\n--editor-skel-animation3855 input.skel atlas (limited rotate/translate linear-step-cubic + scale linear-step-cubic + linear shear + attachment/color keys + single-key IK/Transform rotation mix + Deform window)\n--edit-skel-animation-base-world3855 input.skel NEW-output.skel slot placeholder vertex worldX worldY [animation [time]] (rotate/translate linear-step-cubic + scale linear-step-cubic + linear shear + attachment/color keys + single-key IK/Transform rotation mix + Deform)\n--edit-skel-animation-deform-world3855 input.skel NEW-output.skel animation slot placeholder frame vertex worldX worldY\n--insert-skel-animation-deform-world3855 input.skel NEW-output.skel animation slot placeholder time vertex worldX worldY\n--preview input.json output-directory skin [animation [time]]\n--render input.json atlas output-directory skin [animation [time]]\n--render-skia input.json atlas output-directory skin [animation [time]] (experimental native renderer)\n--inspect-skel input.skel output-directory (header and unchanged bytes only)\n--inspect-skel-metadata38 input.skel output-directory (experimental public metadata prefix)\n--inspect-skel-root38 input.skel output-directory (experimental root prefix; stops before transform mode)\n--editor-skel-setup41 input.skel atlas (setup editing, animation playback and Deform key editing; unrelated bytes preserved)\n--editor-skel-setup4117 input.skel atlas (limited exact4.1.11/4.1.17 animation0 setup window)\n--preview-skel-setup4117 input.skel output-directory skin (complete animation0 FK only)\n--render-skel-setup-skia4117 input.skel atlas output-directory skin (complete animation0 FK only)\n--edit-skel-world4117 input.skel NEW-output.skel slot placeholder vertex worldX worldY (complete animation0 FK only)\n--set-skel-slot-attachment4117 input.skel NEW-output.skel slot placeholder (existing default attachment only)\n--clear-skel-slot-attachment4117 input.skel NEW-output.skel slot\n--edit-skel-attachment-color4117 input.skel NEW-output.skel slot placeholder RRGGBBAA (existing default region/mesh only)\n--edit-skel-slot-color4117 input.skel NEW-output.skel slot RRGGBBAA (complete animation0; slot light color only)\n--edit-skel-ik4117 input.skel NEW-output.skel constraint mix bendPositive (complete animation0; true/false bend; solver unchanged)\n--edit-skel-local4117 input.skel NEW-output.skel slot placeholder vertex localX localY (complete nonanimated profile; unweighted only)\n--inspect-skel-animationslots4117 input.skel output-directory (first animation attachment slots prefix only; read-only/partial)\n--inspect-skel-setup4117 input.skel output-directory (exact 4.1.17 limited setup; complete only animation0 plus EOF)\n--inspect-skel-default4117 input.skel output-directory (exact 4.1.17 region/mesh default skin; read-only)\n--inspect-skel-path4117 input.skel output-directory (exact 4.1.17 raw Path setup; read-only)\n--inspect-skel-transform4117 input.skel output-directory (exact 4.1.17 raw Transform setup; read-only)\n--inspect-skel-ik4117 input.skel output-directory (exact 4.1.17 limited IK setup; read-only)\n--inspect-skel-slots4117 input.skel output-directory (exact 4.1.17 limited slots; read-only)\n--inspect-skel-bones4117 input.skel output-directory (exact 4.1.17 limited bone section; read-only)\n--inspect-skel-bones3855 input.skel output-directory (limited exact3.8.55 bone section)\n--inspect-skel-slots3855 input.skel output-directory (limited exact3.8.55 slots and references)\n--inspect-skel-ik3855 input.skel output-directory (limited exact3.8.55 IK setup section)\n--inspect-skel-transform3855 input.skel output-directory (limited exact3.8.55 Transform setup)\n--inspect-skel-default3855 input.skel output-directory (limited default region/unweighted polygon/mesh with weights)\n--inspect-skel-setup3855 input.skel output-directory (limited setup, complete only when animation0 and exact EOF)\n--edit-skel-local3855 input.skel NEW-output.skel slot placeholder vertex localX localY (complete nonanimated/unweighted only)\n--inspect-skel-deform3855 input.skel output-directory (full limited Deform-only body)\n--inspect-skel-animation3855 input.skel output-directory (read-only rotate/translate linear-step-cubic + scale linear-step-cubic + linear shear + attachment/color keys + single-key IK/Transform rotation mix + Deform)\n--preview-skel-animation3855 input.skel output-directory skin [animation [time]] (read-only bone animation profile)\n--edit-skel-deform-frame3855 input.skel NEW-output.skel animation slot placeholder frame offset JSON-values (existing frame only)\n--preview-skel3855 input.skel output-directory skin [animation [time]] (limited own binary model)\n--render-skel-skia3855 input.skel atlas output-directory skin [animation [time]] (limited native preview)\n--edit-skel-world3855 input.skel NEW-output.skel slot placeholder vertex worldX worldY (complete nonanimated only)\n--edit-skel-deform-world3855 input.skel NEW-output.skel animation slot placeholder frame vertex worldX worldY (existing key only)\n--edit-skel-base-world3855 input.skel NEW-output.skel slot placeholder vertex worldX worldY [animation [time]] (complete Deform-only profile)\n--insert-skel-deform-world3855 input.skel NEW-output.skel animation slot placeholder time vertex worldX worldY (existing animation/default mesh; exact support times)\n--editor input.json atlas (isolated experimental window)\n--editor-skia input.json atlas (separate native experimental window)\n--editor-skel3855 input.skel atlas (limited Deform-only binary experimental window)";
            if(args.Length==1&&args[0]=="--help"){Console.WriteLine(usage);return 0;}
            if(args.Length==3&&args[0] is "--editor" or "--editor-skia")return new System.Windows.Application().Run(new EditorWindow(args[1],args[2],args[0]=="--editor-skia"));
            if(args.Length==3&&args[0]=="--inspect-skel-animations41")return ExportAnimations41(args[1],null,args[2],null,0);
            if(args.Length==6&&args[0]=="--render-skel-animations41")return ExportAnimations41(args[1],args[2],args[3],args[4],BinaryCoordinate(args[5]));
            if(args.Length==5&&args[0]=="--render-skel-attachments41")return RenderAttachmentPrefix41(args[1],args[2],args[3],BinaryCoordinate(args[4]));
            if(args.Length==3&&args[0]=="--editor-skel-setup4117")return new System.Windows.Application().Run(new EditorWindow(args[1],args[2],skia:true,binary:true,setup4117:true));
            if(args.Length==3&&args[0]=="--editor-skel-setup41")return new System.Windows.Application().Run(new EditorWindow(args[1],args[2],skia:true,binary:true,setup4117:true,preserveAnimations:true));
            if(args.Length==3&&args[0] is "--editor-skel3855" or "--editor-skel-animation3855")return new System.Windows.Application().Run(new EditorWindow(args[1],args[2],skia:true,binary:true,rotation:args[0]=="--editor-skel-animation3855"));
            if(args.Length==3&&args[0]=="--inspect-skel-metadata38")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Metadata38);Console.WriteLine("Experimental public metadata prefix inspected; remaining body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-root38")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Root38);Console.WriteLine("Experimental root prefix inspected; transform mode, remaining body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-animationslots4117")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.AnimationSlots4117);Console.WriteLine("Limited first4.1.17 animation attachment slots inspected; bone/remaining body, preview and editing pending");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-setup4117")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Setup4117);Console.WriteLine("Limited 4.1.17 setup inspected; completeness is explicit; editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-default4117")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.DefaultSkin4117);Console.WriteLine("Limited 4.1.17 default region/mesh inspected; extra skins, full body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-path4117")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Path4117);Console.WriteLine("Limited 4.1.17 raw Path setup inspected; default skin, solver and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-transform4117")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Transform4117);Console.WriteLine("Limited 4.1.17 raw Transform setup inspected; Path body, JSON defaults and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-ik4117")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Ik4117);Console.WriteLine("Limited 4.1.17 IK setup inspected; Transform body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-slots4117")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Slots4117);Console.WriteLine("Limited 4.1.17 slot section inspected; IK body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-bones4117")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Bones4117);Console.WriteLine("Limited 4.1.17 bone section inspected; remaining body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-bones3855")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Bones3855);Console.WriteLine("Limited 3.8.55 bone section inspected; remaining body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-slots3855")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Slots3855);Console.WriteLine("Limited 3.8.55 slot section inspected; remaining body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-ik3855")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Ik3855);Console.WriteLine("Limited 3.8.55 IK setup inspected; remaining body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-transform3855")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Transform3855);Console.WriteLine("Limited 3.8.55 Transform setup inspected; remaining body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-default3855")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.DefaultSkin3855);Console.WriteLine("Limited 3.8.55 default skin inspected; remaining body and editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-setup3855")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Setup3855);Console.WriteLine("Limited 3.8.55 setup inspected; completeness is explicit in provenance; editing unsupported");return 0;}
            if(args.Length==4&&args[0]=="--preview-skel-setup4117")
            {Experiment.Export(BinaryScene.ReadStatic4117(BinaryDocument.Load(args[1])),args[2],args[3],null,0);Console.WriteLine("Limited exact4.1.17 static FK preview; external compatibility unverified");return 0;}
            if(args.Length==5&&args[0]=="--render-skel-setup-skia4117")
            {TexturedRaster.Export(BinaryScene.ReadStatic4117(BinaryDocument.Load(args[1])),TextureAtlas.Load(args[2]),args[3],args[4],null,0,true);Console.WriteLine("Limited exact4.1.17 static FK native render; external compatibility unverified");return 0;}
            if(args.Length==8&&args[0]=="--edit-skel-world4117")
            {var editor=BinaryEditor.ForSetup4117(BinaryDocument.Load(args[1]).UnchangedBytes());var world=editor.SetWorldVertex(int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture),args[4],int.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture),new(BinaryCoordinate(args[6]),BinaryCoordinate(args[7])));editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="exact4.1.17 static FK float32 base edit only",World=world,editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==5&&args[0]=="--set-skel-slot-attachment4117"||args.Length==4&&args[0]=="--clear-skel-slot-attachment4117")
            {var editor=BinaryEditor.ForSetup4117(BinaryDocument.Load(args[1]).UnchangedBytes());var placeholder=args.Length==5?args[4]:null;editor.SetSlotAttachment4117(args[3],placeholder);editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="exact4.1.17 complete animation0 existing default attachment reference only",Attachment=placeholder,editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==6&&args[0]=="--edit-skel-attachment-color4117")
            {if(args[5].Length!=8||!uint.TryParse(args[5],System.Globalization.NumberStyles.AllowHexSpecifier,System.Globalization.CultureInfo.InvariantCulture,out var color))throw new InvalidDataException("Binary attachment color requires RRGGBBAA");var editor=BinaryEditor.ForSetup4117(BinaryDocument.Load(args[1]).UnchangedBytes());editor.SetAttachmentColor4117(args[3],args[4],color);editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="exact4.1.17 complete animation0 default region/mesh RGBA byte edit only",Color=color.ToString("x8",System.Globalization.CultureInfo.InvariantCulture),editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==5&&args[0]=="--edit-skel-slot-color4117")
            {if(args[4].Length!=8||!uint.TryParse(args[4],System.Globalization.NumberStyles.AllowHexSpecifier,System.Globalization.CultureInfo.InvariantCulture,out var color))throw new InvalidDataException("Binary slot color requires RRGGBBAA");var editor=BinaryEditor.ForSetup4117(BinaryDocument.Load(args[1]).UnchangedBytes());editor.SetSlotColor4117(args[3],color);editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="exact4.1.17 complete animation0 slot RGBA byte edit only",Color=color.ToString("x8",System.Globalization.CultureInfo.InvariantCulture),editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==6&&args[0]=="--edit-skel-ik4117")
            {var editor=BinaryEditor.ForSetup4117(BinaryDocument.Load(args[1]).UnchangedBytes());var bend=bool.Parse(args[5]);var mix=editor.SetIkSetup4117(args[3],BinaryCoordinate(args[4]),bend);editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="exact4.1.17 complete animation0 IK setup mix/bend byte edit only",Mix=(double)mix,BendPositive=bend,editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==8&&args[0]=="--edit-skel-local4117")
            {var editor=BinaryEditor.ForSetup4117(BinaryDocument.Load(args[1]).UnchangedBytes());var local=editor.SetLocalVertex(int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture),args[4],int.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture),new(BinaryCoordinate(args[6]),BinaryCoordinate(args[7])));editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="exact4.1.17 local float32 base edit only",Local=local,editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==8&&args[0]=="--edit-skel-local3855")
            {var editor=new BinaryEditor(BinaryDocument.Load(args[1]).UnchangedBytes());var local=editor.SetLocalVertex(int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture),args[4],int.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture),new(BinaryCoordinate(args[6]),BinaryCoordinate(args[7])));editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="local float32 base edit only",Local=local,editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-deform3855")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Deform3855);Console.WriteLine("Limited Deform-only 3.8.55 body fully inspected; editing unsupported");return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel-animation3855")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2],BinaryInspection.Animation3855);Console.WriteLine("Limited rotation linear/step plus Deform profile inspected; read-only");return 0;}
            if(args.Length==9&&args[0]=="--edit-skel-deform-frame3855")
            {var editor=BinaryEditor.ForDeform(BinaryDocument.Load(args[1]).UnchangedBytes());var values=BinaryValues(args[8]);editor.ReplaceDeformValues(args[3],int.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture),args[5],int.Parse(args[6],System.Globalization.CultureInfo.InvariantCulture),uint.Parse(args[7],System.Globalization.CultureInfo.InvariantCulture),values);editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="stored Deform frame values only",ValueCount=values.Length,editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==10&&args[0] is "--insert-skel-deform-world3855" or "--insert-skel-animation-deform-world3855")
            {var bytes=BinaryDocument.Load(args[1]).UnchangedBytes();var editor=args[0]=="--insert-skel-animation-deform-world3855"?BinaryEditor.ForAnimation(bytes):BinaryEditor.ForDeform(bytes);var result=editor.InsertDeformedWorldVertex(args[3],int.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture),args[5],BinaryCoordinate(args[6]),int.Parse(args[7],System.Globalization.CultureInfo.InvariantCulture),new(BinaryCoordinate(args[8]),BinaryCoordinate(args[9])));editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="new key/timeline in existing animation/default mesh; exact float32 support times",Time=(double)result.Time,result.World,editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==10&&args[0] is "--edit-skel-deform-world3855" or "--edit-skel-animation-deform-world3855")
            {var bytes=BinaryDocument.Load(args[1]).UnchangedBytes();var editor=args[0]=="--edit-skel-animation-deform-world3855"?BinaryEditor.ForAnimation(bytes):BinaryEditor.ForDeform(bytes);var world=editor.SetDeformedWorldVertex(args[3],int.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture),args[5],int.Parse(args[6],System.Globalization.CultureInfo.InvariantCulture),int.Parse(args[7],System.Globalization.CultureInfo.InvariantCulture),new(BinaryCoordinate(args[8]),BinaryCoordinate(args[9])));editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="existing Deform key world vertex edit only",World=world,editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==8&&args[0]=="--edit-skel-world3855"||args.Length is >=8 and <=10&&args[0] is "--edit-skel-base-world3855" or "--edit-skel-animation-base-world3855")
            {var bytes=BinaryDocument.Load(args[1]).UnchangedBytes();var editor=args[0]=="--edit-skel-animation-base-world3855"?BinaryEditor.ForAnimation(bytes):args[0]=="--edit-skel-base-world3855"?BinaryEditor.ForDeform(bytes):new BinaryEditor(bytes);var world=editor.SetWorldVertex(int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture),args[4],int.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture),new(BinaryCoordinate(args[6]),BinaryCoordinate(args[7])),args.Length>=9?args[8]:null,args.Length==10?BinaryCoordinate(args[9]):0);editor.SaveNew(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{Experimental="selected pose base vertex edit; stored Deform unchanged",World=world,editor.Revision,editor.Dirty,editor.Document.Digest}));return 0;}
            if(args.Length==3&&args[0]=="--inspect-skel")
            {BinaryDocument.Load(args[1]).ExportUnchanged(args[2]);Console.WriteLine("Header inspected and source bytes preserved; SKEL body and editing unsupported");return 0;}
            if(args.Length is >=5 and <=7 && args[0] is "--render" or "--render-skia")
            {
                var s=Scene.Parse(Input.ReadText(args[1]));var loadedAtlas=TextureAtlas.Load(args[2]);var animation=args.Length>=6?args[5]:null;var time=args.Length==7?double.Parse(args[6],System.Globalization.CultureInfo.InvariantCulture):0;
                TexturedRaster.Export(s,loadedAtlas,args[3],args[4],animation,time,args[0]=="--render-skia");Console.WriteLine(args[0]=="--render-skia"?"Isolated Skia texture render complete":"Isolated texture render complete");return 0;
            }
            if(args.Length is >=4 and <=6 && args[0]=="--preview")
            {
                var s=Scene.Parse(Input.ReadText(args[1])); var animation=args.Length>=5?args[4]:null;var time=args.Length==6?double.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture):0;
                Experiment.Export(s,args[2],args[3],animation,time);Console.WriteLine("Isolated diagnostic export complete; external compatibility unverified");return 0;
            }
            if(args.Length is >=4 and <=6&&args[0] is "--preview-skel3855" or "--preview-skel-animation3855")
            {var document=BinaryDocument.Load(args[1]);var model=args[0]=="--preview-skel-animation3855"?BinaryScene.ReadAnimation(document):BinaryScene.Read(document);Experiment.Export(model,args[2],args[3],args.Length>=5?args[4]:null,args.Length==6?BinaryCoordinate(args[5]):0);Console.WriteLine("Limited binary model preview; external compatibility unverified");return 0;}
            if(args.Length is >=5 and <=7&&args[0] is "--render-skel-skia3855" or "--render-skel-animation-skia3855")
            {var document=BinaryDocument.Load(args[1]);var model=args[0]=="--render-skel-animation-skia3855"?BinaryScene.ReadAnimation(document):BinaryScene.Read(document);TexturedRaster.Export(model,TextureAtlas.Load(args[2]),args[3],args[4],args.Length>=6?args[5]:null,args.Length==7?BinaryCoordinate(args[6]):0,true);Console.WriteLine("Limited binary model native render; external compatibility unverified");return 0;}
            throw new ArgumentException(usage);
        }
        catch(Exception e) { if(args.Length==0)System.Windows.MessageBox.Show(e.Message,"편집기 실행 오류",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Error);else Console.Error.WriteLine(e); return 1; }
    }
    static int RenderAttachmentPrefix41(string input,string atlasFile,string output,double time)
    {
        if(time<0)throw new InvalidDataException("Attachment preview time must be nonnegative");
        output=Path.GetFullPath(output);Experiment.CheckOutput(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("Attachment preview output exists");
        var document=BinaryDocument.Load(input);var prefix=document.ReadAnimationSlots4117();var scene=BinaryScene.ReadAttachmentPrefix41(document,prefix);var atlas=TextureAtlas.Load(atlasFile);
        var slots=document.ReadSlots4117().Slots;var unparsed=prefix.Span.Offset+prefix.Span.Length;var report=new{Partial=true,Scope="First animation attachment keys only; setup bones/constraints. Bone, constraint, deform, draw order, event tracks and later animations are unparsed. Not full animation playback.",document.Header.Version,document.Digest,Time=time,DecodedTimelineCount=prefix.SlotAttachments.Length,SlotBindings=prefix.SlotAttachments.Select(track=>new{track.SlotIndex,slots[track.SlotIndex].Name}),Prefix=prefix,UnparsedOffset=unparsed,UnparsedBytes=document.Length-unparsed};
        TexturedRaster.Export(scene,atlas,output,"default",prefix.Name,time,true);
        BundleSave.InstallNew(Path.Combine(output,"attachment-prefix.json"),stream=>JsonSerializer.Serialize(stream,report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));return 0;
    }
    static int ExportAnimations41(string input,string? atlasFile,string output,string? animation,double time)
    {
        if(time<0)throw new InvalidDataException("Animation time must be nonnegative");
        output=Path.GetFullPath(output);Experiment.CheckOutput(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("Animation output exists");
        var document=BinaryDocument.Load(input);var body=document.ReadAnimations41();
        var report=new{document.Header.Version,document.Digest,FullConsumption=true,Editable=false,BoneCurvePolicy=document.Header.Version=="4.1.11"?"First intersection in original curve traversal order; no handle clamping; original Spine/game equivalence unverified":"Strict time handles within the key interval",Scope="Decoded supported binary tracks; own evaluator, external pose equivalence unverified. IK (softness0), Path and independent Transform components supported within the current setup profile; events, split axes, nonzero IK softness and negative draw offsets remain unsupported.",body.Items,body.Span,body.FrameCount,body.ValueCount,UnparsedBytes=0};
        if(atlasFile is not null)
        {
            var item=body.Items.SingleOrDefault(item=>item.Name==animation)??throw new InvalidDataException("Unknown animation");if(item.EvaluationIssues.Length>0)throw new NotSupportedException("Animation evaluation unavailable: "+string.Join("; ",item.EvaluationIssues));
            var scene=BinaryScene.ReadAnimations41(document,body);TexturedRaster.Export(scene,TextureAtlas.Load(atlasFile),output,"default",animation,time,true);
        }
        else Directory.CreateDirectory(output);
        if(atlasFile is null)BundleSave.InstallNew(Path.Combine(output,"animations.json"),stream=>JsonSerializer.Serialize(stream,body.Animations));
        BundleSave.InstallNew(Path.Combine(output,"animation-provenance.json"),stream=>JsonSerializer.Serialize(stream,report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));return 0;
    }
}

