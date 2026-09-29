using System.Collections.ObjectModel;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 二重計上に見えるかもしれない購入記録1件。**既定では移す。**
///
/// 疑いは金額の一致だけで、確かめようがない。勝手に落とすと支出が黙って減るので、
/// 「これは同じ買い物だ」と人が言ったものだけ落とす。
/// </summary>
public sealed class DuplicateRow : ViewModelBase
{
    private bool _isDuplicate;

    public required int Index { get; init; }

    public required string Text { get; init; }

    /// <summary>同じ買い物だと判断したか。印を付けたものは移さない。</summary>
    public bool IsDuplicate
    {
        get => _isDuplicate;
        set => SetField(ref _isDuplicate, value);
    }
}

/// <summary>
/// 商品のIDを変える。
///
/// 仮IDで登録したものの本物のIDが後から分かったときに使う。
/// **押す前に、何が移って何が移らないかを全部出す。**
///
/// 「この商品から外す」とは別の操作にしてある。あちらは*ファイル*を動かすもので、
/// こちらは*商品ごと*を動かすもの。同じボタンに畳むと、押した結果が
/// 「メモが残る／残らない」で変わることになる。
/// </summary>
public sealed class ChangeItemIdDialogViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;

    private string _idInput = string.Empty;
    private ItemIdChangePlan? _plan;
    private string _status = string.Empty;
    private bool _isBusy;

    public ChangeItemIdDialogViewModel(AppServiceContainer services, string fromId, string currentName)
    {
        _services = services;
        FromId = fromId;
        CurrentName = currentName;

        // 下見が出ている間は押せない。同じIDをもう一度BOOTHに聞かないため（入力を打ち直せば下見は捨てられ、また押せる）。
        // 確かめられなかった下見だけは押せる——答えを得ていないので、つながった後に聞き直す意味がある
        CheckCommand = new RelayCommand(
            () => CheckAsync().Forget(),
            () => CanCheck && !IsBusy && (Plan is null || Plan.TargetOnBooth == ItemIdTargetStatus.Unknown));
    }

    public string FromId { get; }

    public string CurrentName { get; }

    public string HeadingText => $"「{CurrentName}」（ID {FromId}）の中身を、別のIDの商品へ移します。";

    public RelayCommand CheckCommand { get; }

    /// <summary>移し先のID。BOOTHの商品URLを貼っても読む。</summary>
    public string IdInput
    {
        get => _idInput;
        set
        {
            if (SetField(ref _idInput, value))
            {
                // 打ち直したら下見は捨てる。**古い下見のまま押させない**
                Plan = null;
                OnPropertyChanged(nameof(CanCheck));
                OnPropertyChanged(nameof(CommitHint));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanCheck => BoothItemId.Parse(IdInput) is { } parsed
        && !string.Equals(parsed, FromId, StringComparison.Ordinal);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ItemIdChangePlan? Plan
    {
        get => _plan;
        private set
        {
            if (SetField(ref _plan, value))
            {
                Duplicates.Clear();
                foreach (var duplicate in value?.Duplicates ?? [])
                {
                    Duplicates.Add(new DuplicateRow
                    {
                        Index = duplicate.Index,
                        Text = $"¥{duplicate.Price:N0}（{DisplayText.PurchaseKindLabel(duplicate.Kind)}）"
                            + $"　移した先にも同じ金額の記録が {duplicate.MatchingAtTarget} 件あります",
                    });
                }

                foreach (var name in new[]
                {
                    nameof(HasPlan), nameof(CommitHint), nameof(TargetText), nameof(MovingText), nameof(Dropped),
                    nameof(HasDropped), nameof(HasDuplicates), nameof(NotOnBoothText),
                    nameof(IsNotOnBooth), nameof(IsEmptySource),
                })
                {
                    OnPropertyChanged(name);
                }

                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasPlan => Plan is not null;

    /// <summary>
    /// 押せないときに、何が足りないかを書く（`ui-dialogs.md`・E9）。
    /// **下見を見ないと押せない**のがこの窓の要で、そこが出ていないと塞がれた理由が分からない。
    /// </summary>
    public string CommitHint => HasPlan
        ? string.Empty
        : CanCheck
            ? "「調べる」を押すと、何が移るかを表示します。それから押せます。"
            : "移し先の商品ID（またはBOOTHの商品ページのURL）を入れてください。";

    public string Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    /// <summary>移し先の姿。何に移るのかを名前で見せる。</summary>
    public string TargetText => Plan switch
    {
        null => string.Empty,
        { TargetExistsLocally: true } plan => $"移し先：{plan.TargetName}（ID {plan.ToId}）。手元にある商品です。",
        { TargetFoundOnBooth: true } plan => $"移し先：ID {plan.ToId}。移すときにBOOTHから商品情報を取得します。",
        var plan => $"移し先：ID {plan.ToId}",
    };

    /// <summary>**BOOTHで見つからなくても・確かめられなくても止めない。**ただし黙って進めない。</summary>
    public bool IsNotOnBooth => Plan is { TargetExistsLocally: false, TargetFoundOnBooth: false };

    /// <summary>
    /// 届かなかっただけのときは「見つからない」と言わない。移せば商品情報は次の取得で取りに行くので、
    /// 「取得できません」と言うと事実と違う（点検 2026-09-29・19）
    /// </summary>
    public string NotOnBoothText => Plan?.TargetOnBooth == ItemIdTargetStatus.Unknown
        ? "BOOTHにつながらず、確かめられませんでした。このまま移すと、商品情報は後で取得します。"
        : "BOOTHでは見つかりませんでした。このIDでも登録できますが、商品情報は取得できません。";

    public bool IsEmptySource => Plan is { IsEmpty: true };

    public string MovingText
    {
        get
        {
            if (Plan is not { } plan)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (plan.FileCount > 0)
            {
                parts.Add($"ファイル {plan.FileCount} 件");
            }

            if (plan.FolderCount > 0)
            {
                parts.Add($"フォルダ {plan.FolderCount} 件");
            }

            if (plan.PurchaseCount > 0)
            {
                parts.Add($"購入記録 {plan.PurchaseCount} 件");
            }

            parts.Add("メモ・ユーザータグ・属性・入手日");

            return "移すもの：" + string.Join("　", parts);
        }
    }

    /// <summary>移せないもの。**名指しで出す。**件数だけにしない。</summary>
    public IReadOnlyList<string> Dropped
        => Plan?.Dropped.Select(thing => thing.Text).ToList() ?? [];

    public bool HasDropped => Dropped.Count > 0;

    public ObservableCollection<DuplicateRow> Duplicates { get; } = [];

    public bool HasDuplicates => Duplicates.Count > 0;

    /// <summary>移さない購入記録の番号。呼び出し側が実行するときに渡す。</summary>
    public IReadOnlySet<int> SkippedPurchases
        => Duplicates.Where(row => row.IsDuplicate).Select(row => row.Index).ToHashSet();

    /// <summary>移し先のID（検査済み）。</summary>
    public string ToId => BoothItemId.Parse(IdInput) ?? string.Empty;

    /// <summary>
    /// 下見を取る。**ここでは何も書かない。**
    /// 手元に無いIDならBOOTHへ1度だけ聞きに行く。
    /// </summary>
    private async Task CheckAsync()
    {
        if (!CanCheck)
        {
            return;
        }

        IsBusy = true;
        Status = "調べています…";
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.PlanItemIdChange(FromId, ToId));

            if (result is CommandResult.ItemIdChangePlanned planned)
            {
                Plan = planned.Plan;
                Status = string.Empty;
            }
            else if (result is CommandResult.Failed failed)
            {
                Plan = null;
                Status = failed.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

}
