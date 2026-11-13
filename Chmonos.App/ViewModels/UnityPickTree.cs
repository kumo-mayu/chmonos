using System.Windows;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>木の行の種類。</summary>
public enum UnityPickRowKind
{
    /// <summary>節の見出し（unitypackage。画像と両方あるときだけ。画像の側には置かない）。</summary>
    Section,

    /// <summary>Unity の中のフォルダ。</summary>
    Folder,

    /// <summary>選べる行（画像1つ・unitypackage 1つ）。</summary>
    Leaf,
}

/// <summary>
/// 「Unityで選択」の木の1行。選べるのは葉（<see cref="UnityPickRowKind.Leaf"/>）だけで、葉は元の候補（<see cref="Image"/> か <see cref="Package"/>）をそのまま持つ。
/// 並べ替えた後の位置から元の一覧を引き直すと、木の並びと元の並びがずれたときに黙って別の物を選ぶので、位置では引かない
/// </summary>
public sealed class UnityPickRow : ViewModelBase
{
    private bool _isExpanded;

    public required UnityPickRowKind Kind { get; init; }

    /// <summary>行に出す名前（フォルダは1本道を畳んだ「A/B」、画像はファイル名、unitypackage は包みの名前）。</summary>
    public required string Name { get; init; }

    /// <summary>名前の右に小さく出す札。画像は見つかり方（同じ画像・名前が同じ）、unitypackage は入っている zip の名前。</summary>
    public string Tag { get; init; } = string.Empty;

    public bool HasTag => Tag.Length > 0;

    /// <summary>乗せたときの吹き出し。画像は今までの平らな行と同じ文（見つかり方と Unity の中の場所）と、商品の側の画像の名前。</summary>
    public string ToolTip { get; internal set; } = string.Empty;

    public int Depth { get; internal set; }

    public UnityPickRow? Parent { get; internal set; }

    public List<UnityPickRow> Children { get; } = [];

    public UnityImageMatch? Image { get; init; }

    public UnityPackageEntry? Package { get; init; }

    /// <summary>下にある葉の数（フォルダ・節）。</summary>
    public int Count { get; internal set; }

    public bool IsLeaf => Kind == UnityPickRowKind.Leaf;

    public bool CanExpand => !IsLeaf;

    public bool IsSection => Kind == UnityPickRowKind.Section;

    public string CountText => IsLeaf ? string.Empty : Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>字下げ。フォルダビューの木と同じ1段16。</summary>
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ToggleName));
            }
        }
    }

    /// <summary>開閉の三角の読み上げの名前（フォルダビューの木と同じ言い方）。</summary>
    public string ToggleName => Controls.ExpandToggle.NameFor(Name, IsExpanded);

    /// <summary>行の読み上げの名前。葉は見つかり方まで、フォルダは件数まで読ませる（見た目で分かる事を声でも分かるように）。</summary>
    public string AutomationName => IsLeaf
        ? (HasTag ? $"{Name}、{Tag}" : Name)
        : $"{Name}、{Count} 件";
}

/// <summary>
/// 「Unityで選択」で、プロジェクトの中で見つかった画像と unitypackage を木に組む（ユーザ判断 2026-11-13：フォルダツリーにする形）。
/// 前は Unity の中のパスのまま1行ずつ平らに並べていた。テクスチャの多い商品では同じフォルダの下の画像が何十行も並び、
/// 長いパスの頭が同じなので見分けるのは末尾だけだった。
///
/// WPF に触れない（試験で組み立て・開き方・キーの動きを確かめる）。窓は <see cref="UnityPickDialogViewModel"/>
/// </summary>
public static class UnityPickTree
{
    /// <summary>
    /// 平らな一覧の窓（<see cref="Services.ListChoice"/>）で、流さずに全部見える行の数。
    /// 行は2段（名前と補足）で約44（字13の行約17・字11の行約15・余白と枠約12）、一覧の高さの上限は360なので8行。
    /// これ以下なら見渡せるので木にしても行が増えるだけになる。超えると流さないと見えない行が出るので、木でまとめる
    /// </summary>
    public const int FlatLimit = 8;

    /// <summary>
    /// 木の窓で、開いた時に見せておく行の数の目安。行は1段で26（中身22・行の余白と枠4。撮って測った）、一覧の高さの上限は420なので16行。
    /// これを超える分は畳んで、フォルダの名前と件数で見渡せるようにする
    /// </summary>
    public const int InitialRows = 16;

    /// <summary>
    /// 平らな窓のままにするか。候補が <see cref="FlatLimit"/> 以下（全部が見える）か、画像が1つ以下（木にするフォルダが無い）なら平ら。
    /// 画像が1つのフォルダに収まっていても、数が多ければ木にする——頭のフォルダを1行にまとめ、行には名前だけを出す方が読める
    /// </summary>
    public static bool IsFlat(int images, int packages) => images <= 1 || images + packages <= FlatLimit;

    /// <summary>木の根の行を組む。開き方は <see cref="ExpandInitially"/> で決める。</summary>
    public static IReadOnlyList<UnityPickRow> Build(IReadOnlyList<UnityImageMatch> images, IReadOnlyList<UnityPackageEntry> packages)
    {
        var imageRoots = BuildFolders(images);
        var packageLeaves = packages
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => System.IO.Path.GetFileName(entry.ZipPath), StringComparer.OrdinalIgnoreCase)
            .Select(entry => new UnityPickRow
            {
                Kind = UnityPickRowKind.Leaf,
                Name = entry.Name,
                Tag = System.IO.Path.GetFileName(entry.ZipPath),
                ToolTip = $"{entry.Name}\n{System.IO.Path.GetFileName(entry.ZipPath)}",
                Package = entry,
            })
            .ToList();

        List<UnityPickRow> roots;
        if (packageLeaves.Count == 0)
        {
            roots = imageRoots;
        }
        else if (imageRoots.Count == 0)
        {
            roots = packageLeaves;
        }
        else
        {
            // 両方あるときは unitypackage だけを節に分ける。unitypackage は Unity の中のフォルダではないので、画像の木に混ぜると入り先の
            // フォルダに見える。画像の側には節の行を置かない（ユーザ判断 2026-11-13「1-b」：節の行と1段の字下げが増え、
            // 候補が9件の形で前の9行が17行になった。画像は Assets/… のフォルダの行で始まるので、節が無くても画像と分かる）
            var packageSection = new UnityPickRow { Kind = UnityPickRowKind.Section, Name = "unitypackage" };
            packageSection.Children.AddRange(packageLeaves);
            roots = [.. imageRoots, packageSection];
        }

        foreach (var root in roots)
        {
            Settle(root, parent: null, depth: 0);
        }

        return roots;
    }

    /// <summary>
    /// 開いた時の形。幅優先で、開いても見える行が <see cref="InitialRows"/> に収まるフォルダを開く。
    /// 兄弟の無い行（根が1つ・1本道）は収まらなくても開く——畳んでも見渡せる物が増えず、押す手間だけが増える
    /// </summary>
    public static void ExpandInitially(IReadOnlyList<UnityPickRow> roots)
    {
        var visible = roots.Count;
        var queue = new Queue<IReadOnlyList<UnityPickRow>>();
        queue.Enqueue(roots);
        while (queue.Count > 0)
        {
            var siblings = queue.Dequeue();
            foreach (var row in siblings.Where(row => row.CanExpand))
            {
                if (siblings.Count == 1 || visible + row.Children.Count <= InitialRows)
                {
                    row.IsExpanded = true;
                    visible += row.Children.Count;
                    queue.Enqueue(row.Children);
                }
            }
        }
    }

    /// <summary>見えている行を上から順に（開いている行の子だけをたどる）。</summary>
    public static List<UnityPickRow> Visible(IReadOnlyList<UnityPickRow> roots)
    {
        var rows = new List<UnityPickRow>();
        void Walk(IEnumerable<UnityPickRow> level)
        {
            foreach (var row in level)
            {
                rows.Add(row);
                if (row.IsExpanded)
                {
                    Walk(row.Children);
                }
            }
        }

        Walk(roots);
        return rows;
    }

    /// <summary>最初の葉（開いた時に選んでおく行）。畳んだ中にしか無ければ null。</summary>
    public static UnityPickRow? FirstVisibleLeaf(IReadOnlyList<UnityPickRow> roots) => Visible(roots).FirstOrDefault(row => row.IsLeaf);

    /// <summary>
    /// 画像を Unity の中のフォルダで木にする。区切りは「/」（Unity のパスの書き方。念のため「\」も同じに扱う）。
    /// 1本道（子がフォルダ1つだけで画像の無い段）は1行に畳むので、全部に共通の頭（Assets/作者/商品）は根の1行になる
    /// </summary>
    private static List<UnityPickRow> BuildFolders(IReadOnlyList<UnityImageMatch> images)
    {
        var top = new FolderNode(string.Empty);
        foreach (var image in images)
        {
            var parts = image.ProjectPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var node = top;
            foreach (var part in parts[..^1])
            {
                if (!node.Folders.TryGetValue(part, out var child))
                {
                    child = new FolderNode(part);
                    node.Folders.Add(part, child);
                }

                node = child;
            }

            node.Images.Add((parts[^1], image));
        }

        return ToRows(top);
    }

    private static List<UnityPickRow> ToRows(FolderNode node)
    {
        var rows = new List<UnityPickRow>();
        foreach (var folder in node.Folders.Values.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
        {
            // 1本道を畳む：画像が無く子のフォルダが1つだけなら、名前をつないで下へ進む
            var name = folder.Name;
            var at = folder;
            while (at.Images.Count == 0 && at.Folders.Count == 1)
            {
                at = at.Folders.Values.First();
                name = $"{name}/{at.Name}";
            }

            var row = new UnityPickRow { Kind = UnityPickRowKind.Folder, Name = name };
            row.Children.AddRange(ToRows(at));
            rows.Add(row);
        }

        // フォルダを先、画像を後（エクスプローラ・Unity のプロジェクトタブと同じ並び）
        rows.AddRange(node.Images
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new UnityPickRow
            {
                Kind = UnityPickRowKind.Leaf,
                Name = entry.Name,
                Tag = KindText(entry.Image.Kind),
                ToolTip = $"{ItemUnityActions.ImageRowText(entry.Image)}\n{entry.Image.SourceName}",
                Image = entry.Image,
            }));
        return rows;
    }

    /// <summary>見つかり方の語。平らな行の頭（<see cref="ItemUnityActions.ImageRowText"/>）と同じ語にする。</summary>
    internal static string KindText(UnityImageMatchKind kind) => kind == UnityImageMatchKind.SameImage ? "同じ画像" : "名前が同じ";

    /// <summary>親・深さ・件数を入れる。フォルダのフルパスは吹き出しに出す（畳んだ名前だけでは場所が分からない）。</summary>
    private static int Settle(UnityPickRow row, UnityPickRow? parent, int depth)
    {
        row.Parent = parent;
        row.Depth = depth;
        if (row.Kind == UnityPickRowKind.Folder)
        {
            row.ToolTip = parent?.Kind == UnityPickRowKind.Folder ? $"{parent.ToolTip}/{row.Name}" : row.Name;
        }

        if (row.IsLeaf)
        {
            return 1;
        }

        row.Count = row.Children.Sum(child => Settle(child, row, depth + 1));
        return row.Count;
    }

    private sealed class FolderNode(string name)
    {
        public string Name { get; } = name;

        public Dictionary<string, FolderNode> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Name, UnityImageMatch Image)> Images { get; } = [];
    }
}
