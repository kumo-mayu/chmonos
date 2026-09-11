namespace BoothAssetManager.Core.Models;

/// <summary>
/// サービスに設定を渡す形。**値ではなく「今の設定を返すもの」を渡す。**
///
/// 以前は起動時の <see cref="AppSettings"/> を各サービスが抱えていた。<see cref="AppSettings"/> は
/// 書き換えられない record なので、設定画面で保存しても新しい値は画面の側にしか届かず、
/// 画像の長辺・画質・取得の間隔などは**起動し直すまで効かなかった**
/// （友人の報告「設定にある画像サイズが反映されていないのでは」）。
/// 使う瞬間に読めば、保存した直後の取得から効く。
/// </summary>
public static class SettingsSource
{
    /// <summary>変わらない設定。試験と、設定を差し替えない呼び出し元のため。</summary>
    public static Func<AppSettings> Fixed(AppSettings? settings)
    {
        var value = settings ?? new AppSettings();
        return () => value;
    }
}
