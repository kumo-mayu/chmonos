using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothZipInspector;

namespace BoothAssetManager.App.ViewModels;

/// <summary>商品ページ：Unityへ送る・改変に足す・使った改変（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ItemViewModel
{
    // 送る・改変に足して送る・選択の中身は ItemUnityActions（カードの右クリックと共用。ユーザ指示 2026-09-19）。
    // ここは商品ページの欄の下の1行へ結果を出すだけ

    /// <summary>
    /// 送っている間の進み具合と「中止」を、この欄の1行に出す（E7・E10）。
    /// Unity 側の読み込みが長いと、押してから窓が出るまで何も起きていないように見えていた。
    /// </summary>
    private UnitySendUi? _sendUi;

    private UnitySendUi SendUi => _sendUi ??= new UnitySendUi(
        sending => IsSendingToUnity = sending,
        text => UnityRecordNotice = text);

    private bool _isSendingToUnity;

    public bool IsSendingToUnity
    {
        get => _isSendingToUnity;
        private set => SetField(ref _isSendingToUnity, value);
    }

    /// <summary>送るのをやめる（`UnityImportQueue.Stop`）。送信は1本ずつなので、どの画面から押しても同じ物が止まる。</summary>
    public RelayCommand StopUnityCommand => _stopUnity ??= new RelayCommand(Services.UnityImportQueue.Stop);

    private RelayCommand? _stopUnity;

    private void SendToUnity(object? parameter)
    {
        if (parameter is Core.Services.UnityPackageEntry package)
        {
            ItemUnityActions.SendAsync(_services, Item, package, SendUi).Forget();
        }
    }

    private async Task SelectInUnityAsync(object? parameter)
    {
        if (parameter is Core.Services.UnityPackageEntry package)
        {
            await ItemUnityActions.SelectAsync(Item, package, Notices.LineOrWindow("Unityで選択", text => UnityRecordNotice = text));
        }
    }

    private async Task SendToUnityWithRecordAsync(object? parameter)
    {
        if (parameter is Core.Services.UnityPackageEntry package)
        {
            await ItemUnityActions.SendWithRecordAsync(
                _services, Item, package, Notices.LineOrWindow("改変に足して送る", text => UnityRecordNotice = text), SendUi);
        }
    }

    /// <summary>
    /// 改変に足す（送らない）。
    ///
    /// **見せる場所と足す場所を同じにする**（ユーザ指摘）。
    /// 使った改変を出しているカードから、そのまま足せるようにした。
    /// </summary>
    private async Task AddToModificationAsync()
    {
        const string title = "改変に足す";

        var model = ModificationPicking.BuildDialog(
            _services,
            title,
            $"「{Item.DisplayName}」を改変に足します。",
            // 送らないので、どのファイルを使ったかは分からない。**推定で埋めない**
            "どのファイルを使ったかは残りません（Unityへ送ると残ります）。",
            (await _services.Modifications.LoadAllAsync()).Modifications,
            existingLabel: "今ある改変に足す",
            commitLabel: "足す",
            emptyText: "改変がまだありません。新しく作って、そこに足せます。");

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return;
        }

        if (await ItemUnityActions.CommitPickedAsync(_services, Item, model, title, project: null, owner: null, package: null)
            is not { } record)
        {
            return;
        }

        UnityRecordNotice = $"「{record.Name}」に足しました。";
        await LoadModificationsAsync();
    }
    private string? _unityRecordNotice;

    /// <summary>
    /// 直前の Unity まわりの結果（改変へ積んだ・Unity で選択した）。
    ///
    /// **積んだことは画面のどこにも出ない。**Unityへ渡した先の反応は
    /// こちらに返ってこないので、記録が入ったことだけは言っておく。「選択」の結果（示した・まだ入っていない）もここで言う
    /// </summary>
    public string? UnityRecordNotice
    {
        get => _unityRecordNotice;
        private set
        {
            if (SetField(ref _unityRecordNotice, value))
            {
                OnPropertyChanged(nameof(HasUnityRecordNotice));
            }
        }
    }

    public bool HasUnityRecordNotice => !string.IsNullOrEmpty(UnityRecordNotice);

    /// <summary>
    /// この商品を使った改変。
    ///
    /// **改変から辿れば分かる情報を商品ページで隠さない。**
    /// 「持っているのに出していない」を直した直後なので、同じ指摘を作らない。
    /// </summary>
    public ObservableCollection<UsedInModificationRowViewModel> UsedInModifications { get; } = [];

    public bool HasUsedInModifications => UsedInModifications.Count > 0;

    public string UsedInModificationsEmptyText =>
        "まだどの改変にも入っていません。下の「改変に足す」で残せます。";

    /// <summary>改変の詳細へ。戻るとこの商品へ帰る（見比べに戻ってくる。画面の履歴・U23）。</summary>
    private void OpenModification(UsedInModificationRowViewModel? row)
    {
        if (row is not null)
        {
            _main.ShowModification(row.Record);
        }
    }

    private async Task LoadModificationsAsync()
    {
        var records = await _services.Modifications.LoadUsingItemAsync(Item.Id);
        var registry = _services.Store.Avatars.Load();

        RunOnUiThread(() =>
        {
            UsedInModifications.Clear();
            foreach (var record in records)
            {
                UsedInModifications.Add(new UsedInModificationRowViewModel
                {
                    Record = record,
                    AvatarText = registry.Entries.FirstOrDefault(entry =>
                        string.Equals(entry.ItemId, record.AvatarItemId, StringComparison.Ordinal))
                        is { } found
                            ? AvatarNames.ShownName(found)
                            : record.AvatarItemId,

                    // 同じ商品を別のバージョンで2回足せるので、何回入っているかを出す
                    UseCount = record.UsedMembers.Count(member =>
                        string.Equals(member.ItemId, Item.Id, StringComparison.Ordinal)),
                });
            }

            OnPropertyChanged(nameof(HasUsedInModifications));
        });
    }

    /// <summary>この商品にUnityへ送れるものが1つでもあるか。無ければ送り先の話もしない。</summary>
    public bool HasAnyUnityPackage => LocalFiles.Any(file => file.HasUnityPackages);

    /// <summary>
    /// 送り先の表示を読み直す。
    ///
    /// **Unityの開き閉じはこのアプリの外で起きる。**画面を組んだときの値を
    /// 持ち続けると、「開いていません」と出したまま実は開いている状態になる。
    /// ウィンドウが手前に戻ったら読み直す（<see cref="MainViewModel.NoteWindowActivated"/>）。
    /// </summary>
    public void NoteUnityChanged() => OnPropertyChanged(nameof(UnityTargetText));

    /// <summary>いま送るとどこへ行くか。押す前に見えている必要がある。</summary>
    public string UnityTargetText
    {
        get
        {
            var editors = Services.UnityEditors.Open();
            return editors.Count switch
            {
                0 => "Unityが開いていません（開いてから送れます）",
                1 => $"送り先：Unityの「{editors[0].ProjectName ?? "名前不明のプロジェクト"}」",
                // 窓を名指しして送るので、複数開いていても送るときに選べる（U14）
                _ => $"Unityが {editors.Count} つ開いています（送るときにどれへ送るか選べます）",
            };
        }
    }
}
