using System.Windows.Threading;
using BoothAssetManager.Core.Booth;

namespace BoothAssetManager.App.ViewModels;

/// <summary>常設の1行に出す作業の出どころ。</summary>
public enum WorkSource
{
    /// <summary>取り込み。人が始めた作業。</summary>
    Import,

    /// <summary>使っていない間の取得（⑤の残り・アバターの1枚目・⑦の期限）。</summary>
    Background,
}

/// <summary>
/// 今BOOTHに対して何をしているか。アプリに1つだけ。
///
/// 取得は <see cref="BoothClient"/> 1つを通って直列化されているので、
/// どの瞬間も起きていることは1つしかない。だから画面ごとに持たず、これを見る。
/// 常設の1行も、取り込み画面の詳細も、同じここから描く（食い違いようがない）。
/// </summary>
public sealed class BoothActivityViewModel : ViewModelBase
{
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<WorkSource, (string Label, int Done, int Total)> _work = new();
    private BoothActivity _activity = BoothActivity.Idle;

    public BoothActivityViewModel(IBoothClient client, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        client.ActivityChanged += OnActivityChanged;
    }

    public BoothActivity Activity
    {
        get => _activity;
        private set
        {
            if (SetField(ref _activity, value))
            {
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsThrottled));
                NotifyLine();
            }
        }
    }

    /// <summary>
    /// 今の作業を「何を・何件中何件目」で知らせる。**どのスレッドから呼んでもよい。**
    ///
    /// 常設の1行はもともと通信の様子（間隔待ち・送信中）だけを出していたが、
    /// 間隔待ちは1.5秒ごとに必ず起きる決め事で、読んでも何も分からない。
    /// 知りたいのは「何をしていて、あとどれくらいか」なので、取り込み画面と同じく
    /// 作業の名前と件数を出す（ユーザ指示）。
    /// </summary>
    public void ReportWork(WorkSource source, string label, int done, int total)
        => OnUiThread(() =>
        {
            _work[source] = (label, done, total);
            NotifyLine();
        });

    /// <summary>その出どころの作業が終わった。</summary>
    public void EndWork(WorkSource source)
        => OnUiThread(() =>
        {
            if (_work.Remove(source))
            {
                NotifyLine();
            }
        });

    /// <summary>
    /// 1行に出す作業。取り込みと裏の取得が重なったら取り込みを出す——
    /// 取り込みの方が優先順位が上で、実際に通信しているのもそちらだから。
    /// </summary>
    private (string Label, int Done, int Total)? CurrentWork
        => _work.TryGetValue(WorkSource.Import, out var import) ? import
            : _work.TryGetValue(WorkSource.Background, out var background) ? background
            : null;

    /// <summary>
    /// 作業の名前を出すか。**再試行の待ちだけは通信の様子を出す**——
    /// 応答が無くて止まっているのは、作業の名前では分からない唯一の理由なので。
    /// </summary>
    private bool ShowsWork => _activity.Kind != BoothActivityKind.Retrying && CurrentWork is not null;

    public string Text => ShowsWork && CurrentWork is { } work
        ? work.Total > 0 ? $"{work.Label} {work.Done}/{work.Total}" : work.Label
        : _activity.Text;

    /// <summary>
    /// 1行を出すか。**作業が続いている間は、問い合わせの合間でも畳まない。**
    /// 通信は1件ごとに一瞬 Idle に戻るので、通信の様子だけで出し入れすると
    /// 1.5秒ごとに下の高さが変わり、画面の中身が上下に揺れる（ショップ一覧・検索の空表示で見えた）。
    /// </summary>
    public bool IsActive => _activity.IsActive || CurrentWork is not null;

    /// <summary>バーに長さを出せるか。作業の件数か、長さの分かっている待ち。通信そのものは分からないので不定表示になる。</summary>
    public bool HasProgress => ShowsWork ? CurrentWork is { Total: > 0 } : _activity.Progress is not null;

    public double Progress => ShowsWork && CurrentWork is { Total: > 0 } work
        ? Math.Clamp((double)work.Done / work.Total, 0, 1)
        : _activity.Progress ?? 0;

    public bool IsThrottled => _activity.IsThrottled;

    /// <summary>
    /// 閉じても続きから進む作業をしているか。常設の1行の横に、その旨を出すために見る（ユーザ判断 2026-09-21）。
    ///
    /// 取り込みも裏の取得も、途中で閉じて構わない作業（取り込みは押し直し、裏の取得は次の起動で自動）。
    /// **人が押した取り直しだけのときは出さない**——それは押し直さないと進まないので、
    /// 「続きから進みます」が嘘になる。作業として登録されるのは取り込みと裏の取得の2つだけなので、
    /// 作業が在るかどうかがそのまま判定になる。
    /// </summary>
    public bool CanResumeLater => _work.Count > 0;

    /// <summary>
    /// 使っていない間の取得をしているか。取り込み画面にも出す——取り込みではないが、
    /// ユーザからは取り込みの続きに見える（ユーザ判断 2026-09-12）。下の1行を押すと取り込み画面へ行くので、
    /// 行った先で何をしているのか分かるようにする。取り込みと重なっても、こちらはこちらで出す。
    /// </summary>
    public bool HasBackgroundWork => _work.ContainsKey(WorkSource.Background);

    public string BackgroundText => _work.TryGetValue(WorkSource.Background, out var work)
        ? work.Total > 0 ? $"{work.Label} {work.Done}/{work.Total}" : work.Label
        : string.Empty;

    public double BackgroundProgress => _work.TryGetValue(WorkSource.Background, out var work) && work.Total > 0
        ? Math.Clamp((double)work.Done / work.Total, 0, 1)
        : 0;

    private void NotifyLine()
    {
        // 作業があれば通信の合間でも1行を出すので、作業の出入りでも知らせ直す
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(CanResumeLater));
        OnPropertyChanged(nameof(HasBackgroundWork));
        OnPropertyChanged(nameof(BackgroundText));
        OnPropertyChanged(nameof(BackgroundProgress));
    }

    /// <summary>
    /// 取得は別のスレッドから知らせてくるので、UIスレッドへ渡し直す。
    /// 0.2秒ごとに来るだけなので、まとめる必要はない。
    /// </summary>
    private void OnActivityChanged(BoothActivity activity) => OnUiThread(() => Activity = activity);

    private void OnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }
}
