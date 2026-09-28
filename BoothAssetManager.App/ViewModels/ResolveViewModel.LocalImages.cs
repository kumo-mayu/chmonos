using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 未確定画面：BOOTHに無い商品として登録するときに、画像を添える（ユーザ判断 2026-09-29）。
///
/// BOOTHから取らない商品は画像が1枚も無く、検索のカードでも見分けが付かない。「この名前で登録する」の横で画像を選ぶ（ドロップも可）と、
/// 登録と一緒に「自分で足す画像」として入れる。登録の後に足すのは商品ページの「＋」（今ある作り）で、ここでは登録の前だけを受け持つ。
/// 入れ方は商品ページと同じ <see cref="UiCommand.AddUserImage"/>（BOOTHと同じ圧縮を通す）。
/// </summary>
public sealed partial class ResolveViewModel
{
    /// <summary>添える画像（選んだ順）。登録するまでは場所だけを持ち、読むのは登録するとき。</summary>
    public ObservableCollection<LocalImageRow> LocalImages { get; } = [];

    public bool HasLocalImages => LocalImages.Count > 0;

    public string LocalImagesText => $"画像 {LocalImages.Count} 枚を一緒に追加します。";

    public RelayCommand ChooseLocalImagesCommand => _chooseLocalImagesCommand ??= new RelayCommand(ChooseLocalImages, () => HasSelection);

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

    /// <summary>
    /// 選び直したら捨てる。添えた画像は選んでいたファイルの商品のつもりなので、別のファイルの登録に持ち越すと取り違える。
    /// </summary>
    private void ClearLocalImages()
    {
        if (LocalImages.Count == 0)
        {
            return;
        }

        LocalImages.Clear();
        NotifyLocalImages();
    }

    private void NotifyLocalImages()
    {
        OnPropertyChanged(nameof(HasLocalImages));
        OnPropertyChanged(nameof(LocalImagesText));
    }

    /// <summary>
    /// 作った商品に添えた画像を入れる。読めなかった・画像として読めなかった枚数を返す（商品は作れているので、登録は取り消さない）。
    /// </summary>
    private async Task<int> AddLocalImagesToAsync(string itemId, IReadOnlyList<LocalImageRow> images)
    {
        var failed = 0;
        foreach (var image in images)
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(image.Path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed++;
                continue;
            }

            if (await _services.Commands.ExecuteAsync(new UiCommand.AddUserImage(itemId, bytes)) is not CommandResult.UserImageAdded)
            {
                failed++;
            }
        }

        return failed;
    }
}

/// <summary>添える画像1枚。名前だけを見せ、× で外す。</summary>
public sealed record LocalImageRow(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
}
