using Chmonos.Api;

namespace Chmonos.App.ViewModels;

/// <summary>許しを聞く項目1行（足す・変わる・消えるの節に並べる）。</summary>
/// <param name="Label">項目の名前（下の段なら「親 ▸ 子」）。</param>
/// <param name="Detail">出す場所と、押したときに渡す情報（消える項目は出す場所だけ）。</param>
public sealed record PluginMenuApprovalLine(string Label, string Detail);

/// <summary>
/// 許したソフトが項目の宣言を変えたときの窓（plan 44 の②・docs/spec/plugins-menu.md）。足す・変わる・消える項目を並べて人に聞く
/// （ユーザ判断 2026-11-12「変っていたら再認証が必要でしょう」）。許すまでは前の宣言のまま出す。
/// ［拒否］の数え方は許しの窓と同じ（<see cref="RefusalClick"/>）
/// </summary>
public sealed class PluginMenuApprovalViewModel
{
    public PluginMenuApprovalViewModel(PluginMenuRequestInfo request)
    {
        Name = request.Name;
        IsFirst = request.IsFirst;
        ExePathText = request.ExePath ?? "確かめられませんでした";
        HasExePath = request.ExePath is not null;
        Origin = request.Origin;
        Added = request.Change.Added.Select(line => Line(line, withNeeds: true)).ToList();
        Changed = request.Change.Changed.Select(line => Line(line, withNeeds: true)).ToList();
        Removed = request.Change.Removed.Select(line => Line(line, withNeeds: false)).ToList();
        LastRefusal = request.RefusalsLeft == 1;
    }

    public string Name { get; }

    /// <summary>前に許した項目が無い（見出しを「追加しようとしています」にし、「前の項目のまま」は言わない）。</summary>
    public bool IsFirst { get; }

    public bool IsChange => !IsFirst;

    public string Heading => IsFirst ? $"「{Name}」がメニューに項目を追加しようとしています" : $"「{Name}」がメニューの項目を変えようとしています";

    public string ExePathText { get; }

    public bool HasExePath { get; }

    public string? Origin { get; }

    public bool HasOrigin => Origin is not null;

    public IReadOnlyList<PluginMenuApprovalLine> Added { get; }

    public IReadOnlyList<PluginMenuApprovalLine> Changed { get; }

    public IReadOnlyList<PluginMenuApprovalLine> Removed { get; }

    public bool HasAdded => Added.Count > 0;

    public bool HasChanged => Changed.Count > 0;

    public bool HasRemoved => Removed.Count > 0;

    /// <summary>次の拒否で止まるか（許しの窓と同じく、そのときだけ言う）。</summary>
    public bool LastRefusal { get; }

    internal static PluginMenuApprovalLine Line(PluginMenuLine line, bool withNeeds)
    {
        var label = line.Parent is { } parent ? $"{parent} ▸ {line.Label}" : line.Label;
        var where = "表示する場所：" + string.Join("・", line.Where.Select(WhereLabel));
        if (!withNeeds)
        {
            return new PluginMenuApprovalLine(label, where);
        }

        // 渡す情報は人が許すかを決める物なので、何も詰めない項目でも「商品ID」は言う（ID はいつも渡す）
        var needs = new[] { "itemId" }.Concat(line.Needs).Distinct(StringComparer.Ordinal).Select(NeedLabel);
        return new PluginMenuApprovalLine(label, $"{where}　渡す情報：{string.Join("・", needs)}");
    }

    /// <summary>出す場所の画面の名前。</summary>
    internal static string WhereLabel(string where) => where switch
    {
        PluginMenus.WhereItem => "商品の右クリック",
        PluginMenus.WhereSelection => "複数選択の帯",
        _ => where,
    };

    /// <summary>渡す情報の画面の名前。</summary>
    internal static string NeedLabel(string need) => need switch
    {
        "itemId" => "商品ID",
        "name" => "名前",
        "shop" => "ショップ",
        "category" => "カテゴリ",
        "files" => "ファイルの場所",
        "unityPackages" => "unitypackage",
        "avatars" => "対応アバター",
        "tags" => "タグ",
        _ => need,
    };

    private readonly RefusalClick _refusal = new();

    public void NotePointerDown() => _refusal.NotePointerDown();

    public void NoteKey() => _refusal.NoteKey();

    public void Deny() => _refusal.Deny();

    /// <summary>窓の結果を答えにする（許しの窓と同じ分け方。許すときは種類を持たない）。</summary>
    public ApiClientDecision Decide(bool? dialogResult) => _refusal.Decide(dialogResult, () => new ApiClientDecision(true, []));
}
