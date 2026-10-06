using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>保存先の使用状況。設定画面に出す。</summary>
public sealed record StorageUsage
{
    public required string Root { get; init; }

    // 読めなかった（途中で権限が無い・外付けが外れた）ときは null。0 は本当に空のとき（外部の点検 2026-10-06）
    public required long? ImageBytes { get; init; }

    public required int? ImageCount { get; init; }

    public required long? ItemBytes { get; init; }

    public required int? ItemCount { get; init; }
}

/// <summary>非表示にした商品1件。設定画面から戻せるようにする。</summary>
public sealed record HiddenItem
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }
}

/// <summary>管理から外したファイル1件。</summary>
public sealed record ExcludedFile
{
    public required string Hash { get; init; }

    public required string Path { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset ExcludedAt { get; init; }
}

/// <summary>
/// 「この商品のものではない」と外したファイル1件。
/// 除外と違い、外したのはその商品への紐付けだけで、ファイル自体は管理下に残る。
/// </summary>
public sealed record DetachedRecord
{
    public required string Hash { get; init; }

    public required string ItemId { get; init; }

    /// <summary>外したときの商品名。今は消えているかもしれないので、引けなければIDのまま。</summary>
    public required string ItemName { get; init; }

    public required string Path { get; init; }
}

public interface ISettingsService
{
    /// <summary>今の設定。書くたびに差し替わる。</summary>
    AppSettings Current { get; }

    /// <summary>今の設定を変える。変え方を関数で渡す。</summary>
    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default);

    /// <summary>画面が覚えている状態。書くたびに差し替わる。</summary>
    UiState UiState { get; }

    /// <summary>画面が覚えている状態を変える。変え方を関数で渡す。</summary>
    Task<UiState> UpdateUiStateAsync(Func<UiState, UiState> change, CancellationToken cancellationToken = default);

    /// <summary>検索の履歴を変える。変え方を関数で渡す。</summary>
    Task<SearchHistoryList> ChangeSearchHistoryAsync(
        Func<SearchHistoryList, SearchHistoryList> change,
        CancellationToken cancellationToken = default);

    /// <summary>保存した検索を変える。変え方を関数で渡す。</summary>
    Task<SavedSearchList> ChangeSavedSearchesAsync(
        Func<SavedSearchList, SavedSearchList> change,
        CancellationToken cancellationToken = default);

    Task<StorageUsage> LoadUsageAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HiddenItem>> LoadHiddenAsync(CancellationToken cancellationToken = default);

    Task UnhideAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>除外したファイルを新しい順に。**読むのは呼んだスレッドの外**（同期の読み口は持たない。設定の画面が開くたびに読むため）。</summary>
    Task<IReadOnlyList<ExcludedFile>> LoadExcludedAsync(CancellationToken cancellationToken = default);

    /// <summary>除外を解除し、ファイルが元の場所に在れば、その場で未確定に戻す。</summary>
    Task<ExclusionLiftOutcome> RestoreExcludedAsync(string hash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DetachedRecord>> LoadDetachedAsync(CancellationToken cancellationToken = default);

    Task ForgetDetachedAsync(string hash, string itemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 設定の保存と、設定画面に出す集計。
///
/// 「非表示にした商品」と「管理から外したファイル」をここから戻せるようにしているのは、
/// どちらも普段の画面からは見えなくなる操作で、設定画面以外に取り消す場所が無いため。
/// </summary>
/// <summary>除外を解除した結果。画面の1行を書き分けるために返す。</summary>
public enum ExclusionLiftOutcome
{
    /// <summary>未確定に戻した（元から未確定に在った物も含む）。</summary>
    BackInUnresolved,

    /// <summary>元の場所にファイルが無い（移した・消した・中身が変わった）。除外は解いたが未確定には足していない。</summary>
    FileNotFound,

    /// <summary>同じ中身を商品が持っている。行き先が決まっているので未確定には足していない。</summary>
    OwnedByItem,

    /// <summary>除外の記録に無かった（別の画面で先に解除した）。</summary>
    NotExcluded,
}

public sealed class SettingsService : ISettingsService
{
    private readonly DataStore _store;

    public SettingsService(DataStore store)
    {
        _store = store;

        // 以前の版は取得の間隔を500msまで保存できた。約束（1.5秒以上）の範囲に戻してから使う
        Current = store.Settings.Load().Normalized();
        UiState = store.UiState.Load();
    }

    /// <summary>画面が覚えている状態（ui-state.json）。設定と同じく、持つのはここだけ。</summary>
    public UiState UiState { get; private set; }

    /// <summary>画面が覚えている状態を変える。設定と同じく、錠の中で今の値に当てる。</summary>
    public Task<UiState> UpdateUiStateAsync(Func<UiState, UiState> change, CancellationToken cancellationToken = default)
        // 設定と同じく、手元の値は錠の中で差し替える（錠の外だと古い方が後から代入され得る）
        => _store.UiState.UpdateAsync(change, written => UiState = written, cancellationToken);

    /// <summary>
    /// 今の設定。サービスには値ではなく「今の設定を読む関数」を渡しているので、書けばすぐ効く。
    /// **設定を持つのはここだけ**（技術的負債 1-1）。前は画面ごとに写しを持ち、取り込み画面だけディスクから読んで書いていたので、
    /// 取り込み画面で足した取り込み元が、別の画面の保存（古い写し）で消えていた。
    /// </summary>
    public AppSettings Current { get; private set; }

    /// <summary>
    /// 設定を変える。**丸ごと書かず、変え方を関数で渡す。**錠の中でディスクの今の設定に当てるので、
    /// 別の画面が同時に別の項目を書いても消し合わない（同じファイルへの書き込みも重ならない・技術的負債 1-4）。
    /// 画面からは <see cref="Commands.UiCommand.ChangeSettings"/> で呼ぶ。
    /// </summary>
    public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default)
        // 手元の値は錠の中で差し替える。錠の外だと、2本の保存が重なったときに先に書いた方が後から代入され、
        // ディスクより古い設定を持ち続けていた
        => _store.Settings.UpdateAsync(
            current => change(current).Normalized(),
            written => Current = written,
            cancellationToken);

    /// <summary>
    /// 検索の履歴を変える。前は検索画面と設定画面が読んだ写しを丸ごと書いていた（技術的負債 3-1）。
    /// 錠の中で今の履歴に当てる。画面からは <see cref="Commands.UiCommand.ChangeSearchHistory"/> で呼ぶ。
    /// </summary>
    public Task<SearchHistoryList> ChangeSearchHistoryAsync(
        Func<SearchHistoryList, SearchHistoryList> change,
        CancellationToken cancellationToken = default)
        => _store.SearchHistory.UpdateAsync(change, cancellationToken);

    /// <summary>
    /// 保存した検索を変える（足す・上書き・名前の変更・削除・並べ替え）。履歴と同じく錠の中で今の並びに当てる。
    /// 画面からは <see cref="Commands.UiCommand.ChangeSavedSearches"/> で呼ぶ。
    /// </summary>
    public Task<SavedSearchList> ChangeSavedSearchesAsync(
        Func<SavedSearchList, SavedSearchList> change,
        CancellationToken cancellationToken = default)
        => _store.SavedSearches.UpdateAsync(change, cancellationToken);

    /// <summary>保存先が何をどれだけ使っているか。画像は実ファイルを数える。</summary>
    public Task<StorageUsage> LoadUsageAsync(CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                var images = Measure(_store.Paths.ImagesDir, cancellationToken);
                var items = Measure(_store.Paths.ItemsDir, cancellationToken);

                return new StorageUsage
                {
                    Root = _store.Paths.Root,
                    ImageBytes = images?.Bytes,
                    ImageCount = images?.Count,
                    ItemBytes = items?.Bytes,
                    ItemCount = items?.Count,
                };
            },
            cancellationToken);

    /// <summary>
    /// フォルダの中のファイルの大きさと数。読めなかったら null（無いフォルダは空なので 0）。
    /// </summary>
    /// <remarks>
    /// **リンクの先は数えない**（<see cref="StoreTree"/>。外部の点検 2026-10-06）。前は <see cref="SearchOption.AllDirectories"/> で
    /// 保存先の中のジャンクションの先まで数え、ドライブを指すリンクがあると設定を開くたびにドライブ中を数えていた。
    /// 取り消しは列挙の途中でも見る（画面を離れたら止める）。途中で読めなくなったら、前は項目ごと 0 にしていた——空と見分けが付かない
    /// </remarks>
    internal static (long Bytes, int Count)? Measure(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return (0, 0);
        }

        try
        {
            long bytes = 0;
            var count = 0;
            foreach (var (_, length) in StoreTree.FilesWithLength(directory, cancellationToken))
            {
                bytes += length;
                count++;
            }

            return (bytes, count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
    public async Task<IReadOnlyList<HiddenItem>> LoadHiddenAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items
            .Where(item => item.Local.IsHidden)
            .Select(item => new HiddenItem
            {
                ItemId = item.Id,
                Name = item.DisplayName,
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task UnhideAsync(string itemId, CancellationToken cancellationToken = default)
    {
        // ほかの書き込みと同じく、商品の錠の中で今の値に当てる（2026-10-05・file-lifecycle.md「気になった所」3）。
        // 書くのは isHidden だけなので、前の写しで書く形でも消える物は無かったが、決まりを1つにそろえる
        await _store.Items.ChangeLocalAsync(
            itemId,
            current => current.IsHidden ? current with { IsHidden = false } : null,
            LocalOwners.Visibility,
            cancellationToken);
    }

    /// <summary>
    /// 除外したファイルを新しい順に。
    /// 除外した日時は記録に必ずある（無い記録はファイルごと読めない。古い形に合わせる救済は足さない）。
    /// 同じ日時の物は、記録の後ろ（後から足した物）を上にする——まとめて除外すると日時の揃った物が並び得るので、
    /// 並びを決めておかないと、読み直すたびに上下が入れ替わって見えかねない。
    /// </summary>
    private IReadOnlyList<ExcludedFile> LoadExcluded()
        => _store.Excluded.Load()
            .Select((entry, index) => (Index: index, File: new ExcludedFile
            {
                Hash = entry.Hash,
                Path = entry.Paths.FirstOrDefault() ?? entry.Hash,
                Reason = entry.Reason,
                ExcludedAt = entry.ExcludedAt,
            }))
            .OrderByDescending(pair => pair.File.ExcludedAt)
            .ThenByDescending(pair => pair.Index)
            .Select(pair => pair.File)
            .ToList();

    /// <summary>
    /// <see cref="LoadExcluded"/> を呼んだスレッドの外で。記録は除外するほど大きくなる（上限なし。5,000 件で約 15ms・2万件で約 57ms。
    /// `docs/research/large-files-2026-09-30.md`）。設定の画面は開くたびに読むので、その間画面を止めない。
    /// 同期の読み口を外に出さないのは、画面から呼ばれて画面のスレッドで読む道を作らないため。
    /// </summary>
    public Task<IReadOnlyList<ExcludedFile>> LoadExcludedAsync(CancellationToken cancellationToken = default)
        => Task.Run(LoadExcluded, cancellationToken);

    /// <summary>
    /// 除外を解除し、元の場所に同じ中身が在れば、その場で未確定に戻す。
    /// ここで解除しないと、一度除外したファイルは二度と現れない。
    /// </summary>
    public async Task<ExclusionLiftOutcome> RestoreExcludedAsync(string hash, CancellationToken cancellationToken = default)
    {
        bool Same(string other) => string.Equals(other, hash, StringComparison.OrdinalIgnoreCase);

        // 取り込みも同じファイルへ書くので、読み直してから消す（`docs/spec/data-model.md`）。
        // 錠の外で読むと、解除の最中に取り込みが足した除外が消える
        ExcludedEntry? lifted = null;
        await _store.Excluded.TryUpdateAsync(
            entries =>
            {
                lifted = entries.FirstOrDefault(entry => Same(entry.Hash));
                return entries.RemoveAll(entry => Same(entry.Hash)) > 0 ? entries : null;
            },
            cancellationToken);
        if (lifted is null)
        {
            return ExclusionLiftOutcome.NotExcluded;
        }

        // **その場で未確定に戻す**（ユーザ判断 2026-10-05・file-lifecycle.md「気になった所」10）。前は記録を消すだけで、
        // その取り込み元を取り込み直すまでどこにも出なかった（走査の控えに載っているので、監視の新着にも数えない）。
        // 画面の「次の取り込みでまた未確定として出てきます」は、対象に積んだときにしか正しくなかった。
        // 商品が同じ中身を持つなら行き先は決まっているので出さない（未確定を開いたときの均しと同じ見方）
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        if (loaded.Items.Any(item => item.Local.AttachedFiles.Any(file => Same(file.Hash))))
        {
            return ExclusionLiftOutcome.OwnedByItem;
        }

        var entry = await Task.Run(() => ToUnresolvedAsync(lifted, cancellationToken), cancellationToken);
        if (entry is null)
        {
            return ExclusionLiftOutcome.FileNotFound;
        }

        await _store.Unresolved.TryUpdateAsync(
            current =>
            {
                if (current.Any(file => Same(file.Hash)))
                {
                    return null;
                }

                current.Add(entry);
                return current;
            },
            cancellationToken);
        return ExclusionLiftOutcome.BackInUnresolved;
    }

    /// <summary>
    /// 除外の記録から、未確定の記録を作り直す。除外の記録は場所しか持たないので、取り込みが作るときと同じ物をそのファイルから読む
    /// （大きさ・日時・ダウンロード元の記録・zip の中身と開けたか）。候補は走査の控えの手掛かり（控えの3点が合うときだけ）。
    /// **同じ場所でも中身が変わっていれば戻さない**：控えの3点が合わなければハッシュを取り直して確かめる（人の1回の操作で、その1本だけ）。
    /// </summary>
    private async Task<UnresolvedFile?> ToUnresolvedAsync(ExcludedEntry lifted, CancellationToken cancellationToken)
    {
        var cache = _store.ScanCache.Load();
        var alive = new List<string>();
        IReadOnlyList<string> clues = [];
        foreach (var path in lifted.Paths)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            var cached = cache.FirstOrDefault(row =>
                string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase)
                && row.SizeBytes == info.Length
                && row.ModifiedAtUtc == modified);

            string current;
            if (cached is not null)
            {
                current = cached.Hash;
            }
            else
            {
                try
                {
                    current = await Scanning.FileHasher.ComputeSha256Async(path, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
            }

            if (!string.Equals(current, lifted.Hash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            alive.Add(path);
            if (clues.Count == 0 && cached?.ClueItemIds is { Count: > 0 } found)
            {
                clues = found;
            }
        }

        if (alive.Count == 0)
        {
            return null;
        }

        var first = new FileInfo(alive[0]);
        IReadOnlyList<string> contents = [];
        var broken = false;
        if (string.Equals(first.Extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                contents = BoothZipInspector.ZipInspector.Inspect(alive[0]).Summary.Files
                    .Select(file => file.RelativePath)
                    .ToList();
            }
            catch (InvalidDataException)
            {
                // 取り込みと同じ印。ほかのアプリが開いていた・権限が無いは、壊れているとは言えないので立てない
                broken = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        // ダウンロード元の記録は未確定の画面が読まない（記録の値だけを見る）ので、作るここで読む（DetachFileAsync と同じ）
        var zone = BoothZipInspector.ZoneIdentifierReader.Read(alive[0]);
        return new UnresolvedFile
        {
            Hash = lifted.Hash,
            Paths = alive,
            SizeBytes = first.Length,
            ModifiedAtUtc = new DateTimeOffset(first.LastWriteTimeUtc, TimeSpan.Zero),
            FirstSeenAt = DateTimeOffset.Now,
            Contents = contents,
            ZoneHostUrl = zone.HostUrl,
            ZoneReferrerUrl = zone.ReferrerUrl,
            CandidateItemIds = clues,
            ArchiveBroken = broken,
        };
    }

    /// <summary>
    /// 商品ページで外したファイルの一覧。
    ///
    /// 出すのは、**外した記録を全商品まとめて見られる場所がここしかない**ため
    /// （1件ずつなら商品ページで灰色の行として見え、「この商品に戻す」で戻せる）。
    /// 外した印は商品のJSONの中にあるので全商品から集める。日時は持たない（ユーザ判断）ので、商品名の順に並べる。
    /// </summary>
    public async Task<IReadOnlyList<DetachedRecord>> LoadDetachedAsync(
        CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items
            .SelectMany(item => item.Local.LocalFiles
                .Where(file => file.Detached)
                .Select(file => new DetachedRecord
                {
                    Hash = file.Hash,
                    ItemId = item.Id,
                    ItemName = item.DisplayName,
                    Path = file.Paths.FirstOrDefault() ?? file.Hash,
                }))
            .OrderBy(record => record.ItemName, StringComparer.CurrentCulture)
            .ThenBy(record => record.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 外した記録を捨てる（商品のJSONから、印の付いた行ごと消す）。
    /// 次の取り込みで、手掛かりが指すならまたその商品へ紐付く。
    /// </summary>
    public async Task ForgetDetachedAsync(
        string hash,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        // 商品の錠の中で今の一覧から消す（2026-10-05・file-lifecycle.md「気になった所」3）。前は錠の外で読んだ写しで
        // localFiles ごと書いていたので、読んでから書くまでに取り込みが足したファイル・付けた日時が消え得た
        await _store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                var files = current.LocalFiles
                    .Where(file => !(file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                return files.Count == current.LocalFiles.Count ? null : current with { LocalFiles = files };
            },
            [LocalField.LocalFiles],
            cancellationToken);
    }
}
