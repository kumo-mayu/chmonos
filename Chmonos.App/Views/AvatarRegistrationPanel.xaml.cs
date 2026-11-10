using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Views;

/// <summary>
/// 「アバターとして登録」の欄の中身。商品ページと編集画面で同じ部品を使う（DataContext は ItemAvatarRegistration）。
/// 違いは、自動操作の名前の頭（<see cref="IdPrefix"/>）と、「プロファイルを検索で開く」を出すか（<see cref="ShowsProfilesButton"/>）だけ。
/// </summary>
public partial class AvatarRegistrationPanel : UserControl
{
    /// <summary>自動操作の名前の頭。商品ページは Item、編集画面は Edit。</summary>
    public static readonly DependencyProperty IdPrefixProperty =
        DependencyProperty.Register(nameof(IdPrefix), typeof(string), typeof(AvatarRegistrationPanel), new PropertyMetadata("Item"));

    /// <summary>「プロファイルを検索で開く」を出すか。商品ページだけに出す（docs/spec/conversion.md）。</summary>
    public static readonly DependencyProperty ShowsProfilesButtonProperty =
        DependencyProperty.Register(nameof(ShowsProfilesButton), typeof(bool), typeof(AvatarRegistrationPanel), new PropertyMetadata(false));

    public AvatarRegistrationPanel()
    {
        InitializeComponent();
    }

    public string IdPrefix
    {
        get => (string)GetValue(IdPrefixProperty);
        set => SetValue(IdPrefixProperty, value);
    }

    public bool ShowsProfilesButton
    {
        get => (bool)GetValue(ShowsProfilesButtonProperty);
        set => SetValue(ShowsProfilesButtonProperty, value);
    }
}
