using Chmonos.Api;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>書き込みか見張りの種類1つのチェック（許しの窓と、設定の「つないでいるソフト」の行）。</summary>
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
        Scopes = request.Scopes.Where(ApiScopes.IsWrite).Select(scope => new ApiScopeChoice(scope, isChecked: true)).ToList();
        Watches = request.Scopes.Where(ApiScopes.IsWatch).Select(scope => new ApiScopeChoice(scope, isChecked: true)).ToList();
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
    /// 求められた見張りの種類（plan 44。既定はすべてチェック）。見張りは書き込みではないが、使う人の操作を外へ出すので、人が選ぶ（ユーザ判断 2026-11-12）。
    /// 書き込みと分けて並べる：「変更」の下に置くと、データを変えない物まで変更に見える
    /// </summary>
    public IReadOnlyList<ApiScopeChoice> Watches { get; }

    public bool HasWatches => Watches.Count > 0;

    /// <summary>変更を求めていないときの1行。見張りを求めているなら「読み取りだけ」とは言わない（見張りも許すので、嘘になる）。</summary>
    public string NoScopesText => HasWatches ? "変更は求めていません。" : "変更は求めていません。読み取りだけを許可します。";

    /// <summary>
    /// 次に拒否すると止まるか（「あと1回」を出す）。2回以上残っているときは出さない：
    /// 毎回の窓に回数を並べると、許すかを決める文が埋もれる。知らせが要るのは、次の拒否で窓が出なくなる時だけ
    /// </summary>
    public bool LastRefusal { get; }

    /// <summary>許したときの答え（チェックした種類だけ）。</summary>
    public ApiClientDecision Approve() => new(true, Scopes.Concat(Watches).Where(choice => choice.IsChecked).Select(choice => choice.Scope).ToList());

    /// <summary>
    /// 止めるまでの回数に数える拒否か。数えるのは、マウス（タッチもマウスの押下として来る）で［拒否］をクリックしたときだけ（ユーザ判断 2026-11-11）。
    /// Enter・Space は数えない：窓が開くと［拒否］にフォーカスがあるので、別の所へ打っている途中のキーが当たっただけで止まり得る。
    /// Esc・×・Alt+F4 は押していない
    /// </summary>
    public bool Refused { get; private set; }

    /// <summary>今の押下がマウスか（押し下げで立て、キーを押したら下ろす。押したまま外へずらして放し、後で Space を押したときに数えないように）。</summary>
    private bool _pointerPressed;

    /// <summary>［拒否］の上でマウスのボタンを押し下げた。</summary>
    public void NotePointerDown() => _pointerPressed = true;

    /// <summary>窓でキーが押された（Enter・Space で［拒否］が押されるより先に来る）。</summary>
    public void NoteKey() => _pointerPressed = false;

    /// <summary>［拒否］が押された。マウスのクリックなら回数に数える。ソフトへの答えはどちらも denied。</summary>
    public void Deny() => Refused = _pointerPressed;

    /// <summary>
    /// 窓の結果を答えにする。拒否に数えるのはマウスで［拒否］をクリックしたときだけ（ユーザ判断 2026-11-11）。
    /// 閉じた・Esc は「答えなかった」：止める回数に数えると、後で決めようと閉じただけで止まってしまう
    /// </summary>
    public ApiClientDecision Decide(bool? dialogResult)
        => dialogResult == true ? Approve() : Refused ? ApiClientDecision.Refusal : ApiClientDecision.Dismissed;
}
