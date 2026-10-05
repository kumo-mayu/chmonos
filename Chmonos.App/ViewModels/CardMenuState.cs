using System.Globalization;
using System.Windows.Data;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品のカード・行の右クリック（`ItemCardResources` の `CardMenu`）の、項目ごとの「押せるか」と吹き出しの文。
///
/// **項目はいつも全部出し、要る物が無いときは押せなくして理由を言う**（ユーザ判断 2026-10-04・メモ20-①。前は項目を隠していて、
/// 画面ごと・商品ごとに並びが変わり、なぜ無いのかも分からなかった）。
/// 要る物は2種類：**商品の JSON**（カードを持つこと。名前が挙がっただけで商品が手元に無いアバターの行は持たない）と、
/// **手元のファイル**（ファイルかフォルダを1つ以上持つこと＝所持の定義）。使った記録を残すだけの項目はファイルを要らない。
/// </summary>
internal static class CardMenuState
{
    internal const string NoJsonTip = "商品の情報がまだありません";

    internal const string NoFilesTip = "手元にファイルがありません";

    internal const string NoArchiveTip = "手元にzipがありません";

    /// <summary>持っているファイル・フォルダが、記録の上で全部「見つからない」とき（メモ65-①）。</summary>
    internal const string AllMissingTip = "手元のファイルが見つかりません";

    /// <summary>Unity の項目で、送れる unitypackage が zip にもフォルダにも無いとき（メモ65-③）。</summary>
    internal const string NoPackageTip = "手元にunitypackageがありません";

    /// <summary>子の理由がそろわないとき、親の吹き出しに出すまとめの理由。</summary>
    internal const string AllUnavailableTip = "今は使える操作がありません";

    /// <summary>メニューの宛先（カードそのもの、またはカードを持つ行）から商品のカードを取り出す。無ければ null。</summary>
    private static ItemCardViewModel? CardOf(object? target) => SearchViewModel.AsCard(target);

    /// <summary>手元にファイルかフォルダが1つ以上あるか。</summary>
    internal static bool HasFiles(ItemCardViewModel card)
        => card.Item.IsOwned;

    /// <summary>
    /// 記録の上で「見つからない」ファイルか（場所が空か、見つからなくなった日時が付いている）。
    ///
    /// **ここで自分で決める**（`ItemRecord.HasMissingFile` を使わない）：あちらは「1つでも無いか」で、カードの札・検索の条件・統計が同じ数になるよう
    /// 数え方が変わり得る。ここが知りたいのは「在る物が1つも無いか」で、問いが違う。
    /// **記録だけで決め、ディスクは見ない**（メニューは開くたびに引き直す）。つながっていないドライブの上の物は日時が付かない
    /// （無くなったのではなく、今は見えないだけ）ので「見つからない」に数えず、今までどおり押せる——押せば「取り外しているドライブ」と言う
    /// </summary>
    internal static bool IsMissing(LocalFileRecord file) => file.Paths.Count == 0 || file.MissingSince is not null;

    /// <summary>記録の上で「見つからない」フォルダか（取り込みか見回りが無いと見た日時が付いている）。</summary>
    internal static bool IsMissing(LocalFolderRecord folder) => folder.MissingSince is not null;

    /// <summary>
    /// 持っているファイル・フォルダが**全部**見つからないか（ユーザ判断 2026-10-05・メモ65-①）。1つでも在れば false。
    /// 持っていない商品も false（そちらは「手元にファイルがありません」で言う）。
    /// </summary>
    internal static bool AllMissing(ItemRecord item)
        => item.IsOwned
            && item.Local.OwnedFiles.All(IsMissing)
            && item.Local.LocalFolders.All(IsMissing);

    /// <summary>見つからないと記録されていない、外していないファイル（展開に使える zip）があるか。</summary>
    private static bool HasPresentFile(ItemRecord item) => item.Local.OwnedFiles.Any(file => !IsMissing(file));

    /// <summary>
    /// Unity へ送れる unitypackage を、見つからないと記録されていない zip かフォルダに持っているか（メモ65-③）。
    /// zip は中身の一覧（`contents`）、フォルダは数えたときの一覧（`unityPackages`）で見る。どちらも記録で、ディスクを見ない
    /// （フォルダの中を開くたびに並べると、大きなフォルダや HDD でメニューが待たされる）。
    /// </summary>
    internal static bool HasSendablePackage(ItemRecord item)
        => item.Local.OwnedFiles.Any(file => !IsMissing(file) && UnityHandoff.PackageEntriesIn(file).Count > 0)
            || item.Local.LocalFolders.Any(folder => !IsMissing(folder) && (folder.UnityPackages ?? []).Count > 0);

    private static bool NeedsFiles(string key)
        => key is "Reveal" or "Unpack" or "SendToUnity" or "SendToUnityWithRecord" or "SelectInUnity";

    /// <summary>
    /// フォルダでは足りず、zip が要る項目。展開は zip を広げる操作なので、フォルダだけの商品では押せなくする
    /// （押してから「無い」と言われるより、薄く出して理由を言う決まり）。エクスプローラで開くはフォルダも開ける
    /// </summary>
    private static bool NeedsArchive(string key) => key is "Unpack";

    /// <summary>
    /// unitypackage が要る項目。zip の中の物も、登録したフォルダの中の物も送れる（ユーザ判断 2026-10-05・メモ65-③。
    /// 前は zip が要る項目に入れていて、展開してあるフォルダの中に unitypackage があっても押せなかった）。
    /// </summary>
    private static bool NeedsPackage(string key)
        => key is "SendToUnity" or "SendToUnityWithRecord" or "SelectInUnity";

    /// <summary>
    /// 子を持つ親の項目（「開く ▸」「Unity ▸」）と、その子。**子が全部押せないときは、親も押せなくして理由を言う**
    /// （ユーザ判断 2026-10-05・メモ49。子だけ薄くて親が押せると、開いてから全部押せないと分かる）。
    /// </summary>
    private static readonly Dictionary<string, string[]> Parents = new()
    {
        ["OpenParent"] = ["Reveal", "Unpack"],
        ["UnityParent"] = ["SendToUnity", "SendToUnityWithRecord", "SelectInUnity"],
    };

    /// <summary>項目が押せるか。<paramref name="key"/> は項目の名前（<see cref="Tip"/> と同じ）。</summary>
    internal static bool IsEnabled(string key, object? target)
    {
        if (Parents.TryGetValue(key, out var children))
        {
            return children.Any(child => IsEnabled(child, target));
        }

        var card = CardOf(target);
        return key switch
        {
            // 商品の JSON が要る
            "Favorite" or "OpenItem" or "Edit" or "AddToModification" or "Hide" => card is not null,
            "OpenBooth" or "CopyLink" => card is { HasBoothPage: true },
            "OpenShop" => card is { HasShop: true },
            // 手元のファイルが要る。全部見つからない商品は、どれも押せない（押しても「無い」と言うだけ。メモ65-①）
            _ when NeedsArchive(key) => card is not null && HasPresentFile(card.Item),
            _ when NeedsPackage(key) => card is not null && HasSendablePackage(card.Item),
            _ when NeedsFiles(key) => card is not null && HasFiles(card) && !AllMissing(card.Item),
            // 選ぶ箱を持つのはカードそのものだけ（行の一覧は選びを持たない）
            "Select" => target is ItemCardViewModel,
            "ShowUpdate" => target is ItemCardViewModel { HasUpdate: true, ShowUpdateCommand: not null },
            "MarkUpdateRead" => target is ItemCardViewModel { CanMarkUpdateRead: true },
            _ => true,
        };
    }

    /// <summary>吹き出しの文。押せるときは何が起きるか、押せないときはその理由。</summary>
    internal static string? Tip(string key, object? target)
    {
        var card = CardOf(target);
        if (Parents.TryGetValue(key, out var children))
        {
            // 子の理由が同じならそれを、違えば短くまとめた理由を言う
            var reasons = children.Select(child => Tip(child, target)).Distinct().ToList();
            return IsEnabled(key, target) ? null : reasons.Count == 1 ? reasons[0] : AllUnavailableTip;
        }

        if (IsEnabled(key, target))
        {
            return key switch
            {
                "OpenBooth" => card!.OpenBoothTip,
                "CopyLink" => card!.CopyLinkTip,
                "OpenShop" => card!.OpenShopTip,
                "Reveal" => "手元のファイルの場所を開きます。2つ以上あれば、どれを開くか選べます。",
                "Unpack" => "zipを一時フォルダに展開して開きます。展開したものはアプリを閉じると消えます。",
                "SendToUnity" => "開いているUnityへ送ります。2つ以上あれば選べます。",
                "SendToUnityWithRecord" => "送ったうえで、どの改変に使ったかを残します。",
                "SelectInUnity" => "開いているUnityのプロジェクトタブで、入った場所を示します。",
                "AddToModification" => "どの改変に追加するかを選んで、記録だけ残します。選んでいる最中は、選んだ商品をまとめて追加します。",
                _ => null,
            };
        }

        if (key == "Select")
        {
            return "この一覧では選べません";
        }

        if (card is null)
        {
            return NoJsonTip;
        }

        return key switch
        {
            "OpenBooth" => card.OpenBoothTip,
            "CopyLink" => card.CopyLinkTip,
            "OpenShop" => card.OpenShopTip,
            "ShowUpdate" or "MarkUpdateRead" => target is ItemCardViewModel { HasUpdate: false }
                ? "未読の更新の通知はありません"
                : "この画面では使えません",
            _ when NeedsFiles(key) && !HasFiles(card) => NoFilesTip,
            _ when NeedsFiles(key) && AllMissing(card.Item) => AllMissingTip,
            _ when NeedsArchive(key) => NoArchiveTip,
            _ when NeedsPackage(key) => NoPackageTip,
            _ => null,
        };
    }

    /// <summary>お気に入りの項目の見出し。商品が無い行でも項目は出すので、仮の見出しを返す。</summary>
    internal static string FavoriteHeader(object? target)
        => CardOf(target)?.FavoriteTip ?? "お気に入りに入れる";
}

/// <summary>`CardMenu` の項目の IsEnabled。{Binding} でメニューの宛先を受け、ConverterParameter に項目の名前を書く。</summary>
public sealed class CardMenuEnabledConverter : IValueConverter
{
    public static readonly CardMenuEnabledConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => CardMenuState.IsEnabled((string)parameter!, value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>`CardMenu` の項目の吹き出し。<see cref="CardMenuEnabledConverter"/> と同じ書き方。</summary>
public sealed class CardMenuTipConverter : IValueConverter
{
    public static readonly CardMenuTipConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => CardMenuState.Tip((string)parameter!, value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>`CardMenu` のお気に入りの見出し。</summary>
public sealed class CardMenuFavoriteHeaderConverter : IValueConverter
{
    public static readonly CardMenuFavoriteHeaderConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => CardMenuState.FavoriteHeader(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
