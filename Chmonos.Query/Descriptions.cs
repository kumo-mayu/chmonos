using Chmonos.Core.Models;

namespace Chmonos.Query;

// 説明文の答え（GET /v1/items/{id}/description。docs/spec/local-api-shapes.md「説明文」）。
// 形は外の道具との約束（Contracts.cs と同じ決まり）。Contracts.cs を大きくしないよう、別のファイルに置く。

/// <summary>説明文の見出し1つ（画面の商品ページの見出しごとの欄と同じ物）。</summary>
/// <param name="Heading">見出しの原文（出品者の書いた装飾記号を含む）。</param>
/// <param name="Text">見出しの下の文（タグを除いた素の文）。</param>
public sealed record DescriptionSectionRef(string Heading, string Text);

/// <summary>商品の説明文。</summary>
/// <param name="Summary">BOOTH の商品の短い説明（商品ページの頭の文。画面の商品ページの説明の最初の欄）。無ければ null。</param>
/// <param name="Sections">見出しごとの文（画面と同じ並び）。取っていなければ空。</param>
/// <param name="Html">
/// 表示用の HTML（保存先の <c>items/{id}.h2.html</c> そのまま）。動く物（script・埋め込み・フォーム・イベントの属性・http 以外の URL）は
/// 保存するときに取り除いてある。中の画像やリンクは BOOTH の場所を指す。無ければ null。
/// </param>
public sealed record ItemDescription(string Id, string? Summary, IReadOnlyList<DescriptionSectionRef> Sections, string? Html);

/// <summary>
/// 説明文の答えを作る。読み口は画面と同じ物を使い、別の変換・別の無害化を作らない：
/// 見出しごとの文は画面の商品ページと同じ <see cref="BoothBlock.H2Sections"/>、表示用の HTML はアプリの読み口
/// （<c>ItemRepository.LoadDescriptionHtmlAsync</c>）。写しが説明文を落としているとき（アプリと控えを共有していない）は、記録のファイルから読む
/// </summary>
public static class Descriptions
{
    public static async Task<ItemDescription> OfAsync(ItemRecord item, QuerySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var full = snapshot.FullItem(item);
        var html = await snapshot.LoadDescriptionHtmlAsync(item.Id, cancellationToken);
        return new ItemDescription(
            item.Id,
            string.IsNullOrWhiteSpace(full.Booth.Description) ? null : full.Booth.Description,
            full.Booth.H2Sections.Select(section => new DescriptionSectionRef(section.Heading, section.Text)).ToList(),
            html);
    }
}
