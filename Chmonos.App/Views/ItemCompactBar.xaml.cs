using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 商品ページと編集の画面の左に重ねる1行の見出し（小さな絵・星・商品名・ショップ名）。
/// 名前とショップ名は外から渡す：編集の画面は、入力欄に打った名前をそのまま映す頭の段（EditViewModel.Name）と揃えるため、
/// 商品ページの値（ItemViewModel.Name）とは出どころが違う。星と絵はどちらも商品ページの持ち主（<see cref="Page"/>）から引く
/// </summary>
public partial class ItemCompactBar : UserControl
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(ItemViewModel), typeof(ItemCompactBar));

    public static readonly DependencyProperty ItemNameProperty = DependencyProperty.Register(
        nameof(ItemName), typeof(string), typeof(ItemCompactBar));

    public static readonly DependencyProperty ShopNameProperty = DependencyProperty.Register(
        nameof(ShopName), typeof(string), typeof(ItemCompactBar));

    /// <summary>ショップ名を押したときの命令。無ければ（編集の画面）ショップ名はただの字。</summary>
    public static readonly DependencyProperty ShopCommandProperty = DependencyProperty.Register(
        nameof(ShopCommand), typeof(ICommand), typeof(ItemCompactBar),
        new PropertyMetadata(null, (sender, _) => ((ItemCompactBar)sender).HasShopCommand = ((ItemCompactBar)sender).ShopCommand is not null));

    private static readonly DependencyPropertyKey HasShopCommandKey = DependencyProperty.RegisterReadOnly(
        nameof(HasShopCommand), typeof(bool), typeof(ItemCompactBar), new PropertyMetadata(false));

    public static readonly DependencyProperty HasShopCommandProperty = HasShopCommandKey.DependencyProperty;

    /// <summary>自動操作の名前の頭（商品ページは「Item」、編集の画面は「Edit」）。同じ部品が2つの画面に出るので分ける。</summary>
    public static readonly DependencyProperty IdPrefixProperty = DependencyProperty.Register(
        nameof(IdPrefix), typeof(string), typeof(ItemCompactBar), new PropertyMetadata("Item"));

    public ItemCompactBar()
    {
        InitializeComponent();

        // 中の結び付けはこの部品の値を見る（外の DataContext は画面ごとに違う）
        Root.DataContext = this;
    }

    public ItemViewModel? Page
    {
        get => (ItemViewModel?)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public string? ItemName
    {
        get => (string?)GetValue(ItemNameProperty);
        set => SetValue(ItemNameProperty, value);
    }

    public string? ShopName
    {
        get => (string?)GetValue(ShopNameProperty);
        set => SetValue(ShopNameProperty, value);
    }

    public ICommand? ShopCommand
    {
        get => (ICommand?)GetValue(ShopCommandProperty);
        set => SetValue(ShopCommandProperty, value);
    }

    public bool HasShopCommand
    {
        get => (bool)GetValue(HasShopCommandProperty);
        private set => SetValue(HasShopCommandKey, value);
    }

    public string IdPrefix
    {
        get => (string)GetValue(IdPrefixProperty);
        set => SetValue(IdPrefixProperty, value);
    }
}
