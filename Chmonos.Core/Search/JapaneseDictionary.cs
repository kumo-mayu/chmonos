using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Chmonos.Core.Search;

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

    /// <summary>
    /// 読み1つにつき返す英語の数（日英変換・2026-09-16）。1番目の意味の語を先に並べるので、
    /// 浅くても外しにくい。深くすると2番目以降の意味（サメ→「loan shark」）まで探して誤爆が増える
    /// </summary>
    private const int KanaEnglish = 3;

    /// <summary>組み上げた索引の形式。上げると古いキャッシュを作り直す（3：読み→英語の索引を足した）。</summary>
    private const int CacheVersion = 3;

    // 控えの節の並び（英語→表記・読み→表記・読み→英語）。控えのファイルにもこの順で書く
    private const int EnglishSection = 0;
    private const int ReadingSection = 1;
    private const int KanaSection = 2;
    private const int SectionCount = 3;

    private readonly string _dictionaryPath;
    private readonly string _cachePath;
    private readonly object _sync = new();

    /// <summary>
    /// 節ごとに、**引かれたときに初めて**控えから読む。「別表記でも検索」の中の変換は1つずつ切れるので、
    /// 切っている変換の節まで持つと、使わない索引がアプリを閉じるまで残る。
    /// </summary>
    private readonly PackedIndex?[] _sections = new PackedIndex?[SectionCount];
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
        => EnsureLoaded(EnglishSection)?.Lookup(word.ToLowerInvariant()) ?? [];

    /// <summary>読み（ひらがな）から日本語の表記を引く。</summary>
    public IReadOnlyList<string> ByReading(string reading)
        => EnsureLoaded(ReadingSection)?.Lookup(reading) ?? [];

    /// <summary>
    /// 読み（ひらがな）から英語を引く（日英変換・2026-09-16）。カタカナで打った外来語から、
    /// 英語で名付けた商品に届くように。漢字を持たない語（サメ・リボン）も引ける。
    /// </summary>
    public IReadOnlyList<string> ByKana(string reading)
        => EnsureLoaded(KanaSection)?.Lookup(reading) ?? [];

    /// <summary>試験用：いま手元に持っている節の数（引いていない節を読んでいないかを見る）。</summary>
    internal int LoadedSectionCount => _sections.Count(section => section is not null);

    /// <summary>
    /// 索引を組む。**最初に必要になったときだけ**動く。
    ///
    /// 素のXMLは63MBあるので、一度組んだらキャッシュに書いて次からはそれを読む。
    /// キャッシュは手元で組んだものなので配布物には入らない
    /// （＝加工した辞書を配らないので、継承条項に触れる物が無い）。
    /// </summary>
    private PackedIndex? EnsureLoaded(int section)
    {
        if (_sections[section] is not null || _failed)
        {
            return _sections[section];
        }

        lock (_sync)
        {
            if (_sections[section] is not null || _failed)
            {
                return _sections[section];
            }

            try
            {
                if (TryLoadCacheSafely(section))
                {
                    return _sections[section];
                }

                // 組むときは XML を1度通すので、3つの節を全部作って持つ（控えが無い最初の1回だけ）
                Build();
            }
            // 壊れた gz は InvalidDataException で来る。受けないと _failed が立たず、
            // 検索のたびに63MBを読み直しては投げていた（点検 2026-09-23）
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
            {
                // 引けなくても検索そのものは動く。橋が架からないだけ。
                // 理由は残す——黙って引けなくなると、辞書が壊れているのか
                // 語が無いだけなのかを区別できない
                _failed = true;
                Array.Clear(_sections);
                LoadError = exception.Message;
                return null;
            }

            // 書き出しは組むのと別に受ける。同じ try に入れていたので、保存先に書けないだけで
            // 組めた索引まで捨てて「辞書が読めない」にしていた。書けなければ次の起動でまた組むだけ
            try
            {
                SaveCache();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                CacheSaveError = exception.Message;
            }

            return _sections[section];
        }
    }

    /// <summary>索引の控えを書けなかった理由（引くのには差し支えない。次の起動でまた組む）。</summary>
    public string? CacheSaveError { get; private set; }

    /// <summary>
    /// 控えを読む。読めなければ（ほかのアプリが掴んでいる・壊れている）組み直しに回す——
    /// 控えは手元で組んだ物なので、読めないことを「辞書が無い」にしない。
    /// </summary>
    private bool TryLoadCacheSafely(int section)
    {
        try
        {
            return TryLoadCache(section);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Build()
    {
        var english = new Dictionary<string, List<Ranked>>(StringComparer.Ordinal);
        var reading = new Dictionary<string, List<Ranked>>(StringComparer.Ordinal);
        var kanaToEnglish = new Dictionary<string, List<Ranked>>(StringComparer.Ordinal);

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

            if (entry.EnglishForms.Length > 0)
            {
                foreach (var kana in entry.AllKana)
                {
                    Add(kanaToEnglish, kana, entry.EnglishForms, entry.FormRank);
                }
            }
        }

        _sections[EnglishSection] = Pack(english, EnglishForms);
        _sections[ReadingSection] = Pack(reading, ReadingForms);
        _sections[KanaSection] = Pack(kanaToEnglish, KanaEnglish);
    }

    private sealed record Ranked(string[] Forms, int Rank);

    /// <param name="AllKana">漢字の有無を問わない全部の読み（ひらがな）。読み→英語の鍵。</param>
    /// <param name="EnglishForms">英語の語義を意味の順に（1番目の意味の最初の語が先頭）。</param>
    private sealed record Entry(
        string[] Forms,
        int FormRank,
        List<(string Gloss, int Rank)> Glosses,
        List<string> Readings,
        List<string> AllKana,
        string[] EnglishForms);

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

        var allKana = kana
            .Select(entry => ToHiragana(entry.Text))
            .Where(IsKana)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var englishForms = glosses
            .OrderByDescending(entry => entry.Rank)
            .Select(entry => entry.Gloss)
            .Distinct(StringComparer.Ordinal)
            .Take(KanaEnglish)
            .ToArray();

        return new Entry(
            forms,
            formRank,
            glosses.Select(entry => (entry.Gloss, entry.Rank + formRank)).ToList(),
            readings,
            allKana,
            englishForms);
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

    private static PackedIndex Pack(Dictionary<string, List<Ranked>> index, int keep)
    {
        var packed = new PackedIndex.Builder(index.Count);

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

            packed.BeginKey(key);
            foreach (var form in forms)
            {
                packed.AddForm(form);
            }
        }

        return packed.Build();
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
    // 「鍵\t表記\t表記…」を1行ずつ。英語の索引・読みの索引・読み→英語の索引を空行で区切る。
    // 人が開いて読める形にしてあるのは、このツール全体の方針に合わせたもの。
    // 読むのは頼まれた節だけ。ほかの節の行は、文字列に直さずに読み飛ばす

    private bool TryLoadCache(int section)
    {
        if (!File.Exists(_cachePath))
        {
            return false;
        }

        // 1行ずつ ReadLine すると、読み飛ばす節の行まで文字列になる（48.5万行・割り当て188MB）。
        // バイトのまま行を切り、頼まれた節の行だけを使い回す文字の置き場へ直して詰める。
        // 頼まれた節を読み終えたら、後ろの節は読まない
        using var stream = new FileStream(_cachePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        var reader = new ByteLines(stream);
        try
        {
            if (reader.Next() is not { } header
                || !Encoding.UTF8.GetString(StripPreamble(header)).Equals($"bridge\t{CacheVersion}\t{DictionaryStamp()}", StringComparison.Ordinal))
            {
                return false;
            }

            // 1行はおよそ52バイト（2026-09-24 の控え：25.2MB・48.5万行）。節は3つでほぼ同じ行数
            var builder = new PackedIndex.Builder((int)(stream.Length / 52 / SectionCount));
            var chars = ArrayPool<char>.Shared.Rent(256);
            try
            {
                // 空行ごとに次の節へ（3つ目より後ろの空行は3つ目のまま。前の読み方と同じ）
                var current = 0;
                while (reader.Next() is { } line)
                {
                    if (line.Length == 0)
                    {
                        current = Math.Min(current + 1, SectionCount - 1);
                        if (current > section)
                        {
                            break;
                        }

                        continue;
                    }

                    if (current != section)
                    {
                        continue;
                    }

                    if (Encoding.UTF8.GetMaxCharCount(line.Length) > chars.Length)
                    {
                        ArrayPool<char>.Shared.Return(chars);
                        chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(line.Length));
                    }

                    var count = Encoding.UTF8.GetChars(line.Span, chars);
                    AddRow(builder, chars.AsSpan(0, count));
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
            }

            _sections[section] = builder.Build();
            return true;
        }
        finally
        {
            reader.Dispose();
        }
    }

    private static ReadOnlySpan<byte> StripPreamble(ReadOnlyMemory<byte> line)
        => line.Span.StartsWith(Encoding.UTF8.Preamble) ? line.Span[Encoding.UTF8.Preamble.Length..] : line.Span;

    /// <summary>1行（鍵\t表記…）を足す。表記の無い行は前と同じく読み飛ばす。</summary>
    private static void AddRow(PackedIndex.Builder builder, ReadOnlySpan<char> row)
    {
        var tab = row.IndexOf('\t');
        if (tab < 0)
        {
            return;
        }

        builder.BeginKey(row[..tab]);
        var rest = row[(tab + 1)..];
        while (true)
        {
            var next = rest.IndexOf('\t');
            if (next < 0)
            {
                builder.AddForm(rest);
                return;
            }

            builder.AddForm(rest[..next]);
            rest = rest[(next + 1)..];
        }
    }

    /// <summary>
    /// ファイルをバイトのまま1行ずつ返す（改行 LF・CRLF を除いた中身）。置き場は借り物で、
    /// 返した行は次の <see cref="Next"/> まで有効。1行が置き場より長ければ置き場を広げる。
    /// </summary>
    private sealed class ByteLines(Stream stream) : IDisposable
    {
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(1 << 16);
        private int _start;
        private int _end;
        private bool _eof;

        public ReadOnlyMemory<byte>? Next()
        {
            while (true)
            {
                var newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
                if (newline >= 0)
                {
                    var line = Trim(_start, newline);
                    _start = newline + 1;
                    return line;
                }

                if (_eof)
                {
                    if (_start >= _end)
                    {
                        return null;
                    }

                    var last = Trim(_start, _end);
                    _start = _end;
                    return last;
                }

                Fill();
            }
        }

        private ReadOnlyMemory<byte> Trim(int start, int end)
            => _buffer.AsMemory(start, (end > start && _buffer[end - 1] == (byte)'\r' ? end - 1 : end) - start);

        private void Fill()
        {
            // 残りを頭へ寄せる。1行が置き場いっぱいなら置き場を倍にする
            var remaining = _end - _start;
            if (remaining == _buffer.Length)
            {
                var larger = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
                Buffer.BlockCopy(_buffer, _start, larger, 0, remaining);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = larger;
            }
            else
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, remaining);
            }

            _start = 0;
            _end = remaining;
            var read = stream.Read(_buffer, _end, _buffer.Length - _end);
            if (read == 0)
            {
                _eof = true;
            }

            _end += read;
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);
    }

    private void SaveCache()
    {
        if (_sections.Any(section => section is null))
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
            for (var section = 0; section < SectionCount; section++)
            {
                if (section > 0)
                {
                    writer.WriteLine();
                }

                WriteSection(writer, _sections[section]!);
            }
        }

        File.Move(temp, _cachePath, overwrite: true);
    }

    private static void WriteSection(TextWriter writer, PackedIndex index)
    {
        foreach (var (key, forms) in index.Entries())
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
