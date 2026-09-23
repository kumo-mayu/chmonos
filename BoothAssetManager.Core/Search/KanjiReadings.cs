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

    private readonly string _path;
    private readonly object _sync = new();

    private Dictionary<char, string[]>? _readings;
    private bool _failed;

    public KanjiReadings(string path) => _path = path;

    public bool IsAvailable => File.Exists(_path);

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

            try
            {
                _readings = Load();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
            {
                // 読めなくても検索は動く。造語の読みが作れないだけ。
                // 壊れた gz（InvalidDataException）も受ける——受けないと引くたびに読み直しては投げる（点検 2026-09-23）
                _failed = true;
            }
        }
    }

    private Dictionary<char, string[]> Load()
    {
        var map = new Dictionary<char, string[]>();

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
            var (literal, readings) = ReadCharacter(xml);
            if (literal is { Length: 1 } && readings.Count > 0)
            {
                map[literal[0]] = readings.ToArray();
            }
        }

        return map;
    }

    private static (string? Literal, List<string> Readings) ReadCharacter(XmlReader xml)
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
        return (literal, kun.Concat(on).ToList());
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
