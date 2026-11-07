using System.Text.Json;

namespace Obozny;

internal static class BackupService
{
    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void EnsureLayout()
    {
        Directory.CreateDirectory(Paths.BackupDir);
    }

    public static void RememberWorkDay(AppData data)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (!data.WorkDays.Contains(today))
            data.WorkDays.Add(today);
        data.LastActiveDate = today;
        Prune(data);
        File.WriteAllText(Paths.WorkDaysFile, JsonSerializer.Serialize(data.WorkDays, JsonOpt));
    }

    public static void Movement(AppData data, Document doc, string reason)
    {
        EnsureLayout();
        RememberWorkDay(data);
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        var folder = Path.Combine(Paths.BackupDir, day);
        Directory.CreateDirectory(folder);
        var payload = new
        {
            kind = "рух",
            reason,
            at = DateTime.Now,
            document = doc
        };
        var name = $"рух_{DateTime.Now:HHmmss}_{Safe(doc.Number)}_{Safe(reason)}.json";
        File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(payload, JsonOpt));
        Prune(data);
    }

    public static void Stock(AppData data, string reason)
    {
        EnsureLayout();
        RememberWorkDay(data);
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        var folder = Path.Combine(Paths.BackupDir, day);
        Directory.CreateDirectory(folder);
        var payload = new
        {
            kind = "залишок",
            reason,
            at = DateTime.Now,
            properties = data.Properties
        };
        var name = $"залишок_{DateTime.Now:HHmmss}_{Safe(reason)}.json";
        File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(payload, JsonOpt));
        data.HadFirstStockBackup = true;
        Prune(data);
    }

    /// <summary>
    /// Повний знімок AppData перед «Очистити склад» (не лише properties).
    /// </summary>
    public static void BeforeWarehouseClear(AppData data)
    {
        EnsureLayout();
        RememberWorkDay(data);
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        var folder = Path.Combine(Paths.BackupDir, day);
        Directory.CreateDirectory(folder);
        var payload = new
        {
            kind = "перед_очищенням",
            reason = "перед_очищенням",
            at = DateTime.Now,
            data
        };
        var name = $"перед_очищенням_{DateTime.Now:HHmmss}.json";
        File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(payload, JsonOpt));
        data.HadFirstStockBackup = true;
        Prune(data);
    }

    public static bool NeedsEndOfDayStock(AppData data)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        return !string.IsNullOrWhiteSpace(data.LastActiveDate) && data.LastActiveDate != today;
    }

    private static void Prune(AppData data)
    {
        var ordered = data.WorkDays
            .Where(x => DateTime.TryParse(x, out _))
            .Select(DateTime.Parse)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        while (ordered.Count > 30)
        {
            var oldest = ordered[0];
            ordered.RemoveAt(0);
            var key = oldest.ToString("yyyy-MM-dd");
            data.WorkDays.Remove(key);
            var folder = Path.Combine(Paths.BackupDir, key);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        data.WorkDays = ordered.Select(d => d.ToString("yyyy-MM-dd")).ToList();
    }

    private static string Safe(string value)
    {
        var chars = Path.GetInvalidFileNameChars();
        var clean = new string((value ?? "").Select(c => chars.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "без_номера" : clean.Trim();
    }
}
