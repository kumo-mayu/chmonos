namespace BoothAssetManager.Cli;

/// <summary>
/// 報告を呼び出し元のスレッドでそのまま実行する <see cref="IProgress{T}"/>。
/// 標準の <see cref="Progress{T}"/> はコンソールアプリだと報告をスレッドプールへ投げるため、
/// 順序が入れ替わったり、状態の比較が競合したりする。表示順が意味を持つ場面ではこちらを使う。
/// </summary>
public sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value) => _handler(value);
}
