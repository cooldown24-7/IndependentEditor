namespace IndependentDev;

internal static class BundleSave
{
    internal static void InstallNew(string path,byte[] bytes)
        =>InstallNew(path,stream=>stream.Write(bytes));
    internal static void InstallNew(string path,Action<Stream> write)
    {
        Experiment.CheckOutput(path);var temporary=Path.Combine(Path.GetDirectoryName(path)!,Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){write(stream);stream.Flush(true);}
            File.Move(temporary,path,false);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static TextureAtlas Save(Editor editor,string jsonPath,string atlasPath,string outputRoot,string? expectedDigest,IReadOnlyDictionary<string,string>? expectedSources=null)
    {
        Experiment.CheckOutput(jsonPath);jsonPath=Path.GetFullPath(jsonPath);
        var root=Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!jsonPath.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!string.Equals(Path.GetExtension(jsonPath),editor.IsBinary?".skel":".json",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(editor.IsBinary?"Bundle SKEL destination":"Bundle JSON destination");
        if(Directory.Exists(jsonPath))throw new IOException("Bundle destination is a directory");
        var destination=Path.GetDirectoryName(jsonPath)!;var source=Path.GetDirectoryName(Path.GetFullPath(atlasPath))!;
        var atlas=TextureAtlas.Load(atlasPath);
        if(expectedSources is not null&&(expectedSources.Count!=atlas.Sources.Count||expectedSources.Any(s=>!atlas.Sources.TryGetValue(s.Key,out var digest)||digest!=s.Value)))throw new IOException("Source companion changed since open");
        var files=new Dictionary<string,(byte[] Bytes,string Digest)>(StringComparer.OrdinalIgnoreCase);
        var copiedBytes=0;
        void Add(string target,string input)
        {
            target=Path.GetFullPath(target);Experiment.CheckOutput(target);
            if(string.Equals(target,jsonPath,StringComparison.OrdinalIgnoreCase)||string.Equals(target,jsonPath+".bak",StringComparison.OrdinalIgnoreCase))throw new IOException("Bundle filename collision");
            using var stream=File.OpenRead(input);var bytes=Input.ReadBytes(stream,Math.Min(Input.MaxBytes,Input.MaxBytes+TextureAtlas.MaxEncodedBytes-copiedBytes));copiedBytes+=bytes.Length;
            if(!atlas.Sources.TryGetValue(Path.GetFullPath(input),out var digest)||digest!=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)))throw new IOException("Source companion changed during save");
            if(!files.TryAdd(target,(bytes,digest))&&!files[target].Bytes.AsSpan().SequenceEqual(bytes))throw new IOException("Duplicate bundle destination");
        }
        Add(Path.ChangeExtension(jsonPath,".atlas"),atlasPath);
        foreach(var page in atlas.Pages.Keys)Add(Path.Combine(destination,page),Path.Combine(source,page));
        if(files.Keys.Any(Directory.Exists))throw new IOException("Bundle destination is a directory");
        if(Editor.Digest(jsonPath)!=expectedDigest)throw new IOException("External modification or destination conflict");
        foreach(var file in files)if(File.Exists(file.Key)&&Editor.Digest(file.Key)!=file.Value.Digest)throw new IOException("Bundle file conflict: "+Path.GetFileName(file.Key));
        // shortcut: companions are installed before the JSON commit; a failed save may leave new identical companion files.
        foreach(var file in files)
        {
            if(File.Exists(file.Key))continue;
            Directory.CreateDirectory(Path.GetDirectoryName(file.Key)!);Experiment.CheckOutput(file.Key);
            InstallNew(file.Key,file.Value.Bytes);
        }
        var savedAtlas=atlas with{SourcePath=Path.ChangeExtension(jsonPath,".atlas"),Sources=files.ToDictionary(f=>f.Key,f=>f.Value.Digest,StringComparer.OrdinalIgnoreCase)};
        editor.Save(jsonPath,outputRoot,expectedDigest);
        return savedAtlas;
    }
}
