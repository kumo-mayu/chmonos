using System.IO.Enumeration;

namespace Chmonos.Core.Storage;

/// <summary>
/// 保存先の中を、**リンクの先を辿らずに**数える（外部の点検 2026-10-06・ユーザ判断「A」）。
///
/// <see cref="SearchOption.AllDirectories"/> はジャンクション・シンボリックリンクの先（保存先の外）まで降りる。
/// 引越しはそれを写したうえで元を消し、書き出しは zip に入れ、<c>.tmp</c> の片付けは外の <c>.tmp</c> を消していた。
/// 保存先の中を消す・移す・写すときは、この数え方だけを使う。
///
/// 「リンク」は行き先を持つ物（シンボリックリンク・ジャンクション・マウントポイント）に限る。
/// 属性の <see cref="FileAttributes.ReparsePoint"/> だけで見ると、OneDrive の「必要なときにダウンロード」の
/// ファイルとフォルダ（クラウドの印も同じ属性）まで飛ばし、保存先を OneDrive に置いた人の引越しを丸ごと断ってしまう。
/// 行き先を持つかは <see cref="FileSystemInfo.LinkTarget"/> で見る（その属性の付いた物だけ聞くので、ふつうのファイルでは重くならない）。
/// </summary>
public static class StoreTree
{
    /// <summary>保存先の中のファイル。リンクのファイルは入れず、リンクのフォルダには降りない。</summary>
    public static IEnumerable<string> Files(string root, string pattern = "*", bool recurse = true)
        => Walk(root, pattern, recurse, includeFiles: true, includeDirectories: false, linksOnly: false, FullPath, default);

    /// <summary>
    /// <see cref="Files"/> と同じ物を、大きさを添えて返す。登録したフォルダ・保存先の容量を数える所が使う
    /// （列挙で分かっている大きさを、ファイルごとに聞き直さない）。
    /// 取り消しはフォルダに降りる所とファイルごとに見る。空のフォルダばかりの深い木でも止まれるように、降りる所でも見る
    /// </summary>
    public static IEnumerable<(string Path, long Length)> FilesWithLength(string root, CancellationToken cancellationToken = default)
        => Walk<(string Path, long Length)>(root, "*", recurse: true, includeFiles: true, includeDirectories: false, linksOnly: false,
            static (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.Length), cancellationToken);

    /// <summary>保存先の中のフォルダ。リンクのフォルダは入れず、降りもしない。</summary>
    public static IEnumerable<string> Directories(string root)
        => Walk(root, "*", recurse: true, includeFiles: false, includeDirectories: true, linksOnly: false, FullPath, default);

    /// <summary>
    /// 保存先の中にある、ほかの場所を指すリンク（ファイルでもフォルダでも）の、保存先からの相対の場所。無ければ null。
    /// 保存先そのものがリンクなのは構わない（その場所を保存先に選んだのは使う人）。
    /// </summary>
    public static string? FindLink(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        var link = Walk(root, "*", recurse: true, includeFiles: true, includeDirectories: true, linksOnly: true, FullPath, default).FirstOrDefault();
        return link is null ? null : Path.GetRelativePath(root, link);
    }

    /// <summary>リンクがあって始めないときの文。引越しと書き出しで同じ文を出す。</summary>
    public static string LinkRefusal(string relative)
        => $"保存先の中の「{relative}」は、ほかの場所を指すリンクです。リンクを外すか、中身を保存先へ戻してから、もう一度選んでください。";

    private static string FullPath(ref FileSystemEntry entry) => entry.ToFullPath();

    private static IEnumerable<T> Walk<T>(
        string root, string pattern, bool recurse, bool includeFiles, bool includeDirectories, bool linksOnly,
        FileSystemEnumerable<T>.FindTransform transform, CancellationToken cancellationToken)
    {
        // 隠し・システムの属性の物も数える（SearchOption.AllDirectories と同じ。EnumerationOptions の既定は飛ばす）。
        // 読めないフォルダで投げるのも前と同じにする（引越しで黙って飛ばすと、運ばずに元を消す側へ倒れる）
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recurse,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
        };

        return new FileSystemEnumerable<T>(root, transform, options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (linksOnly)
                {
                    return IsLink(ref entry);
                }

                if (entry.IsDirectory ? !includeDirectories : !includeFiles)
                {
                    return false;
                }

                return FileSystemName.MatchesSimpleExpression(pattern, entry.FileName) && !IsLink(ref entry);
            },
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return !IsLink(ref entry);
            },
        };
    }

    private static bool IsLink(ref FileSystemEntry entry)
        => (entry.Attributes & FileAttributes.ReparsePoint) != 0 && IsLink(entry.ToFileSystemInfo());

    /// <summary>
    /// リンク（行き先を持つ印）か。ほかの数え方もこれで見分ける——数え方が所によって違うと、
    /// 登録の時に数えた値と後で数え直した値が合わなくなる（引越しの候補。2026-10-07）
    /// </summary>
    public static bool IsLink(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        try
        {
            return info.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 行き先を読めない印は、リンクと見て辿らない（外へ出るかもしれない方を避ける）
            return true;
        }
    }
}
