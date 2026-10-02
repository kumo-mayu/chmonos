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
