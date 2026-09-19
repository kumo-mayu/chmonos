using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り込み画面が「Nothing to import!」かを絵で見分ける（2026-09-19）。
/// 絵は実機の2枚（366×589・暗い見た目）の配置を写した作り物。実機の2枚そのものでも手元で確かめた（入れていない：購入した物の名前が写る）
/// </summary>
public sealed class UnityImportWindowLookTests
{
    private const int Width = 366;
    private const int Height = 589;

    private const int Frame = unchecked((int)0xFF000000);
    private const int DarkBody = unchecked((int)0xFF383838);
    private const int DarkButton = unchecked((int)0xFF585858);
    private const int DarkText = unchecked((int)0xFFD2D2D2);
    private const int LightBody = unchecked((int)0xFFC2C2C2);
    private const int LightButton = unchecked((int)0xFFE4E4E4);
    private const int LightText = unchecked((int)0xFF101010);

    private sealed class Canvas
    {
        public Canvas(int width, int height, int fill)
        {
            W = width;
            H = height;
            Pixels = Enumerable.Repeat(fill, width * height).ToArray();
        }

        public int W { get; }

        public int H { get; }

        public int[] Pixels { get; }

        public Canvas Box(int x, int y, int width, int height, int color)
        {
            for (var row = y; row < y + height; row++)
            {
                for (var column = x; column < x + width; column++)
                {
                    Pixels[(row * W) + column] = color;
                }
            }

            return this;
        }

        public bool IsNothing() => UnityImportWindowLook.IsNothingToImport(W, H, Pixels);
    }

    /// <summary>黒い枠の中に本文。題の帯（0〜30）と下の帯（548〜580）を描く。</summary>
    private static Canvas Window(int height, int body, int text) => new Canvas(Width, height, Frame)
        .Box(8, 30, Width - 16, height - 38, body)
        .Box(16, 10, 150, 10, text); // 題の文字

    private static Canvas Nothing(int height, int body, int button, int text) => Window(height, body, text)
        .Box(12, 36, 110, 12, text) // Nothing to import!
        .Box(14, 52, 300, 12, text) // All assets from this package are already in your project.
        .Box(Width - 50, height - 34, 28, 18, button); // OK

    private static Canvas Contents(int height, int rows, int body, int button, int text)
    {
        var canvas = Window(height, body, text)
            .Box(12, 45, 130, 14, text) // パッケージの名前
            .Box(18, 92, 50, 18, button) // All
            .Box(72, 92, 50, 18, button); // None
        for (var row = 0; row < rows; row++)
        {
            canvas.Box(30 + ((row % 4) * 12), 120 + (row * 16), 150, 10, text);
        }

        return canvas
            .Box(Width - 124, height - 34, 52, 18, button) // Cancel
            .Box(Width - 66, height - 34, 48, 18, button); // Import
    }

    [Fact]
    public void Nothing_to_importの窓()
        => Assert.True(Nothing(Height, DarkBody, DarkButton, DarkText).IsNothing());

    [Fact]
    public void 明るい見た目でも同じに見分ける()
        => Assert.True(Nothing(Height, LightBody, LightButton, LightText).IsNothing());

    [Fact]
    public void 中身の一覧の窓は違う()
        => Assert.False(Contents(Height, rows: 25, DarkBody, DarkButton, DarkText).IsNothing());

    [Fact]
    public void 中身が1件で縦に伸ばした窓も違う()
    {
        // 本文の15%より下は一覧の地だけになる。下の Cancel と Import の2つで見分ける
        var canvas = Contents(1400, rows: 1, DarkBody, DarkButton, DarkText);
        Assert.False(canvas.IsNothing());
    }

    [Fact]
    public void まだ描かれていない窓はNothingと言わない()
    {
        // 出てすぐは中が真っ黒のことがある。閉じるまで見直すので、ここでは決めない
        var canvas = new Canvas(Width, Height, Frame).Box(16, 10, 150, 10, DarkText);
        Assert.False(canvas.IsNothing());
    }

    [Fact]
    public void 足りない絵は見分けない()
        => Assert.False(UnityImportWindowLook.IsNothingToImport(Width, Height, new int[10]));
}
