using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// マウスで押せる枠（カードの星・「中を見る」・下の帯）。見た目は Border のままで、読み上げ・自動操作から「押せるボタン」として見えるようにする。
///
/// 素の Border は UI Automation に出ない。押せるのに名前も操作も無いので、読み上げでは在ることが分からず、
/// 画面の確かめでは中の文字の座標を取って実際のマウスで押すしかなかった（2026-09-30）。
/// Button に替えると暗黙の Button スタイルの枠と高さが付いて見た目が変わるので、窓口だけを足す。
/// 「押す」が来たら <see cref="Invoked"/> を上げ、<see cref="Command"/> があれば呼ぶ。マウスの側の処理は今までどおり置き場所が持つ。
/// 名前は置き場所が AutomationProperties.Name で付ける（付けないと、名前の無いボタンになる）。
/// </summary>
public sealed class PressableBorder : Border
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(PressableBorder));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(PressableBorder));

    /// <summary>読み上げ・自動操作から「押す」が来た。</summary>
    public event EventHandler? Invoked;

    /// <summary>「押す」で呼ぶコマンド（マウスの側を InputBindings で受けている枠に使う）。</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>コマンドが今は実行できないなら、押せない（商品の無い知らせの行など。押せない枠を「押せる」と読ませない）。</summary>
    private bool CanPress => Command is not { } command || command.CanExecute(CommandParameter);

    private void RaiseInvoked()
    {
        Invoked?.Invoke(this, EventArgs.Empty);
        if (Command is { } command && command.CanExecute(CommandParameter))
        {
            command.Execute(CommandParameter);
        }
    }

    private sealed class Peer(PressableBorder owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(PressableBorder);

        // 「操作できる部品か」は上書きしない。既定は見えている間だけ部品として出すので、
        // 隠している枠（フォルダの行の星など）が名前の無いボタンとして探す側に混ざらない

        protected override bool IsEnabledCore() => base.IsEnabledCore() && owner.CanPress;

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        void IInvokeProvider.Invoke()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            // 押した先が窓を出すと、呼んだ側はその窓が閉じるまで返ってこない。既定のボタンと同じく、呼び出しを返してから動かす
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, owner.RaiseInvoked);
        }
    }
}
