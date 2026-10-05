using System.Reflection;
using System.Windows;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// アプリの外からは作れない状態を、作り物で入れる所。**private に手を入れるのはここだけ。**
///
/// 取り込みの結果・一時展開の進み具合・知らせの窓は、実際に動かせば作れるが、動かすと描くだけの道具でなくなる
/// （読めないフォルダを用意する・大きな zip を展開して途中で撮る・窓を出す）。描きたいのは「その値のときの見た目」なので、値を直に入れる。
/// アプリの側に差し替え口を足さないのは、本体の動きを台の都合で変えないため。名前が変わったら、ここが何を探して
/// 見つからなかったかを言って止まる（黙って古い見た目を描かない）。
/// </summary>
internal static class Backdoor
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>取り込みの結果の欄。取り込みが終わったときに入る値を、そのまま入れる。</summary>
    public static void ShowImportSummary(ImportViewModel import, ImportSummary summary)
        => SetProperty(import, nameof(ImportViewModel.Summary), summary);

    /// <summary>
    /// 下の帯の「一時展開」の様子。主画面は、展開が知らせてきたときに今の様子を控える。その控えを直に入れて、変わったことを知らせる。
    /// </summary>
    public static void ShowUnpacking(MainViewModel main, int count, string name, long doneBytes, long totalBytes, bool stopping)
    {
        SetField(main, "_unpacking", new UnpackingStatus(count, name, doneBytes, totalBytes, stopping));
        Raise(
            main,
            nameof(MainViewModel.IsUnpacking),
            nameof(MainViewModel.UnpackText),
            nameof(MainViewModel.UnpackTargetText),
            nameof(MainViewModel.HasUnpackProgress),
            nameof(MainViewModel.UnpackProgress),
            nameof(MainViewModel.CanStopUnpack));
    }

    /// <summary>改変の詳細の上の帯の知らせの文（写真を貼った・使ったものを足した、の後に出る文）。</summary>
    public static void ShowModificationStatus(ModificationViewModel modification, string text)
        => SetProperty(modification, nameof(ModificationViewModel.Status), text);

    /// <summary>「プロジェクトの中を調べる」の結果の1行（調べる処理は zip の中身が要るので走らせない）。</summary>
    public static void ShowProjectFindText(ModificationViewModel modification, string text)
        => SetProperty(modification, nameof(ModificationViewModel.ProjectFindText), text);

    /// <summary>知らせと確認の窓を、出さずに作る（アプリは <c>Services.Notice</c> からしか作らず、作るとすぐ出す）。</summary>
    public static Window NewNotice(
        string text, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var layout = NoticeLayout.For((NoticeButtonSet)button, (NoticeAnswer)defaultResult);
        var constructor = typeof(NoticeWindow).GetConstructor(
            Hidden, [typeof(Window), typeof(string), typeof(string), typeof(NoticeLayout), typeof(MessageBoxImage)])
            ?? throw Missing(typeof(NoticeWindow), "コンストラクタ（持ち主・本文・題・並び・印）");
        return (Window)constructor.Invoke([null, text, caption, layout, icon]);
    }

    /// <summary>
    /// 未確定の「商品IDを決める」の欄で登録している最中の帯。実際に登録するには BOOTH の返事を止める作り物が要るので、控えを直に入れる。
    /// <paramref name="left"/> は BOOTH への問い合わせの残り（目安の時間は本体が間隔から出す）。
    /// </summary>
    public static void ShowRegisteringInDecision(ResolveViewModel resolve, int done, int total, int? left)
    {
        var area = target(resolve, "_registeringArea").FieldType;
        SetField(resolve, "_registeringArea", Enum.Parse(area, "Decision"));
        SetField(resolve, "_registeringDone", done);
        SetField(resolve, "_registeringTotal", total);
        SetField(resolve, "_registeringRequestsLeft", left);
        var notify = typeof(ResolveViewModel).GetMethod("NotifyRegistering", Hidden)
            ?? throw Missing(typeof(ResolveViewModel), "NotifyRegistering()");
        notify.Invoke(resolve, null);

        static FieldInfo target(object owner, string name)
            => owner.GetType().GetField(name, Hidden) ?? throw Missing(owner.GetType(), $"欄 {name}");
    }

    /// <summary>
    /// 未確定の「このIDで登録」の順番待ち（メモ60）。実際に積むと列が走って BOOTH へ行くので、列の中身を直に入れ、走っている印を立てて始めさせない。
    /// 先頭が走っている物（<paramref name="requestsLeft"/> はその残り）、残りは待っている物。<paramref name="failed"/> は「登録に失敗」の札にする行。
    /// </summary>
    public static void ShowRegistrationQueue(
        ResolveViewModel resolve,
        RegistrationQueue queue,
        IReadOnlyList<(string Hash, string ItemId, string Name, int? Estimate)> jobs,
        int? requestsLeft,
        (string Hash, string Reason)? failed)
    {
        SetField(queue, "_running", true);
        var list = (List<RegistrationJob>)(queue.GetType().GetField("_jobs", Hidden)?.GetValue(queue)
            ?? throw Missing(queue.GetType(), "欄 _jobs"));
        foreach (var (hash, itemId, name, estimate) in jobs)
        {
            list.Add(new RegistrationJob
            {
                Record = new Chmonos.Core.Models.QueuedRegistration { ItemId = itemId, ItemName = name, FileHashes = [hash], EstimatedRequests = estimate },
            });
        }

        if (list.Count > 0)
        {
            SetProperty(list[0], nameof(RegistrationJob.IsRunning), true);
            SetProperty(list[0], nameof(RegistrationJob.RequestsLeft), requestsLeft);
            SetProperty(list[0], "Reports", 3);
        }

        if (failed is { } failure)
        {
            var failures = (Dictionary<string, string>)(queue.GetType().GetField("_failures", Hidden)?.GetValue(queue)
                ?? throw Missing(queue.GetType(), "欄 _failures"));
            failures[failure.Hash] = failure.Reason;
        }

        var refresh = typeof(ResolveViewModel).GetMethod("RefreshQueueState", Hidden)
            ?? throw Missing(typeof(ResolveViewModel), "RefreshQueueState()");
        refresh.Invoke(resolve, null);
    }

    private static void SetProperty(object target, string name, object? value)
    {
        var setter = target.GetType().GetProperty(name, Hidden)?.GetSetMethod(nonPublic: true)
            ?? throw Missing(target.GetType(), $"{name} の set");
        setter.Invoke(target, [value]);
    }

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, Hidden) ?? throw Missing(target.GetType(), $"欄 {name}");
        field.SetValue(target, value);
    }

    private static void Raise(ViewModelBase target, params string[] properties)
    {
        var raise = typeof(ViewModelBase).GetMethod("OnPropertyChanged", Hidden, [typeof(string)])
            ?? throw Missing(typeof(ViewModelBase), "OnPropertyChanged(string)");
        foreach (var property in properties)
        {
            raise.Invoke(target, [property]);
        }
    }

    private static InvalidOperationException Missing(Type type, string what)
        => new($"{type.Name} に {what} が見つかりません。本体の名前が変わったので、tools/ViewShot/Backdoor.cs を合わせてください。");
}
