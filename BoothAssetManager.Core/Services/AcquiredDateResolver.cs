using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 表示に使う入手日と、それが手入力かフォールバックか。
/// 由来を持ち回るのは、画面で「これはファイルの日付です」と断れるようにするため。
/// </summary>
public readonly record struct AcquiredDate(DateOnly? Value, bool IsFallback)
{
    public bool HasValue => Value is not null;
}

/// <summary>
/// 入手日を決める。ユーザの手入力があればそれを使い、無ければファイルの日付で代える。
///
/// 代えた事実は隠さない。内部でこっそり補った値を、手で入れた値と同じ顔で出すと
/// 「いつ入れたか」の記録として信用できなくなる。
/// ファイル側は書き換えず、表示のたびに求める（ユーザが空欄に戻したら素直に空欄へ戻る）。
/// </summary>
public static class AcquiredDateResolver
{
    public static AcquiredDate Resolve(ItemRecord item)
    {
        if (item.Local.AcquiredAt is { } entered)
        {
            return new AcquiredDate(entered, false);
        }

        DateTime? earliest = null;

        foreach (var path in item.Local.LocalFiles.SelectMany(file => file.Paths))
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                // 最初に手に入れた日を知りたいので、複数あるときは古い方を採る
                var written = File.GetLastWriteTime(path);
                if (earliest is null || written < earliest)
                {
                    earliest = written;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 触れないファイルは判断材料から外すだけ
            }
        }

        return earliest is null
            ? new AcquiredDate(null, false)
            : new AcquiredDate(DateOnly.FromDateTime(earliest.Value), true);
    }
}
