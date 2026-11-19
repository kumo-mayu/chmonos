using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「このアバターの衣装を着せられるアバター」の欄（<c>docs/spec/conversion.md</c>「人が足す組」・ユーザ判断 2026-11-20）。
/// 見せているアバターを元にした組（<c>conversion-pairs.json</c>）を並べ、候補付きの欄で先（アバターか共通素体）を足す・× で外す。
/// 書き込みは <see cref="UiCommand.ChangeConversionPairs"/> 1本。人が書いた組なので確認待ちの印は無い。
/// </summary>
public sealed class ConversionPairEditor : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly Func<string, ItemRecord?>? _findItem;
    private readonly Services.ThumbnailLoader? _thumbnails;

    /// <summary>書いた後に、変換経由を使う所（検索の絞り込み）へ知らせる。</summary>
    private readonly Action? _written;

    private ConversionEnd? _from;

    /// <summary>候補に出した共通素体の行から素体の名前を引く表（アバターと同じ名前の素体を取り違えない）。</summary>
    private Dictionary<string, string> _basesBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private Dictionary<string, string> _avatarIdsBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private Dictionary<string, Controls.SuggestInfo> _info = new(StringComparer.CurrentCultureIgnoreCase);

    public ConversionPairEditor(
        AppServiceContainer services,
        Services.ThumbnailLoader? thumbnails = null,
        Func<string, ItemRecord?>? findItem = null,
        Action? written = null)
    {
        _services = services;
        _thumbnails = thumbnails;
        _findItem = findItem;
        _written = written;
        AddCommand = new RelayCommand(parameter => AddAsync(parameter as string).Forget(), _ => _from is not null);
    }

    /// <summary>見せているアバターを元にした組の行（足した順）。</summary>
    public IReadOnlyList<ConversionPairRow> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>先を選ぶ欄の候補（アバターと、「（共通素体）」を添えた共通素体）。見せているアバター自身は出さない。</summary>
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

    /// <summary>候補を確定したとき（引数は候補の文字）。候補に無い文字は足さない（新しいアバターは作らない）。</summary>
    public RelayCommand AddCommand { get; }

    /// <summary>書けなかったときだけ出る1行。</summary>
    public AreaNotice Notice { get; } = new();

    private ItemRecord? FindItem(string itemId) => _findItem?.Invoke(itemId);

    /// <summary>見せるアバターを替える（一覧で選び直したとき）。null なら空にする。</summary>
    public void ShowFor(string? avatarItemId)
    {
        _from = avatarItemId is null ? null : ConversionEnd.Avatar(avatarItemId);
        Notice.Clear();
        Reload(_services.Store.ConversionPairs.Load());
    }

    private void Reload(ConversionPairFile file)
    {
        if (_from is not { } from)
        {
            Rows = [];
            Suggestions = [];
            Raise();
            return;
        }

        var registry = _services.CachedAvatars.Load();
        var names = AvatarNames.Map(registry.Entries);
        string NameOf(ConversionEnd end)
            => end.BaseName is { } baseName
                ? $"{baseName}（共通素体）"
                : end.AvatarItemId is { } id ? names.TryGetValue(id, out var name) ? name : id : string.Empty;

        Rows = [.. file.Pairs
            .Where(pair => pair.From.Same(from) && !pair.IsEmpty)
            .Select(pair => new ConversionPairRow(pair, NameOf(pair.To), pair.Tool, new RelayCommand(() => RemoveAsync(pair).Forget())))];

        // 候補の群（所持アバター／共通素体／未所持アバター）と呼び方は、変換扱いのつなぎ先の欄と同じ作り
        _basesBySuggestion = registry.BaseGroups
            .Where(group => !group.Rejected && group.Name.Trim().Length > 0)
            .Select(group => group.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(name => $"{name}（共通素体）", name => name, StringComparer.CurrentCultureIgnoreCase);
        _avatarIdsBySuggestion = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        _info = new Dictionary<string, Controls.SuggestInfo>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in registry.Entries.Where(AvatarService.IsAvatar))
        {
            if (entry.ItemId == from.AvatarItemId)
            {
                continue;
            }

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
        Raise();
    }

    /// <summary>候補の文字から先の端を引く。素体の表を先に見る（同じ名前のアバターと取り違えない）。</summary>
    internal ConversionEnd? EndOf(string text)
        => _basesBySuggestion.TryGetValue(text.Trim(), out var baseName) ? ConversionEnd.Base(baseName)
        : _avatarIdsBySuggestion.TryGetValue(text.Trim(), out var id) ? ConversionEnd.Avatar(id)
        : null;

    internal async Task AddAsync(string? text)
    {
        if (_from is not { } from || text is null)
        {
            return;
        }

        if (EndOf(text) is not { } to)
        {
            Notice.Warn("候補からアバターか共通素体を選んでください。");
            return;
        }

        await RunAsync(file => ConversionPairs.Add(file, new ConversionPair { From = from, To = to }));
    }

    internal Task RemoveAsync(ConversionPair pair) => RunAsync(file => ConversionPairs.Remove(file, pair));

    private async Task RunAsync(Func<ConversionPairFile, ConversionPairFile> change)
    {
        Notice.Clear();
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.ChangeConversionPairs(change));
            switch (result)
            {
                case CommandResult.ConversionPairsChanged changed:
                    Reload(changed.Pairs);
                    _written?.Invoke();
                    break;
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
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(Suggestions));
        RelayCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>組の1行（先の名前と、分かればツールの名前）。</summary>
public sealed record ConversionPairRow(ConversionPair Pair, string Name, string? Tool, RelayCommand RemoveCommand)
{
    public bool HasTool => !string.IsNullOrWhiteSpace(Tool);
}
