using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>未確定画面：展開した中身の見分けとフォルダごとの登録（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ResolveViewModel
{
    /// <summary>判定はフォルダ単位で同じになるので、フォルダをキーに覚えておく。</summary>
    private ArchiveContentJudgement JudgeCached(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (_judgements.TryGetValue(directory, out var cached))
        {
            return cached;
        }

        var judgement = ArchiveContentDetector.Judge(path);
        _judgements[directory] = judgement;
        return judgement;
    }

    /// <summary>展開物とみなせるものの件数。0なら案内も出さない。</summary>
    public int ArchiveContentCount => Files.Count(row => row.IsArchiveContent);

    public bool HasArchiveContent => ArchiveContentCount > 0;

    public string ArchiveContentText
    {
        get
        {
            var rows = Files.Where(row => row.IsArchiveContent).ToList();
            if (rows.Count == 0)
            {
                return string.Empty;
            }

            var folders = rows
                .Select(row => Path.GetFileName(row.ProductFolder ?? string.Empty))
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var where = folders.Count switch
            {
                0 => string.Empty,
                1 => $"（{folders[0]} の中）",
                _ => $"（{string.Join("・", folders.Take(3))}{(folders.Count > 3 ? " ほか" : string.Empty)} の中）",
            };

            return $"配布物を展開した中身とみなせるものが {rows.Count} 件あります{where}。";
        }
    }

    /// <summary>1件でも理由を見せる。まとめて外す前に何を根拠にしたかが分かるように。</summary>
    public string ArchiveContentReason =>
        Files.FirstOrDefault(row => row.IsArchiveContent)?.ContentReason ?? string.Empty;

    /// <summary>展開物とみなしたものだけを選ぶ。外すかどうかは見てから決めてもらう。</summary>
    private void SelectArchiveContent()
    {
        foreach (var row in Files.Where(row => row.IsArchiveContent))
        {
            row.IsSelected = true;
        }
    }

    /// <summary>
    /// 登録の対象にするフォルダ。
    /// 取り込み元フォルダの直下の子を選ぶ（zipが展開されたときの単位と一致するため）。
    /// 取り込み元が分からなければ、目印のあるフォルダをそのまま使う。
    /// </summary>
    public string? RegisterTargetFolder
    {
        get
        {
            var row = Selected ?? Files.FirstOrDefault(entry => entry.IsArchiveContent);
            if (row?.ProductFolder is not { } marker || row.File.Paths.Count == 0)
            {
                return null;
            }

            var path = row.File.Paths[0];

            foreach (var root in _services.Settings.ImportFolders)
            {
                var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
                if (!path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rest = path[(normalizedRoot.Length + 1)..];
                var firstSegment = rest.Split(Path.DirectorySeparatorChar)[0];
                return Path.Combine(normalizedRoot, firstSegment);
            }

            return ClimbSingleChildFolders(marker);
        }
    }

    /// <summary>
    /// 取り込み元が分からないときの当て。
    /// 「そのフォルダしか入っていない親」が続く限り遡る。
    /// zipを展開すると rurune_v1.1.3/rurune のように1段包まれることが多く、
    /// 配布の単位は外側だから。中に他のものが混ざった時点で止める。
    /// </summary>
    private static string ClimbSingleChildFolders(string folder)
    {
        var current = folder;

        for (var depth = 0; depth < 4; depth++)
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                return current;
            }

            try
            {
                var entries = Directory.EnumerateFileSystemEntries(parent).Take(2).ToList();
                if (entries.Count != 1)
                {
                    return current;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return current;
            }

            current = parent;
        }

        return current;
    }

    public string RegisterTargetName => Path.GetFileName(RegisterTargetFolder ?? string.Empty);

    public bool CanRegisterFolder => RegisterTargetFolder is not null && HasPreview && !IsBusy;

    public string RegisterFolderText => RegisterTargetName.Length > 0
        ? $"「{RegisterTargetName}」をこの商品として登録"
        : "このフォルダをこの商品として登録";

    /// <summary>
    /// フォルダを商品に紐付ける。zipを落とし直せない場合の受け皿。
    /// 紐付けると配下がスキャン対象から外れるので、未確定も一緒に片付く。
    /// </summary>
    private async Task RegisterFolderAsync()
    {
        if (RegisterTargetFolder is not { } folder || Preview is null)
        {
            return;
        }

        var (count, bytes) = RegisteredFolderSet.Measure(folder);

        var answer = System.Windows.MessageBox.Show(
            $"次のフォルダを「{Preview.Name}」（ID {Preview.Id}）として登録します。\n\n"
            + $"{folder}\n{count} ファイル / {Core.Models.DisplayText.Size(bytes)}\n\n"
            + "以降このフォルダの中はスキャンしなくなり、未確定にも出てこなくなります。\n"
            + "フォルダを移動するとリンクが切れるので、その場合は登録し直してください。",
            "フォルダを商品として登録",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.RegisterFolder(Preview.Id, folder));

            if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
                OnPropertyChanged(nameof(HasStatus));
                return;
            }

            if (!_settledItemIds.Contains(Preview.Id))
            {
                _settledItemIds.Add(Preview.Id);
            }

            // itemの中身が変わったので、持ち回っているライブラリも読み直す。
            // これをしないと商品ページに登録したフォルダが出てこない
            await _main.ReloadLibraryAsync();

            await ReloadAsync();
            StatusText = $"「{RegisterTargetName}」を登録しました。配下の未確定は一覧から外れます。";
            OnPropertyChanged(nameof(HasStatus));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
