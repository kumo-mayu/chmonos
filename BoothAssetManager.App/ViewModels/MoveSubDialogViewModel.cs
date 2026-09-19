using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// サブレベルの移動先を尋ねるダイアログ。
///
/// 移動は滅多に使わないので、入力欄を常に出しておくと画面が重くなる。
/// ボタンから開いて、ここで移動先と「空になったトップの扱い」を決める。
///
/// 空になったトップをどうするかをアプリで決めないのは、そのトップが
/// このサブのためだけに付いていたとは限らないため（サブなしの単独指定もあり得る）。
/// </summary>
public sealed class MoveSubDialogViewModel : ViewModelBase
{
    private readonly IUserTagService _userTags;

    private string? _target;
    private MoveSubPreview? _preview;
    private bool _dropEmptySourceTop;

    public MoveSubDialogViewModel(IUserTagService userTags, string fromTop, string sub, IReadOnlyList<string> targets)
    {
        _userTags = userTags;
        FromTop = fromTop;
        Sub = sub;
        Targets = targets;

        PickTargetCommand = new RelayCommand(parameter => PickAsync(parameter as string).Forget());
    }

    public string FromTop { get; }

    public string Sub { get; }

    public IReadOnlyList<string> Targets { get; }

    public RelayCommand PickTargetCommand { get; }

    public string HeadingText => $"「{Sub}」を「{FromTop}」から別の大分類へ移します。";

    public string? Target
    {
        get => _target;
        private set
        {
            if (SetField(ref _target, value))
            {
                OnPropertyChanged(nameof(HasTarget));
                OnPropertyChanged(nameof(TargetText));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasTarget => !string.IsNullOrEmpty(Target);

    public string TargetText => HasTarget ? $"移動先： {Target}" : "移動先をまだ選んでいません";

    /// <summary>移動後に何が起きるか。押す前に見えていないと判断できない。</summary>
    public string ImpactText
    {
        get
        {
            if (_preview is null)
            {
                return string.Empty;
            }

            if (_preview.ItemCount == 0)
            {
                return "どの商品にも付いていないので、商品側の書き換えはありません。";
            }

            var lines = new List<string> { $"{_preview.ItemCount} 件の商品を書き換えます。" };

            if (_preview.ItemsGainingTop > 0)
            {
                lines.Add($"うち {_preview.ItemsGainingTop} 件には「{Target}」が新しく付きます。");
            }

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
        set => SetField(ref _dropEmptySourceTop, value);
    }

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

    private async Task PickAsync(string? name)
    {
        var target = name?.Trim();
        if (string.IsNullOrEmpty(target)
            || string.Equals(target, FromTop, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        _preview = await _userTags.PreviewMoveSubAsync(FromTop, Sub, target);

        RunOnUiThread(() =>
        {
            Target = target;
            OnPropertyChanged(nameof(ImpactText));
            OnPropertyChanged(nameof(AsksAboutEmptySourceTop));
            OnPropertyChanged(nameof(EmptySourceTopText));
        });
    }
}
