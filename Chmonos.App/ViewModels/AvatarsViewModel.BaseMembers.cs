using Chmonos.Core.Commands;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 共通素体の側からアバターを足す・外す（メモ46・ユーザ判断 2026-10-05）と、アバターの詳細に推した素体を薄く出すところ。
/// </summary>
public sealed partial class AvatarsViewModel
{
    private RelayCommand? _addMemberCommand;
    private RelayCommand? _removeMemberCommand;

    /// <summary>
    /// 素体の詳細の「アバターを足す欄」の候補。登録簿のアバターのうち、この素体に入っていない物。
    /// 行は「名前（商品ID）」にする（検索の対応アバターの候補と同じ形。名前が重なってもIDで選び分けられ、IDでも引ける）。
    /// アバターでないと分かっている物は出さない（足しても素体の一員に数えない・<see cref="AvatarCompatibilityIndex"/>）
    /// </summary>
    public IReadOnlyList<string> MemberCandidates
    {
        get
        {
            if (SelectedBase is not { } selected)
            {
                return [];
            }

            var members = selected.Summary.MemberIds.ToHashSet(StringComparer.Ordinal);
            return _all
                .Where(row => row.Summary.IsAvatar && !members.Contains(row.ItemId))
                .OrderByDescending(row => row.IsOwned)
                .ThenBy(row => row.Name, StringComparer.CurrentCulture)
                .Select(row => AvatarSuggestionText.Format(row.Name, row.ItemId))
                .ToList();
        }
    }

    /// <summary>候補の群（所持アバター／未所持アバター）と、名前以外で当たる語（呼び方・正式名）。ほかのアバターの候補と同じ作り（メモ48・メモ58）</summary>
    public Func<string, Controls.SuggestInfo?> MemberInfoSelector => text =>
        AvatarSuggestionText.IdOf(text) is { } id && RowOf(id) is { } row
            ? AvatarSuggestionText.InfoOf(row.Summary.Entry, row.Name, row.IsOwned ? AvatarSuggestionText.OwnedGroup : AvatarSuggestionText.OtherGroup)
            : null;

    public IReadOnlyList<string> MemberGroupHeadings => AvatarSuggestionText.Headings;

    /// <summary>候補の頭の絵。一覧の行と同じ探し方（持っていれば商品の1枚目、無ければ控えの1枚）</summary>
    public Func<string, System.Windows.Media.ImageSource?> MemberIconSelector => text =>
        AvatarSuggestionText.IdOf(text) is { } id
        && AvatarImageSync.IconPath(_services.Paths, id, _main.Search.FindItem(id)) is { } path
            ? _main.Thumbnails.LoadForTile(path)
            : null;

    private AvatarRowViewModel? RowOf(string itemId) => _all.FirstOrDefault(row => row.ItemId == itemId);

    /// <summary>候補を選ぶと、そのアバターをこの素体に入れる。</summary>
    public RelayCommand AddMemberCommand => _addMemberCommand ??= new RelayCommand(parameter =>
    {
        if (parameter is string text)
        {
            AddMemberAsync(text).Forget();
        }
    });

    /// <summary>一覧の行の右クリック「この素体から外す」。</summary>
    public RelayCommand RemoveMemberCommand => _removeMemberCommand ??= new RelayCommand(parameter =>
    {
        if (parameter is AvatarRowViewModel row)
        {
            RemoveMemberAsync(row).Forget();
        }
    });

    /// <summary>
    /// アバターをこの素体に入れる。**ほかの素体に入っていた物は聞かずに移し、欄の下で知らせる**（メモ46 決定2）。
    /// 1体に素体は1つで、アバターの側で一覧から選ぶと聞かずに入るのと揃える。新しく入っただけなら知らせない
    /// （欄が空き、下の一覧に並ぶので見て分かる。素体を足す欄と同じ）
    /// </summary>
    internal async Task AddMemberAsync(string text)
    {
        if (SelectedBase is not { } selected)
        {
            return;
        }

        var baseName = selected.Name;
        if (AvatarSuggestionText.IdOf(text) is not { } id || RowOf(id) is not { } row)
        {
            selected.MemberNote.Warn("候補の中から選んでください。");
            return;
        }

        // 移ったかは書く前の所属で見る（推した仲間も所属に数える。素体の一覧の人数と同じ数え方）
        var previous = Bases.FirstOrDefault(other => other.Name != baseName && other.Summary.MemberIds.Contains(id))?.Name;

        if (await WriteAsync(new UiCommand.SetAvatarBase(id, baseName), "アバターを追加できませんでした。", selected.MemberNote.Warn) is null)
        {
            return;
        }

        NoteRegistryChanged();
        await LoadAsync();

        if (previous is not null)
        {
            ShowOnBase(baseName, other => other.MemberNote, $"「{row.Name}」を「{previous}」から移しました。", warn: false);
        }
    }

    /// <summary>
    /// アバターをこの素体から外す。手で決めた所属は空にし、名前から推した仲間には「素体に入れない」の印を立てる（Core が錠の中で決める）。
    /// 外したアバターは一覧から消えるので、できたことは知らせない
    /// </summary>
    internal async Task RemoveMemberAsync(AvatarRowViewModel row)
    {
        if (SelectedBase is not { } selected)
        {
            return;
        }

        var baseName = selected.Name;
        if (await WriteAsync(new UiCommand.RemoveAvatarFromBase(row.ItemId, baseName), "素体から外せませんでした。", selected.MemberNote.Warn) is null)
        {
            return;
        }

        NoteRegistryChanged();
        await LoadAsync();
    }

    // ── アバターの詳細：推した素体（メモ46 決定3）──

    /// <summary>
    /// 選んだアバターの今の素体。手で決めた素体か、無ければ名前から推した素体。
    /// 「素体から外す」はどちらでも外す（推した素体は印を立てて外す）
    /// </summary>
    private string? CurrentBaseOfSelected => Selected?.Summary is { } summary
        ? string.IsNullOrWhiteSpace(summary.Entry.BaseName) ? summary.InferredBaseName : summary.Entry.BaseName
        : null;

    /// <summary>
    /// 名前から推した素体を、手で決めた素体と見分けて薄く出す。欄は手で決めた素体だけを映すので、
    /// 出さないと「欄は空なのに素体の一覧には入っている」と食い違って見えた
    /// </summary>
    public string SelectedInferredBaseText => Selected?.Summary.InferredBaseName is { } name
        ? $"名前から「{name}」に入っています。"
        : string.Empty;

    public bool HasSelectedInferredBase => SelectedInferredBaseText.Length > 0;

    /// <summary>「素体から外す」を押せるか。どの素体にも入っていなければ押せない</summary>
    private bool CanClearBase() => CurrentBaseOfSelected is not null;
}
