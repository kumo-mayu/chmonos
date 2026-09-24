using System.IO.Compression;
using System.Text;
using System.Xml;

namespace BoothAssetManager.Core.Search;

/// <summary>
/// 漢字1字ごとの音訓（kanjidic2）から、**商品名の読み**を組み立てる。
///
/// 辞書に載っていない造語のためにある。「撫で音」は複合語なのでどの語彙辞書にも無いが、
/// 撫（な.でる）と音（おと）を継げば「なでおと」が作れる。
///
/// **わざと多めに作る。**「なでね」「ぶでおと」のような外れも一緒に出るが、
/// これは<b>商品名の側</b>に張る索引なので害にならない——
/// 誰も打たない読みは、索引に置かれているだけで当たらない。
/// 引くときに完全一致させるので、**多めに作って引きで絞る**形になる。
///
/// 形態素解析（読みが1つに定まる）を使えば外れは消えるが、
/// 辞書が15倍になる。この使い方では精度の差が効かないので、軽い方を採った。
/// </summary>
public sealed class KanjiReadings
{
    /// <summary>1つの区間から作る読みの上限。漢字が続くと組み合わせが積で増える。</summary>
    private const int MaxReadingsPerSpan = 48;

    /// <summary>1字あたり試す読みの数。多いほど当たるが、組み合わせが増える。</summary>
    private const int ReadingsPerKanji = 6;

    /// <summary>これより長い区間は読みを作らない。長い文は組み合わせが爆発する割に引かれない。</summary>
    private const int MaxSpanLength = 12;

    /// <summary>字の表の控えの形式。上げると古い控えを作り直す（2：訓の数を持たせた。名前の読みの順で訓と音を選び分けるため）。</summary>
    private const int CacheVersion = 2;

    private readonly string _path;
    private readonly string? _cachePath;
    private readonly object _sync = new();

    private Dictionary<char, string[]>? _readings;

    /// <summary>字ごとの訓の数。読みの並びは訓が先なので、先頭からこの数が訓、残りが音。</summary>
    private Dictionary<char, int> _kunCounts = [];
    private bool _failed;

    /// <param name="path">同梱の KANJIDIC2（gz の XML）。</param>
    /// <param name="cachePath">
    /// 字の表の控え（表記の橋渡しの索引と同じく手元で組む物）。XML から組むと起動のたびに 0.3秒・割り当て19MB かかっていた
    /// （2026-09-24 実測）。渡さなければ毎回 XML から組む（試験・実験用）。
    /// </param>
    public KanjiReadings(string path, string? cachePath = null)
    {
        _path = path;
        _cachePath = cachePath;
    }

    public bool IsAvailable => File.Exists(_path);

    /// <summary>字の表の控えを書けなかった理由（引くのには差し支えない。次の起動でまた組む）。</summary>
    public string? CacheSaveError { get; private set; }

    /// <summary>
    /// 字の表を先に読んでおく。読み込みの裏のスレッドから呼ぶ——商品名の読みは造語変換で初めて照らすときに
    /// 画面のスレッドで作るので、そこで表まで読むと固まる。読み終えていれば何もしない。
    /// </summary>
    public void Prepare() => EnsureLoaded();

    /// <summary>
    /// 商品名から読みの候補を作る。
    ///
    /// 漢字とひらがなが続いている区間ごとに作る。カタカナ・ラテン文字・記号で切れる。
    /// 「【フカさんの！】撫で音ギミック」なら「撫で音」の区間から「なでおと」が出る。
    /// </summary>
    public IReadOnlyList<string> Of(string text)
    {
        EnsureLoaded();
        if (_readings is null || string.IsNullOrEmpty(text))
        {
            return [];
        }

        var results = new List<string>();

        foreach (var span in Spans(text))
        {
            Compose(span, results);
        }

        return results;
    }

    /// <summary>
    /// 名前の読みの順に使う、1字の代表の読み（ひらがな・送り仮名を除く）。知らない字なら null。
    ///
    /// **熟語の中なら音、1字だけなら訓を先に見る**（魔法 → まほう・鳥 → とり・撫で → な＋で）。
    /// 造語変換の読み（<see cref="Of"/>）のように多めに作って引きで絞ることができないので、1つに決める。
    /// 推定なので外れる（指輪 → しりん・衣装 → いそう）。並びが実際の読みとずれることがあるのは spec に書いてある。
    /// </summary>
    public string? PrimaryReading(char kanji, bool inCompound)
    {
        EnsureLoaded();
        if (_readings is null || !_readings.TryGetValue(kanji, out var all))
        {
            return null;
        }

        var kunCount = Math.Min(_kunCounts.GetValueOrDefault(kanji), all.Length);
        var kun = all.AsSpan(0, kunCount);
        var on = all.AsSpan(kunCount);

        var picked = inCompound
            ? FirstUsable(on) ?? FirstUsable(kun)
            : FirstUsable(kun) ?? FirstUsable(on);

        return picked is null ? null : Split(picked).Reading;
    }

    /// <summary>接頭・接尾の形（「-ずつ」「お-」）でない最初の読み。それしか無ければその先頭。</summary>
    private static string? FirstUsable(ReadOnlySpan<string> readings)
    {
        foreach (var reading in readings)
        {
            if (!reading.StartsWith('-') && !reading.EndsWith('-'))
            {
                return reading;
            }
        }

        return readings.Length > 0 ? readings[0] : null;
    }

    /// <summary>漢字とひらがなが続く区間を切り出す。漢字を1字も含まない区間は読む意味が無い。</summary>
    private static IEnumerable<string> Spans(string text)
    {
        var builder = new StringBuilder();
        var hasKanji = false;

        foreach (var c in text)
        {
            if (IsKanji(c) || IsHiragana(c))
            {
                builder.Append(c);
                hasKanji |= IsKanji(c);
                continue;
            }

            if (hasKanji && builder.Length <= MaxSpanLength)
            {
                yield return builder.ToString();
            }

            builder.Clear();
            hasKanji = false;
        }

        if (hasKanji && builder.Length <= MaxSpanLength)
        {
            yield return builder.ToString();
        }
    }

    /// <summary>
    /// 1区間の読みを組み立てる。
    ///
    /// 送り仮名（<c>な.でる</c>）は、続く文字が送りと一致するときだけ送りごと読む。
    /// これで「撫で」が「なで」になり、「撫でる」でなくても継げる。
    /// </summary>
    private void Compose(string span, List<string> results)
    {
        var partial = new List<string> { string.Empty };

        for (var index = 0; index < span.Length;)
        {
            var c = span[index];

            if (!IsKanji(c))
            {
                // ひらがなはそのまま読みになる
                for (var i = 0; i < partial.Count; i++)
                {
                    partial[i] += c;
                }

                index++;
                continue;
            }

            if (_readings is null || !_readings.TryGetValue(c, out var candidates))
            {
                // 読みを知らない字が混じったら、その区間は諦める
                return;
            }

            var rest = span.AsSpan(index + 1);
            var next = new List<string>(Math.Min(partial.Count * ReadingsPerKanji, MaxReadingsPerSpan));
            var consumed = 1;

            foreach (var candidate in candidates.Take(ReadingsPerKanji))
            {
                var (reading, okurigana) = Split(candidate);

                // 送り仮名が続いていれば、そこまで読んだことにして飛ばす
                var take = okurigana.Length > 0 && rest.StartsWith(okurigana) ? okurigana.Length : 0;
                var text = reading + (take > 0 ? okurigana : string.Empty);

                foreach (var head in partial)
                {
                    if (next.Count >= MaxReadingsPerSpan)
                    {
                        break;
                    }

                    next.Add(head + text);
                }

                // 送りを消費する読みが1つでもあれば、区間の進み方はそちらに合わせる。
                // 送りは表記の一部なので、読まずに残すと二重に数えてしまう
                if (take > 0)
                {
                    consumed = 1 + take;
                }
            }

            if (next.Count == 0)
            {
                return;
            }

            partial = next;
            index += consumed;
        }

        foreach (var reading in partial)
        {
            if (reading.Length > 0 && !results.Contains(reading))
            {
                results.Add(reading);
            }
        }
    }

    /// <summary>「な.でる」を「な」と「でる」に分ける。印（<c>-</c>）は落とす。</summary>
    private static (string Reading, string Okurigana) Split(string candidate)
    {
        var text = candidate.Trim('-');
        var dot = text.IndexOf('.');
        return dot < 0 ? (text, string.Empty) : (text[..dot], text[(dot + 1)..]);
    }

    private void EnsureLoaded()
    {
        if (_readings is not null || _failed)
        {
            return;
        }

        lock (_sync)
        {
            if (_readings is not null || _failed)
            {
                return;
            }

            if (TryLoadCache() is { } cached)
            {
                _kunCounts = cached.KunCounts;
                _readings = cached.Readings;
                return;
            }

            try
            {
                var loaded = Load();
                _kunCounts = loaded.KunCounts;
                _readings = loaded.Readings;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
            {
                // 読めなくても検索は動く。造語の読みが作れないだけ。
                // 壊れた gz（InvalidDataException）も受ける——受けないと引くたびに読み直しては投げる（点検 2026-09-23）
                _failed = true;
                return;
            }

            // 書けなくても組めた表は使う。次の起動でまた組むだけ
            try
            {
                SaveCache(_readings, _kunCounts);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                CacheSaveError = exception.Message;
            }
        }
    }

    // ---- 控え ----
    //
    // 「字\t読み\t読み…」を1行ずつ（読みの並びは KANJIDIC2 のまま・訓が先）。人が開いて読める形にしてある。
    // 見出しに辞書の大きさと更新日時を入れ、辞書を差し替えたら組み直す

    private string CacheHeader()
    {
        var info = new FileInfo(_path);
        return $"kanjidic\t{CacheVersion}\t{info.Length}-{info.LastWriteTimeUtc.Ticks}";
    }

    /// <summary>控えを読む。無い・見出しが違う・読めない・形が崩れているときは null（XML から組み直す）。</summary>
    private (Dictionary<char, int> KunCounts, Dictionary<char, string[]> Readings)? TryLoadCache()
    {
        if (_cachePath is null || !File.Exists(_cachePath) || !File.Exists(_path))
        {
            return null;
        }

        try
        {
            using var reader = new StreamReader(_cachePath, Encoding.UTF8);
            if (reader.ReadLine() != CacheHeader())
            {
                return null;
            }

            var map = new Dictionary<char, string[]>();
            var kunCounts = new Dictionary<char, int>();
            while (reader.ReadLine() is { } line)
            {
                var parts = line.Split('\t');

                // 1字・訓の数・読み1つ以上。崩れた行があれば控えごと信じない（手で直した控えで字が抜けるより、組み直す方がよい）
                if (parts.Length < 3 || parts[0].Length != 1 || !int.TryParse(parts[1], out var kunCount) || kunCount < 0)
                {
                    return null;
                }

                map[parts[0][0]] = parts[2..];
                kunCounts[parts[0][0]] = kunCount;
            }

            return (kunCounts, map);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveCache(Dictionary<char, string[]> map, Dictionary<char, int> kunCounts)
    {
        if (_cachePath is null)
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
            writer.WriteLine(CacheHeader());
            foreach (var (literal, readings) in map)
            {
                writer.Write(literal);
                writer.Write('\t');
                writer.Write(kunCounts.GetValueOrDefault(literal));
                foreach (var reading in readings)
                {
                    writer.Write('\t');
                    writer.Write(reading);
                }

                writer.WriteLine();
            }
        }

        File.Move(temp, _cachePath, overwrite: true);
    }

    private (Dictionary<char, int> KunCounts, Dictionary<char, string[]> Readings) Load()
    {
        var map = new Dictionary<char, string[]>();
        var kunCounts = new Dictionary<char, int>();

        using var file = File.OpenRead(_path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var xml = XmlReader.Create(gzip, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            MaxCharactersFromEntities = 0,
            IgnoreWhitespace = true,
            IgnoreComments = true,
        });

        while (xml.ReadToFollowing("character"))
        {
            var (literal, readings, kunCount) = ReadCharacter(xml);
            if (literal is { Length: 1 } && readings.Count > 0)
            {
                map[literal[0]] = readings.ToArray();
                kunCounts[literal[0]] = kunCount;
            }
        }

        return (kunCounts, map);
    }

    private static (string? Literal, List<string> Readings, int KunCount) ReadCharacter(XmlReader xml)
    {
        string? literal = null;
        var kun = new List<string>();
        var on = new List<string>();

        using var subtree = xml.ReadSubtree();
        subtree.Read();

        while (!subtree.EOF)
        {
            if (subtree.NodeType == XmlNodeType.Element)
            {
                if (subtree.Name == "literal")
                {
                    literal = subtree.ReadElementContentAsString();
                    continue;
                }

                if (subtree.Name == "reading")
                {
                    var type = subtree.GetAttribute("r_type");
                    var text = subtree.ReadElementContentAsString();

                    if (type == "ja_kun")
                    {
                        kun.Add(text);
                    }
                    else if (type == "ja_on")
                    {
                        // 音読みはカタカナで書かれている。索引はひらがなに寄せる
                        on.Add(ToHiragana(text));
                    }

                    continue;
                }
            }

            subtree.Read();
        }

        // 訓を先に見る。商品名は訓で読むことが多い（撫で音・指輪・鳥）
        return (literal, kun.Concat(on).ToList(), kun.Count);
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

    private static bool IsKanji(char c) => c is >= '一' and <= '鿿' or '々';

    private static bool IsHiragana(char c) => c is >= 'ぁ' and <= 'ゖ' or 'ー';
}
