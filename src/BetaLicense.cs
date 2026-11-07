using System.Windows;
using System.Windows.Threading;

namespace Obozny;

/// <summary>Умови бети, строк 3 місяці, HMAC у JSON, обмеження імпорту/експорту документів.</summary>
internal static class BetaLicense
{
    public const int AccessMonths = 3;
    public const int WarningDays = 14;

    private static bool _resolved;
    private static bool _tamperedOrLocked;
    private static DateTime? _startDate;

    public static DateTime? AcceptedAt
    {
        get
        {
            EnsureResolved();
            return _startDate;
        }
    }

    public static bool IsAccepted
    {
        get
        {
            EnsureResolved();
            return _startDate != null && !_tamperedOrLocked;
        }
    }

    public static DateTime? EndsAt
    {
        get
        {
            var at = AcceptedAt;
            return at?.AddMonths(AccessMonths);
        }
    }

    public static int? DaysRemaining
    {
        get
        {
            var end = EndsAt;
            if (end == null) return null;
            return (int)Math.Ceiling((end.Value - DateTime.Today).TotalDays);
        }
    }

    public static bool IsExpired
    {
        get
        {
            EnsureResolved();
            if (_tamperedOrLocked) return true;
            if (_startDate == null) return false;
            return DaysRemaining is <= 0;
        }
    }

    public static bool ShouldWarnExpiring
    {
        get
        {
            EnsureResolved();
            if (_tamperedOrLocked || _startDate == null || IsExpired) return false;
            var days = DaysRemaining;
            return days is > 0 and <= WarningDays;
        }
    }

    /// <summary>Бета-блок вимкнено для роздачі ванілі — імпорт/експорт завжди дозволені.</summary>
    public static bool ImportExportBlocked => false;

    public static bool CanEnterWithoutTermsDialog
    {
        get
        {
            EnsureResolved();
            return _startDate != null || _tamperedOrLocked;
        }
    }

    public static void Accept()
    {
        EnsureResolved();
        if (_startDate != null || _tamperedOrLocked)
            return;

        var day = DateTime.Today;
        var hmac = BetaSeal.Compute(day);
        SyncSettings(day, hmac);
        TrySaveSettings();
        _startDate = day;
        _tamperedOrLocked = false;
        _resolved = true;
    }

    /// <summary>Вікно угоди вимкнено — старт одразу в робочий режим.</summary>
    public static bool EnsureAccepted(Window? owner = null) => true;

    /// <summary>Стартові повідомлення про строк/печатку вимкнено.</summary>
    public static void ShowStartupNotices(Window? owner = null) { }

    /// <summary>Блок імпорту/експорту документів вимкнено.</summary>
    public static bool TryAllowDocumentImportExport(Window? owner) => true;

    public static IReadOnlyList<(string Heading, string Body)> Sections => BetaTermsText.Sections;

    private static void EnsureResolved()
    {
        if (_resolved) return;
        _resolved = true;
        try
        {
            Resolve();
        }
        catch
        {
            _tamperedOrLocked = true;
            _startDate = null;
        }
    }

    private static void Resolve()
    {
        _tamperedOrLocked = false;
        _startDate = null;
        var s = AppStore.Instance.Data.Settings;

        if (s.BetaTermsAcceptedAt != null && !string.IsNullOrWhiteSpace(s.BetaSeal))
        {
            var day = s.BetaTermsAcceptedAt.Value.Date;
            if (day > DateTime.Today || !BetaSeal.Verify(day, s.BetaSeal))
            {
                _tamperedOrLocked = true;
                return;
            }

            _startDate = day;
            SyncSettings(day, s.BetaSeal);
            return;
        }

        // Стара збірка: Accepted без HMAC — один раз видати підпис у JSON.
        if (s.BetaTermsAccepted && s.BetaTermsAcceptedAt != null)
        {
            var day = s.BetaTermsAcceptedAt.Value.Date;
            if (day > DateTime.Today)
            {
                _tamperedOrLocked = true;
                return;
            }

            var hmac = BetaSeal.Compute(day);
            if (SyncSettings(day, hmac))
                TrySaveSettings();
            _startDate = day;
            return;
        }

        // Уламки полів при Accepted=false → locked (не новий 3-місячний цикл).
        if (!string.IsNullOrWhiteSpace(s.BetaSeal) || s.BetaTermsAcceptedAt != null)
            _tamperedOrLocked = true;
    }

    private static bool SyncSettings(DateTime day, string hmac)
    {
        var s = AppStore.Instance.Data.Settings;
        var normalized = hmac.ToUpperInvariant();
        var dirty = !s.BetaTermsAccepted
                    || s.BetaTermsAcceptedAt?.Date != day.Date
                    || !string.Equals(s.BetaSeal, normalized, StringComparison.OrdinalIgnoreCase);
        s.BetaTermsAccepted = true;
        s.BetaTermsAcceptedAt = day.Date;
        s.BetaSeal = normalized;
        return dirty;
    }

    private static void TrySaveSettings()
    {
        try
        {
            AppStore.Instance.Save(notify: false);
        }
        catch
        {
            // диск/OneDrive — не валимо старт
        }
    }

    private static string DaysWord(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11) return "день";
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return "дні";
        return "днів";
    }
}
