namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 未確定画面：登録している間の進み具合（ユーザ指示 2026-09-29）。
///
/// 「このIDで確定する」は、手元に無い商品ならBOOTHから取ってくる（1本ずつ1.5秒の順番待ちで、取り込み中は更に後ろに並ぶ）。
/// 押してから一覧が動くまで何も出ていなかったので、どうなっているのか分からなかった。
/// 押した場所の近くに「登録しています…」と帯を出す。確定の欄で押す物は確定の欄に、「その他」で押す物は「その他」に出す
/// （画面の上に1つだけ置くと、押した所から離れていて見えない）。
/// </summary>
public sealed partial class ResolveViewModel
{
    /// <summary>進み具合を出す場所。</summary>
    private enum RegisteringArea
    {
        None,

        /// <summary>「商品IDを決める」の欄（このIDで確定・まとめて確定）。</summary>
        Decision,

        /// <summary>「その他」の「BOOTHに無い商品として登録する」。</summary>
        Local,

        /// <summary>
        /// フォルダのまま登録。ボタンは元のzipが残っていれば「分かっていること」、zipが無ければ「その他」にあり、
        /// どちらか一方しか出ない（元のzipが残っているかで分かれる）ので、両方に同じ帯を置く。
        /// </summary>
        Folder,
    }

    private RegisteringArea _registeringArea;
    private int _registeringDone;
    private int _registeringTotal;

    public bool IsRegisteringInDecision => _registeringArea == RegisteringArea.Decision;

    public bool IsRegisteringLocal => _registeringArea == RegisteringArea.Local;

    public bool IsRegisteringFolder => _registeringArea == RegisteringArea.Folder;

    /// <summary>2件以上なら件数まで言う。束を登録すると1件ずつ順に掛けるので、止まっていないことが分かるように。</summary>
    public string RegisteringText => _registeringTotal > 1
        ? $"登録しています… {_registeringDone} / {_registeringTotal} 件"
        : "登録しています…";

    /// <summary>件数が分かるときだけ実際の進み具合を出し、1件なら動いていることだけ示す（自動検索の帯と同じ）。</summary>
    public bool HasRegisteringTotal => _registeringTotal > 1;

    public int RegisteringTotal => _registeringTotal;

    public int RegisteringDone => _registeringDone;

    private void StartRegistering(RegisteringArea area, int total)
    {
        _registeringArea = area;
        _registeringTotal = total;
        _registeringDone = 0;
        NotifyRegistering();
    }

    private void StepRegistering(int done)
    {
        _registeringDone = done;
        NotifyRegistering();
    }

    private void EndRegistering() => StartRegistering(RegisteringArea.None, 0);

    private void NotifyRegistering()
    {
        OnPropertyChanged(nameof(IsRegisteringInDecision));
        OnPropertyChanged(nameof(IsRegisteringLocal));
        OnPropertyChanged(nameof(IsRegisteringFolder));
        OnPropertyChanged(nameof(RegisteringText));
        OnPropertyChanged(nameof(HasRegisteringTotal));
        OnPropertyChanged(nameof(RegisteringTotal));
        OnPropertyChanged(nameof(RegisteringDone));
    }
}
