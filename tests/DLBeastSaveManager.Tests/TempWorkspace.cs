namespace DLBeastSaveManager.Tests;

public sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "dlbsm-tests", Guid.NewGuid().ToString("N"));
        SavePath = Path.Combine(Root, "save");
        BackupRoot = Path.Combine(Root, "backups");
        Directory.CreateDirectory(SavePath);
        Directory.CreateDirectory(BackupRoot);
    }

    public string Root { get; }
    public string SavePath { get; }
    public string BackupRoot { get; }

    public string WriteSave(string name, string content)
    {
        var path = Path.Combine(SavePath, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void SeedTypicalSaveSet()
    {
        WriteSave("save_ft_0.sav", "main slot");
        WriteSave("save_ft_pw_0.sav", "profile");
        WriteSave("save_ft_0_chp000.sbk", "chapter backup");
    }

    public void SeedTwoRuns()
    {
        WriteSave("save_ft_0.sav", "slot zero");
        WriteSave("save_ft_pw_0.sav", "slot zero world");
        WriteSave("save_ft_1.sav", "slot one");
    }

    public IReadOnlyList<string> SaveFileNames() =>
        Directory.GetFiles(SavePath, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(SavePath, p).Replace('\\', '/'))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
