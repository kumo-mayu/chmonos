namespace Chmonos.Core.Tests.Robustness;

/// <summary>
/// 意地の悪い条件の試験（2026-11-13 の Codex の点検から移した物）が書く一時フォルダ。
/// 消す前に一時フォルダの下かを確かめる：<c>recursive</c> の削除が、組み立ての誤りで保存先や作業ツリーへ向かないように。
/// </summary>
internal sealed class RobustnessDirectory : IDisposable
{
    private static readonly string Parent = Path.Combine(Path.GetTempPath(), "chmonos-robustness");

    public string Root { get; } = Path.GetFullPath(Path.Combine(Parent, Guid.NewGuid().ToString("N")));

    public RobustnessDirectory() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Parent) + Path.DirectorySeparatorChar, Root, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(Root, recursive: true);
    }
}
