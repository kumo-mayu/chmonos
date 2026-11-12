using System.IO;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 手で改変に足すときに選べる「使ったファイル」1つ（メモ26-②・ユーザ判断 2026-10-04）。
///
/// 記録の形は「Unityへ送って足す」と同じ：zip の中に unitypackage があれば unitypackage ごと（どちらを入れたかが再現に要る）、
/// 無いファイル（unitypackage の無い zip・zip でないファイル）はファイルごと。
/// **フォルダは出さない**：フォルダは中身のハッシュを持たない（中の1ファイルで別物になる）ので、
/// 記録の <see cref="ModificationMember.FileHash"/> に入れる物が無い。
/// </summary>
public sealed class MemberFileOption
{
    /// <summary>「選ばない」。記録は今までどおり空のまま。</summary>
    public static readonly MemberFileOption None = new() { Label = "選ばない", Detail = string.Empty };

    public required string Label { get; init; }

    /// <summary>どの zip に入っているか（同じ名前の unitypackage を見分ける）。無ければ空。</summary>
    public required string Detail { get; init; }

    public string? FileHash { get; init; }

    public string? Package { get; init; }

    public long? VariationId { get; init; }

    /// <summary>
    /// 「選ばない」か。ハッシュだけでは見ない：登録したフォルダの中の物の記録はハッシュが無く unitypackage の場所だけを持つ
    /// （選び直しの窓で「今の記録のまま」に出る）
    /// </summary>
    public bool IsNone => FileHash is null && Package is null;

    /// <summary>今の記録が手元の一覧に無いときに、選び直しの窓だけに足す「今の記録のまま」。</summary>
    public bool IsKeepCurrent { get; init; }

    /// <summary>この選びが記録の行と同じファイルを指すか。種類は見ない（候補の種類は今のファイルの物で、記録の時と違い得る）。</summary>
    public bool Matches(ModificationMember member)
        => !IsNone
            && string.Equals(FileHash, member.FileHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Package, member.Package, StringComparison.Ordinal);

    public ModificationMemberFile ToFile() => new(FileHash, Package, VariationId);

    /// <summary>選ぶ欄の1行。閉じた欄にも同じ1行を出す。</summary>
    /// <summary>主はパッケージの名前で、zip の名前は補足。主が2つに見えないよう「名前 (zip名)」の形にする（メモ59・ユーザ決定 2026-10-05）。</summary>
    public string Text => Detail.Length > 0 ? $"{Label} ({Detail})" : Label;

    /// <summary>
    /// 商品の手元のファイルから選べる物を作る（手元のファイルの順、zip の中は zip の中の順）。
    ///
    /// **zip を開かない**：中の一覧は記録の中身の一覧（<see cref="LocalFileRecord.Contents"/>）から引く。
    /// 窓を出すまでの間に 1GB 級の zip を読むと止まって見える（送る窓で踏んだ。2026-09-29）。
    /// 外したファイルは出さない（今は使っていない物）。
    /// </summary>
    public static IReadOnlyList<MemberFileOption> ForItem(ItemRecord item)
    {
        var options = new List<MemberFileOption>();
        foreach (var file in item.Local.OwnedFiles)
        {
            var fileName = Path.GetFileName(file.Paths.FirstOrDefault() ?? string.Empty);
            var packages = UnityHandoff.PackageEntriesIn(file);
            if (packages.Count == 0)
            {
                options.Add(new MemberFileOption
                {
                    Label = fileName.Length > 0 ? fileName : "名前の分からないファイル",
                    Detail = string.Empty,
                    FileHash = file.Hash,
                    VariationId = file.VariationId,
                });
                continue;
            }

            options.AddRange(packages.Select(entry => new MemberFileOption
            {
                Label = Path.GetFileName(entry),
                Detail = fileName,
                FileHash = file.Hash,
                Package = entry,
                VariationId = file.VariationId,
            }));
        }

        return options;
    }
}

/// <summary>
/// 商品1つぶんの「使ったファイル」の選び。欄は人が足したり消したりできる（ユーザ指示 2026-11-12「二つまでしか選べないのは変」）。
///
/// 欄を1つ足すごとに、その商品の行が1つ増える（依存物と本体が同じ zip にあって両方入れた、を2行で記録するのと同じ形）。
/// 欄の数の上限は選べる物の数（「選ばない」を除く）。**同じファイルを2つの欄で選べない**：同じ商品・同じ足した日時・同じファイルの
/// 行が2つになると、行を中身で指す作り（J5）で見分けられない。
/// </summary>
public sealed class MemberFileChoice : ViewModelBase
{
    private RelayCommand? _addSlot;
    private RelayCommand? _removeSlot;

    /// <param name="candidates">選べるファイル（「選ばない」を除く）。</param>
    public MemberFileChoice(ItemRecord item, IReadOnlyList<MemberFileOption> candidates)
        // **1つだけなら最初から選んでおく**（ユーザ判断 2026-10-04）。窓で人が見て確定するので、推定で埋めるのとは違う。
        // 2つ以上なら、どれかに寄せると外れても気付きにくいので選ばない
        : this(item, [MemberFileOption.None, .. candidates], candidates.Count == 1 ? candidates[0] : MemberFileOption.None)
    {
    }

    /// <summary>選び直しの窓の1行（<see cref="MemberFilePickViewModel.ForRepick"/>）。最初に選ぶ物は呼び手が決める。</summary>
    internal MemberFileChoice(ItemRecord item, IReadOnlyList<MemberFileOption> options, MemberFileOption selected)
    {
        Item = item;
        Options = options;
        MaxSlots = options.Count(option => !option.IsNone);
        Slots.Add(new MemberFileSlot(this, selected));
        Refresh();
    }

    public ItemRecord Item { get; }

    public string Name => Item.DisplayName;

    /// <summary>選べる物（先頭は「選ばない」）。どの欄にも同じ物が並び、ほかの欄で選んだ物は押せない。</summary>
    public IReadOnlyList<MemberFileOption> Options { get; }

    /// <summary>選ぶ欄。最初は1つ。</summary>
    public System.Collections.ObjectModel.ObservableCollection<MemberFileSlot> Slots { get; } = [];

    /// <summary>欄の数の上限（選べる物の数）。全部の欄で別の物を選べば、それ以上は足しても選ぶ物が無い。</summary>
    public int MaxSlots { get; }

    /// <summary>欄を足す口を出すか（選べる物が2つ以上のときだけ。1つなら欄は1つで足りる）。</summary>
    public bool AllowsMore => MaxSlots >= 2;

    public bool CanAddSlot => Slots.Count < MaxSlots;

    /// <summary>1つ目の欄の選び（欄が1つだけの使い方と試験のための近道）。</summary>
    public MemberFileOption Selected
    {
        get => Slots[0].Selected;
        set => Slots[0].Selected = value;
    }

    /// <summary>欄を1つ足す。足した欄は「選ばない」から始める（推定で埋めない）。上限なら足さずに null。</summary>
    public MemberFileSlot? AddSlot()
    {
        if (!CanAddSlot)
        {
            return null;
        }

        var slot = new MemberFileSlot(this, MemberFileOption.None);
        Slots.Add(slot);
        Refresh();
        return slot;
    }

    /// <summary>欄を消す。最後の1つは消さない（「選ばない」にすれば記録を空にできる）。</summary>
    public void RemoveSlot(MemberFileSlot slot)
    {
        if (Slots.Count <= 1 || !Slots.Remove(slot))
        {
            return;
        }

        Refresh();
    }

    public RelayCommand AddSlotCommand => _addSlot ??= new RelayCommand(() => AddSlot());

    public RelayCommand RemoveSlotCommand => _removeSlot ??= new RelayCommand(parameter =>
    {
        if (parameter is MemberFileSlot slot)
        {
            RemoveSlot(slot);
        }
    });

    /// <summary>ほかの欄で選んでいる物か（「選ばない」はいくつの欄でもよい）。</summary>
    internal bool IsTakenByOther(MemberFileSlot slot, MemberFileOption option)
        => !option.IsNone && Slots.Any(other => !ReferenceEquals(other, slot) && SameFile(other.Selected, option));

    /// <summary>欄の選びが変わったとき。ほかの欄の押せない印と、足す・消す口を今の欄に合わせる。</summary>
    internal void Refresh()
    {
        foreach (var slot in Slots)
        {
            slot.RefreshTaken();
            slot.CanRemove = Slots.Count > 1;
        }

        OnPropertyChanged(nameof(CanAddSlot));
        OnPropertyChanged(nameof(Selected));
    }

    private static bool SameFile(MemberFileOption a, MemberFileOption b)
        => string.Equals(a.FileHash, b.FileHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Package, b.Package, StringComparison.Ordinal);

    /// <summary>
    /// 選んだファイル（欄の順。「選ばない」を除き、同じ物は1つにまとめる）。0件なら記録を空に戻す。
    /// 欄で同じ物を選べないようにしてあっても、ここでもまとめる（画面を通らない呼び手と、選びの途中の食い違いの守り）
    /// </summary>
    public IReadOnlyList<MemberFileOption> Picked => Slots
        .Select(slot => slot.Selected)
        .Where(option => !option.IsNone)
        .DistinctBy(option => (option.FileHash?.ToUpperInvariant(), option.Package))
        .ToList();

    /// <summary>記録する行（欄の順）。選ばなければファイルの欄が空の1行。</summary>
    public IReadOnlyList<ModificationMember> ToMembers(DateTimeOffset addedAt)
    {
        var picked = Picked;
        return picked.Count == 0
            ? [new ModificationMember { ItemId = Item.Id, AddedAt = addedAt }]
            : picked.Select(option => new ModificationMember
            {
                ItemId = Item.Id,
                VariationId = option.VariationId,
                FileHash = option.FileHash,
                Package = option.Package,
                AddedAt = addedAt,
            }).ToList();
    }
}

/// <summary>選ぶ欄1つ。並べる物は商品の選べる物と同じで、ほかの欄で選んだ物は押せない印を付ける。</summary>
public sealed class MemberFileSlot : ViewModelBase
{
    private readonly MemberFileChoice _owner;
    private MemberFileSlotEntry _selectedEntry;
    private bool _canRemove;

    internal MemberFileSlot(MemberFileChoice owner, MemberFileOption selected)
    {
        _owner = owner;

        // 並べる物は欄ごとに1回だけ作り、差し替えない：一覧を差し替えると、ComboBox が選びを一度 null に書き戻して、
        // ほかの欄を変えただけで選びが消える
        Entries = owner.Options.Select(option => new MemberFileSlotEntry(option)).ToList();
        _selectedEntry = Entries.FirstOrDefault(entry => ReferenceEquals(entry.Option, selected))
            ?? Entries.FirstOrDefault(entry => entry.Option.IsNone)
            ?? Entries[0];
    }

    public IReadOnlyList<MemberFileSlotEntry> Entries { get; }

    /// <summary>
    /// 欄で選んでいる物（ComboBox に結ぶ）。null は受けない（並べる物の出し入れの途中で ComboBox が書く値）。
    /// ほかの欄で選んでいる物も受けない（押せない印をくぐった書き込み）
    /// </summary>
    public MemberFileSlotEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedEntry) || _owner.IsTakenByOther(this, value.Option))
            {
                OnPropertyChanged();
                return;
            }

            _selectedEntry = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Selected));
            _owner.Refresh();
        }
    }

    public MemberFileOption Selected
    {
        get => _selectedEntry.Option;
        set => SelectedEntry = Entries.FirstOrDefault(entry => ReferenceEquals(entry.Option, value ?? MemberFileOption.None))
            ?? Entries.FirstOrDefault(entry => entry.Option.IsNone);
    }

    /// <summary>外すボタンを出すか（欄が2つ以上のとき）。</summary>
    public bool CanRemove
    {
        get => _canRemove;
        internal set => SetField(ref _canRemove, value);
    }

    public MemberFileChoice Owner => _owner;

    internal void RefreshTaken()
    {
        foreach (var entry in Entries)
        {
            entry.IsSelectable = !_owner.IsTakenByOther(this, entry.Option);
        }
    }
}

/// <summary>欄に並べる1行。ほかの欄で選んだ物は押せない。</summary>
public sealed class MemberFileSlotEntry(MemberFileOption option) : ViewModelBase
{
    private bool _isSelectable = true;

    public MemberFileOption Option { get; } = option;

    public string Text => Option.Text;

    public bool IsSelectable
    {
        get => _isSelectable;
        internal set => SetField(ref _isSelectable, value);
    }
}

/// <summary>
/// 手で改変に足すときの「使ったファイル」の欄（メモ26-②）。改変を選ぶ窓の中（商品ページ・右クリック・まとめて足す）と、
/// 改変の詳細で名前で足すときの小さな窓の両方に同じ物を出す。
///
/// **まとめて足すときも窓は1つ**：商品ごとに1行（名前と選ぶ欄）を並べる。商品ごとに窓を出すと、数十件で数十回聞かれる。
/// 選べるファイルの無い商品は行を出さない（選ぶ物が無い）。
/// </summary>
public sealed class MemberFilePickViewModel
{
    public MemberFilePickViewModel(IEnumerable<ItemRecord> items)
    {
        var all = items.ToList();
        Choices = all
            .Select(item => (item, options: MemberFileOption.ForItem(item)))
            .Where(pair => pair.options.Count > 0)
            .Select(pair => new MemberFileChoice(pair.item, pair.options))
            .ToList();
        ShowsNames = all.Count > 1;
    }

    private MemberFilePickViewModel(MemberFileChoice choice, string notice)
    {
        Choices = [choice];
        Notice = notice;
    }

    /// <summary>
    /// 記録した使ったファイルを選び直す窓の中身（ユーザ指示 2026-11-11：版を人が変えられない）。
    ///
    /// **今の記録を選んだ状態で開く**：確定を押しただけで記録が替わったり消えたりしない。今の記録が手元の一覧に無い
    /// （外した・消えたファイル、登録したフォルダの中の物、商品ごと手元に無い）ときは「今の記録のまま」を足してそれを選び、
    /// 一覧の上に1行で理由を出す。
    /// </summary>
    /// <param name="item">手元の商品。無ければ null（選べるのは「選ばない」と「今の記録のまま」だけ）。</param>
    /// <param name="preferNewVersion">
    /// 使ったものの更新の促し（used-updates）の「新しい版を選ぶ…」から開くとき。新しい版のファイル（控えの後に持った物）から
    /// 1つに決まるときだけそれを選んで開く。決まらなければ今の記録のまま開く（推定で入れない）
    /// </param>
    public static MemberFilePickViewModel ForRepick(ModificationMember member, string name, ItemRecord? item, bool preferNewVersion = false)
    {
        var owner = item ?? new ItemRecord
        {
            Id = member.ItemId,
            Booth = new BoothBlock { Name = name, FetchedAt = DateTimeOffset.MinValue },
            Local = new LocalBlock(),
        };
        var candidates = item is null ? [] : MemberFileOption.ForItem(item);
        var current = candidates.FirstOrDefault(option => option.Matches(member));

        var notice = string.Empty;
        MemberFileOption? keep = null;
        if (member.HasFile && current is null)
        {
            keep = new MemberFileOption
            {
                Label = "今の記録のまま：" + ModificationRowBuilder.FileTextOf(member, item),
                Detail = string.Empty,
                FileHash = member.FileHash,
                Package = member.Package,
                VariationId = member.VariationId,
                IsKeepCurrent = true,
            };
            notice = member.FileHash is null
                ? "今の記録は登録したフォルダの中のファイルで、一覧からは選べません。"
                : "記録しているファイルは手元にありません。";
        }

        List<MemberFileOption> options = [MemberFileOption.None];
        if (keep is not null)
        {
            options.Add(keep);
        }

        options.AddRange(candidates);
        var selected = keep ?? current ?? MemberFileOption.None;

        if (preferNewVersion && item?.Local.ContentUpdate is { } update)
        {
            var newer = candidates
                .Where(option => option.FileHash is { } hash && !update.FilesBefore.Contains(hash, StringComparer.OrdinalIgnoreCase))
                .ToList();

            // 同じ名前の unitypackage があれば同じ物の新しい版と見る（依存物と本体が同じ zip にある商品で、本体を選び直したいとき）
            var samePackage = member.Package is { } package
                ? newer.Where(option => string.Equals(Path.GetFileName(option.Package), Path.GetFileName(package), StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
            selected = newer.Count == 1 ? newer[0]
                : samePackage.Count == 1 ? samePackage[0]
                : selected;
        }

        return new MemberFilePickViewModel(new MemberFileChoice(owner, options, selected), notice);
    }

    /// <summary>一覧の上に出す1行（選び直しの窓で、今の記録が一覧に無いとき）。無ければ空。</summary>
    public string Notice { get; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    public IReadOnlyList<MemberFileChoice> Choices { get; }

    public bool HasChoices => Choices.Count > 0;

    /// <summary>行ごとに商品名を出すか。1件だけなら窓の見出しに名前があるので出さない。</summary>
    public bool ShowsNames { get; }

    /// <summary>
    /// この商品の記録する行（選んだファイルの数だけ、欄の順）。欄が無い商品（選べるファイルが無い）と、選ばなかった商品は、
    /// ファイルの欄が空の1行
    /// </summary>
    public IReadOnlyList<ModificationMember> MembersFor(string itemId, DateTimeOffset addedAt)
        => Choices.FirstOrDefault(choice => choice.Item.Id == itemId)?.ToMembers(addedAt)
            ?? [new ModificationMember { ItemId = itemId, AddedAt = addedAt }];

    /// <summary>
    /// 窓を出す代わりに答える口。**アプリでは null のまま**（窓を出す）。試験と ViewShot が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）
    /// </summary>
    internal static Func<MemberFilePickViewModel, bool>? Intercept { get; set; }

    /// <summary>改変の詳細で名前で足すときの窓。「追加」なら true。</summary>
    public static bool Ask(MemberFilePickViewModel model, string title, string headingText)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.PickMemberFilesDialog(model, title, headingText).ShowDialog() == true;

    /// <summary>選び直しの窓。「変更」なら true。</summary>
    public static bool AskRepick(MemberFilePickViewModel model, string headingText)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.PickMemberFilesDialog(model, MemberFileRepick.Title, headingText, repick: true).ShowDialog() == true;
}

/// <summary>
/// 使ったものの行の「使ったファイルを選び直す…」（ユーザ指示 2026-11-11）。改変の詳細と改変の画面の左の一覧の両方から使う。
/// 書くのは <see cref="UiCommand.ChangeModificationMemberFiles"/> 1本で、錠の中で今の行に当てる。
/// </summary>
public static class MemberFileRepick
{
    public const string Title = "使ったファイルを選び直す";

    /// <summary>窓を出して、選び直した物を書く。返すのは行の下に出す文（何もしなかったら null）と、失敗か。</summary>
    public static async Task<(string? Text, bool Failed)> RunAsync(
        AppServiceContainer services,
        string modificationId,
        ModificationMember member,
        string name,
        bool preferNewVersion = false)
    {
        var item = await services.Store.Items.LoadAsync(member.ItemId);
        var model = MemberFilePickViewModel.ForRepick(member, name, item, preferNewVersion);
        var choice = model.Choices[0];
        if (choice.Options.Count <= 1)
        {
            return ("選べるファイルが手元にありません。", false);
        }

        if (!MemberFilePickViewModel.AskRepick(model, $"「{name}」で使ったファイルを選びます。"))
        {
            return (null, false);
        }

        var picked = choice.Picked;

        // 選びを変えずに確定したなら書かない（足した日時や並びを触らずに済む。書く必要が無い）
        var unchanged = picked.Count == 0
            ? !member.HasFile
            : picked.Count == 1 && (picked[0].IsKeepCurrent || picked[0].Matches(member));
        if (unchanged)
        {
            return (null, false);
        }

        var result = await services.Commands.ExecuteAsync(new UiCommand.ChangeModificationMemberFiles(
            modificationId, member, picked.Select(option => option.ToFile()).ToList()));
        if (result is CommandResult.Failed failed)
        {
            return (failed.Message, true);
        }

        return (picked.Count == 0
            ? $"「{name}」の使ったファイルの記録を削除しました。"
            : $"「{name}」の使ったファイルを変更しました。", false);
    }
}
