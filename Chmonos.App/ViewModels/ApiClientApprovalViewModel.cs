using Chmonos.Api;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>書き込みの種類1つのチェック（許しの窓と、設定の「つないでいるソフト」の行）。</summary>
public sealed class ApiScopeChoice : ViewModelBase
{
    private bool _isChecked;

    public ApiScopeChoice(string scope, bool isChecked, Action<ApiScopeChoice>? changed = null)
    {
        Scope = scope;
        _isChecked = isChecked;
        _changed = changed;
    }

    private readonly Action<ApiScopeChoice>? _changed;

    /// <summary>種類の名前（<see cref="ApiScopes"/>）。</summary>
    public string Scope { get; }

    public string Label => ApiScopes.Label(Scope);

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetField(ref _isChecked, value))
            {
                _changed?.Invoke(this);
            }
        }
    }
}

/// <summary>
/// ソフトが API の許しを求めてきたときの窓（plan 31）。名乗った名前・接続を開いた exe・ブラウザならオリジンを見せ、
/// 求められた書き込みの種類から、人が許す物を選ぶ。読み取りは許せばいつもできる。
/// 名前はソフトが名乗った物で本人の確かめにならないので、そう書く（exe のパスで見分けてもらう）
/// </summary>
public sealed class ApiClientApprovalViewModel : ViewModelBase
{
    public ApiClientApprovalViewModel(ApiClientRequestInfo request)
    {
        Name = request.Name;
        ExePathText = request.ExePath ?? "確かめられませんでした";
        HasExePath = request.ExePath is not null;
        Origin = request.Origin;
        Replaces = request.Replaces;
        Scopes = request.Scopes.Select(scope => new ApiScopeChoice(scope, isChecked: true)).ToList();
        LastRefusal = request.RefusalsLeft == 1;
    }

    public string Name { get; }

    public string Heading => $"「{Name}」がAPIの利用を求めています";

    public string ExePathText { get; }

    /// <summary>exe が引けたか（引けなければ「確かめられませんでした」を注意の色で出す）。</summary>
    public bool HasExePath { get; }

    /// <summary>ブラウザのページから求めたときのオリジン（それ以外は null で行を出さない）。</summary>
    public string? Origin { get; }

    public bool HasOrigin => Origin is not null;

    /// <summary>前に許した同じソフト。許すと前のトークンは使えなくなる。</summary>
    public bool Replaces { get; }

    /// <summary>求められた書き込みの種類（既定はすべてチェック。外した物は許さない）。</summary>
    public IReadOnlyList<ApiScopeChoice> Scopes { get; }

    public bool HasScopes => Scopes.Count > 0;

    /// <summary>
    /// 次に拒否すると止まるか（「あと1回」を出す）。2回以上残っているときは出さない：
    /// 毎回の窓に回数を並べると、許すかを決める文が埋もれる。知らせが要るのは、次の拒否で窓が出なくなる時だけ
    /// </summary>
    public bool LastRefusal { get; }

    /// <summary>許したときの答え（チェックした種類だけ）。</summary>
    public ApiClientDecision Approve() => new(true, Scopes.Where(choice => choice.IsChecked).Select(choice => choice.Scope).ToList());

    /// <summary>人が［拒否］を押したか（ボタン・フォーカスのある所で Enter。Esc・×・Alt+F4 は押していない）。</summary>
    public bool Refused { get; private set; }

    public void Refuse() => Refused = true;

    /// <summary>
    /// 窓の結果を答えにする。拒否に数えるのは［拒否］を押したときだけ（2026-11-11）。
    /// 閉じた・Esc は「答えなかった」：止める回数に数えると、後で決めようと閉じただけで止まってしまう
    /// </summary>
    public ApiClientDecision Decide(bool? dialogResult)
        => dialogResult == true ? Approve() : Refused ? ApiClientDecision.Refusal : ApiClientDecision.Dismissed;
}
