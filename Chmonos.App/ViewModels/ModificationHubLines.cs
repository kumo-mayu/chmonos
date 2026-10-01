using System.Windows;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 改変の画面の左の一覧の1行（仮想化の単位）。
///
/// 見出し（プロジェクト・アバター）→ 改変 → 使ったもの、の3段の入れ子を ItemsControl に入れていた頃は、
/// 見えていない行まで全部作っていた（改変300件で、改変の見方へ切り替えると約8秒固まった。2026-09-24）。
/// 3段を1本に並べて仮想化し、入れ子の字下げ・縦線・行の間は、行ごとに持つ余白と線で描き分ける。
///
/// 数字は入れ子の見た目（ModificationHubView.xaml の HubProjectGroup・HubModificationRow）と同じ：
/// 見出しの中身は左 13 の縦線の右 8、改変の中身は左 9 の縦線の右 26。見出しの下の間 8、改変の上 4・下 2、使ったものの前後 4。
/// </summary>
public abstract class HubLine : ViewModelBase
{
    private Thickness _outerMargin;
    private Thickness _innerMargin;

    /// <summary>見出しの中の行か（見出しの縦線を左に引く）。</summary>
    public bool InGroup { get; init; }

    /// <summary>見出しの縦線を引く器の余白（下は、見出しの中の最後の行のあとの間）。</summary>
    public Thickness OuterMargin
    {
        get => _outerMargin;
        set => SetField(ref _outerMargin, value);
    }

    /// <summary>行そのものの余白（縦線の内側）。</summary>
    public Thickness InnerMargin
    {
        get => _innerMargin;
        set => SetField(ref _innerMargin, value);
    }

    /// <summary>見出しの縦線（見出しの中の行だけ）。</summary>
    public Thickness GroupRail => InGroup ? new Thickness(1, 0, 0, 0) : default;

    public Thickness GroupRailPadding => InGroup ? new Thickness(8, 0, 0, 0) : default;
}

/// <summary>プロジェクトの見出し。</summary>
public sealed class HubProjectLine(HubProjectGroup group) : HubLine
{
    public HubProjectGroup Group { get; } = group;
}

/// <summary>アバターの見出し。</summary>
public sealed class HubAvatarLine(HubAvatarGroup group) : HubLine
{
    public HubAvatarGroup Group { get; } = group;
}

/// <summary>改変1件の見出しの行（使ったものは続く行）。</summary>
public sealed class HubModLine(HubModificationRow row) : HubLine
{
    public HubModificationRow Row { get; } = row;
}

/// <summary>使ったもの1件。改変の縦線の内側に置く。</summary>
public sealed class HubMemberLine(HubMemberRow row) : HubLine
{
    private Thickness _railMargin;

    public HubMemberRow Row { get; } = row;

    /// <summary>改変の縦線を引く器の余白（下は、改変の中の最後の行のあとの間）。</summary>
    public Thickness RailMargin
    {
        get => _railMargin;
        set => SetField(ref _railMargin, value);
    }
}

/// <summary>一覧の下の余白（入れ子の頃は一覧の下の余白 12 がいちばん下まで流したときだけ見えた）。</summary>
public sealed class HubEndLine
{
    public static HubEndLine Instance { get; } = new();
}

/// <summary>
/// 左の一覧の入れ子（<see cref="ModificationHubViewModel.Groups"/>）から、平らな行を組む。
/// 行は元の行（見出し・改変・使ったもの）ごとに1つ作って使い回す——開く・畳むのたびに作り直すと、
/// 見えている行の部品が作り直される
/// </summary>
internal sealed class HubLineBuilder
{
    // 入れ子の見た目の余白（ModificationHubView.xaml）
    private const double GroupRailLeft = 13;
    private const double GroupBottom = 8;
    private const double GroupChildrenTop = 2;
    private const double ModTop = 4;
    private const double ModBottom = 2;
    private const double MembersTop = 4;
    private const double MembersBottom = 4;
    private const double ModRailLeft = 9;

    private readonly Dictionary<object, HubLine> _lines = new(ReferenceEqualityComparer.Instance);

    /// <summary>見出しや改変の行を作り直したとき（読み直し・見方の切り替え）は、前の行を捨てる。</summary>
    public void Reset() => _lines.Clear();

    public List<object> Build(IEnumerable<object> groups)
    {
        var target = new List<object>();
        foreach (var entry in groups)
        {
            switch (entry)
            {
                case HubProjectGroup project:
                    AddGroup(target, Line(project, () => new HubProjectLine(project)), project.IsExpanded, project.Modifications);
                    break;
                case HubAvatarGroup avatar:
                    AddGroup(target, Line(avatar, () => new HubAvatarLine(avatar)), avatar.IsExpanded, avatar.Modifications);
                    break;
                case HubModificationRow mod:
                    AddMod(target, mod, inGroup: false, lastInGroup: false);
                    break;
            }
        }

        target.Add(HubEndLine.Instance);
        return target;
    }

    private void AddGroup(List<object> target, HubLine head, bool expanded, IReadOnlyList<HubModificationRow> mods)
    {
        // 開いた見出しの中身は、見出しの 2 下から縦線の中に入る。中身が無ければ、その 2 と見出しの下の間だけが残る
        head.InnerMargin = new Thickness(0, 0, 0, !expanded ? GroupBottom : mods.Count > 0 ? GroupChildrenTop : GroupChildrenTop + GroupBottom);
        target.Add(head);

        if (!expanded)
        {
            return;
        }

        for (var i = 0; i < mods.Count; i++)
        {
            AddMod(target, mods[i], inGroup: true, lastInGroup: i == mods.Count - 1);
        }
    }

    private void AddMod(List<object> target, HubModificationRow mod, bool inGroup, bool lastInGroup)
    {
        var line = (HubModLine)Line(mod, () => new HubModLine(mod) { InGroup = inGroup });
        var showsMembers = mod.IsExpanded && mod.Members.Count > 0;

        // 開いているのに中身の無い改変は、中身の器の上下の間（4＋4）だけが残る
        var bottom = !mod.IsExpanded ? ModBottom : showsMembers ? MembersTop : MembersTop + MembersBottom + ModBottom;
        line.InnerMargin = new Thickness(0, ModTop, 0, bottom);
        line.OuterMargin = GroupOuter(inGroup, closesGroup: lastInGroup && !showsMembers);
        target.Add(line);

        if (!showsMembers)
        {
            return;
        }

        for (var i = 0; i < mod.Members.Count; i++)
        {
            var member = mod.Members[i];
            var last = i == mod.Members.Count - 1;
            var memberLine = (HubMemberLine)Line(member, () => new HubMemberLine(member) { InGroup = inGroup });

            // 改変の最後の使ったものの下に、中身の器の下の間と改変の下の間が続く（どちらも改変の縦線の外）
            memberLine.RailMargin = new Thickness(ModRailLeft, 0, 0, last ? MembersBottom + ModBottom : 0);
            memberLine.OuterMargin = GroupOuter(inGroup, closesGroup: lastInGroup && last);
            target.Add(memberLine);
        }
    }

    /// <summary>見出しの縦線の器。見出しの中の最後の行では、縦線のあとに見出しの下の間を空ける。</summary>
    private static Thickness GroupOuter(bool inGroup, bool closesGroup)
        => inGroup ? new Thickness(GroupRailLeft, 0, 0, closesGroup ? GroupBottom : 0) : default;

    private HubLine Line(object row, Func<HubLine> create)
    {
        if (!_lines.TryGetValue(row, out var line))
        {
            line = create();
            _lines[row] = line;
        }

        return line;
    }
}
