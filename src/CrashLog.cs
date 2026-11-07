using System.Text;

namespace Obozny;

/// <summary>Діагностичний лог поруч з exe (vyd-crash.log) — для крашів UI Виду.</summary>
internal static class CrashLog
{
    private static readonly object Gate = new();

    public static string LogPath =>
        Path.Combine(Paths.Root, "vyd-crash.log");

    public static void Write(string where, Exception? ex = null, string? detail = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append(" [").Append(where).Append(']');
            if (!string.IsNullOrWhiteSpace(detail))
                sb.Append(' ').Append(detail);
            if (ex != null)
            {
                sb.AppendLine();
                sb.AppendLine(ex.GetType().FullName).AppendLine(ex.Message);
                sb.AppendLine(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    sb.AppendLine("--- inner ---");
                    sb.AppendLine(ex.InnerException.GetType().FullName);
                    sb.AppendLine(ex.InnerException.Message);
                    sb.AppendLine(ex.InnerException.StackTrace);
                }
            }
            sb.AppendLine();
            lock (Gate)
                File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            /* лог не повинен валити застосунок */
        }
    }
}
