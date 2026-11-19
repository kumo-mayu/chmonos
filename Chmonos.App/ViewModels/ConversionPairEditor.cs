using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// アバターの管理の「変換」の一覧の上の「追加した組」の節（<c>docs/spec/conversion.md</c>「人が足す組」・ユーザ判断 2026-11-20）。
/// 組（<c>conversion-pairs.json</c>）を全部並べ（元 → 先・メモ・×）、「組を追加」の行で元と先の両方にアバターか共通素体を選んで足す。
/// 前はアバターの詳細に置いていたが、元がいつもそのアバターで、共通素体を元にできなかった（ユーザ判断 2026-11-20：「変換」の側で編集する）。
/// 「逆も」は置かない（逆はもう1つの組として足す）。書き込みは <see cref="UiCommand.ChangeConversionPairs"/> 1本。
/// </summary>
public sealed class ConversionPairEditor : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly Func<string, ItemRecord?>? _findItem;
    private readonly Services.ThumbnailLoader? _thumbnails;

    /// <summary>組を足した・外した後に、変換経由を使う所（検索の絞り込み・アバターの詳細の札）へ知らせる。メモだけの変更では呼ばない（計算は変わらない）。</summary>
    private readonly Action? _written;

    /// <summary>メモを書いた後（アバターの詳細の札の吹き出しだけが変わる）。</summary>
    private readonly Action? _memoWritten;

    /// <summary>行ごとのメモの自動保存。書く先（行）と文は打った時点で決める（<see cref="MemoAutoSave{TOwner}"/>）。</summary>
    private readonly MemoAutoSave<ConversionPairRow> _memo;

    /// <summary>候補に出した共通素体の行から素体の名前を引く表（アバターと同じ名前の素体を取り違えない）。</summary>
    private Dictionary<string, string> _basesBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private Dictionary<string, string> _avatarIdsBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private Dictionary<string, Controls.SuggestInfo> _info = new(StringComparer.CurrentCultureIgnoreCase);

    private string _fromText = string.Empty;
    private string _toText = string.Empty;
    private bool _isExpanded = true;

    public ConversionPairEditor(
        AppServiceContainer services,
        Services.ThumbnailLoader? thumbnails = null,
        Func<string, ItemRecord?>? findItem = null,
        Action? written = null,
        Action? memoWritten = null)
    {
        _services = services;
        _thumbnails = thumbnails;
        _findItem = findItem;
        _written = written;
        _memoWritten = memoWritten;
        _memo = new MemoAutoSave<ConversionPairRow>(SaveMemoAsync);
        AddCommand = new RelayCommand(() => AddAsync().Forget());

        // 先の欄で候補を決めたら、そのまま足す（Enter は入力の確定・ui-input.md）。元の欄は選んだ語を残すだけ
        CommitToCommand = new RelayCommand(parameter => CommitToAsync(parameter as string).Forget());
    }

    /// <summary>
    /// 先の欄で候補を決めた。決めた語を先に入れてから足す（欄の文字の結び付けが先に届く事に頼らない）。
    /// 元がまだ決まっていなければ足さずに待つ（先から埋める人に「選んでください」を出すと、間違えたように見える）
    /// </summary>
    internal Task CommitToAsync(string? text)
    {
        if (text is not null)
        {
            ToText = text;
        }

        return EndOf(FromText) is null ? Task.CompletedTask : AddAsync();
    }

    /// <summary>組の行（足した順）。</summary>
    public IReadOnlyList<ConversionPairRow> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>節の見出し（数を添える。畳んでいても何組あるか分かるように）。</summary>
    public string HeaderText => $"追加した組（{Rows.Count}）";

    /// <summary>節を開いているか。見方を替えて戻っても保つ（View に持たせると作り直しで戻る）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>元と先を選ぶ欄の候補（アバターと、「（共通素体）」を添えた共通素体）。</summary>
    public IReadOnlyList<string> Suggestions { get; private set; } = [];

    public Func<string, Controls.SuggestInfo?> InfoSelector => text => _info.GetValueOrDefault(text);

    public IReadOnlyList<string> GroupHeadings => AvatarSuggestionText.Headings;

    /// <summary>候補の頭の絵（変換扱いのつなぎ先の欄と同じ）。素体かは確定の時と同じ表で先に見る。</summary>
    public Func<string, System.Windows.Media.ImageSource?> IconSelector => text =>
        _thumbnails is not { } thumbnails ? null
        : _basesBySuggestion.TryGetValue(text, out var baseName) ? AvatarBaseIcons.Load(_services, baseName, FindItem, thumbnails.LoadForTile)
        : _avatarIdsBySuggestion.TryGetValue(text, out var id) && AvatarImageSync.IconPath(_services.Paths, id, FindItem(id), _services.Settings.ShowRemovedBoothImages) is { } path
            ? thumbnails.LoadForTile(path)
        : null;

    /// <summary>元の欄の文字。候補を選ぶと候補の表記が入る（KeepsCommittedText）。打った綴りが候補と同じなら選んだのと同じに扱う（ui-input.md）。</summary>
    public string FromText
    {
        get => _fromText;
        set
        {
            if (SetField(ref _fromText, value ?? string.Empty))
            {
                Notice.Clear();
            }
        }
    }

    public string ToText
    {
        get => _toText;
        set
        {
            if (SetField(ref _toText, value ?? string.Empty))
            {
                Notice.Clear();
            }
        }
    }

    /// <summary>「追加」。元と先の欄の文字から組を作って足す。</summary>
    public RelayCommand AddCommand { get; }

    /// <summary>先の欄で候補を決めたとき。</summary>
    public RelayCommand CommitToCommand { get; }

    /// <summary>足せなかった・書けなかったときだけ出る1行。</summary>
    public AreaNotice Notice { get; } = new();

    /// <summary>メモの書くのを待っている物があるか（画面を離れる前に書き切る）。</summary>
    public bool IsMemoPending => _memo.IsPending;

    private ItemRecord? FindItem(string itemId) => _findItem?.Invoke(itemId);

    /// <summary>待っているメモを今書く（画面を離れる・閉じる前）。</summary>
    public Task FlushPendingWritesAsync() => _memo.FlushAsync();

    /// <summary>
    /// ファイルと登録簿から読み直す（「変換」に入った時・登録簿を読み直した時）。組は素体の改名・削除と商品 ID の変更でも書き換わるので、
    /// 読み直さないと古い行の × が何も外さない
    /// </summary>
    public void Reload() => ReloadAsync().Forget();

    /// <summary>
    /// 待っているメモを書き切ってからファイルを読む。書き切る前に読むと、打ったばかりのメモが古い文の行で作り直されて画面から消える
    /// （ファイルには後から書かれるので、次に開くまで食い違う）。待つ物が無ければ同期のまま作り直す
    /// </summary>
    internal async Task ReloadAsync()
    {
        await _memo.FlushAsync();
        Rebuild(_services.Store.ConversionPairs.Load());
    }

    private void Rebuild(ConversionPairFile file)
    {
        var registry = _services.CachedAvatars.Load();
        var names = AvatarNames.Map(registry.Entries);
        Rows = [.. file.Pairs
            .Where(pair => !pair.IsEmpty)
            .Select(pair => new ConversionPairRow(this, pair, ConversionPairText.NameOf(pair.From, names), ConversionPairText.NameOf(pair.To, names)))];

        // 候補の群（所持アバター／共通素体／未所持アバター）と呼び方は、変換扱いのつなぎ先の欄と同じ作り
        _basesBySuggestion = registry.BaseGroups
            .Where(group => !group.Rejected && group.Name.Trim().Length > 0)
            .Select(group => group.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(ConversionPairText.BaseText, name => name, StringComparer.CurrentCultureIgnoreCase);
        _avatarIdsBySuggestion = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        _info = new Dictionary<string, Controls.SuggestInfo>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in registry.Entries.Where(AvatarService.IsAvatar))
        {
            var name = names[entry.ItemId];
            if (_avatarIdsBySuggestion.TryAdd(name, entry.ItemId))
            {
                var group = entry.IsOwnedManually || FindItem(entry.ItemId)?.IsOwned == true
                    ? AvatarSuggestionText.OwnedGroup
                    : AvatarSuggestionText.OtherGroup;
                _info[name] = AvatarSuggestionText.InfoOf(entry, name, group);
            }
        }

        foreach (var baseText in _basesBySuggestion.Keys)
        {
            _info[baseText] = new Controls.SuggestInfo(AvatarSuggestionText.BaseGroup, []);
        }

        Suggestions = [.. _avatarIdsBySuggestion.Keys
            .Concat(_basesBySuggestion.Keys)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)];
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(Suggestions));
    }

    /// <summary>候補の文字から端を引く。素体の表を先に見る（同じ名前のアバターと取り違えない）。</summary>
    internal ConversionEnd? EndOf(string text)
        => _basesBySuggestion.TryGetValue(text.Trim(), out var baseName) ? ConversionEnd.Base(baseName)
        : _avatarIdsBySuggestion.TryGetValue(text.Trim(), out var id) ? ConversionEnd.Avatar(id)
        : null;

    /// <summary>
    /// 元と先の欄から組を足す。足さない理由（候補に無い・同じ物同士・既にある）はここで言う。
    /// Core の <see cref="ConversionPairs.Add"/> も同じ物を黙って足さない（錠の中の今の値で判定する）が、理由は返さないので画面で先に見る
    /// </summary>
    internal async Task AddAsync()
    {
        if (EndOf(FromText) is not { } from || EndOf(ToText) is not { } to)
        {
            Notice.Warn("候補からアバターか共通素体を選んでください。");
            return;
        }

        var pair = new ConversionPair { From = from, To = to };
        if (pair.PointsToItself)
        {
            Notice.Warn("元と先に同じ物は選べません。");
            return;
        }

        if (Rows.FirstOrDefault(row => row.Pair.SameEnds(pair)) is { } known)
        {
            Notice.Warn($"「{known.Text}」は既にあります。");
            return;
        }

        if (await WriteThenRebuildAsync(file => ConversionPairs.Add(file, pair)))
        {
            // 同じ元から続けて足せるように、元は残して先だけ空にする
            ToText = string.Empty;
        }
    }

    internal async Task RemoveAsync(ConversionPairRow row)
        => await WriteThenRebuildAsync(file => ConversionPairs.Remove(file, row.Pair));

    /// <summary>
    /// 足す・外す。先に待っているメモを書き切る（書き切らずに書いた後の値で作り直すと、打ったばかりのメモが古い文の行に戻る）
    /// </summary>
    private async Task<bool> WriteThenRebuildAsync(Func<ConversionPairFile, ConversionPairFile> change)
    {
        await _memo.FlushAsync();
        if (await RunAsync(change) is not { } written)
        {
            return false;
        }

        Rebuild(written);
        _written?.Invoke();
        return true;
    }

    /// <summary>行の欄で打った（行から呼ぶ）。</summary>
    internal void MemoEdited(ConversionPairRow row, string text) => _memo.Edited(row, text);

    /// <summary>
    /// 打った時点の行の組へメモを書く。行は作り直さずにその場で組を差し替える（作り直すと、次の行のメモへ移ったフォーカスが飛ぶ）
    /// </summary>
    private async Task SaveMemoAsync(ConversionPairRow row, string text)
    {
        if (await RunAsync(file => ConversionPairs.SetMemo(file, row.Pair, text)) is { } written
            && written.Pairs.FirstOrDefault(pair => pair.SameEnds(row.Pair)) is { } saved)
        {
            row.Pair = saved;
            _memoWritten?.Invoke();
        }
    }

    /// <summary>書く。書けたら書いた後の値、書けなければ null（理由は欄の下の1行）。</summary>
    private async Task<ConversionPairFile?> RunAsync(Func<ConversionPairFile, ConversionPairFile> change)
    {
        Notice.Clear();
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.ChangeConversionPairs(change));
            switch (result)
            {
                case CommandResult.ConversionPairsChanged changed:
                    return changed.Pairs;
                case CommandResult.Failed failed:
                    Notice.Warn(failed.Message);
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Core.Diagnostics.AppLog.Error("変換の組の保存", exception);
            Notice.Warn($"保存できませんでした。{FailureText.Cause(exception)}");
        }

        return null;
    }
}

/// <summary>「追加した組」の1行（元 → 先・メモ・×）。</summary>
public sealed class ConversionPairRow : ViewModelBase
{
    private readonly ConversionPairEditor _owner;
    private string _memoText;

    public ConversionPairRow(ConversionPairEditor owner, ConversionPair pair, string fromName, string toName)
    {
        _owner = owner;
        Pair = pair;
        FromName = fromName;
        ToName = toName;
        _memoText = pair.Memo ?? string.Empty;
        RemoveCommand = new RelayCommand(() => owner.RemoveAsync(this).Forget());
    }

    /// <summary>今の組（メモを書いた後はその場で差し替える）。外す・メモを書く時は端で当てるので、メモが古くても同じ組を指す。</summary>
    public ConversionPair Pair { get; set; }

    public string FromName { get; }

    public string ToName { get; }

    public string Text => ConversionPairText.Format(FromName, ToName);

    /// <summary>使うツールの名前（ファイルを直して書いた物だけ。画面では書かない）。</summary>
    public string? Tool => Pair.Tool;

    /// <summary>行の名前の後ろに添える「・ツール名」。無ければ空（Run は隠せないので、区切りごと文字で出し分ける）。</summary>
    public string ToolText => string.IsNullOrWhiteSpace(Tool) ? string.Empty : $"・{Tool}";

    /// <summary>メモの欄（押さずに残る・I9）。</summary>
    public string MemoText
    {
        get => _memoText;
        set
        {
            if (SetField(ref _memoText, value ?? string.Empty))
            {
                _owner.MemoEdited(this, _memoText);
            }
        }
    }

    public RelayCommand RemoveCommand { get; }
}

/// <summary>組の書き方（「変換」の節とアバターの詳細の札で同じにする）。</summary>
public static class ConversionPairText
{
    public static string BaseText(string baseName) => $"{baseName}（共通素体）";

    public static string Format(string fromName, string toName) => $"{fromName} → {toName}";

    /// <summary>端の名前。登録簿から消えたアバターは ID のまま出す（何を指していたか分かるように）。</summary>
    public static string NameOf(ConversionEnd end, IReadOnlyDictionary<string, string> names)
        => end.BaseName is { } baseName ? BaseText(baseName)
        : end.AvatarItemId is { } id ? names.TryGetValue(id, out var name) ? name : id
        : string.Empty;
}

/// <summary>
/// アバターの詳細の「変換の組」（読むだけ・ユーザ判断 2026-11-20）。そのアバターが元か先にある組を札で並べ、
/// 「変換で編集」で「変換」へ移る。所属する共通素体が端の組は出さない（このアバターを直に指す組だけ）
/// </summary>
public sealed class ConversionPairSummary : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private string? _avatarItemId;

    public ConversionPairSummary(AppServiceContainer services, Action goToConversion)
    {
        _services = services;
        EditCommand = new RelayCommand(goToConversion);
    }

    public IReadOnlyList<ConversionPairChip> Chips { get; private set; } = [];

    public bool HasChips => Chips.Count > 0;

    /// <summary>「変換で編集」。アバターの管理の「変換」へ移る。</summary>
    public RelayCommand EditCommand { get; }

    /// <summary>見せるアバターを替える（一覧で選び直したとき）。null なら空にする。</summary>
    public void ShowFor(string? avatarItemId)
    {
        _avatarItemId = avatarItemId;
        Refresh();
    }

    /// <summary>組を書いた後・登録簿を読み直した後に読み直す。</summary>
    public void Refresh()
    {
        var self = _avatarItemId is { } id ? ConversionEnd.Avatar(id) : null;
        if (self is null)
        {
            Chips = [];
        }
        else
        {
            var names = AvatarNames.Map(_services.CachedAvatars.Load().Entries);
            Chips = [.. _services.Store.ConversionPairs.Load().Pairs
                .Where(pair => !pair.IsEmpty && (pair.From.Same(self) || pair.To.Same(self)))
                .Select(pair => new ConversionPairChip(
                    ConversionPairText.Format(ConversionPairText.NameOf(pair.From, names), ConversionPairText.NameOf(pair.To, names)),
                    pair.Memo))];
        }

        OnPropertyChanged(nameof(Chips));
        OnPropertyChanged(nameof(HasChips));
    }
}

/// <summary>アバターの詳細の組の札1枚（元 → 先。メモは吹き出し）。</summary>
public sealed record ConversionPairChip(string Text, string? Memo)
{
    public bool HasMemo => !string.IsNullOrWhiteSpace(Memo);
}
