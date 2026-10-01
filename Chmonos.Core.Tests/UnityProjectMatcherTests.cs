using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>Unity プロジェクトのフォルダを見て、手元のどの商品が入っているかを数える（#72）。</summary>
public sealed class UnityProjectMatcherTests
{
    private const string Project = @"C:\proj";

    private static Func<string, bool> Has(params string[] unityPaths)
    {
        var set = unityPaths.Select(path => Path.Combine(Project, path.Replace('/', Path.DirectorySeparatorChar)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    /// <summary>パスごとに決まった GUID を付ける（同じパスなら同じ GUID。別々の商品に同じ部品が入っているのと同じ形）。</summary>
    private static Dictionary<string, IReadOnlyList<UnityPackageAsset>> Items(params (string Id, string[] Paths)[] items)
        => items.ToDictionary(item => item.Id, item => (IReadOnlyList<UnityPackageAsset>)item.Paths.Select(path => new UnityPackageAsset(GuidOf(path), path)).ToList());

    private static string GuidOf(string path)
        => Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())));

    private static readonly IReadOnlyDictionary<string, string> NoGuids = new Dictionary<string, string>();

    /// <summary>GUID の表を渡さなければ空（ディスクを見ない）。</summary>
    private static IReadOnlyList<UnityProjectMatch> Match(
        IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>> items,
        Func<string, bool> exists,
        IReadOnlyDictionary<string, string>? guids = null)
        => UnityProjectMatcher.Match(Project, items, exists, () => guids ?? NoGuids);

    [Fact]
    public void 入っているファイルを数えて割合で並べる()
    {
        var items = Items(
            ("pen", ["Assets/nHaruka", "Assets/nHaruka/Pen.asset", "Assets/nHaruka/Pen.prefab", "Assets/nHaruka/Ink.mat"]),
            ("sweater", ["Assets/Kuuta/Sweater.fbx", "Assets/Kuuta/Sweater.mat"]));

        var matches = Match(items,
            Has("Assets/nHaruka/Pen.asset", "Assets/Kuuta/Sweater.fbx", "Assets/Kuuta/Sweater.mat"));

        Assert.Equal(["sweater", "pen"], matches.Select(match => match.ItemId));
        Assert.Equal((2, 2), (matches[0].Present, matches[0].Total));
        Assert.Equal((1, 3), (matches[1].Present, matches[1].Total));
    }

    [Fact]
    public void フォルダは数えない()
    {
        // 上のフォルダは同じ作者の別の商品とも重なるので、ファイルだけを見る
        var items = Items(("a", ["Assets/FUKA", "Assets/FUKA/Addon", "Assets/FUKA/Addon/Sound.wav"]));

        Assert.Empty(Match(items, Has("Assets/FUKA", "Assets/FUKA/Addon")));
    }

    [Fact]
    public void ほかの商品と共有しているファイルは数えない()
    {
        // lilToon を同梱したアバターが2つ。lilToon が入っているだけでは候補に出さない
        var items = Items(
            ("avatarA", ["Assets/lilToon/Shader/lts.shader", "Assets/A/A.fbx"]),
            ("avatarB", ["Assets/lilToon/Shader/lts.shader", "Assets/B/B.fbx"]));

        var matches = Match(items, Has("Assets/lilToon/Shader/lts.shader", "Assets/B/B.fbx"));

        var only = Assert.Single(matches);
        Assert.Equal("avatarB", only.ItemId);
        Assert.Equal((1, 1), (only.Present, only.Total));
    }

    [Fact]
    public void Packagesに入る物も数える()
    {
        // BlendShare は Packages/com.triturbo.blendshare に入る
        var items = Items(("blendshare", ["Packages/com.triturbo.blendshare/package.json", "Packages/com.triturbo.blendshare/Editor/A.cs"]));

        var only = Assert.Single(Match(items, Has("Packages/com.triturbo.blendshare/package.json")));
        Assert.Equal((1, 2), (only.Present, only.Total));
    }

    [Fact]
    public void 一つも無ければ候補に出さない()
        => Assert.Empty(Match(Items(("a", ["Assets/A/A.fbx"])), Has()));

    [Fact]
    public void パッケージを読めなかった商品は飛ばす()
        => Assert.Empty(Match(Items(("a", [])), Has("Assets/A/A.fbx")));

    // ---- GUID で探す（2026-09-29） ----

    [Fact]
    public void パスで無くてもGUIDで見つかれば入っていると数える()
    {
        // 利用者が Assets/Kuuta を Assets/Mine/Sweater へ移した。.meta の GUID は変わらない
        var items = Items(("sweater", ["Assets/Kuuta/Sweater.fbx", "Assets/Kuuta/Sweater.mat"]));
        var guids = new Dictionary<string, string>
        {
            [GuidOf("Assets/Kuuta/Sweater.fbx")] = "Assets/Mine/Sweater/Sweater.fbx",
            [GuidOf("Assets/Kuuta/Sweater.mat")] = "Assets/Mine/Sweater/Sweater.mat",
        };

        var only = Assert.Single(Match(items, Has("Assets/Mine/Sweater/Sweater.fbx", "Assets/Mine/Sweater/Sweater.mat"), guids: guids));

        Assert.Equal((2, 2), (only.Present, only.Total));
    }

    [Fact]
    public void パスでもGUIDでも無ければ数えない()
    {
        var items = Items(("a", ["Assets/A/A.fbx"]));
        var guids = new Dictionary<string, string> { ["ffffffffffffffffffffffffffffffff"] = "Assets/Other/A.fbx" };

        Assert.Empty(Match(items, Has("Assets/Other/A.fbx"), guids: guids));
    }

    [Fact]
    public void GUIDの表にあってもファイルが消えていれば数えない()
    {
        // .meta だけ残った物（Unity を閉じたまま本体を消した）
        var items = Items(("a", ["Assets/A/A.fbx"]));
        var guids = new Dictionary<string, string> { [GuidOf("Assets/A/A.fbx")] = "Assets/Moved/A.fbx" };

        Assert.Empty(Match(items, Has(), guids: guids));
    }

    [Fact]
    public void 同じGUIDを持つ二つの商品のファイルは数えない()
    {
        // 同梱の部品を、商品ごとに別の場所へ入れる作りでも、取り込めば同じ1つのファイルになる
        var shared = "0123456789abcdef0123456789abcdef";
        var items = new Dictionary<string, IReadOnlyList<UnityPackageAsset>>
        {
            ["avatarA"] = [new UnityPackageAsset(shared, "Assets/A/lilToon/lts.shader"), new UnityPackageAsset(GuidOf("a"), "Assets/A/A.fbx")],
            ["avatarB"] = [new UnityPackageAsset(shared, "Assets/B/lilToon/lts.shader"), new UnityPackageAsset(GuidOf("b"), "Assets/B/B.fbx")],
        };

        var matches = Match(items, Has("Assets/A/lilToon/lts.shader", "Assets/B/B.fbx"));

        var only = Assert.Single(matches);
        Assert.Equal("avatarB", only.ItemId);
        Assert.Equal((1, 1), (only.Present, only.Total));
    }

    [Fact]
    public void パスで全部見つかればGUIDの表を作らない()
    {
        // 表を作るのはプロジェクトの .meta を全部読む重い仕事なので、要るときだけ
        var items = Items(("a", ["Assets/A/A.fbx"]));
        var built = 0;

        UnityProjectMatcher.Match(Project, items, Has("Assets/A/A.fbx"), () =>
        {
            built++;
            return NoGuids;
        });

        Assert.Equal(0, built);
    }

    [Fact]
    public void GUIDの表は一度の照らし合わせで一度だけ作る()
    {
        var items = Items(("a", ["Assets/A/A.fbx", "Assets/A/B.fbx"]), ("b", ["Assets/B/C.fbx"]));
        var built = 0;

        UnityProjectMatcher.Match(Project, items, Has(), () =>
        {
            built++;
            return NoGuids;
        });

        Assert.Equal(1, built);
    }

    // ---- 「Unityで選択」で開くフォルダ ----

    private static Func<string, bool> HasFolder(params string[] unityPaths) => Has(unityPaths);

    private static UnityRootLocation Locate(
        string root, IReadOnlyList<UnityPackageAsset> assets, Func<string, bool> files, Func<string, bool> folders,
        IReadOnlyDictionary<string, string>? guids = null)
        => UnityProjectMatcher.LocateRoot(Project, root, assets, files, folders, () => guids ?? NoGuids);

    [Fact]
    public void 入り先にパスで入っていればそのまま開く()
    {
        var assets = Items(("a", ["Assets/FUKA", "Assets/FUKA/Addon/Sound.wav"]))["a"];

        var location = Locate("Assets/FUKA", assets, Has("Assets/FUKA/Addon/Sound.wav"), HasFolder("Assets/FUKA"));

        Assert.Equal(new UnityRootLocation("Assets/FUKA", false), location);
    }

    [Fact]
    public void 入り先のフォルダを丸ごと移していれば移した先を開く()
    {
        // 入り先のフォルダ自身も unitypackage に GUID を持つ。名前を変えても同じ GUID
        var assets = Items(("a", ["Assets/FUKA", "Assets/FUKA/Addon/Sound.wav"]))["a"];
        var guids = new Dictionary<string, string>
        {
            [GuidOf("Assets/FUKA")] = "Assets/Shops/FUKA音",
            [GuidOf("Assets/FUKA/Addon/Sound.wav")] = "Assets/Shops/FUKA音/Addon/Sound.wav",
        };

        var location = Locate("Assets/FUKA", assets, Has("Assets/Shops/FUKA音/Addon/Sound.wav"), HasFolder("Assets/Shops/FUKA音"), guids);

        Assert.Equal(new UnityRootLocation("Assets/Shops/FUKA音", true), location);
    }

    [Fact]
    public void フォルダがパッケージに無くてもファイルの今の場所から移した先を決める()
    {
        var assets = Items(("a", ["Assets/FUKA/Addon/Sound.wav", "Assets/FUKA/Addon/Sound.mat"]))["a"];
        var guids = new Dictionary<string, string>
        {
            [GuidOf("Assets/FUKA/Addon/Sound.wav")] = "Assets/Mine/Addon/Sound.wav",
            [GuidOf("Assets/FUKA/Addon/Sound.mat")] = "Assets/Mine/Addon/Sound.mat",
        };

        var location = Locate("Assets/FUKA", assets, Has("Assets/Mine/Addon/Sound.wav", "Assets/Mine/Addon/Sound.mat"), HasFolder(), guids);

        Assert.Equal(new UnityRootLocation("Assets/Mine", true), location);
    }

    [Fact]
    public void ファイルだけ別の所へ移していればそのファイルのあるフォルダを開く()
    {
        // 下の段まで同じでない（名前も変えた）ので、入り先に当たるフォルダは決められない。ファイルのある所を開く
        var assets = Items(("a", ["Assets/FUKA/Addon/Sound.wav"]))["a"];
        var guids = new Dictionary<string, string> { [GuidOf("Assets/FUKA/Addon/Sound.wav")] = "Assets/Sounds/撫で音.wav" };

        var location = Locate("Assets/FUKA", assets, Has("Assets/Sounds/撫で音.wav"), HasFolder(), guids);

        Assert.Equal(new UnityRootLocation("Assets/Sounds", true), location);
    }

    [Fact]
    public void パスでもGUIDでも見つからなければ入り先のまま()
    {
        var assets = Items(("a", ["Assets/FUKA/Addon/Sound.wav"]))["a"];

        var location = Locate("Assets/FUKA", assets, Has(), HasFolder());

        Assert.Equal(new UnityRootLocation("Assets/FUKA", false), location);
    }

    // ---- 頭の記号を消したプロジェクト（2026-09-29 に GUID へ置き換えた） ----
    // ショップが Assets の先頭に並べるために付けた記号（_FUKA）を、利用者が消していることがある（ユーザ指摘 2026-09-16）。
    // 前は記号を外した名前で読み替えていた。今は名前を変えた物の1つとして、.meta の GUID で見つける

    /// <summary>パッケージ：<c>Assets/_FUKA</c> に入る音の商品（フォルダも GUID を持つ）。</summary>
    private static readonly IReadOnlyList<UnityPackageAsset> FukaPackage = Items(("fuka",
        ["Assets/_FUKA", "Assets/_FUKA/Addon", "Assets/_FUKA/Addon/Sound.wav", "Assets/_FUKA/Addon/Sound.mat"]))["fuka"];

    /// <summary>
    /// <c>_FUKA</c> を <c>.meta</c> ごと <c>FUKA</c> に名前を変えたプロジェクトのディスク。
    /// GUID の表は本物の <see cref="UnityProjectGuids"/> で、<c>.meta</c> の一覧と中身だけを差し替えて作る。
    /// </summary>
    private sealed class RenamedProject
    {
        public readonly Dictionary<string, string> Metas = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Assets/FUKA.meta"] = GuidOf("Assets/_FUKA"),
            ["Assets/FUKA/Addon.meta"] = GuidOf("Assets/_FUKA/Addon"),
            ["Assets/FUKA/Addon/Sound.wav.meta"] = GuidOf("Assets/_FUKA/Addon/Sound.wav"),
            ["Assets/FUKA/Addon/Sound.mat.meta"] = GuidOf("Assets/_FUKA/Addon/Sound.mat"),
        };

        public Func<string, bool> Files => Has("Assets/FUKA/Addon/Sound.wav", "Assets/FUKA/Addon/Sound.mat");

        public Func<string, bool> Folders => HasFolder("Assets/FUKA", "Assets/FUKA/Addon");

        public Func<IReadOnlyDictionary<string, string>> Guids()
        {
            // 更新時刻は決まった値（時計に依らない）。表は変わった .meta だけ読み直すが、ここでは1回しか作らない
            var written = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);
            var table = new UnityProjectGuids(
                () => Metas.Keys.Select(path => new UnityMetaFile(path, written)).ToList(),
                path => Metas.GetValueOrDefault(path));
            return table.Current;
        }
    }

    [Fact]
    public void 頭の記号を消したプロジェクトでもGUIDで入っていると数える()
    {
        var project = new RenamedProject();

        var only = Assert.Single(UnityProjectMatcher.Match(
            Project, new Dictionary<string, IReadOnlyList<UnityPackageAsset>> { ["fuka"] = FukaPackage }, project.Files, project.Guids()));

        Assert.Equal((2, 2), (only.Present, only.Total));
    }

    [Fact]
    public void 頭の記号を消したプロジェクトでも既に全部入っていると分かる()
    {
        // 連続送りの前の「既に全部入っているか」（UnityImportQueue）は、この照らし合わせの Present == Total で決める。
        // Unity も GUID で同じ物と見て「Nothing to import!」を出す（2026-09-29 に Unity 2022.3 で確かめた）
        var project = new RenamedProject();

        var match = UnityProjectMatcher.Match(
            Project, new Dictionary<string, IReadOnlyList<UnityPackageAsset>> { ["fuka"] = FukaPackage }, project.Files, project.Guids()).Single();

        Assert.True(match.Total > 0 && match.Present == match.Total);
    }

    [Fact]
    public void 頭の記号を消したプロジェクトではUnityで選択が名前を変えたフォルダを開く()
    {
        var project = new RenamedProject();

        var location = UnityProjectMatcher.LocateRoot(
            Project, "Assets/_FUKA", FukaPackage, project.Files, project.Folders, project.Guids());

        Assert.Equal(new UnityRootLocation("Assets/FUKA", true), location);
    }

    [Fact]
    public void 頭の記号を消したフォルダのmetaが無くても中のファイルのGUIDで開く()
    {
        // エクスプローラで名前を変えると、フォルダの .meta（_FUKA.meta）は元の名前のまま残り、Unity が新しい GUID を振る。
        // 中のファイルの .meta はフォルダと一緒に動くので、そこから入り先に当たるフォルダを推す
        var project = new RenamedProject();
        project.Metas["Assets/FUKA.meta"] = "0123456789abcdef0123456789abcdef";

        var location = UnityProjectMatcher.LocateRoot(
            Project, "Assets/_FUKA", FukaPackage, project.Files, project.Folders, project.Guids());

        Assert.Equal(new UnityRootLocation("Assets/FUKA", true), location);
    }

    [Fact]
    public void 頭の記号を消してファイルのmetaも無ければ入っていると言わない()
    {
        // .meta を持たずに写した物は、Unity が新しい GUID を振るので別の物になる（Unity も取り込み直す）。前の名前の読み替えなら当たっていた
        var project = new RenamedProject();
        project.Metas.Clear();

        Assert.Empty(UnityProjectMatcher.Match(
            Project, new Dictionary<string, IReadOnlyList<UnityPackageAsset>> { ["fuka"] = FukaPackage }, project.Files, project.Guids()));
    }
}
