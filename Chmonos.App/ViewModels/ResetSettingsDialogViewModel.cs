namespace Chmonos.App.ViewModels;

/// <summary>
/// 「すべての設定を既定に戻す」の確認の窓（ユーザ判断 2026-11-10）。
/// 通知の保管の容量は他の設定と違い、大きくしていた人は既定の 50MB に戻すと閉じるときに保管庫が削られる。
/// 他の設定は戻したいのにこの1つのせいで押せなくならないよう、容量だけ残すチェックを付ける。
/// **既定はオン**（押すのに勇気が要らないように。残す方が取り返しがつく側）
/// </summary>
public sealed class ResetSettingsDialogViewModel : ViewModelBase
{
    private bool _keepArchiveCapacity = true;

    /// <summary>通知の保管に使う容量を今の値のまま残す。</summary>
    public bool KeepArchiveCapacity
    {
        get => _keepArchiveCapacity;
        set => SetField(ref _keepArchiveCapacity, value);
    }

    /// <summary>窓の文（試験が読む。取り返しがつかないことを最後の行に言う）。</summary>
    public const string Body =
        "表示・画面の幅・操作・ショートカット・取り込みの動き・画像・通信の間隔の設定を、すべて既定に戻します。\n\n"
        + "取り込み元・監視するフォルダ・非表示にしたもの・保存先と、画面にない細かな調整は、そのままです。保管している通知も消えません。\n\n"
        + "この操作は元に戻せません。";

    public const string KeepArchiveCapacityText = "通知の保管に使う容量はそのままにする";

    /// <summary>
    /// 窓を出す代わりに答える口。**アプリでは null のまま**（窓を出す）。試験と ViewShot が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）
    /// </summary>
    internal static Func<ResetSettingsDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>窓で聞く。「既定に戻す」なら true（チェックは <see cref="KeepArchiveCapacity"/> に残る）。</summary>
    public static bool Ask(ResetSettingsDialogViewModel model)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.ResetSettingsDialog(model).ShowDialog() == true;
}
