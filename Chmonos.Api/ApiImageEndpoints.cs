using System.Globalization;
using Chmonos.Core.Images;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// 画像の道（<c>/v1/images/{itemId}/{name}</c>・<c>/v1/modifications/{id}/images/{name}</c>）の <c>?format=</c>・<c>?size=</c>（2026-10-14）。
/// Unity の <c>Texture2D.LoadImage</c> は PNG と JPEG しか読めず、保存の形式（WebP）を読めない（Unity の API-GAPS 3-1）。
/// どちらも省けば今までどおり元のファイルをそのまま返す（書き直さない。ブラウザは WebP を読める）
/// </summary>
internal static partial class ApiEndpoints
{
    /// <summary>
    /// 画像の答え。書き直すのは頼まれたときだけ（Core の <see cref="ImageConversion"/>。復号の守りは保存のときと同じ）。
    /// 誤り：400 invalidFormat・invalidSize、422 imageUnreadable（保存先の画像が読めない・大きすぎる）
    /// </summary>
    private static async Task<IResult> ImageAnswerAsync(string file, string? format, string? size)
    {
        var output = format switch
        {
            null => (ImageOutputFormat?)null,
            "png" => ImageOutputFormat.Png,

            // 拡張子でよく使う綴りも受ける（どちらも同じ JPEG）
            "jpeg" or "jpg" => ImageOutputFormat.Jpeg,
            _ => throw QueryException.Invalid("invalidFormat", $"format は png か jpeg です: {format}"),
        };

        int? edge = null;
        if (size is not null)
        {
            edge = int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed is >= ImageConversion.MinEdge and <= ImageConversion.MaxEdge
                ? parsed
                : throw QueryException.Invalid("invalidSize", $"size は長い辺の画素で {ImageConversion.MinEdge}〜{ImageConversion.MaxEdge} です: {size}");
        }

        if (output is null && edge is null)
        {
            return Results.File(file, ContentTypeOf(file));
        }

        var bytes = await File.ReadAllBytesAsync(file);
        try
        {
            var converted = ImageConversion.Convert(bytes, output, edge);
            return Results.Bytes(converted.Bytes, converted.ContentType);
        }
        catch (SixLabors.ImageSharp.ImageFormatException)
        {
            // 壊れた画像・読まない形式・寸法が大きすぎる画像。元のファイルは format・size を省けば取れる
            throw new QueryException(422, "imageUnreadable", "画像を読めないので、形式や大きさを変えられません。format と size を省くと元のファイルを返します。");
        }
    }

    /// <summary>仕様書の印：画像の道の答え（元の WebP か、頼まれた形式）と誤り。</summary>
    private static RouteHandlerBuilder ImageAnswers(this RouteHandlerBuilder builder, string summary)
        => builder
            .WithSummary(summary)
            .WithDescription(
                $"format（png・jpeg）と size（長い辺の画素。{ImageConversion.MinEdge}〜{ImageConversion.MaxEdge}。拡大はしない）で書き直して返す。"
                + "どちらも省けば元のファイル（多くは WebP）をそのまま返す。size だけなら元の形式で縮める。JPEG は透けた所を白で埋める。"
                + "誤り：400 invalidFormat・invalidSize、404 itemNotFound・modificationNotFound・imageNotFound、422 imageUnreadable")
            .Produces(StatusCodes.Status200OK, (Type?)null, "image/webp", "image/png", "image/jpeg")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}
