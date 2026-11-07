namespace Obozny;

internal static class Paths
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var src = Path.Combine(dir.FullName, "src");
                var proj = Path.Combine(src, "Obozny.csproj");
                if (File.Exists(proj))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return AppContext.BaseDirectory;
        }
    }

    public static string DataFile => Path.Combine(Root, "obozny.json");
    public static string BackupDir => Path.Combine(Root, "Бекапи");
    public static string WorkDaysFile => Path.Combine(BackupDir, "workdays.json");
}
