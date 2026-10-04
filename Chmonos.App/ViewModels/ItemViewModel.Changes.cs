using Chmonos.Core.Commands;

namespace Chmonos.App.ViewModels;

/// <summary>上の帯の並びの1つが指す場所。</summary>
public enum ChangePlace
{
    Name,
    Variations,
    Gallery,
    Sale,
    Description,
    Section,

    /// <summary>ページに見せる欄が無い（押せない）。</summary>
    None,
}

/// <summary>上の帯に並べる「変わった所」1つ（メモ17⑤）。押すとそこまで流す。</summary>
public sealed record ChangeTarget(string Label, ChangeTone Tone, ChangePlace Place, SectionRow? Section = null)
{
    public bool CanGo => Place != ChangePlace.None;

    public string Tip => CanGo ? "ここまでスクロールします。" : "ページにない見出しです。";
}

/// <summary>
/// 商品ページ：BOOTH の商品ページで変わった所の印（メモ7-①・ユーザ判断 2026-10-02）。
///
/// 要確認・ショップの「変更あり」から開いても、どこが変わったのかがページの上で分からなかった。
/// 未読の更新の知らせ（<see cref="Core.Models.NotificationKind.ItemUpdated"/>）の差から、変わった欄の左に色の線と札を付ける。
/// 足した・消えた行・見出し・バリエーション・名前は帯（地と左の線）で示す（メモ17・ユーザ指示 2026-10-03）。
/// **印は「既読にする」を押すまで残す**（開いただけで既読にすると、読み終える前に別の画面へ移ったときに印が消える）。
/// 既読にするのは要確認の画面と同じ命令（<see cref="UiCommand.MarkNotificationsRead"/>）で、ナビの要確認の数も数え直す
/// </summary>
public sealed partial class ItemViewModel
{
    /// <summary>上の帯の見出しの名前を切る長さ。帯は1行なので、長い見出し1つで並びが埋まらないように。</summary>
    private const int TargetHeadingLength = 16;

    private ItemChanges _changes = ItemChanges.None;
    private bool _markingChangesRead;
    private bool _changesRead;
    private RelayCommand? _markChangesRead;
    private RelayCommand? _goToChange;

    /// <summary>未読の更新があるか。印と「既読にする」を出す。</summary>
    public bool HasUnreadChanges => _changes.HasAny;

    /// <summary>
    /// 上の帯を出しているか。**既読にした後も、このページを開いている間は帯を残す**（「既読にしました」と出す）：
    /// 帯が消えると、その高さの分だけ本文が一気に上へ動く（ユーザ指示 2026-10-03「画面が一瞬で大きくズレるような動作は避ける」）
    /// </summary>
    public bool ShowsChangesBar => _changes.HasAny || _changesRead;

    /// <summary>既読にした後の帯の文。</summary>
    public bool IsChangesRead => _changesRead && !_changes.HasAny;

    public ChangeSlot NameChange => _changes.Name;

    /// <summary>前の商品名（赤の帯の行）。変わっていなければ空。</summary>
    public string PreviousName => _changes.NameBefore ?? string.Empty;

    public bool HasPreviousName => PreviousName.Length > 0;

    public ChangeSlot VariationsChange => _changes.Variations;

    public ChangeSlot GalleryChange => _changes.Gallery;

    public ChangeSlot SaleChange => _changes.Sale;

    public ChangeSlot DescriptionChange => _changes.Description;

    /// <summary>見出しの無い商品の説明文の、変わった行の印（メモ13-②）。見出しのある商品は見出しごと（<see cref="SectionRow.Lines"/>）。</summary>
    public ChangedLineMarks DescriptionLines { get; private set; } = ChangedLineMarks.None;

    /// <summary>上の帯に並べる、変わった所（ページの上から下の順）。</summary>
    public IReadOnlyList<ChangeTarget> ChangeTargets { get; private set; } = [];

    /// <summary>
    /// 変わった所へ流すよう画面に頼む。畳んだ欄は先に開いてあるので、画面は並べ直してから位置を測る
    /// （開く前に測ると、開いて伸びた分だけ行き先がずれる）
    /// </summary>
    public event Action<ChangeTarget>? ChangeRevealRequested;

    /// <summary>既読にして印を外す直前。画面は、見ている所より上で消える帯の高さを測り、外した後に位置を詰め直す。</summary>
    public event Action? ChangesClearing;

    public RelayCommand MarkChangesReadCommand => _markChangesRead ??= new RelayCommand(
        () => MarkChangesReadAsync().Forget(),
        () => _changes.HasAny && !_markingChangesRead);

    public RelayCommand GoToChangeCommand => _goToChange ??= new RelayCommand(
        parameter =>
        {
            if (parameter is ChangeTarget target)
            {
                GoToChange(target);
            }
        },
        parameter => parameter is ChangeTarget { CanGo: true });

    /// <summary>
    /// この商品の未読の更新を読む。知らせのファイルを読むだけなので、画面から直に引く（書き込みは命令を通す）。
    ///
    /// **開くときに同期で読む**（メモ17）。前は裏で読んで後から当てていて、上の帯が開いた後に現れ、その高さの分だけ本文を押し下げていた。
    /// 知らせのファイルは上限200件で、読んだ物は大きさと日時が変わるまで使い回す（<c>JsonFileStore.Load</c>）
    /// </summary>
    private void LoadChanges()
    {
        var itemId = Item.Id;
        ApplyChanges(ItemChanges.From(
            _services.Notifications.Load().Where(record => ItemChanges.IsUnreadUpdateOf(record, itemId)),
            _pageSections.Select(section => section.Key)));
    }

    private void ApplyChanges(ItemChanges changes)
    {
        _changes = changes;
        foreach (var section in _pageSections)
        {
            section.Change = changes.Sections.TryGetValue(section.Key, out var slot) ? slot : ChangeSlot.Empty;
            var lines = changes.SectionLines.TryGetValue(section.Key, out var found) ? found : ChangedLines.None;
            section.Lines = ChangedLineMarks.For(lines, section.Text);
            section.Band = lines.WholeAdded ? ChangeTone.Added : null;
        }

        // 消えた見出しは元の位置へ差し込む。印を外すときは行を残して隠す（一覧を作り直すと、見ている所が動く）
        foreach (var section in Sections.Where(section => section.IsRemoved))
        {
            section.IsShown = false;
            section.Change = ChangeSlot.Empty;
            section.Lines = ChangedLineMarks.None;
            section.Band = null;
        }

        if (changes.RemovedSections.Count > 0)
        {
            Sections = WithRemovedSections(_pageSections, changes.RemovedSections);
            OnPropertyChanged(nameof(Sections));
        }

        DescriptionLines = ChangedLineMarks.For(changes.DescriptionLines, Description);

        if (changes.VariationLines.HasAny || changes.VariationPrices.Count > 0 || Variations.Any(row => row.Band is not null))
        {
            var rows = WithPrices(VariationRows(), changes.VariationPrices);
            Variations.Clear();
            foreach (var row in WithBands(rows, Item.Booth.Variations.Count, changes.VariationLines))
            {
                Variations.Add(row);
            }

            OnPropertyChanged(nameof(VariationsCountText));
        }

        ChangeTargets = Targets(changes, Sections);

        OnPropertyChanged(nameof(HasUnreadChanges));
        OnPropertyChanged(nameof(ShowsChangesBar));
        OnPropertyChanged(nameof(IsChangesRead));
        OnPropertyChanged(nameof(DescriptionLines));
        OnPropertyChanged(nameof(NameChange));
        OnPropertyChanged(nameof(PreviousName));
        OnPropertyChanged(nameof(HasPreviousName));
        OnPropertyChanged(nameof(VariationsChange));
        OnPropertyChanged(nameof(GalleryChange));
        OnPropertyChanged(nameof(SaleChange));
        OnPropertyChanged(nameof(DescriptionChange));
        OnPropertyChanged(nameof(ChangeTargets));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>今のページの見出しの並びに、消えた見出しを元の位置（前のページで直前にあった見出しの後ろ）で差し込む。</summary>
    private static List<SectionRow> WithRemovedSections(IReadOnlyList<SectionRow> page, IReadOnlyList<RemovedSection> removed)
    {
        var rows = removed.Select(section =>
        {
            var row = SectionRow.ForRemoved(section.Key);
            row.Change = new ChangeSlot([section.Mark]);
            row.Lines = ChangedLineMarks.For(section.Lines, null);
            row.Band = ChangeTone.Removed;
            return (section.Key, section.Follows, row);
        }).ToList();

        var result = page.ToList();
        var placed = ChangedLines.Place(page.Select(section => section.Key).ToList(), rows);
        for (var index = placed.Count - 1; index >= 0; index--)
        {
            result.Insert(placed[index].Before, placed[index].Item);
        }

        return result;
    }

    /// <summary>
    /// 上の帯の並び。ページの上から、右の列（商品名・バリエーション・販売）→ 左の列（画像・説明文の見出しの順）。
    /// 価格は値段の出ているバリエーションの欄へ流すので、バリエーションとまとめて1つ
    /// </summary>
    internal static IReadOnlyList<ChangeTarget> Targets(ItemChanges changes, IReadOnlyList<SectionRow> sections)
    {
        var targets = new List<ChangeTarget>();
        void Add(string label, ChangeSlot slot, ChangePlace place)
        {
            if (slot.Edge is { } tone)
            {
                targets.Add(new ChangeTarget(label, tone, place));
            }
        }

        Add("商品名", changes.Name, ChangePlace.Name);
        Add(changes.Variations.Marks.All(mark => mark.Tone == ChangeTone.Price) ? "価格" : "バリエーション", changes.Variations, ChangePlace.Variations);
        Add("販売状況", changes.Sale, ChangePlace.Sale);
        Add("画像", changes.Gallery, ChangePlace.Gallery);

        foreach (var section in sections.Where(section => section.Change.Edge is not null))
        {
            var heading = section.Heading.Trim();
            heading = heading.Length > TargetHeadingLength ? heading[..TargetHeadingLength] + "…" : heading;
            targets.Add(new ChangeTarget($"説明文：{heading}", section.Change.Edge!.Value, ChangePlace.Section, section));
        }

        if (changes.Description.Edge is { } description && changes.Sections.Count == 0 && changes.RemovedSections.Count == 0)
        {
            targets.Add(new ChangeTarget("説明文", description, ChangePlace.Description));
        }

        targets.AddRange(changes.Others.Select(other => new ChangeTarget(other, ChangeTone.Changed, ChangePlace.None)));
        return targets;
    }

    /// <summary>変わった所の欄を開いてから、画面に流すよう頼む。開いた欄は開いたままにする（画面内検索と同じ）。</summary>
    private void GoToChange(ChangeTarget target)
    {
        switch (target.Place)
        {
            case ChangePlace.Variations:
                IsVariationsExpanded = true;
                break;

            case ChangePlace.Description:
                IsDescriptionExpanded = true;
                break;

            case ChangePlace.Section when target.Section is { } section:
                IsDescriptionExpanded = true;
                if (!section.IsOpen)
                {
                    section.IsOpen = true;
                    OnPropertyChanged(nameof(ToggleAllSectionsText));
                }

                break;
        }

        ChangeRevealRequested?.Invoke(target);
    }

    private async Task MarkChangesReadAsync()
    {
        var ids = _changes.NotificationIds;
        if (ids.Count == 0 || _markingChangesRead)
        {
            return;
        }

        _markingChangesRead = true;
        RelayCommand.RaiseCanExecuteChanged();
        try
        {
            // 要確認の画面の「この束を既読にする」と同じ命令。保存を待ってから数え直す（待たずに数えるとナビの数が1つ古いまま残った）
            if (await _services.Commands.ExecuteAsync(new UiCommand.MarkNotificationsRead(ids)) is CommandResult.Failed failed)
            {
                Services.Notice.Show(failed.Message, "既読にする",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ChangesClearing?.Invoke();
            _changesRead = true;
            ApplyChanges(ItemChanges.None);
            _main.RefreshBadges();
        }
        finally
        {
            _markingChangesRead = false;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
