namespace Chmonos.Core.Images;

/// <summary>
/// 検索の読み直しで、商品ごとの画像のフォルダの日時と「絵が1枚でもあるか」を見る所。
///
/// 読み直しは取り込み中は10秒ごとに走り、2000件で「存在の確認＋日時」を4,000回、
/// さらに「画像を取得中」の判定で「存在の確認＋列挙」を4,000回していた（温まった状態で 201ms。
/// <c>docs/research/memory-budget.md</c> 2026-09-29）。日時は1回の問い合わせにし、
/// 絵があるかは、フォルダの日時が前の読み直しから変わった商品だけ調べ直す（40ms）。
///
/// 親の <c>images</c> を1回列挙して子の日時を得る形は使わない。列挙で返る子フォルダの日時は、
/// 子にファイルを足しても消しても古いままで、直接問い合わせるまで新しくならなかった（測った担当が確かめた）。
/// </summary>
public static class ImageFolderPresence
{
    /// <summary>フォルダが無いときに <see cref="Directory.GetLastWriteTimeUtc"/> が返す値（1601-01-01）。</summary>
    private static readonly DateTime NoFolder = DateTime.FromFileTimeUtc(0);

    /// <summary>
    /// フォルダの更新時刻（中のファイルを足す・消すと変わる）。無ければ最小値（作られたら変わったと分かる）。
    ///
    /// <see cref="Directory.Exists"/> で先に確かめると問い合わせが2回になる。
    /// 無いフォルダは例外にならず 1601-01-01 が返るので、それを最小値に読み替える。
    /// </summary>
    public static DateTime Stamp(string directory)
    {
        try
        {
            var written = Directory.GetLastWriteTimeUtc(directory);
            return written == NoFolder ? DateTime.MinValue : written;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>そのフォルダに絵（webp）が1枚でもあるか。</summary>
    public static bool HasAnyImage(string directory)
    {
        try
        {
            return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.webp").Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 絵があるかを、前の答えを使い回しながら調べる。
    ///
    /// フォルダの中のファイルを足す・消すとフォルダの日時が変わるので、日時が前と同じ商品は前の答えのまま正しい。
    /// 日時が変わった商品と、前に調べていない商品だけ <paramref name="probe"/> で調べ直す。
    /// 日時は調べる前に取った物を渡すこと（調べた後に足された絵は、次の読み直しで日時の違いとして拾える）。
    /// </summary>
    /// <param name="folders">調べる商品と、そのフォルダの今の日時。</param>
    /// <param name="previous">前の読み直しの答え（商品 → 日時と、絵があったか）。</param>
    /// <param name="probe">商品の ID を受けて、絵があるかを実際に調べる。</param>
    /// <returns>絵がまだ1枚も無い商品と、次の読み直しに渡す答え（今回調べた商品の分だけ）。</returns>
    public static (HashSet<string> Missing, Dictionary<string, (DateTime Stamp, bool HasImage)> Memo) FindWithoutImages(
        IEnumerable<(string Id, DateTime Stamp)> folders,
        IReadOnlyDictionary<string, (DateTime Stamp, bool HasImage)> previous,
        Func<string, bool> probe)
    {
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var memo = new Dictionary<string, (DateTime Stamp, bool HasImage)>(StringComparer.Ordinal);

        foreach (var (id, stamp) in folders)
        {
            var hasImage = previous.TryGetValue(id, out var known) && known.Stamp == stamp
                ? known.HasImage
                : probe(id);

            memo[id] = (stamp, hasImage);
            if (!hasImage)
            {
                missing.Add(id);
            }
        }

        return (missing, memo);
    }
}
