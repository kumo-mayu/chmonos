using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 選んだ商品へまとめて当てる変更1つ（検索・ショップ・フォルダの右の選択の帯。ユーザ判断 2026-11-20）。
/// 商品ごとに錠の中で今の値に当てる（<c>ChangeLocalAsync</c>）。帯が抱えるカードの写しを書き戻すと、
/// その間に取り込み・検出・別の画面が書いた分を古い値に戻す。
/// </summary>
/// <remarks>
/// **既にそうなっている商品は書かずに数えるだけ**（ユーザ判断 2026-11-20：既にある物の追加・無い物の削除は何もせず、終わった後に件数を出す）。
/// 数えるのは錠の中の今の値で（カードの写しで数えると、間に変わった分を取り違える）。
/// </remarks>
public abstract record BulkItemChange
{
    private protected BulkItemChange() { }

    /// <summary>書く欄（名指しした欄だけを書く）。</summary>
    public abstract IReadOnlyCollection<LocalField> Owns { get; }

    /// <summary>今の値に当てた結果。既にそうなっていれば null（書かない）。</summary>
    public abstract LocalBlock? Apply(LocalBlock current);

    /// <summary>ユーザータグを足す。<paramref name="Sub"/> が null なら大分類だけ。小分類を足すときは大分類も付ける。</summary>
    public sealed record AddUserTag(string Top, string? Sub = null) : BulkItemChange
    {
        public override IReadOnlyCollection<LocalField> Owns => LocalOwners.UserTags;

        public override LocalBlock? Apply(LocalBlock current)
        {
            var tags = current.UserTags.ToList();
            var index = tags.FindIndex(tag => SameName(tag.Top, Top));
            if (index < 0)
            {
                tags.Add(new UserTagAssignment { Top = Top, Subs = Sub is null ? [] : [Sub] });
            }
            else if (Sub is not null && !tags[index].Subs.Any(known => SameName(known, Sub)))
            {
                tags[index] = tags[index] with { Subs = [.. tags[index].Subs, Sub] };
            }
            else
            {
                return null;
            }

            return current with { UserTags = tags };
        }
    }

    /// <summary>ユーザータグを外す。<paramref name="Sub"/> が null なら大分類ごと、あればその小分類だけ（大分類は残す）。</summary>
    public sealed record RemoveUserTag(string Top, string? Sub = null) : BulkItemChange
    {
        public override IReadOnlyCollection<LocalField> Owns => LocalOwners.UserTags;

        public override LocalBlock? Apply(LocalBlock current)
        {
            var index = current.UserTags.ToList().FindIndex(tag => SameName(tag.Top, Top));
            if (index < 0)
            {
                return null;
            }

            var tags = current.UserTags.ToList();
            if (Sub is null)
            {
                tags.RemoveAt(index);
            }
            else if (tags[index].Subs.Any(known => SameName(known, Sub)))
            {
                tags[index] = tags[index] with { Subs = [.. tags[index].Subs.Where(known => !SameName(known, Sub))] };
            }
            else
            {
                return null;
            }

            return current with { UserTags = tags };
        }
    }

    /// <summary>
    /// 属性を値を付けずに足す（付いているが値はまだ。ユーザ判断 2026-11-20）。値は編集の画面で「未評価」の行から付ける。
    /// 既に付いている商品（値がある・値はまだ）は何もしない——値を 50 などで埋めると、付けただけか評価したかが分からなくなる。
    /// </summary>
    public sealed record AddAttribute(string Name) : BulkItemChange
    {
        public override IReadOnlyCollection<LocalField> Owns => LocalOwners.Attributes;

        public override LocalBlock? Apply(LocalBlock current) => ItemAttributes.AddPending(current, [Name]);
    }

    /// <summary>属性を外して付いていない状態に戻す（値も、値はまだも消す。0 にはしない）。</summary>
    public sealed record RemoveAttribute(string Name) : BulkItemChange
    {
        public override IReadOnlyCollection<LocalField> Owns => LocalOwners.Attributes;

        public override LocalBlock? Apply(LocalBlock current) => ItemAttributes.Without(current, Name);
    }

    public sealed record SetFavorite(bool Value) : BulkItemChange
    {
        public override IReadOnlyCollection<LocalField> Owns => LocalOwners.Favorite;

        public override LocalBlock? Apply(LocalBlock current)
            => current.IsFavorite == Value ? null : current with { IsFavorite = Value };
    }

    public sealed record SetHidden(bool Value) : BulkItemChange
    {
        public override IReadOnlyCollection<LocalField> Owns => LocalOwners.Visibility;

        public override LocalBlock? Apply(LocalBlock current)
            => current.IsHidden == Value ? null : current with { IsHidden = Value };
    }

    /// <summary>更新の通知を入れる・切る。</summary>
    public sealed record SetNotifyOnUpdate(bool Value) : BulkItemChange
    {
        public override IReadOnlyCollection<LocalField> Owns => LocalOwners.UpdateNotice;

        public override LocalBlock? Apply(LocalBlock current)
            => current.NotifyOnUpdate == Value ? null : current with { NotifyOnUpdate = Value };
    }

    /// <summary>
    /// 確認待ちの対応アバターを全部確認済みにする（商品ページの「すべて確認済みにする」と同じ当て方）。
    /// どれが確認待ちかは錠の中の今の一覧で決める（カードの写しで決めると、間に検出が足した行を取りこぼす）。
    /// </summary>
    public sealed record ConfirmAvatars : BulkItemChange
    {
        public override IReadOnlyCollection<LocalField> Owns => LocalOwners.SupportedAvatars;

        public override LocalBlock? Apply(LocalBlock current)
        {
            var waiting = current.Avatars.Where(link => !link.Confirmed && !link.Rejected).Select(link => link.AvatarItemId).ToList();
            return AvatarService.WithConfirmedAvatars(current.Avatars, waiting) is { } links
                ? current with { Avatars = links }
                : null;
        }
    }

    // タグの名前は大文字と小文字を区別しない（外から使う API の足し引き・検索の条件と同じ）
    private static bool SameName(string left, string right)
        => string.Equals(left.Trim(), right.Trim(), StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>まとめて当てた結果の数。</summary>
/// <param name="Changed">書いた商品の数。</param>
/// <param name="AlreadyDone">既にそうなっていて書かなかった商品の数。</param>
/// <param name="NotFound">その間に消えていた商品の数。</param>
public sealed record BulkItemChangeOutcome(int Changed, int AlreadyDone, int NotFound);
