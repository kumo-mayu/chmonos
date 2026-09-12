using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 名簿のアバターを画面に出すときの名前を決める（#54）。**名前を出す所はすべてここを通す。**
///
/// <list type="bullet">
/// <item>ユーザが付けた名前（<see cref="AvatarRegistryEntry.DisplayName"/>）があればそれ</item>
/// <item>無ければ、BOOTHの正式名から計算する（<see cref="AvatarText.DisplayNameFrom"/>）。名前が2通りで書かれていれば「Ciel（シエル）」</item>
/// <item>同じ名前のアバターが2体以上あれば、ショップ名を後ろに付ける（「レイ（〇〇工房）」）</item>
/// </list>
///
/// **計算した名前は保存しない。**付け方を直せば全員の名前が直る。
/// </summary>
public static class AvatarNames
{
    /// <summary>
    /// ユーザが付けた名前。付けていなければ null。**null でなければ人が付けた名前**で、形は問わない。
    ///
    /// 以前の版は自動の名前も <c>DisplayName</c> に書いていた。それを読むときに「以前の付け方でできる名前か」で
    /// 見分けていたが、人が呼び名そのものを選ぶと同じ形になり、保存しても元の名前に戻って見えた。
    /// 公開前なので古い記録に合わせる見分けは持たず、残っている記録はデータの側で直す（ユーザ判断 2026-09-12）。
    /// </summary>
    public static string? ManualName(AvatarRegistryEntry entry)
    {
        var name = entry.DisplayName?.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>ショップ名を付ける前の名前。</summary>
    public static string ShownName(AvatarRegistryEntry entry)
    {
        if (ManualName(entry) is { } manual)
        {
            return manual;
        }

        if (entry.BoothName is { Length: > 0 } booth
            && AvatarText.DisplayNameFrom(booth, entry.Aliases.Select(alias => alias.Text)) is { Length: > 0 } computed)
        {
            return computed;
        }

        // 販売終了で正式名を引けなかったもの。手元の手掛かりから入った名前があればそれ
        return string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.ItemId : entry.DisplayName.Trim();
    }

    /// <summary>
    /// 名簿の全員の名前（商品ID → 名前）。同じ名前が2体以上あれば、ショップ名が分かる物にだけ後ろに付ける。
    /// ショップ名がまだ分からない物（販売終了などで引けていない）はそのまま。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Map(IEnumerable<AvatarRegistryEntry> entries)
    {
        var list = entries.ToList();
        var shown = list.ToDictionary(entry => entry.ItemId, ShownName, StringComparer.Ordinal);

        // 名簿にはアバターでないと分かった物（そのアバター向けのテクスチャなど）も残っている。
        // それと名前が重なっただけで、本物のアバターにショップ名が付いていた
        // （「しなの / 〇〇 Makeup」というテクスチャのせいで、アバターの「しなの」が「しなの（〇〇研究所）」になった）。
        // 見分ける相手はアバターだけにする。販売終了などで確かめられていない物はアバターかもしれないので数に入れる
        var duplicated = list
            .Where(entry => entry.Category is null || AvatarService.IsAvatar(entry))
            .GroupBy(entry => AvatarText.Normalize(shown[entry.ItemId]), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group);

        foreach (var entry in duplicated)
        {
            if (entry.ShopName is { Length: > 0 } shop)
            {
                shown[entry.ItemId] = $"{shown[entry.ItemId]}（{shop.Trim()}）";
            }
        }

        return shown;
    }

    /// <summary>
    /// 名前を形作る部分。「Ciel（シエル）」なら Ciel と シエル。照合に使う
    /// （どちらの書き方で呼ばれても当たるように）。
    /// </summary>
    public static IEnumerable<string> Parts(string name)
        => name.Split(['（', '）'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
