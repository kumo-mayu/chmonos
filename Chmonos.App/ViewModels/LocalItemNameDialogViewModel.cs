namespace Chmonos.App.ViewModels;

/// <summary>
/// ファイルを持たない「BOOTHに無い商品」を名前だけで作るときに、名前を聞く小窓（2026-11-20 のメモ「ファイルの無いlocalItemが作成できない気がする」）。
/// 新しく名付ける欄なので候補は付けない（screen-and-wording の例外）。商品は同じ名前を許すので、同じ名前を断らない
/// </summary>
public sealed class LocalItemNameDialogViewModel : ViewModelBase
{
    // 長さの上限は置かない。未確定の「BOOTHに無い商品として登録する」の名前の欄も置いておらず、入口によって切り方を変えないため
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanCommit));
            }
        }
    }

    public bool CanCommit => _name.Trim().Length > 0;

    // ---- 画像（ユーザ 2026-11-20「画像を入れられるUIである必要がある。機能は未確定画面のlocalアイテム登録と同等」）----
    // 作りは未確定の「BOOTHに無い商品として登録する」（ResolveViewModel.LocalImages）と揃える。登録するまでは場所だけを持ち、読むのは登録の後

    /// <summary>添える画像（選んだ順）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<LocalImageRow> LocalImages { get; } = [];

    public bool HasLocalImages => LocalImages.Count > 0;

    public string LocalImagesText => $"画像 {LocalImages.Count} 枚を一緒に追加します。";

    public RelayCommand ChooseLocalImagesCommand => _chooseLocalImagesCommand ??= new RelayCommand(ChooseLocalImages);

    public RelayCommand RemoveLocalImageCommand => _removeLocalImageCommand ??= new RelayCommand(
        parameter =>
        {
            if (parameter is LocalImageRow row)
            {
                LocalImages.Remove(row);
                NotifyLocalImages();
            }
        },
        parameter => parameter is LocalImageRow);

    private RelayCommand? _chooseLocalImagesCommand;
    private RelayCommand? _removeLocalImageCommand;

    private void ChooseLocalImages()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "この商品に追加する画像を選ぶ",
            Filter = "画像 (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp",
            Multiselect = true,
        };

        if (dialog.ShowDialog() == true)
        {
            AddLocalImages(dialog.FileNames);
        }
    }

    /// <summary>画像を添える（選んだ・落とした）。画像でない物と、既に添えた物は飛ばす。</summary>
    public void AddLocalImages(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(Core.Services.DropRouting.LooksLikeImage))
        {
            if (!LocalImages.Any(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                LocalImages.Add(new LocalImageRow(path));
            }
        }

        NotifyLocalImages();
    }

    private void NotifyLocalImages()
    {
        OnPropertyChanged(nameof(HasLocalImages));
        OnPropertyChanged(nameof(LocalImagesText));
    }

    /// <summary>試験の差し替え口。窓を出すと答える人がいないので止まる（<see cref="ModificationNameDialogViewModel.Intercept"/> と同じ）</summary>
    internal static Func<LocalItemNameDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>窓を出して聞く。確定したら true（名前は <see cref="Name"/>）。</summary>
    public static bool Ask(LocalItemNameDialogViewModel model)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.LocalItemNameDialog(model).ShowDialog() == true;
}
