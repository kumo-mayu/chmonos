using System.Windows;
using Chmonos.Api;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace Chmonos.App.Services;

/// <summary>
/// 外から使う API の許しを、画面の窓で人に聞く（plan 31）。API の要求のスレッドから来るので、画面のスレッドへ渡す。
/// 窓は API の側の列が1つずつ頼むので、ここで重ならない。
/// </summary>
/// <param name="mainWindow">主の窓（画面のスレッドで呼ぶ。画面を出さない起動でまだ開いていなければ null）。</param>
internal sealed class ApiClientApprovalPrompt(Func<Window?> mainWindow) : IApiClientApprovals
{
    /// <summary>
    /// 主の窓が見えているときだけ聞く。画面を出さない起動（<c>--headless</c>）で窓が無い・閉じて隠しているときは断る
    /// （まとめ役の判断 2026-11-11：待たせずに断り、「画面を開いてから求めて」と答える）。
    /// 通知領域から窓を開いた後は聞ける（起動の引数ではなく今の窓で決める。待たせる形に変えるなら、ここを変える）
    /// </summary>
    public bool CanAsk
    {
        get
        {
            if (Application.Current?.Dispatcher is not { } dispatcher || dispatcher.HasShutdownStarted)
            {
                return false;
            }

            // 画面のスレッドが塞がっている（閉じる途中など）ときに要求のスレッドを止め続けないよう、2秒で諦めて断る
            var visible = dispatcher.InvokeAsync(() => mainWindow() is { IsVisible: true });
            return visible.Task.Wait(TimeSpan.FromSeconds(2)) && visible.Result;
        }
    }

    public Task<ApiClientDecision> AskAsync(ApiClientRequestInfo request, CancellationToken cancellationToken)
    {
        if (Application.Current?.Dispatcher is not { } dispatcher || dispatcher.HasShutdownStarted)
        {
            return Task.FromResult(ApiClientDecision.Denied);
        }

        return dispatcher.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ApiClientDecision.Denied;
            }

            var model = new ApiClientApprovalViewModel(request);
            var dialog = new ApiClientApprovalDialog(model, mainWindow());

            // API が止まったら（オフにした・閉じる）窓を閉じる。答えは拒否
            using var registration = cancellationToken.Register(() => dispatcher.BeginInvoke(() =>
            {
                if (dialog.IsVisible)
                {
                    dialog.DialogResult = false;
                }
            }));
            return dialog.ShowDialog() == true && !cancellationToken.IsCancellationRequested ? model.Approve() : ApiClientDecision.Denied;
        }).Task;
    }
}
