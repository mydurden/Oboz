using System.Globalization;
using System.Text;

namespace Obozny;

/// <summary>
/// Відбір номенклатури без Виду «за схожістю назви» до обраного Виду
/// (слова з назви Виду / дужок + короткі синоніми для трьох загороджень
/// + вивчені ключі з призначень автора).
/// </summary>
internal static class NomenclatureVydNameMatch
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

    private static readonly HashSet<string> StopWords = new(StringComparer.Create(Uk, ignoreCase: true))
    {
        "і", "й", "та", "для", "на", "з", "у", "в", "по", "до", "від", "або", "чи", "а", "о",
        "як", "що", "про", "під", "над", "між", "без", "при", "це", "той", "ця",
    };

    /// <summary>Сміття з назв позицій при навчанні (од. вим., розміри, службові).</summary>
    private static readonly HashSet<string> LearnTrash = new(StringComparer.Create(Uk, ignoreCase: true))
    {
        "мм", "см", "м", "м2", "м3", "м²", "м³", "кг", "г", "т", "шт", "л", "мл",
        "пм", "пог", "х", "x", "×", "н", "№", "тип", "арт", "арт№",
    };

    /// <summary>Додаткові голки лише для трьох видів «Загородження інженерні (…)».</summary>
    private static readonly Dictionary<string, string[]> SynonymsByVyd = new(StringComparer.Ordinal)
    {
        ["Загородження інженерні (Єгоза)"] = new[] { "егоза", "єгоза", "егоз", "єгоз" },
        ["Загородження інженерні (Колючий дріт)"] = new[] { "колюч", "колючий", "дріт" },
        ["Загородження інженерні (Плутанка)"] = new[] { "плутанк", "плутанка" },
    };

    /// <summary>Чи назва майна «схожа» на обраний Вид (каталог + синоніми + вивчені ключі).</summary>
    public static bool MatchesName(
        string? itemName,
        string? vyd,
        IEnumerable<VydKeywordEntry>? learned = null)
    {
        var hay = Normalize(itemName);
        if (hay.Length == 0) return false;
        foreach (var needle in BuildNeedles(vyd, learned))
        {
            if (needle.Length == 0) continue;
            if (hay.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Зі списку без Виду лишити лише ті, чия назва схожа на <paramref name="vyd"/>.</summary>
    public static IReadOnlyList<NomenclatureItem> FilterByName(
        IEnumerable<NomenclatureItem> withoutVyd,
        string? vyd,
        IEnumerable<VydKeywordEntry>? learned = null)
    {
        return withoutVyd
            .Where(n => MatchesName(n.Name, vyd, learned))
            .OrderBy(n => n.Name, StringComparer.Create(Uk, ignoreCase: true))
            .ThenBy(n => n.Unit, StringComparer.Create(Uk, ignoreCase: true))
            .ToList();
    }

    /// <summary>Голки пошуку (для тестів / діагностики).</summary>
    internal static IReadOnlyList<string> NeedlesFor(
        string? vyd,
        IEnumerable<VydKeywordEntry>? learned = null) =>
        BuildNeedles(vyd, learned);

    /// <summary>
    /// Ключові частини з назви позиції для навчання (напр. «ДОШКА ОБРІЗНА…» → «дошк»).
    /// Відкидає stop-words, од. вим., «х», числа, коротке сміття.
    /// </summary>
    internal static IReadOnlyList<string> ExtractKeys(string? itemName)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in Tokenize(itemName ?? ""))
        {
            var n = Normalize(word);
            if (!IsLearnableToken(n)) continue;
            var key = ToLearnKey(n);
            if (key.Length < 3) continue;
            if (StopWords.Contains(key) || LearnTrash.Contains(key)) continue;
            if (!seen.Add(key)) continue;
            result.Add(key);
        }
        return result;
    }

    /// <summary>
    /// Запам’ятати ключі з назви → Вид.
    /// Той самий ключ уже прив’язаний до іншого Виду — лишаємо старе (не перезаписуємо).
    /// </summary>
    /// <returns>true, якщо словник змінено.</returns>
    public static bool LearnFromName(string? itemName, string? vyd, IList<VydKeywordEntry> store)
    {
        if (store == null) return false;
        var v = (vyd ?? "").Trim();
        if (v.Length == 0) return false;

        var changed = false;
        foreach (var key in ExtractKeys(itemName))
        {
            var existing = store.FirstOrDefault(e =>
                string.Equals(e.Keyword, key, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                // Конфлікт або дубль — старе лишається.
                continue;
            }

            store.Add(new VydKeywordEntry { Keyword = key, Vyd = v });
            changed = true;
        }
        return changed;
    }

    private static List<string> BuildNeedles(string? vyd, IEnumerable<VydKeywordEntry>? learned)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? raw)
        {
            var n = Normalize(raw);
            if (n.Length < 3) return;
            if (StopWords.Contains(n)) return;
            if (!seen.Add(n)) return;
            result.Add(n);
        }

        var v = (vyd ?? "").Trim();
        foreach (var word in Tokenize(v))
        {
            Add(word);
            // Стем: «колючий» → «колюч» (як у ТЗ); короткі слова не ріжемо.
            if (word.Length >= 5)
            {
                Add(word[..^1]);
                if (word.Length >= 6)
                    Add(word[..^2]);
            }
        }

        if (SynonymsByVyd.TryGetValue(v, out var syns))
        {
            foreach (var s in syns)
                Add(s);
        }

        if (learned != null && v.Length > 0)
        {
            foreach (var e in learned)
            {
                if (e == null) continue;
                if (!string.Equals(e.Vyd, v, StringComparison.Ordinal)) continue;
                Add(e.Keyword);
            }
        }

        return result;
    }

    private static bool IsLearnableToken(string n)
    {
        if (n.Length < 3) return false;
        if (StopWords.Contains(n) || LearnTrash.Contains(n)) return false;
        // Числа / розміри: «50», «150», «6000», «2», «2.5» уже розбиті токенайзером.
        if (n.All(char.IsDigit)) return false;
        // Змішані коди на кшталт «45а» — лишаємо лише якщо є ≥3 літери.
        var letters = n.Count(char.IsLetter);
        return letters >= 3;
    }

    /// <summary>«дошка» → «дошк»; коротші (≥3) — як є.</summary>
    private static string ToLearnKey(string normalizedWord)
    {
        if (normalizedWord.Length >= 5)
            return normalizedWord[..^1];
        return normalizedWord;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                continue;
            }
            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private static string Normalize(string? s) =>
        (s ?? "").Trim().ToLower(Uk);
}
