using Chmonos.App.Services;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>商品ページの動画の1本（押すとブラウザで YouTube を開く）。</summary>
public sealed class VideoRow : ViewModelBase
{
    private string? _title;
    private bool _isLoading = true;

    public required VideoLink Link { get; init; }

    public string Url => Link.Url;

    public string ThumbnailUrl => Link.ThumbnailUrl;

    public string TitleText => _title ?? (_isLoading ? "タイトルを確かめています…" : "タイトルを取得できませんでした。押すとYouTubeで開きます。");

    public RelayCommand OpenCommand => new(() => Shell.OpenUrl(Url));

    internal async Task LoadTitleAsync(YouTubeInfo youTube)
    {
        _title = await youTube.TitleAsync(Link.VideoId, Link.Url);
        _isLoading = false;
        OnPropertyChanged(nameof(TitleText));
    }
}

/// <summary>
/// 商品ページ：商品説明を畳む・動画の欄（ユーザ指示 2026-09-14）。
/// </summary>
public sealed partial class ItemViewModel
{
    /// <summary>商品説明を開いているか。畳んだかどうかは「BOOTHのタグ」と同じく、商品を移っても保つ（アプリを閉じるまで）。</summary>
    public bool IsDescriptionExpanded
    {
        get => SectionFolds.DescriptionExpanded;
        set
        {
            if (SectionFolds.DescriptionExpanded != value)
            {
                SectionFolds.DescriptionExpanded = value;
                OnPropertyChanged(nameof(IsDescriptionExpanded));
            }
        }
    }

    private IReadOnlyList<VideoRow>? _videos;
    private bool _isVideosExpanded;
    private bool _videoTitlesRequested;

    /// <summary>説明（埋め込み・短い説明・見出しの本文）に載っている YouTube の動画。</summary>
    public IReadOnlyList<VideoRow> Videos => _videos ??= VideoLinks.Find(Item.Booth).Select(link => new VideoRow { Link = link }).ToList();

    public bool HasVideos => Videos.Count > 0;

    /// <summary>見出しに添える件数。無ければその旨を言い、欄は開けない（ユーザ指示）。</summary>
    public string VideosHeaderText => HasVideos ? $"（{Videos.Count} 本）" : "（この商品の説明にYouTubeの動画はありません）";

    /// <summary>
    /// 動画の欄を開いているか。**既定は畳む**（ユーザ指示）。商品ごとに畳んだ状態で始める——
    /// 開いたときに YouTube へタイトルと絵を取りに行くので、開いたままにして商品を移るたびに問い合わせない。
    /// </summary>
    public bool IsVideosExpanded
    {
        get => _isVideosExpanded;
        set
        {
            if (!HasVideos || !SetField(ref _isVideosExpanded, value) || !value || _videoTitlesRequested)
            {
                return;
            }

            _videoTitlesRequested = true;
            foreach (var video in Videos)
            {
                video.LoadTitleAsync(_services.YouTube).Forget();
            }
        }
    }
}
