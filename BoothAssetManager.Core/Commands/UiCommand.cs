using BoothAssetManager.Core.Scanning;

namespace BoothAssetManager.Core.Commands;

/// <summary>
/// UIからバックエンドへの依頼。UIはこれを組み立てて渡すだけで、実処理には触れない。
/// 継承をこのファイル内に閉じているので、ケースの一覧はここを見れば分かる。
///
/// 参照実装（vrc-osc-recorder）と違い、結果は相関IDとポーリングではなく戻り値で返す。
/// あちらはOSC受信という継続的な外部イベント源のために非同期のチャネルが要ったが、
/// こちらの操作は全て「ユーザの操作に対する応答」なので、その仕組みは要らない。
/// </summary>
public abstract record UiCommand
{
    private protected UiCommand() { }

    /// <summary>指定フォルダを取り込む（走査 → BoothID解決 → BOOTH取得）。</summary>
    /// <summary>
    /// フォルダを取り込む。<paramref name="Work"/> は走っている最中にも足せるので、
    /// 画面は同じ集合に積むだけでよく、2本目の取り込みを起こさずに済む。
    /// </summary>
    public record ScanFolders(Scanning.ImportWorkSet Work) : UiCommand
    {
        public ScanFolders(IReadOnlyList<string> folders)
            : this(new Scanning.ImportWorkSet(folders))
        {
        }
    }

    /// <summary>未確定ファイルに商品IDを与えて確定させる。</summary>
    public record AssignItemId(string Hash, string ItemId) : UiCommand;

    /// <summary>
    /// 未確定ファイルを「BOOTHに無い商品」として登録する。
    /// 仮IDを与えるので、BOOTHへは問い合わせない。
    /// </summary>
    public record RegisterLocalItem(string Hash, string DisplayName) : UiCommand;

    /// <summary>IDを変更したら何が起きるかの下見。書き込まない。</summary>
    public record PlanItemIdChange(string FromId, string ToId) : UiCommand;

    /// <summary>商品まるごとを別のIDへ移す。移し終えたら元の商品は消える。</summary>
    public record ChangeItemId(
        string FromId,
        string ToId,
        IReadOnlySet<int>? SkippedPurchases = null) : UiCommand;

    /// <summary>自分で足す画像を1枚入れる。BOOTHと同じ圧縮を通す。</summary>
    public record AddUserImage(string ItemId, byte[] Bytes, string? Caption = null) : UiCommand;

    /// <summary>自分で足した画像を消す。ファイルごと消える。</summary>
    public record RemoveUserImage(string ItemId, string FileName) : UiCommand;

    /// <summary>自分で足した画像の並びを1つ動かす（-1 で前へ、+1 で後ろへ）。</summary>
    public record MoveUserImage(string ItemId, string FileName, int Delta) : UiCommand;

    /// <summary>サムネイルに使う1枚を指名する。null で指名を外す。</summary>
    public record PinThumbnail(string ItemId, string? FileName) : UiCommand;

    /// <summary>画像に役割を付ける。出どころから決まる値と同じなら記録されない</summary>
    public record SetImageRole(
        string ItemId,
        string FileName,
        Models.ImageRole Role,
        bool IsUserAdded) : UiCommand;

    /// <summary>1件のitemをBOOTHから取り直す。</summary>
    public record RefreshItem(string ItemId) : UiCommand;

    /// <summary>この商品の未取得の画像を、行列の先頭で取る。</summary>
    public record FetchItemImages(string ItemId) : UiCommand;

    /// <summary>
    /// ファイルを持たない商品として登録する。
    /// 贈った商品や、気になっている未購入品の入口。取り込みからは入らない。
    /// </summary>
    public record RegisterItem(string ItemId) : UiCommand;

    /// <summary>ファイルを管理対象から外す。再スキャンで未確定に出てこなくなる。</summary>
    public record ExcludeFile(string Hash, IReadOnlyList<string> Paths, string? Reason = null) : UiCommand;

    /// <summary>アーカイブの展開先フォルダを削除する。展開元のzipが残っていることを確かめてから消す。</summary>
    public record RemoveUnpackedFolders(IReadOnlyList<UnpackedFolder> Folders) : UiCommand;

    /// <summary>
    /// 画面の入力を保存する。<c>local</c> のうち <paramref name="Owns"/> で名指しした項目だけを書く。
    /// 名指ししなかった項目は、保存の直前に読み直したものが残る。
    /// </summary>
    public record SaveItemLocal(
        string ItemId,
        Models.LocalBlock Local,
        IReadOnlyCollection<Models.LocalField> Owns) : UiCommand;

    /// <summary>userTagをマスタへ追加する。<paramref name="Sub"/> を省くとトップだけを足す。</summary>
    public record AddUserTag(string Top, string? Sub = null) : UiCommand;

    /// <summary>属性をマスタへ追加する。</summary>
    public record AddAttribute(string Name) : UiCommand;

    /// <summary>
    /// userTagを改名する。<paramref name="Sub"/> を省くとトップの改名。
    /// item側は名前で参照しているので全itemの書き換えを伴い、既存の名前を指すと統合になる。
    /// </summary>
    public record RenameUserTag(string Top, string? Sub, string NewName) : UiCommand;

    /// <summary>userTagをマスタから消し、付けていたitemからも外す。<paramref name="Sub"/> を省くとトップ。</summary>
    public record DeleteUserTag(string Top, string? Sub = null) : UiCommand;

    /// <summary>userTagのメモを書き換える。item側には影響しない。</summary>
    public record SetUserTagMemo(string Top, string? Sub, string? Memo) : UiCommand;

    /// <summary>
    /// userTagを並べ替える。<paramref name="Top"/> を省くとトップレベル、指定するとその配下のサブ。
    /// マスタの並びは検索の絞り込みや編集の候補にそのまま出るので、追加順に縛られないようにする。
    /// </summary>
    public record ReorderUserTags(IReadOnlyList<string> Names, string? Top = null) : UiCommand;

    /// <summary>
    /// サブレベルを別のトップへ移す。削除して付け直すとitemの割当てが失われるので、
    /// 移動を専用の操作として持つ。
    /// </summary>
    /// <param name="DropEmptySourceTop">
    /// サブが無くなった元のトップをitemから外すか。他の理由で付いている可能性があるので、
    /// アプリでは決めずにユーザに聞く。
    /// </param>
    public record MoveUserTagSub(string FromTop, string Sub, string ToTop, bool DropEmptySourceTop) : UiCommand;

    /// <summary>
    /// 属性を改名する。既存の名前を指すと統合になり、両方に値が入っているitemでは
    /// <paramref name="Keep"/> の側の値を残す。
    /// </summary>
    public record RenameAttribute(
        string OldName,
        string NewName,
        Services.AttributeMergeValue Keep = Services.AttributeMergeValue.KeepTarget) : UiCommand;

    /// <summary>属性をマスタから消し、付けていたitemからも評価を外す。</summary>
    public record DeleteAttribute(string Name) : UiCommand;

    /// <summary>属性のメモを書き換える。item側には影響しない。</summary>
    public record SetAttributeMemo(string Name, string? Memo) : UiCommand;

    // --- 改変の記録 ---

    /// <summary>改変を作る。アバターが登録簿に無ければその場で足す</summary>
    public record CreateModification(string AvatarItemId, string Name) : UiCommand;

    /// <summary>改変を消す。**貼った画像も一緒に消える**。聞くのは画面側</summary>
    public record DeleteModification(string Id) : UiCommand;

    public record RenameModification(string Id, string Name) : UiCommand;

    public record SetModificationMemo(string Id, string? Memo) : UiCommand;

    /// <summary>Unityプロジェクトを紐付ける。null で外す</summary>
    public record SetModificationProject(string Id, string? Path) : UiCommand;

    /// <summary>使ったものを足す。**末尾に付く**（並びが導入の順）</summary>
    public record AddModificationMember(string Id, Models.ModificationMember Member) : UiCommand;

    /// <summary>位置で外す。並びが意味を持つので商品IDでは指さない</summary>
    public record RemoveModificationMember(string Id, int Index) : UiCommand;

    public record MoveModificationMember(string Id, int Index, int Delta) : UiCommand;

    /// <summary>
    /// 手で足した使ったもの（どのファイルか分からない行）に、Unityへ送るときに選んだファイルを記録する。
    /// 2つ選べば、その位置に2行並ぶ
    /// </summary>
    public record RecordModificationMemberFiles(string Id, int Index, IReadOnlyList<Models.ModificationMember> Members) : UiCommand;

    /// <summary>改変に画像を足す。商品と同じ圧縮を通す</summary>
    public record AddModificationImage(string Id, byte[] Bytes) : UiCommand;

    public record RemoveModificationImage(string Id, string FileName) : UiCommand;

    public record MoveModificationImage(string Id, string FileName, int Delta) : UiCommand;

    /// <summary>編集画面で最初から並べる属性かを切り替える</summary>
    public record SetAttributeDefault(string Name, bool IsDefault) : UiCommand;

    /// <summary>属性を並べ替える。並びは検索の候補にも編集の候補にもそのまま出る。</summary>
    public record ReorderAttributes(IReadOnlyList<string> Names) : UiCommand;

    /// <summary>確定する前にIDの中身を見る。既に持っていればBOOTHへは行かない。</summary>
    public record PreviewItem(string ItemId) : UiCommand;

    /// <summary>手掛かりの無いファイルについて、BOOTH内検索から候補を出す。</summary>
    /// <summary>
    /// 手掛かりの無いファイルについて、BOOTH内検索から候補を出す。
    /// このコマンドだけ進捗の受け口を持つ。取得を1件ずつ間隔を空けて行うため
    /// 待ち時間が長く、黙って待たせるわけにいかないため。
    /// </summary>
    public record ProposeCandidates(string FilePath, IProgress<Resolution.ResolveProgress>? Progress = null) : UiCommand;

    /// <summary>
    /// 対応アバターを検出する（人が押したとき）。
    ///
    /// **取り込みの中の③とは別物。**あちらは待てるので <c>Detection</c> のまま、
    /// こちらは画面の前で結果を待っているので、ここを通して <c>User</c> に乗る。
    /// </summary>
    public record DetectAvatars(IProgress<Services.AvatarDetectProgress>? Progress = null) : UiCommand;

    /// <summary>フォルダを商品に紐付ける。zipが手元に無く展開したものだけが残っている場合に使う。</summary>
    public record RegisterFolder(string ItemId, string FolderPath) : UiCommand;

    /// <summary>フォルダの紐付けを解除する。zipを後から手に入れたときに使う。ファイルには触らない。</summary>
    public record UnregisterFolder(string ItemId, string FolderPath) : UiCommand;

    /// <summary>
    /// ファイルをこの商品から外して未確定へ戻す。間違った紐付けの直し方。
    /// IDは書き換えない（他所から名前で参照されているため）。
    /// </summary>
    /// <param name="DeleteItemWhenEmpty">
    /// 外した結果、手元に何も無くなるときに商品ごと消すか。
    /// 情報だけ残す状態は贈った商品と同じで、それ自体は普通の状態なので既定では消さない。
    /// </param>
    public record DetachFile(string ItemId, string Hash, bool DeleteItemWhenEmpty = false) : UiCommand;

    /// <summary>外したファイルをこの商品に戻す（灰色の行の「この商品に戻す」）。未確定からは取り除く。</summary>
    public record ReattachFile(string ItemId, string Hash) : UiCommand;

    /// <summary>
    /// ファイルがどの種類（BOOTHのバリエーション）のものかを付け直す。値が null なら外す。
    /// 名指ししなかったファイルは今のまま。
    /// </summary>
    public record SetFileVariations(string ItemId, IReadOnlyDictionary<string, long?> VariationByHash) : UiCommand;

    /// <summary>保存先を1つの zip に書き出す（#61）。</summary>
    public record ExportBackup(string Root, string ZipPath, bool IncludeImages) : UiCommand;

    /// <summary>バックアップの zip を空の場所へ展開する（#61）。そこへ移るのは呼ぶ側（保存先の切り替え）。</summary>
    public record RestoreBackup(string ZipPath, string DestinationRoot) : UiCommand;

    /// <summary>zip を一時フォルダへ展開する（#56）。展開先は <see cref="CommandResult.Unpacked"/> で返る。</summary>
    public record UnpackToTemporary(string ZipPath) : UiCommand;

    /// <summary>要確認の既読・未読を切り替える。消さずに既読にするのは「見た」と「無かった」を分けるため。</summary>
    public record SetNotificationRead(string Id, bool IsRead) : UiCommand;

    /// <summary>要確認をまとめて既読にする。</summary>
    public record MarkAllNotificationsRead : UiCommand;
}

/// <summary>コマンドの実行結果。</summary>
public abstract record CommandResult
{
    private protected CommandResult() { }

    public record Imported(ImportSummary Summary) : CommandResult;

    /// <summary>自分で足した画像が入った。</summary>
    public record UserImageAdded(string ItemId, string FileName) : CommandResult;

    /// <summary>IDを変更したら何が起きるかの下見。まだ何も書いていない。</summary>
    public record ItemIdChangePlanned(Services.ItemIdChangePlan Plan) : CommandResult;

    public record ItemSaved(string ItemId) : CommandResult;

    /// <summary>ファイルを外した結果。商品が空になったか、消したかまで返す。</summary>
    public record FileDetached(string ItemId, Services.DetachOutcome Outcome) : CommandResult;

    /// <summary>指名された商品の画像を取り終えた。落とせた枚数を持つ。</summary>
    public record ImagesFetched(string ItemId, int Downloaded) : CommandResult;

    public record Done : CommandResult;

    public record UnpackedFoldersRemoved(IReadOnlyList<UnpackedFolderRemoval> Results) : CommandResult;

    public record UserTagsChanged(Models.UserTagMaster Master) : CommandResult;

    /// <summary>改名・削除の結果。書き換えたitem数を持つのは、何が起きたかを画面に出すため。</summary>
    public record UserTagsRewritten(Services.UserTagEditResult Result) : CommandResult;

    public record AttributesChanged(Models.AttributeMaster Master) : CommandResult;

    /// <summary>改変を作った。画面はこのIDを開く</summary>
    public record ModificationCreated(Models.ModificationRecord Record) : CommandResult;

    /// <summary>改変の一覧が変わった（作った・消した）</summary>
    public record ModificationsChanged() : CommandResult;

    /// <summary>属性の改名・削除の結果。書き換えたitem数を持つ。</summary>
    public record AttributesRewritten(Services.AttributeEditResult Result) : CommandResult;

    public record PreviewLoaded(Services.ItemPreview Preview) : CommandResult;

    public record CandidatesProposed(IReadOnlyList<Resolution.ResolutionCandidate> Candidates) : CommandResult;

    public record AvatarsDetected(Services.AvatarDetectResult Result) : CommandResult;

    public record Failed(string Message) : CommandResult;

    /// <summary>一時フォルダへ展開した。</summary>
    public record Unpacked(string Folder) : CommandResult;

    /// <summary>バックアップを書き出した。</summary>
    public record BackupExported(Storage.BackupResult Result) : CommandResult;

    /// <summary>バックアップを展開した。</summary>
    public record BackupRestored(int Files) : CommandResult;
}
