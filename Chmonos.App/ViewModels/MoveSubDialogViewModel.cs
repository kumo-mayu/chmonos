using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// サブレベルの移動先を尋ねるダイアログ。
///
/// 移動は滅多に使わないので、入力欄を常に出しておくと画面が重くなる。
/// ボタンから開いて、ここで移動先と「空になったトップの扱い」を決める。
///
/// 空になったトップをどうするかをアプリで決めないのは、そのトップが
/// このサブのためだけに付いていたとは限らないため（サブなしの単独指定もあり得る）。
///
/// **大分類を別の大分類の小分類にするときも、この窓を使う**（<see cref="ForNestingTop"/>。ユーザ要望 2026-09-29）。
/// どちらも「入れ先の大分類を1つ選ぶ」操作なので、窓の形を1つにしておく。
/// </summary>
public sealed class MoveSubDialogViewModel : ViewModelBase
{
    private readonly IUserTagService _userTags;

    private string? _target;
    private MoveSubPreview? _preview;
    private NestTopPreview? _nestPreview;
    private bool _dropEmptySourceTop;

    public MoveSubDialogViewModel(IUserTagService userTags, string fromTop, string sub, IReadOnlyList<string> targets)
        : this(userTags, fromTop, targets, sub)
    {
    }

    private MoveSubDialogViewModel(IUserTagService userTags, string fromTop, IReadOnlyList<string> targets, string? sub)
    {
        _userTags = userTags;
        FromTop = fromTop;
        Sub = sub;
        Targets = targets;

        PickTargetCommand = new RelayCommand(parameter => PickAsync(parameter as string).Forget());
    }

    /// <summary>小分類を持たない大分類 <paramref name="top"/> を、選んだ大分類の小分類にする窓。</summary>
    public static MoveSubDialogViewModel ForNestingTop(IUserTagService userTags, string top, IReadOnlyList<string> targets)
        => new(userTags, top, targets, sub: null);

    public string FromTop { get; }

    /// <summary>移す小分類。null なら大分類そのものを小分類にする窓。</summary>
    public string? Sub { get; }

    public bool IsNestingTop => Sub is null;

    public IReadOnlyList<string> Targets { get; }

    public RelayCommand PickTargetCommand { get; }

    public string DialogTitle => IsNestingTop ? "別の大分類の小分類にする" : "小分類を移す";

    public string HeadingText => IsNestingTop
        ? $"「{FromTop}」を、選んだ大分類の小分類にします。"
        : $"「{Sub}」を「{FromTop}」から別の大分類へ移します。";

    public string TargetLabel => IsNestingTop ? "入れ先" : "移動先";

    /// <summary>同じ名前の小分類があれば統合になるので、名前の変更の窓と同じく「統合する」に変える。</summary>
    public string CommitText => IsNestingTop
        ? _nestPreview is { IsMerge: true } ? "統合する" : "小分類にする"
        : "移す";

    public string? Target
    {
        get => _target;
        private set
        {
            if (SetField(ref _target, value))
            {
                OnPropertyChanged(nameof(HasTarget));
                OnPropertyChanged(nameof(TargetText));
                OnPropertyChanged(nameof(UndoText));
                OnPropertyChanged(nameof(CommitHint));
                OnPropertyChanged(nameof(CommitText));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasTarget => !string.IsNullOrEmpty(Target);

    public string TargetText => HasTarget ? $"{TargetLabel}： {Target}" : $"{TargetLabel}をまだ選んでいません";

    /// <summary>押せないときに、何が足りないかを書く（`ui-dialogs.md`・E9）。</summary>
    public string CommitHint => HasTarget ? string.Empty : $"上の欄で{TargetLabel}の大分類を選ぶと押せます。";

    /// <summary>移動後に何が起きるか。押す前に見えていないと判断できない。</summary>
    public string ImpactText => IsNestingTop ? NestImpactText : MoveImpactText;

    private string MoveImpactText
    {
        get
        {
            if (_preview is null)
            {
                return string.Empty;
            }

            if (_preview.ItemCount == 0)
            {
                return "どの商品にも付いていません。";
            }

            var lines = new List<string> { $"{_preview.ItemCount} 件の商品を書き換えます。" };

            if (_preview.ItemsGainingTop > 0)
            {
                lines.Add($"うち {_preview.ItemsGainingTop} 件には「{Target}」が新しく付きます。");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    private string NestImpactText
    {
        get
        {
            if (_nestPreview is null)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            if (_nestPreview.IsMerge)
            {
                lines.Add($"「{Target}」には同じ名前の小分類があるので、統合します。");
            }

            lines.Add(_nestPreview.ItemCount == 0
                ? "どの商品にも付いていません。"
                : $"{_nestPreview.ItemCount} 件の商品を「{Target}」の「{FromTop}」に書き換えます。");

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>元のトップがサブなしで残るitemがあるときだけ、その扱いを聞く。</summary>
    public bool AsksAboutEmptySourceTop => _preview is { ItemsLeavingEmptyTop: > 0 };

    public string EmptySourceTopText => _preview is null
        ? string.Empty
        : $"{_preview.ItemsLeavingEmptyTop} 件の商品では、「{FromTop}」の小分類が1つも残りません。";

    public string KeepSourceTopText => $"「{FromTop}」はそのまま残す";

    public string DropSourceTopText => $"「{FromTop}」も商品から外す";

    /// <summary>
    /// 既定は「残す」。トップが単独で付いていた可能性を消す方が、取り返しがつかない。
    /// </summary>
    public bool DropEmptySourceTop
    {
        get => _dropEmptySourceTop;
        set
        {
            if (SetField(ref _dropEmptySourceTop, value))
            {
                OnPropertyChanged(nameof(UndoText));
            }
        }
    }

    /// <summary>
    /// 取り返しがつくかを、押す前に出す（`ui-rules.md`・D4：属性の統合の窓にだけ出ていた）。
    /// **移すだけなら戻せる。**戻せなくなるのは「大分類も外す」を選んだときで、
    /// 小分類なしで単独に付いていた分まで消え、どの商品がそうだったかを残していない。
    /// 大分類を小分類にしたときは、小分類を大分類へ戻す操作が無いので戻せない。
    /// </summary>
    public string UndoText => !HasTarget
        ? string.Empty
        : IsNestingTop
            ? "小分類にした後は、元に戻せません。"
            : DropEmptySourceTop
                ? $"「{FromTop}」を外した分は元に戻せません。単独で付いていた商品も一緒に外れます。"
                : "移した後は、同じ手順で元の大分類へ戻せます。";

    public bool KeepEmptySourceTop
    {
        get => !_dropEmptySourceTop;
        set
        {
            if (value)
            {
                DropEmptySourceTop = false;
            }
        }
    }

    /// <summary>下見の件数。窓を閉じた後の確認でも同じ数を言う。</summary>
    public NestTopPreview? NestPreview => _nestPreview;

    private async Task PickAsync(string? name)
    {
        var target = name?.Trim();
        if (string.IsNullOrEmpty(target)
            || string.Equals(target, FromTop, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        if (Sub is { } sub)
        {
            _preview = await _userTags.PreviewMoveSubAsync(FromTop, sub, target);
        }
        else
        {
            _nestPreview = await _userTags.PreviewNestTopAsync(FromTop, target);
        }

        RunOnUiThread(() =>
        {
            Target = target;
            OnPropertyChanged(nameof(ImpactText));
            OnPropertyChanged(nameof(CommitText));
            OnPropertyChanged(nameof(AsksAboutEmptySourceTop));
            OnPropertyChanged(nameof(EmptySourceTopText));
        });
    }
}
