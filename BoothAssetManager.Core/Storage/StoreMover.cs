namespace BoothAssetManager.Core.Storage;

/// <summary>引越しの進み具合。件数だけで足りる（1件あたりは小さい）。</summary>
public readonly record struct StoreMoveProgress(int Copied, int Total, string CurrentName);

public sealed record StoreMoveResult
{
    public required bool Succeeded { get; init; }

    /// <summary>コピーできたファイル数。</summary>
    public required int Copied { get; init; }

    public required long Bytes { get; init; }

    /// <summary>失敗したときの理由。成功なら null。</summary>
    public string? Error { get; init; }

    /// <summary>元の場所を消せたか。消せていなければ、そこに残っている。</summary>
    public bool SourceRemoved { get; init; }
}

/// <summary>
/// 保存先の引越し。
///
/// 「コピー → 検証 → 元を消す」の順で行う。途中で失敗しても元が消えていないので、
/// 呼び出し側は保存先を古いままにしておけば何も失われない。
/// 移動（Directory.Move）を使わないのは、ドライブをまたぐと使えないことと、
/// 途中で失敗したときに半分だけ移った状態になるため。
/// </summary>
public static class StoreMover
{
    /// <summary>数えるときも運ぶときも、この名前は除く（実行中のロックは持ち出せない）。</summary>
    private static readonly string[] Skipped = ["app.lock"];

    /// <summary>運ぶ量を先に測る。確認のダイアログに出す。</summary>
    public static (int Files, long Bytes) Measure(string root)
    {
        if (!Directory.Exists(root))
        {
            return (0, 0);
        }

        var files = Enumerate(root).ToList();
        return (files.Count, files.Sum(file => new FileInfo(file).Length));
    }

    public static StoreMoveResult Move(
        string source,
        string destination,
        IProgress<StoreMoveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = Enumerate(source).ToList();
        var copied = 0;
        var bytes = 0L;

        try
        {
            Directory.CreateDirectory(destination);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);

                copied++;
                bytes += new FileInfo(file).Length;
                progress?.Report(new StoreMoveProgress(copied, files.Count, Path.GetFileName(file)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // 元には手を付けていないので、保存先を古いままにすれば何も失われない
            return new StoreMoveResult
            {
                Succeeded = false,
                Copied = copied,
                Bytes = bytes,
                Error = exception is OperationCanceledException ? "中断しました。" : exception.Message,
            };
        }

        // 検証。1件ずつ大きさを突き合わせる。
        // ハッシュまで取ると数GBで現実的な時間に収まらないので、件数と大きさで見る
        if (Verify(source, destination) is { } mismatch)
        {
            return new StoreMoveResult
            {
                Succeeded = false,
                Copied = copied,
                Bytes = bytes,
                Error = $"コピーの確認に失敗しました：{mismatch}",
            };
        }

        return new StoreMoveResult
        {
            Succeeded = true,
            Copied = copied,
            Bytes = bytes,
            SourceRemoved = TryRemoveSource(source, files),
        };
    }

    /// <summary>全部揃っていれば null、足りなければその名前を返す。</summary>
    private static string? Verify(string source, string destination)
    {
        foreach (var file in Enumerate(source))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);

            if (!File.Exists(target))
            {
                return relative;
            }

            if (new FileInfo(target).Length != new FileInfo(file).Length)
            {
                return relative + "（大きさが違います）";
            }
        }

        return null;
    }

    /// <summary>
    /// 元を消す。運んだファイルだけを消し、空になったフォルダも畳む。
    /// <c>app.lock</c> は実行中のこのプロセスが握っているので消せない。
    /// 握りを放してから呼べば消えるが、消せなくても引越し自体は成立している。
    /// </summary>
    private static bool TryRemoveSource(string source, IReadOnlyList<string> files)
    {
        var allRemoved = true;

        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                allRemoved = false;
            }
        }

        // 深い方から畳む
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                allRemoved = false;
            }
        }

        return allRemoved;
    }

    private static IEnumerable<string> Enumerate(string root)
        => Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => !Skipped.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            : [];
}
