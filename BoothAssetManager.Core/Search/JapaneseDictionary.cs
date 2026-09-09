using System.IO.Compression;
using System.Text;
using System.Xml;

namespace BoothAssetManager.Core.Search;

/// <summary>
/// 同梱のJMdictから「英語→日本語」と「読み→日本語」を引けるようにする。
///
/// **通信はしない。**jisho.org はこのJMdictを配っているだけなので、
/// ファイルを持っていればAPIを叩く理由が無い。実測でも、
/// かな漢字変換APIが外した「しんおん→心音」を辞書は当てた。
///
/// JMdictを選んだのはEDICT2より並べ替えに使える情報が多いから。
/// 頻度の階級（<c>nf01</c>〜<c>nf48</c>）と「ふつうかなで書く語」の印（<c>uk</c>）があり、
/// これが無いと裏返した索引で <c>shark</c> が「鮫」に倒れる（手元に当たるのは「サメ」の方）。
/// </summary>
public sealed class JapaneseDictionary
{
    /// <summary>
    /// 英語1語につき返す表記の数。
    ///
    /// 読みより深く採る。**同じ英語を語義に持つ項目が何十とある**ためで、
    /// 4語で切ると `ring` から「指輪」が落ちた（「輪」「環」「土俵」が先に来る）。
    /// </summary>
    private const int EnglishForms = 6;

    /// <summary>読み1つにつき返す表記の数。読みは英語ほど散らないので浅くてよい。</summary>
    private const int ReadingForms = 4;

    /// <summary>組み上げた索引の形式。上げると古いキャッシュを作り直す。</summary>
    private const int CacheVersion = 2;

    private readonly string _dictionaryPath;
    private readonly string _cachePath;
    private readonly object _sync = new();

    private Dictionary<string, string[]>? _byEnglish;
    private Dictionary<string, string[]>? _byReading;
    private bool _failed;

    public JapaneseDictionary(string dictionaryPath, string cachePath)
    {
        _dictionaryPath = dictionaryPath;
        _cachePath = cachePath;
    }

    /// <summary>辞書ファイルが手元にあるか。無ければ橋渡しの機能ごと出さない。</summary>
    public bool IsAvailable => File.Exists(_dictionaryPath);

    /// <summary>索引を組めなかったときの理由。組めていれば null。</summary>
    public string? LoadError { get; private set; }

    /// <summary>英語1語から日本語の表記を引く。</summary>
    public IReadOnlyList<string> ByEnglish(string word)
    {
        EnsureLoaded();
        return _byEnglish is not null && _byEnglish.TryGetValue(word.ToLowerInvariant(), out var forms)
            ? forms
            : [];
    }

    /// <summary>読み（ひらがな）から日本語の表記を引く。</summary>
    public IReadOnlyList<string> ByReading(string reading)
    {
        EnsureLoaded();
        return _byReading is not null && _byReading.TryGetValue(reading, out var forms) ? forms : [];
    }

    /// <summary>
    /// 索引を組む。**最初に必要になったときだけ**動く。
    ///
    /// 素のXMLは63MBあるので、一度組んだらキャッシュに書いて次からはそれを読む。
    /// キャッシュは手元で組んだものなので配布物には入らない
    /// （＝加工した辞書を配らないので、継承条項に触れる物が無い）。
    /// </summary>
    private void EnsureLoaded()
    {
        if (_byEnglish is not null || _failed)
        {
            return;
        }

        lock (_sync)
        {
            if (_byEnglish is not null || _failed)
            {
                return;
            }

            try
            {
                if (TryLoadCache())
                {
                    return;
                }

                Build();
                SaveCache();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
            {
                // 引けなくても検索そのものは動く。橋が架からないだけ。
                // 理由は残す——黙って引けなくなると、辞書が壊れているのか
                // 語が無いだけなのかを区別できない
                _failed = true;
                _byEnglish = null;
                _byReading = null;
                LoadError = exception.Message;
            }
        }
    }

    private void Build()
    {
        var english = new Dictionary<string, List<Ranked>>(StringComparer.Ordinal);
        var reading = new Dictionary<string, List<Ranked>>(StringComparer.Ordinal);

        using var file = File.OpenRead(_dictionaryPath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var xml = XmlReader.Create(gzip, new XmlReaderSettings
        {
            // JMdictは品詞などを実体参照（&n; など）で書くので、DTDを読まないと展開できずに落ちる
            DtdProcessing = DtdProcessing.Parse,

            // 既定の上限（1000万文字）は20万項目ぶんの実体参照で足りない。
            // 0 は「上限なし」。同梱した自分のファイルを読むだけなので、膨張攻撃の心配は無い
            MaxCharactersFromEntities = 0,
            IgnoreWhitespace = true,
            IgnoreComments = true,
        });

        while (xml.ReadToFollowing("entry"))
        {
            var entry = ReadEntry(xml);
            if (entry is null)
            {
                continue;
            }

            foreach (var (gloss, rank) in entry.Glosses)
            {
                Add(english, gloss, entry.Forms, rank);
            }

            foreach (var kana in entry.Readings)
            {
                Add(reading, kana, entry.Forms, entry.FormRank);
            }
        }

        _byEnglish = Pack(english, EnglishForms);
        _byReading = Pack(reading, ReadingForms);
    }

    private sealed record Ranked(string[] Forms, int Rank);

    private sealed record Entry(string[] Forms, int FormRank, List<(string Gloss, int Rank)> Glosses, List<string> Readings);

    /// <summary>
    /// 1項目を読む。使うのは表記・読み・語義・頻度の印だけで、品詞や用例は読み飛ばす。
    /// </summary>
    private static Entry? ReadEntry(XmlReader xml)
    {
        var kanji = new List<(string Text, int Nf, bool Top)>();
        var kana = new List<(string Text, int Nf, bool Top)>();
        var glosses = new List<(string Gloss, int Rank)>();
        var usuallyKana = false;
        var sensePosition = 0;
        var glossPosition = 0;

        using var subtree = xml.ReadSubtree();
        subtree.Read();

        string? currentText = null;
        var nf = 99;
        var top = false;
        var inKanji = false;
        var inKana = false;

        // ReadElementContentAsString は終了タグの先まで進むので、
        // そのあと Read() を呼ぶと次の要素を1つ飛ばしてしまう。
        // 読んだ直後は進めず、今いる節をそのまま見直す形にしてある
        while (!subtree.EOF)
        {
            if (subtree.NodeType == XmlNodeType.Element)
            {
                switch (subtree.Name)
                {
                    case "k_ele":
                        inKanji = true;
                        currentText = null;
                        nf = 99;
                        top = false;
                        break;

                    case "r_ele":
                        inKana = true;
                        currentText = null;
                        nf = 99;
                        top = false;
                        break;

                    case "keb":
                    case "reb":
                        currentText = subtree.ReadElementContentAsString();
                        continue;

                    case "ke_pri":
                    case "re_pri":
                        var priority = subtree.ReadElementContentAsString();
                        if (priority.StartsWith("nf", StringComparison.Ordinal)
                            && int.TryParse(priority.AsSpan(2), out var band))
                        {
                            nf = Math.Min(nf, band);
                        }
                        else if (priority is "ichi1" or "news1" or "spec1" or "gai1")
                        {
                            top = true;
                        }

                        continue;

                    case "sense":
                        sensePosition++;
                        glossPosition = 0;
                        break;

                    case "misc":
                        // uk = ふつうかなで書く語。サメ・クラゲのようにカナの方が本体
                        if (subtree.ReadElementContentAsString().Contains("uk", StringComparison.Ordinal))
                        {
                            usuallyKana = true;
                        }

                        continue;

                    case "gloss":
                        var gloss = Normalise(subtree.ReadElementContentAsString());
                        glossPosition++;
                        if (gloss is not null)
                        {
                            // 「1番目の意味かどうか」を頻度より強く見る。
                            // 頻度だけで並べると ring が「核」に倒れた
                            var rank = (sensePosition == 1 && glossPosition == 1 ? 4000
                                : sensePosition == 1 ? 2000
                                : glossPosition == 1 ? 800 : 0);
                            glosses.Add((gloss, rank));
                        }

                        continue;
                }
            }
            else if (subtree.NodeType == XmlNodeType.EndElement)
            {
                if (subtree.Name == "k_ele" && inKanji)
                {
                    if (currentText is not null)
                    {
                        kanji.Add((currentText, nf, top));
                    }

                    inKanji = false;
                }
                else if (subtree.Name == "r_ele" && inKana)
                {
                    if (currentText is not null)
                    {
                        kana.Add((currentText, nf, top));
                    }

                    inKana = false;
                }
            }

            subtree.Read();
        }

        if (glosses.Count == 0 && kana.Count == 0)
        {
            return null;
        }

        var all = kanji.Concat(kana).ToList();
        if (all.Count == 0)
        {
            return null;
        }

        var bestNf = all.Min(entry => entry.Nf);
        var anyTop = all.Any(entry => entry.Top);
        var formRank = (99 - bestNf) * 4 + (anyTop ? 40 : 0);

        // かなで書くのが普通の語は、かなを先に出す。
        // 「鮫」より「サメ」の方が手元の商品名に当たる
        var forms = (usuallyKana || kanji.Count == 0
                ? kana.Select(entry => entry.Text).Concat(kanji.Select(entry => entry.Text))
                : kanji.Select(entry => entry.Text).Concat(kana.Select(entry => entry.Text)))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        // 漢字を持たない語は読みの索引に入れない。
        // 「さめ→サメ」のようなかな同士の移りは文字コードを足すだけで作れるので、
        // 索引に持つと大きさだけが増える
        var readings = kanji.Count == 0
            ? []
            : kana
                .Select(entry => ToHiragana(entry.Text))
                .Where(IsKana)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        return new Entry(
            forms,
            formRank,
            glosses.Select(entry => (entry.Gloss, entry.Rank + formRank)).ToList(),
            readings);
    }

    private static void Add(Dictionary<string, List<Ranked>> index, string key, string[] forms, int rank)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = [];
            index[key] = list;
        }

        list.Add(new Ranked(forms, rank));
    }

    private static Dictionary<string, string[]> Pack(Dictionary<string, List<Ranked>> index, int keep)
    {
        var packed = new Dictionary<string, string[]>(index.Count, StringComparer.Ordinal);

        foreach (var (key, list) in index)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var forms = new List<string>(keep);

            foreach (var entry in list.OrderByDescending(entry => entry.Rank))
            {
                foreach (var form in entry.Forms)
                {
                    if (seen.Add(form))
                    {
                        forms.Add(form);
                    }

                    if (forms.Count >= keep)
                    {
                        break;
                    }
                }

                if (forms.Count >= keep)
                {
                    break;
                }
            }

            packed[key] = forms.ToArray();
        }

        return packed;
    }

    /// <summary>
    /// 語義を索引の鍵に直す。括弧の中（用法の注記）は落とす。
    /// 3語以上の言い回しは鍵にしない——打たれることが無く、索引を太らせるだけ。
    /// </summary>
    private static string? Normalise(string gloss)
    {
        var text = gloss.ToLowerInvariant();
        var open = text.IndexOf('(');
        if (open >= 0)
        {
            var close = text.IndexOf(')', open);
            text = close > open ? text.Remove(open, close - open + 1) : text[..open];
        }

        text = text.Trim();
        if (text.Length is 0 or > 24)
        {
            return null;
        }

        if (text.Count(c => c == ' ') > 1)
        {
            return null;
        }

        return text.All(c => c is >= 'a' and <= 'z' or ' ' or '\'' or '-') ? text : null;
    }

    private static string ToHiragana(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c);
        }

        return builder.ToString();
    }

    private static bool IsKana(string text)
        => text.Length > 0 && text.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー');

    // ---- キャッシュ ----
    //
    // 「鍵\t表記\t表記…」を1行ずつ。英語の索引と読みの索引を空行で区切る。
    // 人が開いて読める形にしてあるのは、このツール全体の方針に合わせたもの。

    private bool TryLoadCache()
    {
        if (!File.Exists(_cachePath))
        {
            return false;
        }

        using var reader = new StreamReader(_cachePath, Encoding.UTF8);
        if (reader.ReadLine() is not { } header || header != $"bridge\t{CacheVersion}\t{DictionaryStamp()}")
        {
            return false;
        }

        var english = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var reading = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var target = english;

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                target = reading;
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length > 1)
            {
                target[parts[0]] = parts[1..];
            }
        }

        _byEnglish = english;
        _byReading = reading;
        return true;
    }

    private void SaveCache()
    {
        if (_byEnglish is null || _byReading is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_cachePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _cachePath + ".tmp";
        using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
        {
            writer.WriteLine($"bridge\t{CacheVersion}\t{DictionaryStamp()}");
            WriteSection(writer, _byEnglish);
            writer.WriteLine();
            WriteSection(writer, _byReading);
        }

        File.Move(temp, _cachePath, overwrite: true);
    }

    private static void WriteSection(TextWriter writer, Dictionary<string, string[]> index)
    {
        foreach (var (key, forms) in index)
        {
            writer.Write(key);
            foreach (var form in forms)
            {
                writer.Write('\t');
                writer.Write(form);
            }

            writer.WriteLine();
        }
    }

    /// <summary>辞書を差し替えたら索引を組み直すための印。大きさと更新日時で足りる。</summary>
    private string DictionaryStamp()
    {
        var info = new FileInfo(_dictionaryPath);
        return $"{info.Length}-{info.LastWriteTimeUtc.Ticks}";
    }
}
