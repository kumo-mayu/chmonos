namespace Chmonos.Core.Services;

/// <summary>
/// 共通素体の名前の変更が、ほかの素体への統合になるかを見分ける。
///
/// 統合は元に戻せない（どちらに所属・宣言していたかを残さない）のに、前は確認なしで統合されていた（ユーザ判断 2026-09-29）。
/// 見分け方は <c>AvatarService.RenameBaseAsync</c> の統合の条件と同じ（前後の空白を除き、大文字小文字を区別しない）。
/// ずれると、確認を出さずに統合する・統合しないのに確認を出す、のどちらかになる。
/// </summary>
public static class AvatarBaseRename
{
    /// <summary>
    /// 新しい名前が、変える素体とは別の既にある素体を指していれば、その素体の名前（統合先）。ただの名前の変更なら null。
    /// 大文字小文字だけを変えるのは自分自身なので統合ではない。
    /// <paramref name="existingNames"/> には一覧に出ている素体だけを渡す。消した印の付いた素体は、改名では統合先にせず名前を引き継ぐ
    /// （<c>RenameBaseAsync</c>・ユーザ判断 2026-09-29）ので、渡すと統合しないのに確認を出してしまう。
    /// </summary>
    public static string? MergeTarget(IEnumerable<string> existingNames, string oldName, string newName)
    {
        var trimmed = newName.Trim();
        return existingNames.FirstOrDefault(name =>
            !Same(name, oldName) && Same(name, trimmed));
    }

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase);
}
