// AvatarEvalBench
//
// 対応アバター検出の「現行」と「修正案」を、正解付きの試験データに当てて比べる台。
//
// 試験データは第三者（テストに協力してくれた友人）のライブラリなので、**リポジトリの外**に置く。
// 既定は %LOCALAPPDATA%\BoothAssetManager-eval（store\ に写し、labels.json に正解）。
//
// 正解は「商品×アバター」の組ごとに 対応／参考／違う／不明。
//   適合率 = 対応と数えた組のうち、正解が「対応」だった割合（参考・違う・未ラベルは誤り、不明は数えない）
//   再現率 = 正解が「対応」の組のうち、対応と数えられた割合
// 「対応と数える」は、検索の対応アバター絞り込みに出るかどうか。要確認を数えるかも案ごとに違う。
//
// 案は IVariant を足すだけで並ぶ。本体（BoothAssetManager.Core）は読むだけで書き換えない。
//
// 使い方:
//   dotnet run --project experiments/AvatarEvalBench -- [評価フォルダ] [--show <案の名前の一部>] [--limit N]

using System.Text.Json;
using System.Text.RegularExpressions;
using AvatarEvalBench;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// 値を取るオプション（--show 平文）の値を、評価フォルダと取り違えないようにする
var optionValues = args.Select((arg, index) => (arg, index))
    .Where(pair => pair.arg is "--show" or "--limit" or "--exclude")
    .Select(pair => pair.index + 1)
    .ToHashSet();
var evalDir = args.Where((arg, index) => !arg.StartsWith("--") && !optionValues.Contains(index)).FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("BOOTH_EVAL_DIR")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoothAssetManager-eval");
var showIndex = Array.IndexOf(args, "--show");
var show = showIndex >= 0 && showIndex + 1 < args.Length ? args[showIndex + 1] : null;
var limitIndex = Array.IndexOf(args, "--limit");
var limit = limitIndex >= 0 && int.TryParse(args[limitIndex + 1], out var parsedLimit) ? parsedLimit : 25;

var context = EvalContext.Load(evalDir);

// --exclude 123,456：その商品を測らない。1商品に巨大な一覧があると数字がそれに引きずられるので、
// 除いた場合と並べて読むために使う
var excludeIndex = Array.IndexOf(args, "--exclude");
if (excludeIndex >= 0 && excludeIndex + 1 < args.Length)
{
    foreach (var id in args[excludeIndex + 1].Split(','))
    {
        context.Labels.Remove(id.Trim());
    }
}
Console.WriteLine($"試験データ: 商品 {context.Items.Count} 件／正解の組 {context.Labels.Sum(label => label.Value.Avatars.Count)}（うち対応 {context.PositiveCount}）");
Console.WriteLine();

IVariant[] variants =
[
    new StoredVariant(confirmedOnly: false),
    new StoredVariant(confirmedOnly: true),
    new RerunCurrentVariant(),
    new ProposalVariant(ProposalOptions.Exact),
    new ProposalVariant(ProposalOptions.Exact with { PlainDescription = true }),
    new ProposalVariant(ProposalOptions.Exact with
    {
        PlainDescription = true,
        ExtraSupportHeadings = ["プリセット", "位置設定済", "設定済みアバター", "セットアップ済"],
    }),
];

Console.WriteLine($"{"案",-44} {"適合率",7} {"再現率",7} {"F1",6}  正/誤(参考・違う・未ラベル)/漏れ");
foreach (var variant in variants)
{
    var prediction = await variant.RunAsync(context);
    var score = Score.Of(context, prediction);
    Console.WriteLine($"{variant.Name,-44} {score.Precision,7:P1} {score.Recall,7:P1} {score.F1,6:F3}  {score.TruePositive}/{score.FalseReference}・{score.FalseWrong}・{score.FalseUnlabeled}/{score.FalseNegative}");

    if (show is not null && variant.Name.Contains(show, StringComparison.OrdinalIgnoreCase))
    {
        score.PrintExamples(context, limit);
    }
}

// 汎用の判定。正解に汎用の印（true/false）が付いている商品だけで測る
var universalLabels = context.Labels.Values.Where(label => label.Universal is not null).ToList();
if (universalLabels.Count > 0)
{
    var predicted = universalLabels.Count(label => UniversalRule.Looks(context.Items[label.ItemId], context) == label.Universal);
    Console.WriteLine($"\n汎用の手掛かり（正規表現）: 正解 {predicted}/{universalLabels.Count}");
}

namespace AvatarEvalBench
{
    /// <summary>正解1商品ぶん。</summary>
    public sealed record ItemLabel(string ItemId, bool? Universal, IReadOnlyDictionary<string, string> Avatars);

    /// <summary>試験データ一式。読むだけ。</summary>
    public sealed class EvalContext
    {
        public required string StoreDir { get; init; }

        public required Dictionary<string, ItemRecord> Items { get; init; }

        public required Dictionary<string, ItemLabel> Labels { get; init; }

        public required AvatarRegistry Registry { get; init; }

        public required AppSettings Settings { get; init; }

        public int PositiveCount => Labels.Values.Sum(label => label.Avatars.Count(pair => pair.Value == "対応"));

        public string? HtmlOf(string itemId)
        {
            var path = Path.Combine(StoreDir, "items", itemId + ".h2.html");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public static EvalContext Load(string evalDir)
        {
            var storeDir = Path.Combine(evalDir, "store");
            var items = Directory.EnumerateFiles(Path.Combine(storeDir, "items"), "*.json")
                .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^\d+\.json$"))
                .Select(path => JsonStore.Read<ItemRecord>(path)!)
                .ToDictionary(item => item.Id, StringComparer.Ordinal);

            var labelPath = Path.Combine(evalDir, "labels.json");
            if (!File.Exists(labelPath))
            {
                labelPath = Path.Combine(evalDir, "labels.draft.json");
            }

            using var document = JsonDocument.Parse(File.ReadAllText(labelPath));
            var labels = new Dictionary<string, ItemLabel>(StringComparer.Ordinal);
            foreach (var element in document.RootElement.GetProperty("items").EnumerateArray())
            {
                var id = element.GetProperty("itemId").GetString()!;
                bool? universal = element.TryGetProperty("universal", out var u) && u.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? u.GetBoolean()
                    : null;
                var avatars = element.GetProperty("avatars").EnumerateObject()
                    .ToDictionary(pair => pair.Name, pair => pair.Value.GetString()!, StringComparer.Ordinal);
                labels[id] = new ItemLabel(id, universal, avatars);
            }

            return new EvalContext
            {
                StoreDir = storeDir,
                Items = items,
                Labels = labels,
                Registry = JsonStore.Read<AvatarRegistry>(Path.Combine(storeDir, "avatar-registry.json")) ?? new AvatarRegistry(),
                Settings = JsonStore.Read<AppSettings>(Path.Combine(storeDir, "settings.json")) ?? new AppSettings(),
            };
        }
    }

    /// <summary>1つの案。商品ID → 「対応」と数えるアバターIDの集合を返す。</summary>
    public interface IVariant
    {
        string Name { get; }

        Task<Dictionary<string, HashSet<string>>> RunAsync(EvalContext context);
    }

    /// <summary>友人のPCで検出した結果そのもの（保存されている links）。</summary>
    public sealed class StoredVariant(bool confirmedOnly) : IVariant
    {
        public string Name => confirmedOnly ? "保存済み・確定のみ" : "保存済み・要確認も数える（今の絞り込み）";

        public Task<Dictionary<string, HashSet<string>>> RunAsync(EvalContext context)
            => Task.FromResult(context.Items.Values.ToDictionary(
                item => item.Id,
                item => item.Local.Avatars
                    .Where(link => !link.Rejected && (!confirmedOnly || link.Confirmed))
                    .Select(link => link.AvatarItemId)
                    .ToHashSet(StringComparer.Ordinal)));
    }

    /// <summary>
    /// 今の Core の検出を、試験データの**写しの上で**走らせ直す。通信はしない（client なし）。
    /// 登録簿は友人のもの（汚れた別名を含む）から始まるので、「今の不具合がどれだけ効いているか」が出る。
    /// </summary>
    public sealed class RerunCurrentVariant : IVariant
    {
        public string Name => "現行の検出を再実行・要確認も数える";

        public async Task<Dictionary<string, HashSet<string>>> RunAsync(EvalContext context)
        {
            var temp = Path.Combine(Path.GetTempPath(), "avatar-eval-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDirectory(context.StoreDir, temp);
            try
            {
                var store = new DataStore(new AppPaths(temp));
                await new AvatarService(store, context.Settings, client: null).DetectAsync();
                var loaded = await store.Items.LoadAllAsync();
                return loaded.Items.ToDictionary(
                    item => item.Id,
                    item => item.Local.Avatars.Where(link => !link.Rejected).Select(link => link.AvatarItemId).ToHashSet(StringComparer.Ordinal));
            }
            finally
            {
                Directory.Delete(temp, recursive: true);
            }
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            }

            foreach (var directory in Directory.EnumerateDirectories(from))
            {
                CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
            }
        }
    }

    /// <summary>修正案の切り替え。1つずつ足して効き目を見られるようにする。</summary>
    public sealed record ProposalOptions
    {
        /// <summary>タグは完全一致、種類名は「長い名前の一部」と「英単語の途中」を除いた包含。</summary>
        public bool ExactMatch { get; init; }

        /// <summary>別名は、そのアバター自身の正式名に出てくるものだけを使う（覚えた別名を捨てる）。</summary>
        public bool DeriveAliases { get; init; }

        /// <summary>見出しの無い商品・平文の説明文にある対応リストも読む。</summary>
        public bool PlainDescription { get; init; }

        /// <summary>説明文のリンク（要確認）を対応と数えない。</summary>
        public bool ExcludeUnconfirmed { get; init; } = true;

        /// <summary>
        /// 対応を宣言する見出しとして追加で読む語。設定の既定（対応アバター・Supported など）に足す。
        /// 試験データでは「プリセットについて」「位置設定済アバター」の下にも対応の一覧があった。
        /// </summary>
        public IReadOnlyList<string> ExtraSupportHeadings { get; init; } = [];

        public static ProposalOptions Exact { get; } = new() { ExactMatch = true, DeriveAliases = true };
    }

    /// <summary>
    /// 修正案。Core の部品（説明文の節の読み取り・正規化・アバター判定）はそのまま使い、
    /// 照合と別名の持ち方だけを差し替える。
    /// </summary>
    public sealed class ProposalVariant(ProposalOptions options) : IVariant
    {
        public string Name => "案：" + string.Join("＋", new[]
        {
            options.ExactMatch ? "完全一致" : null,
            options.DeriveAliases ? "別名導き直し" : null,
            options.PlainDescription ? "平文" : null,
            options.ExtraSupportHeadings.Count > 0 ? "見出し語追加" : null,
            options.ExcludeUnconfirmed ? "要確認除外" : null,
        }.Where(part => part is not null));

        public Task<Dictionary<string, HashSet<string>>> RunAsync(EvalContext context)
        {
            var names = NameTable.Build(context.Registry, options.DeriveAliases);
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var item in context.Items.Values)
            {
                var found = new HashSet<string>(StringComparer.Ordinal);

                var headings = context.Settings.AvatarSupportHeadings.Concat(options.ExtraSupportHeadings).ToList();
                var scan = AvatarDetector.ScanDescription(
                    context.HtmlOf(item.Id), item.Id, headings, context.Settings.AvatarIgnoredHeadings);
                foreach (var hit in scan.Support)
                {
                    if (names.IsAvatar(hit.ItemId))
                    {
                        found.Add(hit.ItemId);
                    }
                }

                if (!options.ExcludeUnconfirmed)
                {
                    found.UnionWith(scan.Other.Where(names.IsAvatar));
                }

                foreach (var tag in item.Booth.Tags)
                {
                    found.UnionWith(names.MatchTag(tag));
                }

                var variationNames = item.Local.Purchases.Select(purchase => purchase.NameSnapshot)
                    .Concat(item.Booth.Variations.Select(variation => variation.Name))
                    .Where(name => !string.IsNullOrWhiteSpace(name));
                foreach (var variation in variationNames)
                {
                    found.UnionWith(names.MatchInside(variation!));
                }

                if (options.PlainDescription)
                {
                    found.UnionWith(PlainDescription.SupportedIds(item, names));
                }

                found.Remove(item.Id);
                result[item.Id] = found;
            }

            return Task.FromResult(result);
        }
    }

    /// <summary>アバターの呼び名の表。汚れた別名を使わない版を作れる。</summary>
    public sealed class NameTable
    {
        private readonly Dictionary<string, HashSet<string>> _exact = new(StringComparer.Ordinal);
        private readonly List<(string Name, string Id)> _all = [];
        private readonly HashSet<string> _avatars = new(StringComparer.Ordinal);

        /// <summary>名前になり得ない語。実データで別名や表示名に紛れていたもの。</summary>
        private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
        {
            "vr", "vrc", "vrchat", "3d", "男性", "女性", "対応", "標準版", "mobile対応", "mobile", "天使", "悪魔", "人外",
            "素体無料", "3dmodel", "3dアバター", "vrcアバター", "オリジナル3dモデル", "vrchat想定アバター", "vrc対応3dアバター",
            "vrchat対応3dモデル", "セットアップ", "セットアップコーデ", "ドラゴン", "山羊", "cluster", "head",
        };

        public bool IsAvatar(string id) => _avatars.Contains(id);

        public static NameTable Build(AvatarRegistry registry, bool derive)
        {
            var table = new NameTable();
            foreach (var entry in registry.Entries.Where(AvatarService.IsAvatar))
            {
                table._avatars.Add(entry.ItemId);
                var own = AvatarText.Normalize(entry.BoothName);
                var names = new HashSet<string>(StringComparer.Ordinal);

                void Add(string? text)
                {
                    var normalized = Strip(text);
                    if (normalized.Length >= 2 && !Generic.Contains(normalized) && !AvatarText.IsGenericName(text))
                    {
                        names.Add(normalized);
                    }
                }

                var booth = (entry.BoothName ?? string.Empty).Normalize(System.Text.NormalizationForm.FormKC);
                foreach (Match quoted in Regex.Matches(booth, @"[「『｢]([^「」『』｢｣]{1,20})[」』｣]"))
                {
                    Add(quoted.Groups[1].Value);
                }

                foreach (Match latin in Regex.Matches(booth, @"-\s?([A-Za-z][A-Za-z .]{1,20}?)\s?-"))
                {
                    Add(latin.Groups[1].Value);
                }

                if (!derive || own.Contains(AvatarText.Normalize(entry.DisplayName), StringComparison.Ordinal))
                {
                    Add(entry.DisplayName);
                }

                foreach (var alias in entry.Aliases.Where(alias => !alias.Rejected))
                {
                    if (!derive || own.Contains(Strip(alias.Text), StringComparison.Ordinal))
                    {
                        Add(alias.Text);
                    }
                }

                foreach (var name in names)
                {
                    if (!table._exact.TryGetValue(name, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        table._exact[name] = set;
                    }

                    set.Add(entry.ItemId);
                    table._all.Add((name, entry.ItemId));
                }
            }

            // 複数のアバターに当たる表記は使わない（今と同じ規則）
            foreach (var ambiguous in table._exact.Where(pair => pair.Value.Count > 1).Select(pair => pair.Key).ToList())
            {
                table._exact.Remove(ambiguous);
                table._all.RemoveAll(entry => entry.Name == ambiguous);
            }

            return table;
        }

        /// <summary>接尾辞（対応・専用・用…）と敬称（ちゃん・くん）を落として正規化する。</summary>
        public static string Strip(string? text)
        {
            var normalized = AvatarText.Normalize(text);
            for (var round = 0; round < 3; round++)
            {
                var before = normalized;
                foreach (var suffix in new[] { "対応版", "対応衣装", "対応", "専用", "向け", "用", "版", "ちゃん", "くん", "君", "さん", "様", "3d" })
                {
                    if (normalized.Length > suffix.Length + 1 && normalized.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        normalized = normalized[..^suffix.Length];
                        break;
                    }
                }

                if (normalized == before)
                {
                    break;
                }
            }

            return normalized;
        }

        /// <summary>タグは完全一致だけ。</summary>
        public IEnumerable<string> MatchTag(string tag)
            => _exact.TryGetValue(Strip(tag), out var ids) ? ids : [];

        /// <summary>
        /// 文の中に名前が入っているか。種類名（「✧しなの対応✧」「[愛莉] Airi」）のように装飾があるものに使う。
        /// より長いアバター名の一部としてしか現れない（ミルティナ⊃ティナ）ものと、
        /// 英字の名前が単語の途中に埋まっているだけ（Satellite⊃tell）のものは数えない。
        /// </summary>
        public IEnumerable<string> MatchInside(string text)
        {
            var normalized = AvatarText.Normalize(text);
            var lower = text.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
            var hits = _all.Where(entry => normalized.Contains(entry.Name, StringComparison.Ordinal)).ToList();

            foreach (var (name, id) in hits)
            {
                if (hits.Any(other => other.Id != id && other.Name.Length > name.Length && other.Name.Contains(name, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (Regex.IsMatch(name, "^[a-z0-9]+$") && !Regex.IsMatch(lower, $"(^|[^a-z0-9]){Regex.Escape(name)}([^a-z0-9]|$)"))
                {
                    continue;
                }

                yield return id;
            }
        }
    }

    /// <summary>
    /// 平文の説明文にある対応リスト。「・名前 − URL」の形の行が並び、説明のどこかに「対応」とあるときだけ読む。
    /// クレジット（「お借りした」「使用」「サムネ」を含む行の後）は読まない。
    /// </summary>
    public static class PlainDescription
    {
        private static readonly Regex Url = new(@"booth\.pm/(?:[a-z]{2}/)?items/(\d+)", RegexOptions.Compiled);

        public static IEnumerable<string> SupportedIds(ItemRecord item, NameTable names)
        {
            var description = item.Booth.Description ?? string.Empty;
            if (!description.Contains("対応", StringComparison.Ordinal) && !description.Contains("Supported", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            var inCredit = false;
            foreach (var raw in description.Split('\n'))
            {
                var line = raw.Trim();
                // 「使用アバター：URL」のようにクレジットがURLと同じ行に並ぶ形もある
                if (Regex.IsMatch(line, "使用アバター|着用アバター|サムネ|撮影|お借り|使用させ", RegexOptions.IgnoreCase) && Url.IsMatch(line))
                {
                    continue;
                }

                if (Regex.IsMatch(line, "クレジット|credit|お借り|使用させ|サムネ|撮影", RegexOptions.IgnoreCase) && !Url.IsMatch(line))
                {
                    inCredit = true;
                    continue;
                }

                if (Regex.IsMatch(line, "対応|Supported", RegexOptions.IgnoreCase) && !Url.IsMatch(line))
                {
                    inCredit = false;
                    continue;
                }

                if (inCredit)
                {
                    continue;
                }

                foreach (Match match in Url.Matches(line))
                {
                    if (names.IsAvatar(match.Groups[1].Value))
                    {
                        yield return match.Groups[1].Value;
                    }
                }
            }
        }
    }

    /// <summary>汎用品の手掛かり（正規表現）。案A6の出発点。</summary>
    public static class UniversalRule
    {
        private static readonly Regex Pattern = new(
            "汎用|全アバター|ほぼ全て?のアバター|様々なアバター|さまざまなアバター|どのアバターでも|お好きなアバター|特定のアバター向けのセットアップは行っていません|アバターを選ばず|他のアバターでも|多くのアバター|アバター問わず",
            RegexOptions.Compiled);

        public static bool Looks(ItemRecord item, EvalContext context)
            => Pattern.IsMatch(item.Booth.Name + "\n" + item.Booth.Description + "\n" + context.HtmlOf(item.Id) + "\n" + string.Join(' ', item.Booth.Tags));
    }

    /// <summary>適合率・再現率と、誤りの内訳。</summary>
    public sealed class Score
    {
        public int TruePositive { get; private set; }

        public int FalseReference { get; private set; }

        public int FalseWrong { get; private set; }

        public int FalseUnlabeled { get; private set; }

        public int FalseNegative { get; private set; }

        private readonly List<(string Item, string Avatar, string Kind)> _errors = [];

        public double Precision => TruePositive + FalseReference + FalseWrong + FalseUnlabeled == 0
            ? 0
            : (double)TruePositive / (TruePositive + FalseReference + FalseWrong + FalseUnlabeled);

        public double Recall => TruePositive + FalseNegative == 0 ? 0 : (double)TruePositive / (TruePositive + FalseNegative);

        public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);

        public static Score Of(EvalContext context, Dictionary<string, HashSet<string>> prediction)
        {
            var score = new Score();
            foreach (var (itemId, label) in context.Labels)
            {
                var predicted = prediction.TryGetValue(itemId, out var set) ? set : [];
                foreach (var avatar in predicted)
                {
                    var truth = label.Avatars.TryGetValue(avatar, out var value) ? value : null;
                    switch (truth)
                    {
                        case "対応": score.TruePositive++; break;
                        case "参考": score.FalseReference++; score._errors.Add((itemId, avatar, "誤:参考")); break;
                        case "違う": score.FalseWrong++; score._errors.Add((itemId, avatar, "誤:違う")); break;
                        case "不明": break;
                        default: score.FalseUnlabeled++; score._errors.Add((itemId, avatar, "誤:未ラベル")); break;
                    }
                }

                foreach (var (avatar, value) in label.Avatars)
                {
                    if (value == "対応" && !predicted.Contains(avatar))
                    {
                        score.FalseNegative++;
                        score._errors.Add((itemId, avatar, "漏れ"));
                    }
                }
            }

            return score;
        }

        public void PrintExamples(EvalContext context, int limit)
        {
            var names = context.Registry.Entries.ToDictionary(entry => entry.ItemId, entry => entry.DisplayName ?? entry.ItemId, StringComparer.Ordinal);
            foreach (var group in _errors.GroupBy(error => error.Kind))
            {
                // どの商品に固まっているか。1商品の一覧で埋まっているのか、広く薄く漏れているのかで直し方が変わる
                var byItem = group.GroupBy(error => error.Item)
                    .OrderByDescending(items => items.Count())
                    .Take(8)
                    .Select(items => $"{Trim(context.Items.TryGetValue(items.Key, out var r) ? r.Booth.Name : items.Key, 20)}×{items.Count()}");
                Console.WriteLine($"  ── {group.Key} {group.Count()} 件　多い商品: {string.Join("、", byItem)}");
                foreach (var (item, avatar, _) in group.Take(limit))
                {
                    var itemName = context.Items.TryGetValue(item, out var record) ? record.Booth.Name : item;
                    Console.WriteLine($"    {Trim(itemName, 34)} ← {(names.TryGetValue(avatar, out var name) ? name : avatar)}");
                }
            }
        }

        private static string Trim(string? text, int length)
            => text is null ? string.Empty : text.Length <= length ? text : text[..length] + "…";
    }
}
