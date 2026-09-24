namespace BoothAssetManager.App.ViewModels;

/// <summary>アバターの管理：右の詳細の見直し（ユーザ指示 2026-09-17）。所有・共通素体・呼ばれ方・アバターかどうか。</summary>
public sealed partial class AvatarsViewModel
{
    // ---- 所有 ----

    /// <summary>取り込んだファイル（またはフォルダ）でこのアバターを持っているか。持っていれば所有は固定で、切り替えは出さない。</summary>
    public bool IsOwnedByFile => Selected is { } row && _main.Search.OwnedItemIds().Contains(row.ItemId);

    /// <summary>手動の所有の切り替えを出すか。取り込んだファイルで持っていないときだけ（本体を取り込んでいない・BOOTH外で入手した）。</summary>
    public bool ShowsOwnedToggle => Selected is not null && !IsOwnedByFile;

    // ---- 共通素体 ----

    /// <summary>まだ無い名前を打ったときだけ、Enter で新しい素体を作ることを言う（黙ってグループを増やさない）。</summary>
    public string NewBaseHint
    {
        get
        {
            var typed = BaseInput.Trim();
            return typed.Length == 0
                   || string.Equals(typed, Selected?.Summary.Entry.BaseName, StringComparison.CurrentCultureIgnoreCase)
                   || BaseNames.Any(name => string.Equals(name, typed, StringComparison.CurrentCultureIgnoreCase))
                ? string.Empty
                : $"Enterで新しい素体『{typed}』を作って入れます";
        }
    }

    public bool HasNewBaseHint => NewBaseHint.Length > 0;

    // ---- 呼ばれ方 ----

    /// <summary>畳んだ見出し。件数を添える。</summary>
    public string AliasesTitle => $"呼ばれ方（{Aliases.Count}）";

    private static bool _aliasesExpanded;

    /// <summary>呼ばれ方を開いているか。アプリを閉じるまでアバターをまたいで覚える（普段は畳む）。</summary>
    public bool IsAliasesExpanded
    {
        get => _aliasesExpanded;
        set
        {
            if (_aliasesExpanded != value)
            {
                _aliasesExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 消した表記。商品の対応アバターと同じく、戻せるように出す（ユーザ指示 2026-09-17：消したものを自分で戻せなかった）。
    /// 手で足した表記は消すと行ごと無くなるので、ここに出るのは検出が見つけた表記だけ。
    /// </summary>
    public IReadOnlyList<string> RejectedAliases => Selected?.Summary.Entry.Aliases
        .Where(alias => alias.Rejected)
        .Select(alias => alias.Text)
        .ToList() ?? [];

    public bool HasRejectedAliases => RejectedAliases.Count > 0;

    private async Task RestoreAliasAsync(string? text)
    {
        if (Selected is null || text is null)
        {
            return;
        }

        // 消した表記を足し直すと、印を下ろすだけになる（AvatarServiceEditing.AddAliasAsync）
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.AddAvatarAlias(Selected.ItemId, text));
        await LoadAsync();
    }

    // ---- アバターかどうか ----

    /// <summary>
    /// 今の扱い。以前は「自動（今の判定：…）」のボタンが状態の表示も兼ねていて、押す物か読む物かが分からなかった（ユーザ指示 2026-09-17）。
    /// </summary>
    public string JudgementText
    {
        get
        {
            if (Selected is null)
            {
                return string.Empty;
            }

            return Selected.Summary.Entry.AvatarOverride switch
            {
                true => "今の扱い：アバター（手動で指定）",
                false => "今の扱い：アバターとして扱わない（手動で指定）",
                null => Core.Services.AvatarService.IsAvatar(Selected.Summary.Entry)
                    ? "今の扱い：アバター（自動の判定）"
                    : "今の扱い：アバターとして扱わない（自動の判定）",
            };
        }
    }
}
