using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ViewShot;

/// <summary>
/// 網羅の撮影で、触ったファイルに関わる場面だけを選ぶ（<c>catalog --changed</c>。2026-10-05）。
///
/// 全部を撮ると数分かかる。画面を1つ直しただけなら、その画面の場面を撮り直し、残りは前の回の画像を写せば足りる（<c>--from</c>）。
/// 選び方は「ファイル名の語 → 画面のフォルダ」の表で決める。XAML と ViewModel は画面の名前をファイル名に持っている
/// （ImportView.xaml・ImportViewModel.cs）ので、語で当てれば足りる。
/// **当たらない App のファイルは全部を撮る。**色の表・共通の部品・主の窓のように、どの画面にも出る物を取り逃さないため。
/// Core は画面に出る文（失敗の文）を持つ物があるが、ほとんどは画面の外の処理なので、語で当たった物だけにする
/// </summary>
internal static class CatalogChanged
{
    /// <summary>全部の場面を撮るときの印。</summary>
    public const string All = "*";

    /// <summary>
    /// ファイル名（拡張子を除く）に含まれる語 → 画面のフォルダ（<see cref="Catalog"/> の Groups）。上から順に全部当てる（1つのファイルが2つの画面に関わることがある）。
    /// 主の窓・色の表・共通の部品は全部（<see cref="All"/>）
    /// </summary>
    private static readonly (string Word, string[] Folders)[] Words =
    [
        ("MainWindow", [All]), ("MainViewModel", [All]), ("ViewModelBase", [All]), ("App.xaml", [All]),
        ("ItemCardResources", ["01-search", "07-shops", "08-folder"]),
        ("SearchModule", ["02-search-modules"]),
        ("SavedSearch", ["01-search"]),
        ("Search", ["01-search", "02-search-modules"]),
        ("Calendar", ["01-search", "02-search-modules"]),
        ("SortDivider", ["01-search", "07-shops", "15-settings"]),
        ("Card", ["01-search", "07-shops", "08-folder"]),
        ("ItemFile", ["03-item"]), ("ItemPage", ["03-item"]), ("ItemChange", ["03-item"]), ("ItemRow", ["03-item"]),
        ("ItemView", ["03-item"]), ("ItemViewModel", ["03-item"]),
        ("Edit", ["04-edit"]),
        ("Import", ["05-import"]), ("Missing", ["05-import", "03-item"]),
        ("Resolve", ["06-resolve"]), ("Registration", ["06-resolve"]),
        ("Shop", ["07-shops"]),
        ("Folder", ["08-folder"]),
        ("Avatar", ["09-avatars", "03-item"]), ("BaseMember", ["09-avatars"]),
        ("Modification", ["10-modifications"]), ("Hub", ["10-modifications"]), ("Unity", ["10-modifications", "15-settings"]),
        ("PickMember", ["10-modifications"]), ("PickPackages", ["10-modifications"]),
        ("TagManage", ["11-tags"]), ("UserTag", ["11-tags", "03-item", "04-edit"]), ("MoveSub", ["11-tags"]), ("RenameTag", ["11-tags"]),
        ("AttributeManage", ["12-attributes"]), ("Attribute", ["12-attributes", "04-edit"]),
        ("Inbox", ["13-inbox"]),
        ("Stats", ["14-stats"]),
        ("Settings", ["15-settings"]), ("Backup", ["15-settings", "16-nav-bands"]),
        ("Nav", ["16-nav-bands"]), ("Unpack", ["16-nav-bands"]), ("BoothActivity", ["16-nav-bands", "05-import"]),
        ("Notice", ["17-dialogs", "16-nav-bands"]), ("Dialog", ["17-dialogs"]), ("FirstRun", ["17-dialogs"]), ("Choice", ["17-dialogs"]),
    ];

    /// <summary>App のうち、どの画面にも出る所。語に当たらなくても全部を撮る（色・部品の見た目・変換）。</summary>
    private static readonly string[] SharedApp = ["Chmonos.App/Themes/", "Chmonos.App/Controls/", "Chmonos.App/App.xaml"];

    public sealed record Selection(HashSet<string> Folders, HashSet<string> SceneNames, IReadOnlyList<string> Reasons, IReadOnlyList<string> Ignored);

    /// <summary>基準（コミット）から今の作業の木までに触ったファイル（まだコミットしていない物と、加えただけの新しいファイルも入れる）。</summary>
    public static IReadOnlyList<string> ChangedFiles(string? baseRevision)
    {
        var against = baseRevision ?? Git("merge-base HEAD master").Trim();
        var files = Git($"diff --name-only {against}")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(Git("ls-files --others --exclude-standard").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return files;
    }

    public static Selection Select(IEnumerable<string> files, IReadOnlyList<Scene> scenes)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var reasons = new List<string>();
        var ignored = new List<string>();
        var known = scenes.Select(scene => scene.Name).ToHashSet(StringComparer.Ordinal);
        var root = Git("rev-parse --show-toplevel").Trim();

        foreach (var file in files.Select(file => file.Replace('\\', '/')))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (file.StartsWith("tools/ViewShot/", StringComparison.Ordinal))
            {
                // 場面のファイルは、そこに書いてある場面の名前（文字列）で当てる。台そのもの（Stage・Program）を直したら全部
                if (stem.StartsWith("Scenes", StringComparison.Ordinal) && File.Exists(Path.Combine(root, file)))
                {
                    var inFile = Regex.Matches(File.ReadAllText(Path.Combine(root, file)), "\"([a-z0-9-]+)\"").Select(match => match.Groups[1].Value)
                        .Where(known.Contains).ToList();
                    names.UnionWith(inFile);
                    reasons.Add($"{file} → 場面 {inFile.Count}");
                }
                // Backdoor は入れ方を足すことが多く（足した入れ方を使う場面は、場面のファイルの方で当たる）、そのたびに全部を撮るのは重い。
                // 今ある入れ方の中身を変えたときは --only で選ぶ
                else if (stem is "Stage" or "SceneContext" or "Program" or "Fake" or "Isolation")
                {
                    folders.Add(All);
                    reasons.Add($"{file} → 全部（台そのもの）");
                }
                else
                {
                    ignored.Add(file);
                }

                continue;
            }

            var isApp = file.StartsWith("Chmonos.App/", StringComparison.Ordinal);
            var isCore = file.StartsWith("Chmonos.Core/", StringComparison.Ordinal);
            if (!isApp && !isCore)
            {
                ignored.Add(file);
                continue;
            }

            var hits = Words.Where(word => (stem + Path.GetExtension(file)).Contains(word.Word, StringComparison.Ordinal))
                .SelectMany(word => word.Folders).Distinct().ToList();
            if (hits.Count == 0 && isApp && (SharedApp.Any(prefix => file.StartsWith(prefix, StringComparison.Ordinal)) || file.EndsWith(".xaml", StringComparison.Ordinal)
                || file.Contains("/ViewModels/", StringComparison.Ordinal) || file.Contains("/Views/", StringComparison.Ordinal)))
            {
                hits = [All];
            }

            if (hits.Count == 0)
            {
                ignored.Add(file);
                continue;
            }

            folders.UnionWith(hits);
            reasons.Add($"{file} → {string.Join("・", hits.Select(hit => hit == All ? "全部" : hit))}");
        }

        return new Selection(folders, names, reasons, ignored);
    }

    private static string Git(string arguments)
    {
        var start = new ProcessStartInfo("git", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var git = Process.Start(start) ?? throw new InvalidOperationException("git を起動できませんでした。");
        var output = git.StandardOutput.ReadToEnd();
        var errors = git.StandardError.ReadToEnd();
        git.WaitForExit();
        return git.ExitCode == 0
            ? output
            : throw new ArgumentException($"git {arguments} が失敗しました：{errors.Trim()}");
    }
}
