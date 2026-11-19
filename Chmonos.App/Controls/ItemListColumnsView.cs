using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Controls;

/// <summary>
/// 商品のリストの列を、画面の <see cref="ItemListColumns"/> から組む（ユーザ判断 2026-11-20：出す列と順を自分で選ぶ）。
///
/// 一覧に <c>controls:ItemListColumnsView.Columns="{Binding ListColumns}"</c> と付けると、その一覧の GridView をここで作る。
/// - **出さない列は列ごと外す**（幅0で残すと、選ぶ列でチェックがはみ出して絵に重なった前例がある）。
/// - 見出しの右クリックで出す列を選ぶ。メニューは検索の「条件を追加」と同じ見出しで分ける（<see cref="ItemListColumnCatalog.Headings"/>）。
/// - 見出しのドラッグで並べ替えた順を覚える。選ぶ列は左端に戻す（列ではなく操作なので）。
/// 文字だけの列は型を XAML に1つずつ書かず、列の一覧から作る（列は30余りあり、どれも同じ形なので）
/// </summary>
public static class ItemListColumnsView
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.RegisterAttached(
        "Columns", typeof(ItemListColumns), typeof(ItemListColumnsView), new PropertyMetadata(null, OnColumnsChanged));

    public static ItemListColumns? GetColumns(DependencyObject element) => (ItemListColumns?)element.GetValue(ColumnsProperty);

    public static void SetColumns(DependencyObject element, ItemListColumns? value) => element.SetValue(ColumnsProperty, value);

    private static readonly DependencyProperty HostProperty = DependencyProperty.RegisterAttached(
        "Host", typeof(Host), typeof(ItemListColumnsView));

    private static void OnColumnsChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ListView list)
        {
            return;
        }

        if (list.GetValue(HostProperty) is not Host host)
        {
            host = new Host(list);
            list.SetValue(HostProperty, host);
        }

        host.Attach((ItemListColumns?)e.NewValue);
    }

    /// <summary>メニューの名前（読み上げ・自動操作）。</summary>
    public const string MenuAutomationId = "ItemListColumnsMenu";

    /// <summary>
    /// 一覧1つぶんの列。画面の ViewModel（<see cref="ItemListColumns"/>）は一覧より長く生きることがあるので、
    /// 並びの知らせは弱い参照で聞く（強く聞くと、閉じた画面の一覧を放せない）
    /// </summary>
    private sealed class Host
    {
        private readonly ListView _list;
        private readonly GridView _view;
        private readonly Dictionary<GridViewColumn, string> _keys = [];
        private ItemListColumns? _columns;
        private bool _syncing;

        /// <summary>右クリックで出した列を次に置く所。続けて出すと、出した列の右へ順に並ぶ。</summary>
        private string? _anchor;

        /// <summary>
        /// 「この列を左へ・右へ」で動かす列＝メニューを開いた見出しの列。<see cref="_anchor"/> と違い、列を出しても動かさない
        /// （出した列に移ると、「この列」が押した見出しでなくなる）
        /// </summary>
        private string? _target;

        private const string MoveLeftId = MenuAutomationId + ".MoveLeft";
        private const string MoveRightId = MenuAutomationId + ".MoveRight";

        public Host(ListView list)
        {
            _list = list;
            _view = new GridView { AllowsColumnReorder = true, ColumnHeaderContextMenu = BuildMenu() };
            _view.Columns.CollectionChanged += OnColumnsCollectionChanged;
            list.View = _view;
        }

        public void Attach(ItemListColumns? columns)
        {
            if (_columns is not null)
            {
                WeakEventManager<ItemListColumns, EventArgs>.RemoveHandler(_columns, nameof(ItemListColumns.OrderChanged), OnOrderChanged);
            }

            _columns = columns;
            if (columns is not null)
            {
                WeakEventManager<ItemListColumns, EventArgs>.AddHandler(columns, nameof(ItemListColumns.OrderChanged), OnOrderChanged);
            }

            Rebuild(force: true);
        }

        private void OnOrderChanged(object? sender, EventArgs e) => Rebuild(force: false);

        /// <summary>覚えた並びに列を合わせる。同じ並びなら何もしない（並べ替えた直後に作り直して、見出しがちらつかないように）。</summary>
        private void Rebuild(bool force)
        {
            var order = _columns?.Order ?? [];
            if (!force && _view.Columns.Select(column => _keys[column]).SequenceEqual(order, StringComparer.Ordinal))
            {
                return;
            }

            _syncing = true;
            try
            {
                _view.Columns.Clear();
                _keys.Clear();
                if (_columns is null)
                {
                    return;
                }

                foreach (var key in order)
                {
                    if (ItemListColumnCatalog.Find(key) is { } definition)
                    {
                        var column = CreateColumn(_columns, definition);
                        _keys[column] = key;
                        _view.Columns.Add(column);
                    }
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private GridViewColumn CreateColumn(ItemListColumns columns, ItemListColumnDef definition)
        {
            var column = new GridViewColumn
            {
                Header = columns.HeaderOf(definition.Key),
                CellTemplate = definition.Cell == ItemListCell.Template
                    ? (DataTemplate)_list.FindResource(definition.Template!)
                    : TextTemplate(definition),
            };
            BindingOperations.SetBinding(column, GridViewColumn.WidthProperty, new Binding(nameof(ItemListColumnWidth.Width))
            {
                Source = columns.WidthOf(definition.Key),
                Mode = BindingMode.TwoWay,
            });
            return column;
        }

        /// <summary>
        /// 見出しをドラッグで並べ替えた。並べ替えの途中（知らせの中）で列を動かすと例外になるので、覚えて合わせ直すのは後で
        /// （選ぶ列を左端へ戻すのもそこで）
        /// </summary>
        private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncing || e.Action != NotifyCollectionChangedAction.Move || _columns is not { } columns)
            {
                return;
            }

            var order = _view.Columns.Select(column => _keys[column]).ToList();
            _list.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                if (ReferenceEquals(columns, _columns))
                {
                    columns.Reorder(order);
                }
            });
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            AutomationProperties.SetAutomationId(menu, MenuAutomationId);
            AutomationProperties.SetName(menu, "表示する列");
            menu.Opened += (_, _) =>
            {
                // 右クリックした見出しの列の右に出す。見出しの無い右の余白なら右端
                // Shift+F10・メニューキーで開いた時も、焦点のある見出しが PlacementTarget になる（WPF の既定）ので同じ列を指す
                _anchor = (menu.PlacementTarget as GridViewColumnHeader)?.Column is { } clicked && _keys.TryGetValue(clicked, out var key) ? key : null;
                _target = _anchor;
                FillMenu(menu);
            };
            return menu;
        }

        private void FillMenu(ContextMenu menu)
        {
            menu.Items.Clear();
            if (_columns is not { } columns)
            {
                return;
            }

            // キーボードだけでも並べ替えられるように（ドラッグの代わり。ユーザ判断 2026-11-20）。端の列では押せない
            AddMoveItem(menu, "この列を左へ", MoveLeftId, -1);
            AddMoveItem(menu, "この列を右へ", MoveRightId, 1);
            menu.Items.Add(new Separator());

            var index = 0;
            foreach (var heading in ItemListColumnCatalog.Headings)
            {
                var group = new MenuItem { Header = heading };
                AutomationProperties.SetAutomationId(group, $"{MenuAutomationId}.{index++}");
                foreach (var definition in ItemListColumnCatalog.All.Where(column => column.Heading == heading))
                {
                    var item = new MenuItem
                    {
                        Header = definition.MenuLabel,
                        IsCheckable = true,

                        // 続けて何列も選べるよう、押しても閉じない
                        StaysOpenOnClick = true,
                        Tag = definition.Key,
                    };
                    AutomationProperties.SetAutomationId(item, $"ItemListColumn.{definition.Key}");
                    item.Click += (_, _) => OnMenuItemClick(menu, item);
                    group.Items.Add(item);
                }

                menu.Items.Add(group);
            }

            RefreshMenu(menu, columns);
        }

        private void AddMoveItem(ContextMenu menu, string header, string automationId, int direction)
        {
            var item = new MenuItem { Header = header, Tag = direction };
            AutomationProperties.SetAutomationId(item, automationId);
            item.Click += (_, _) =>
            {
                if (_columns is { } columns && _target is { } key)
                {
                    columns.Move(key, direction);
                }
            };
            menu.Items.Add(item);
        }

        private void OnMenuItemClick(ContextMenu menu, MenuItem item)
        {
            if (_columns is not { } columns || item.Tag is not string key)
            {
                return;
            }

            columns.SetShown(key, item.IsChecked, _anchor);
            if (item.IsChecked)
            {
                _anchor = key;
            }

            RefreshMenu(menu, columns);
        }

        /// <summary>チェックと押せるかを今の並びに合わせる（最後の1列は外せない）。</summary>
        private void RefreshMenu(ContextMenu menu, ItemListColumns columns)
        {
            foreach (var move in menu.Items.OfType<MenuItem>().Where(item => item.Tag is int))
            {
                move.IsEnabled = _target is { } target && columns.CanMove(target, (int)move.Tag);
            }

            foreach (var item in menu.Items.OfType<MenuItem>().SelectMany(group => group.Items.OfType<MenuItem>()))
            {
                var key = (string)item.Tag;
                item.IsChecked = columns.IsShown(key);
                item.IsEnabled = !item.IsChecked || columns.CanHide(key);
                item.ToolTip = item.IsEnabled ? null : "ほかの列を表示すると外せます。";
                ToolTipService.SetShowOnDisabled(item, true);
            }
        }

        private static readonly Dictionary<string, DataTemplate> s_textTemplates = new(StringComparer.Ordinal);

        /// <summary>
        /// 文字だけの列の型（列ごとに1つを作って使い回す）。前からあるショップの列と同じ形：
        /// 左に区切りの線（<c>ItemListCellFrame</c>）、字は <c>Caption</c>（ショップの画面の入手日は前もこの形で出ていたので、見た目を変えない）、
        /// 切れたら「…」。長くなり得る列だけ、切れた分を吹き出しで全文
        /// </summary>
        private DataTemplate TextTemplate(ItemListColumnDef definition)
        {
            if (s_textTemplates.TryGetValue(definition.Key, out var cached))
            {
                return cached;
            }

            var path = $"{nameof(ItemCardViewModel.ListRow)}.{definition.RowProperty}";
            var frame = new FrameworkElementFactory(typeof(Border));
            frame.SetValue(FrameworkElement.StyleProperty, _list.FindResource("ItemListCellFrame"));

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(path) { Mode = BindingMode.OneWay });
            text.SetValue(FrameworkElement.StyleProperty, _list.FindResource("Caption"));
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (definition.Cell == ItemListCell.Number)
            {
                text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            }

            if (definition.HasToolTip)
            {
                // 空の文字の吹き出しは小さな空の枠として出てしまうので、空なら出さない
                text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(path) { Mode = BindingMode.OneWay, Converter = EmptyToNull.Instance });
            }

            frame.AppendChild(text);
            var template = new DataTemplate { VisualTree = frame };
            template.Seal();
            s_textTemplates[definition.Key] = template;
            return template;
        }
    }

    private sealed class EmptyToNull : IValueConverter
    {
        public static EmptyToNull Instance { get; } = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string { Length: > 0 } text ? text : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
