using System.IO;
using Chmonos.Core.Storage;

namespace Chmonos.App.Services;

/// <summary>
/// 保存先の小さな JSON（アバターの登録簿・ユーザタグ・属性の一覧）を、**ファイルが変わっていなければ読み直さない**写し。
///
/// 商品ページを開くたび・編集で1件進むたびに、画面のスレッドでこれらを読み直していた（登録簿は400体で数百KB）。
/// ファイルの更新時刻と大きさを1回見て、同じなら前に読んだ物を返す。書き込みは一時ファイルからの置き換えなので、
/// 書けば必ず時刻が変わる（アプリの中からでも、手で直しても）。見てから読むまでの間に書かれたときは、
/// 新しい中身を古い時刻で覚えるだけで、次に見たときに読み直す（古い中身を返し続けることはない）。
///
/// 返す物は共有するので、呼び手は書き換えない（記録の型は読み取り専用の一覧で持っている）。
/// </summary>
public sealed class StoreFileCache<T>(JsonFileStore<T> store)
    where T : class, new()
{
    private readonly object _gate = new();
    private T? _value;
    private (DateTime WrittenAt, long Length) _stamp;

    public T Load()
    {
        var stamp = StampOf(store.Path);
        lock (_gate)
        {
            if (_value is not null && _stamp == stamp)
            {
                return _value;
            }
        }

        var loaded = store.Load();
        lock (_gate)
        {
            _value = loaded;
            _stamp = stamp;
        }

        return loaded;
    }

    private static (DateTime, long) StampOf(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : (DateTime.MinValue, -1);
    }
}
