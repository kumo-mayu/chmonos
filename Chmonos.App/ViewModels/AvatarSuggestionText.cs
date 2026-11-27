namespace Chmonos.App.ViewModels;

/// <summary>
/// アバターの候補1行の字を組み立て、選ばれた行から読み戻す。
///
/// 字は既定では名前だけで、設定「アバターの商品IDを表示する」を入れた時だけ「名前（商品ID）」にする
/// （ユーザ判断 2026-11-27：アバターの商品IDは基本的に重要では無い）。
/// 切でもIDで引けるように、IDは候補の手掛かり（<see cref="InfoOf"/> の Hints）に入れる。
/// VRChatでは対応商品を探すのに商品IDを使う習慣があり、名前を思い出せなくてもIDなら分かる場面がある。
/// 字からIDを読み戻すのは <see cref="AvatarLabels"/> の表で行う（字にIDが無いことがあるため）
/// </summary>
public static class AvatarSuggestionText
{
    /// <summary>候補の群の番号。見出し（<see cref="Headings"/>）の添え字と同じ。</summary>
    public const int OwnedGroup = 0;

    public const int BaseGroup = 1;

    public const int OtherGroup = 2;

    /// <summary>群の見出し（メモ48）。アバターの候補を出す欄はどこもこの3つ。共通素体を選べない欄は、素体の群が出ないだけ。</summary>
    public static readonly IReadOnlyList<string> Headings = ["所持アバター", "共通素体", "未所持アバター"];

    /// <summary>
    /// アバター1体の候補の案内。群と、名前以外の呼び方（登録簿の別名・BOOTHの正式名）と、商品IDを付ける。
    /// IDは最後に足す：名前・呼び方で当たる時はそちらの札を出し、数字を打った時だけIDの札が出るように。
    /// 字にIDが入っている時（設定が入）は字の方で当たるので、この札は出ない
    /// </summary>
    public static Controls.SuggestInfo InfoOf(Core.Models.AvatarRegistryEntry entry, string shownName, int group)
        => new(group, Core.Services.AvatarSearch.Hints(entry, shownName)
            .Select(hint => new Controls.SuggestHint(hint.Text, hint.Label))
            .Append(new Controls.SuggestHint(entry.ItemId, $"商品ID {entry.ItemId}"))
            .ToList());

    public static string Format(string name, string itemId) => $"{name}（{itemId}）";

    /// <summary>
    /// アバターたちの候補の字を決める。<paramref name="showIds"/> が切なら名前だけ、ただし字が重なるアバターにだけIDを添える。
    /// 名前は <see cref="Core.Services.AvatarNames.Map"/> が重なりをショップ名で見分けるが、ショップ名が分からない物や同じショップの物は重なったまま残る。
    /// 候補の欄は字から選んだ物を引くので、重なると片方が候補から消える（<see cref="ListModule"/> は同じ字の2件目を捨てる）
    /// </summary>
    public static AvatarLabels Labels(IEnumerable<(string ItemId, string Name)> avatars, bool showIds)
    {
        var list = avatars.ToList();
        var repeated = showIds
            ? new HashSet<string>()
            : list.GroupBy(avatar => avatar.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return new AvatarLabels(list.Select(avatar =>
            (avatar.ItemId, showIds || repeated.Contains(avatar.Name.Trim()) ? Format(avatar.Name, avatar.ItemId) : avatar.Name)));
    }

    /// <summary>アバター1体だけの字（候補を読む前に札を出す時。重なりは候補を読んだ時に札の字ごと直る）</summary>
    public static string Label(string name, string itemId, bool showIds) => showIds ? Format(name, itemId) : name;

    /// <summary>
    /// 候補の行から商品IDを取り出す。形が違えば null。
    /// 打ち間違いをそのままIDとして扱わないための門番でもある。
    /// </summary>
    public static string? IdOf(string entry)
    {
        var open = entry.LastIndexOf('（');
        var close = entry.LastIndexOf('）');
        if (open < 0 || close <= open + 1)
        {
            return null;
        }

        var id = entry[(open + 1)..close];
        return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : null;
    }

    /// <summary>候補の行から名前だけを取り出す。要約に出すのは名前の方。</summary>
    public static string NameOf(string entry)
    {
        var open = entry.LastIndexOf('（');
        return open <= 0 ? entry : entry[..open];
    }
}

/// <summary>候補の字と商品IDの表（<see cref="AvatarSuggestionText.Labels"/>）。字は大文字小文字を区別せず引く（候補の欄の表と同じ）</summary>
public sealed class AvatarLabels
{
    private readonly Dictionary<string, string> _textOfId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _idOfText = new(StringComparer.CurrentCultureIgnoreCase);

    internal AvatarLabels(IEnumerable<(string ItemId, string Text)> labels)
    {
        foreach (var (id, text) in labels)
        {
            _textOfId.TryAdd(id, text);
            _idOfText.TryAdd(text, id);
        }
    }

    public string? TextOf(string itemId) => _textOfId.GetValueOrDefault(itemId);

    /// <summary>
    /// 字から商品IDを引く。表に無ければ「名前（ID）」の形から読む
    /// （設定を入れていた時に出した札・字が重なってIDを添えた札。字にIDがあればそれで足りる）。どちらでも無ければ null
    /// </summary>
    public string? IdOf(string text) => _idOfText.TryGetValue(text.Trim(), out var id) ? id : AvatarSuggestionText.IdOf(text);
}
