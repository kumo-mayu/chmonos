namespace BoothAssetManager.Core.Services;

/// <summary>
/// Unity の取り込み画面（題は「Import Unity Package」）が、中身の一覧か「Nothing to import!」かを、窓の絵で見分ける。
///
/// **なぜ絵で見るか：**既に全部入っている unitypackage を送ると、Unity は同じ題・同じ大きさの窓に「Nothing to import!」とだけ出す。
/// OK で閉じても Editor.log に1行も出ないので、ログでは Cancel と見分けられず、「Cancel されたので入っていません」と
/// 数えていた（2026-09-19 に cleanTest - コピーで確かめた）。窓の中は IMGUI で、UI Automation にも出てこない。
///
/// **決め方（2026-09-19 に実機の2枚で測った。366×589）：**
/// <list type="number">
/// <item>本文（高さの15〜85%）が1色。「Nothing to import」は 100%、中身の一覧は 73%（行・チェック・All/None）</item>
/// <item>上（3〜15%）に文字がある。まだ描かれていない真っ黒な窓を「Nothing」と取り違えないため</item>
/// <item>下（85%〜）の1行に並ぶボタンの塊が最大1つ（OK だけ）。中身の一覧でも、中身が少なく窓を縦に伸ばすと本文が無地になり得るが、
///       下には必ず Cancel と Import の2つが並ぶ</item>
/// </list>
/// 色では決めない（Unity の明るい見た目でも同じに決まるように、「1色か」「塊がいくつか」だけを見る）。
/// </summary>
public static class UnityImportWindowLook
{
    /// <summary>同じ色とみなす差（各色 0〜255 の差）。縁のなめらかにした画素を別の色と数えないため。</summary>
    private const int Tolerance = 10;

    /// <summary>ボタンの塊の中の切れ目として許す幅（画素）。ボタンの文字の字間で1つの塊が割れないため。</summary>
    private const int GapInsideButton = 4;

    /// <param name="width">窓の幅（画素）。</param>
    /// <param name="height">窓の高さ（画素）。</param>
    /// <param name="argb">上の行から順に並べた画素（0xAARRGGBB）。長さは幅×高さ。</param>
    /// <returns>「Nothing to import!」の窓なら true。見分けられないときも false（Cancel と同じ扱いに落ちる）。</returns>
    public static bool IsNothingToImport(int width, int height, ReadOnlySpan<int> argb)
    {
        if (width < 40 || height < 40 || argb.Length < width * height)
        {
            return false;
        }

        // 左右の端は窓の枠と影なので見ない
        var left = width * 5 / 100;
        var right = width * 95 / 100;

        var bodyTop = height * 15 / 100;
        var bodyBottom = height * 85 / 100;
        var body = argb[(height / 2 * width) + (width / 2)];
        for (var y = bodyTop; y < bodyBottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (!Near(argb[(y * width) + x], body))
                {
                    return false;
                }
            }
        }

        var hasText = false;
        for (var y = height * 3 / 100; y < bodyTop && !hasText; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (!Near(argb[(y * width) + x], body))
                {
                    hasText = true;
                    break;
                }
            }
        }

        if (!hasText)
        {
            return false;
        }

        var most = 0;
        for (var y = bodyBottom; y < height; y++)
        {
            most = Math.Max(most, ButtonRuns(argb.Slice(y * width, width), left, right));
        }

        return most == 1;
    }

    /// <summary>1行の中で、その行の一番多い色と違う画素の塊がいくつあるか。</summary>
    private static int ButtonRuns(ReadOnlySpan<int> row, int left, int right)
    {
        var counts = new Dictionary<int, int>();
        for (var x = left; x < right; x++)
        {
            counts[row[x]] = counts.GetValueOrDefault(row[x]) + 1;
        }

        var ground = counts.MaxBy(pair => pair.Value).Key;
        var runs = 0;
        var inside = false;
        var gap = 0;
        for (var x = left; x < right; x++)
        {
            if (!Near(row[x], ground))
            {
                if (!inside)
                {
                    runs++;
                    inside = true;
                }

                gap = 0;
            }
            else if (inside && ++gap > GapInsideButton)
            {
                inside = false;
            }
        }

        return runs;
    }

    private static bool Near(int a, int b)
        => Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) <= Tolerance
            && Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) <= Tolerance
            && Math.Abs((a & 0xFF) - (b & 0xFF)) <= Tolerance;
}
