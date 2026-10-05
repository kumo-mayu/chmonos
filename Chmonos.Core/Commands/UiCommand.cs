using Chmonos.Core.Scanning;

namespace Chmonos.Core.Commands;

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

    /// <summary>
    /// 未確定ファイルに商品IDを与えて確定させる。
    /// <paramref name="RequestsLeft"/> は、手元に無い商品を取るときの BOOTH への問い合わせの残りの数（画面が目安の時間を出す。問い合わせの間隔と順番は変わらない）。
    /// </summary>
    public record AssignItemId(string Hash, string ItemId, IProgress<int>? RequestsLeft = null) : UiCommand;

    /// <summary>
    /// 未確定ファイルを「BOOTHに無い商品」として登録する。
    /// 仮IDを与えるので、BOOTHへは問い合わせない。
    /// 複数のファイルを1つの商品にまとめるときも1回の命令で渡す（ユーザ指示 2026-10-02：選んだ物をまとめて仮IDで登録したい）。
    /// 仮IDは先頭のファイルから決まる。1件ずつ命令を重ねると、途中で失敗したときに半分だけ登録された商品が残る
    /// </summary>
    public record RegisterLocalItem(IReadOnlyList<string> Hashes, string DisplayName) : UiCommand
    {
        public RegisterLocalItem(string hash, string displayName)
            : this([hash], displayName)
        {
        }
    }

    /// <summary>
    /// 未確定ファイルを、BOOTHで見つからなかった商品IDのまま登録する（ユーザ判断 2026-09-29）。
    /// 「BOOTHで公開されていない」商品として持ち、⑦で確かめ直して、公開されたら情報を取る。BOOTHへは問い合わせない。
    /// </summary>
    public record AssignUnpublishedItemId(string Hash, string ItemId, string DisplayName) : UiCommand;

    /// <summary>
    /// 設定を変える。変え方を関数で渡し、ディスクの今の設定に錠の中で当てる（技術的負債 1-1・1-4）。
    /// 画面は自分の写しを丸ごと書かない——別の画面が書いた項目を古い値で消してしまう。
    /// </summary>
    public record ChangeSettings(Func<Models.AppSettings, Models.AppSettings> Change) : UiCommand;

    /// <summary>画面が覚えている状態（ui-state.json）を変える。設定と同じく変え方を関数で渡す。</summary>
    public record ChangeUiState(Func<Models.UiState, Models.UiState> Change) : UiCommand;

    // ---- アバターの登録簿（アバター画面）。前は画面がサービスを直に呼んでいた（技術的負債 3-1） ----

    /// <summary>アバターに名前を付ける。空にすると自動の名前に戻る。</summary>
    public record SetAvatarName(string ItemId, string Name) : UiCommand;

    public record SetAvatarMemo(string ItemId, string? Memo) : UiCommand;

    /// <summary>手で「持っている」にする（ファイルが無くても持っている扱い）。</summary>
    public record SetAvatarOwned(string ItemId, bool Owned) : UiCommand;

    /// <summary>アバターとして扱うかを手で決める。null で規則の判定に戻す。</summary>
    public record SetAvatarOverride(string ItemId, bool? Value) : UiCommand;

    /// <summary>アバターの共通素体を決める。null で所属を外す。</summary>
    public record SetAvatarBase(string ItemId, string? BaseName) : UiCommand;

    /// <summary>共通素体の一致から衣装の互換を広げるか。</summary>
    public record SetBaseInferClothing(string Name, bool Infer) : UiCommand;

    /// <summary>共通素体に配布商品を結ぶ。null で外す。</summary>
    public record SetBaseItemId(string Name, string? ItemId) : UiCommand;

    /// <summary>共通素体の名前を変える。商品の宣言も書き換える（結果は書き換えた商品の数）。</summary>
    public record RenameBase(string OldName, string NewName) : UiCommand;

    /// <summary>共通素体を消す。取り消せない（結果は書き換えた商品の数）。</summary>
    public record DeleteBase(string Name) : UiCommand;

    /// <summary>共通素体を手で足す（結果は <see cref="CommandResult.BaseAdded"/>）。検出し直しでは消えない。</summary>
    public record AddBase(string Name) : UiCommand;

    public record AddAvatarAlias(string ItemId, string Text) : UiCommand;

    public record RemoveAvatarAlias(string ItemId, string Text) : UiCommand;

    /// <summary>BOOTH に問い合わせてアバターかどうかを確かめ直す。</summary>
    public record RecheckAvatar(string ItemId) : UiCommand;

    // ---- 編集キューの位置（edit-session.json） ----

    /// <summary>
    /// 順番を積み直す。保存した印は空に戻る。入り直して順番を詰めたときは位置も一緒に渡す
    /// （順番と位置を2回に分けて書くと、間で落ちたときに位置が先頭に戻る）。
    /// </summary>
    public record StartEditSession(IReadOnlyList<string> ItemIds, int Index = 0) : UiCommand;

    public record AdvanceEditSession(int Index) : UiCommand;

    public record NoteEditSaved(string ItemId) : UiCommand;

    /// <summary>商品番号を付け替えたので、編集キューの中の番号も付け替える。</summary>
    public record ReplaceEditSessionItemId(string FromId, string ToId) : UiCommand;

    public record ClearEditSession() : UiCommand;

    // ---- 設定画面から戻す操作 ----

    public record UnhideItem(string ItemId) : UiCommand;

    /// <summary>「この商品のものではない」と外した記録を捨てる。</summary>
    public record ForgetDetached(string Hash, string ItemId) : UiCommand;

    /// <summary>管理から外したファイルを戻す（元の場所に在れば、その場で未確定に戻す。結果は CommandResult.ExclusionLifted）。</summary>
    public record RestoreExcluded(string Hash) : UiCommand;

    /// <summary>既にどこかの商品が持っているファイルを、未確定の一覧から取り除く（結果は取り除いた数）。</summary>
    public record ReconcileUnresolved() : UiCommand;

    // ---- BOOTH への問い合わせ（画面が BoothClient・ShopService を直に呼んでいた。優先度は CommandHandler の中で決める） ----

    /// <summary>手元に無い店のアイコンを順に取る（ショップの一覧を開いたとき・梯子の⑥）。<paramref name="OnFetched"/> は1件ごとに呼ぶ。</summary>
    public record SyncShopIcons(
        IReadOnlyList<Services.ShopSummary> Shops,
        Func<string, string, Task>? OnFetched = null) : UiCommand;

    /// <summary>ショップの画面を開いたときに、バナーが手元に無ければ取る。</summary>
    public record EnsureShopBanner(string Subdomain) : UiCommand;

    /// <summary>ショップのバナーとアイコンを取り直す（人が押した）。</summary>
    public record RefreshShopImages(string Subdomain) : UiCommand;

    /// <summary>ブラウザから落とされた BOOTH の画像を取る（画像置き場の URL だけ。確かめるのは DropRouting）。</summary>
    public record FetchBoothImage(string Url) : UiCommand;

    /// <summary>検索の履歴を変える（積む・消す・全部消す）。変え方を関数で渡し、錠の中で今の履歴に当てる。</summary>
    public record ChangeSearchHistory(Func<Services.SearchHistoryList, Services.SearchHistoryList> Change) : UiCommand;

    /// <summary>保存した検索を変える（保存・上書き・名前の変更・削除・並べ替え）。変え方を関数で渡し、錠の中で今の並びに当てる。</summary>
    public record ChangeSavedSearches(Func<Services.SavedSearchList, Services.SavedSearchList> Change) : UiCommand;

    /// <summary>
    /// ショップの星・メモを変える（shops.json・ユーザ判断 2026-09-16）。変え方を関数で渡し、錠の中で今の値に当てる。
    /// <paramref name="NameHint"/> は見分け用の名前の控え、<paramref name="Uuid"/> はサブドメインが変わったときの手がかり。
    /// </summary>
    public record ChangeShopNote(
        string Subdomain,
        string? NameHint,
        string? Uuid,
        Func<Models.ShopNoteRecord, Models.ShopNoteRecord> Change) : UiCommand;

    /// <summary>
    /// 動画のタイトルを控える（video-titles.json）。<paramref name="Title"/> が null なら控えを消す（非公開・削除で取れなくなった）。
    /// 書くついでに30日を過ぎた控えも落とす（YouTube の開発者ポリシー・<see cref="Services.VideoTitleBook"/>）。
    /// </summary>
    public record RememberVideoTitle(string VideoId, string? Title) : UiCommand;

    /// <summary>30日を過ぎた動画のタイトルの控えを消す（起動時）。結果は消した数。</summary>
    public record PruneVideoTitles() : UiCommand;

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

    /// <summary>
    /// ファイルを管理対象から外す。再スキャンで未確定に出てこなくなる。1個でもまとめてでもこれ1本で、記録は1回で書く
    /// （1個ずつ命令を呼ぶと、フォルダごと外したときに 5,000 個で2分半かかっていた）。
    /// 戻すときは同じ一覧と、結果（<see cref="CommandResult.FilesExcluded"/>）の足したハッシュを <see cref="UndoExclude"/> に渡す。
    /// </summary>
    public record ExcludeFiles(IReadOnlyList<Models.UnresolvedFile> Files, string? Reason = null) : UiCommand;

    /// <summary>
    /// 外した直後に戻す。外す前の未確定の記録を戻し、除外の記録からは <paramref name="ExcludedHashes"/>（今回足した物）だけを消す。
    /// 前は一覧のハッシュで全部消していて、前から除外していた物の記録（日時・理由）まで消えていた（2026-10-05）。
    /// </summary>
    public record UndoExclude(IReadOnlyList<Models.UnresolvedFile> Files, IReadOnlyCollection<string> ExcludedHashes) : UiCommand;

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
    /// 小分類を持たない大分類を、別の大分類の小分類にする。付いていた商品は「入れ先＋小分類（元の名前）」に書き換わる。
    /// 小分類を持つ大分類では断る（<see cref="CommandResult.Failed"/>）。
    /// </summary>
    public record NestUserTagTop(string Top, string IntoTop) : UiCommand;

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

    /// <summary>改変を複製する。外した行・blueprint ID・写真は写さない</summary>
    public record DuplicateModification(string Id) : UiCommand;

    /// <summary>改変を消す。**貼った画像も一緒に消える**。聞くのは画面側</summary>
    public record DeleteModification(string Id) : UiCommand;

    public record RenameModification(string Id, string Name) : UiCommand;

    public record SetModificationMemo(string Id, string? Memo) : UiCommand;

    /// <summary>VRChat の blueprint ID（<c>avtr_…</c>）。空で外す（ユーザ指示 2026-09-19）</summary>
    public record SetModificationBlueprintId(string Id, string? BlueprintId) : UiCommand;

    /// <summary>Unityプロジェクトを紐付ける。null で外す</summary>
    public record SetModificationProject(string Id, string? Path) : UiCommand;

    /// <summary>使ったものを足す。**末尾に付く**（並びが導入の順）</summary>
    public record AddModificationMember(string Id, Models.ModificationMember Member) : UiCommand;

    // 使ったものを指すのは**位置ではなく行そのもの**（ユーザ判断 2026-09-21・J5）。
    // 画面は読み込んだ時点の位置を送るので、その間に並びが変わると別の行に当たっていた。
    // 並びは配列の順のまま（人が並べ替えられる）で、指し方だけを変える

    /// <summary>外す・戻す（行と記録は残す。ユーザ指示 2026-09-19）。</summary>
    public record SetModificationMemberDetached(string Id, Models.ModificationMember Member, bool Detached) : UiCommand;

    /// <summary>完全に消す（外した行の「削除」から。記録も残らない）。</summary>
    public record RemoveModificationMember(string Id, Models.ModificationMember Member) : UiCommand;

    public record MoveModificationMember(string Id, Models.ModificationMember Member, int Delta) : UiCommand;

    /// <summary>
    /// 手で足した使ったもの（どのファイルか分からない行）に、Unityへ送るときに選んだファイルを記録する。
    /// 2つ選べば、その行の場所に2行並ぶ
    /// </summary>
    public record RecordModificationMemberFiles(
        string Id, Models.ModificationMember Member, IReadOnlyList<Models.ModificationMember> Members) : UiCommand;

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
    /// <c>Listed</c> は未確定の一覧にあるファイルの場所。同じフォルダの兄弟の zip の間で変わる語（アバター名など）を検索語から外すのに使う。
    /// </summary>
    public record ProposeCandidates(
        string FilePath,
        IProgress<Resolution.ResolveProgress>? Progress = null,
        IReadOnlyCollection<string>? Listed = null) : UiCommand;

    /// <summary>
    /// 対応アバターを検出する（人が押したとき）。
    ///
    /// **取り込みの中の③とは別物。**あちらは待てるので <c>Detection</c> のまま、
    /// こちらは画面の前で結果を待っているので、ここを通して <c>User</c> に乗る。
    /// </summary>
    public record DetectAvatars(IProgress<Services.AvatarDetectProgress>? Progress = null) : UiCommand;

    /// <summary>フォルダを商品に紐付ける。zipが手元に無く展開したものだけが残っている場合に使う。</summary>
    public record RegisterFolder(string ItemId, string FolderPath, IProgress<int>? RequestsLeft = null) : UiCommand;

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
    /// 途中で止まった取り込みの続きを捨てる（import-state.json を空にする）。取れた商品は消さない。
    /// 前は画面が直に書いていて、保存先を運んでいる間の門（StoreWriteGate）も通っていなかった。
    /// </summary>
    public record DiscardInterruptedImport() : UiCommand;

    /// <summary>
    /// 分類・属性の一覧に無い名前を指している商品を探し、要確認へ出す（要確認の画面を開くたび）。
    /// 知らせを書くので書き込みとして通す。結果は <see cref="CommandResult.Counted"/>（見つけた件数）。
    /// </summary>
    public record DetectOrphanReferences() : UiCommand;

    /// <summary>
    /// 記録したパスのドライブ文字と通し番号の組を確かめ直し、volumes.json に控える（フォルダビューを開くたび）。
    /// 結果は <see cref="CommandResult.VolumesObserved"/>（元の文字 → 今の文字の読み替え）。
    /// </summary>
    public record ObserveVolumes(IReadOnlyList<string> RecordedPaths) : UiCommand;

    /// <summary>
    /// ファイルがどの種類（BOOTHのバリエーション）のものかを付け直す。値が null なら外す。
    /// 名指ししなかったファイルは今のまま。
    /// </summary>
    public record SetFileVariations(string ItemId, IReadOnlyDictionary<string, long?> VariationByHash) : UiCommand;

    /// <summary>
    /// 使おうとして見たファイルの在る・無いを、記録の「見つからなくなった日時」に当てる（商品ページ・開く・Unityへ送る。ユーザ判断 2026-10-04）。
    /// 書いたら <see cref="CommandResult.ItemSaved"/>、変える物が無ければ <see cref="CommandResult.Done"/>。
    /// </summary>
    public record NoteFilePresence(string ItemId, IReadOnlyList<Services.FileSighting> Sightings) : UiCommand;

    /// <summary>保存先を1つの zip に書き出す（#61）。</summary>
    public record ExportBackup(
        string Root,
        string ZipPath,
        bool IncludeImages,
        IProgress<Storage.BackupProgress>? Progress = null) : UiCommand;

    /// <summary>バックアップの zip を空の場所へ展開する（#61）。そこへ移るのは呼ぶ側（保存先の切り替え）。</summary>
    public record RestoreBackup(
        string ZipPath,
        string DestinationRoot,
        IProgress<Storage.BackupProgress>? Progress = null) : UiCommand;

    /// <summary>
    /// 保存先を引っ越す・置き換える（ユーザ判断 2026-09-20・E8）。
    /// **ここを通すのは、書き込みを止めてから運ぶため**——前は画面が直に呼んでいて、
    /// 運んでいる間の書き込みが素通りし、コピー済みへ書いた分が元を消すときに失われていた。
    /// 画面のスレッドも塞いでいた（数GBならその間ずっと無反応）。
    /// </summary>
    public record MoveStore(
        string Source,
        string Destination,
        bool Replace,
        IProgress<Storage.StoreMoveProgress>? Progress = null) : UiCommand;

    /// <summary>
    /// 展開フォルダで登録していた商品を、隣に現れたzipで登録し直す（結果は <see cref="CommandResult.ArchiveSwapped"/>）。
    /// zip が除外してある・ほかの商品が持つときは、何も書かずにそう返る。窓で聞いて頼まれたときだけ、
    /// <paramref name="LiftExclusion"/>（除外を解いて付ける）・<paramref name="TakeFromOtherItems"/>（ほかの商品から外して付ける）を立てて呼び直す。
    /// </summary>
    public record SwapFolderForArchive(
        string ItemId,
        string FolderPath,
        bool LiftExclusion = false,
        bool TakeFromOtherItems = false) : UiCommand;

    /// <summary>
    /// zip を一時フォルダへ展開する（#56）。展開先は <see cref="CommandResult.Unpacked"/> で返る。
    /// 中止したときは結果を返さず、<see cref="OperationCanceledException"/> を投げる（書きかけは消してある）。
    /// </summary>
    public record UnpackToTemporary(
        string ZipPath,
        IProgress<Services.TemporaryUnpackProgress>? Progress = null) : UiCommand;

    /// <summary>要確認の既読・未読を切り替える。消さずに既読にするのは「見た」と「無かった」を分けるため。</summary>
    public record SetNotificationRead(string Id, bool IsRead) : UiCommand;

    /// <summary>要確認をまとめて既読にする。</summary>
    public record MarkAllNotificationsRead : UiCommand;

    /// <summary>指した要確認だけをまとめて既読にする（束ごとの「まとめて既読」）。</summary>
    public record MarkNotificationsRead(IReadOnlyList<string> Ids) : UiCommand;

    /// <summary>用が済んだ要確認に「解消済み」の印を付ける（消さずに残す）。</summary>
    public record ResolveNotifications(IReadOnlyList<string> Ids) : UiCommand;

    /// <summary>読めない商品の記録を探して要確認に出す（読めるようになった物は解消済みにする）。結果は件数。</summary>
    public record DetectUnreadableItems : UiCommand;

    /// <summary>
    /// 読めない商品の記録を、アプリが最後に書いた版の控えに戻す（通知の「1つ前の版に戻す」）。
    /// 壊れた記録は items/_broken へよけて残す。
    /// </summary>
    public record RestoreItemCopy(string ItemId) : UiCommand;

    /// <summary>
    /// 読めない商品の記録をよけ、BOOTH から取り直して新しく作る（通知の「BOOTHから作り直す」）。
    /// BOOTH への問い合わせなので、ここを通して人が押した優先度で並ぶ。
    /// </summary>
    public record RebuildItemFromBooth(string ItemId) : UiCommand;

    /// <summary>
    /// 要確認を1件足す。同じIDの未読があれば差し替える（溜めても読む手間が増えるだけ）。
    /// 画面から書くので `UiCommand` を通す。
    /// </summary>
    public record AddNotification(Models.NotificationRecord Record) : UiCommand;

    /// <summary>
    /// 見つからない手元のファイルを、監視フォルダの中から**中身で**探して結び直す（G17）。
    /// 結果は <see cref="CommandResult.MissingFilesSearched"/> で返る。
    /// </summary>
    public record FindMissingFiles(IProgress<(int Hashed, string? Detail)>? Progress = null) : UiCommand;
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

    /// <summary>
    /// ファイルを管理対象から外した。<paramref name="AddedHashes"/> は今回除外の記録に足した物（前から除外していた物は入らない）。
    /// 外した直後に戻すときは、これを <see cref="UiCommand.UndoExclude"/> に渡す。
    /// </summary>
    public record FilesExcluded(IReadOnlyList<string> AddedHashes) : CommandResult;

    /// <summary>除外を解除した。未確定に戻したか（戻せなかった理由）を持つ。</summary>
    public record ExclusionLifted(Services.ExclusionLiftOutcome Outcome) : CommandResult;

    public record UnpackedFoldersRemoved(IReadOnlyList<UnpackedFolderRemoval> Results) : CommandResult;

    public record UserTagsChanged(Models.UserTagMaster Master) : CommandResult;

    /// <summary>改名・削除の結果。書き換えたitem数を持つのは、何が起きたかを画面に出すため。</summary>
    public record UserTagsRewritten(Services.UserTagEditResult Result) : CommandResult;

    public record AttributesChanged(Models.AttributeMaster Master) : CommandResult;

    /// <summary>改変を作った。画面はこのIDを開く</summary>
    public record ModificationCreated(Models.ModificationRecord Record) : CommandResult;

    /// <summary>改変の一覧が変わった（作った・消した）</summary>
    public record ModificationsChanged() : CommandResult;

    /// <summary>設定を書いた。書いた後の設定を持つ。</summary>
    public record SettingsChanged(Models.AppSettings Settings) : CommandResult;

    /// <summary>済んだ。数を持つ（書き換えた商品の数・取り除いた数など）。</summary>
    public record Counted(int Count) : CommandResult;

    /// <summary>共通素体を手で足した。足したか・戻したか・もうあったかを持つ。</summary>
    public record BaseAdded(Services.AvatarBaseAddOutcome Outcome) : CommandResult;

    /// <summary>ドライブ文字の組を確かめた。元の文字 → 今の文字の読み替えを持つ。</summary>
    public record VolumesObserved(IReadOnlyDictionary<string, string> Remap) : CommandResult;

    /// <summary>ショップのバナーを確かめた。手元に無く BOOTH にも無ければ null。</summary>
    public record ShopBannerEnsured(string? Path) : CommandResult;

    /// <summary>ショップの画像を取り直した。</summary>
    public record ShopImagesRefreshed(Services.ShopImageRefresh Result) : CommandResult;

    /// <summary>BOOTH から画像を取った。</summary>
    public record ImageFetched(byte[] Bytes) : CommandResult;

    /// <summary>検索の履歴を書いた。書いた後の履歴を持つ。</summary>
    public record SearchHistoryChanged(Services.SearchHistoryList History) : CommandResult;

    /// <summary>保存した検索を書いた。書いた後の並びを持つ。</summary>
    public record SavedSearchesChanged(Services.SavedSearchList Saved) : CommandResult;

    /// <summary>ショップの星・メモを変えた結果（書いた後の全店ぶん）。</summary>
    public record ShopNotesChanged(IReadOnlyList<Models.ShopNoteRecord> Notes) : CommandResult;

    /// <summary>属性の改名・削除の結果。書き換えたitem数を持つ。</summary>
    public record AttributesRewritten(Services.AttributeEditResult Result) : CommandResult;

    public record PreviewLoaded(Services.ItemPreview Preview) : CommandResult;

    /// <summary>
    /// BOOTHが「その商品は無い」と答えた（一時的に届かないのとは分ける）。
    /// このときだけ、見つからないIDのまま登録する道（<see cref="UiCommand.AssignUnpublishedItemId"/>）を出す。
    /// </summary>
    public record PreviewNotOnBooth(string ItemId, string Message) : CommandResult;

    /// <param name="BoothUnreachable">BOOTH に届かなかったか（E3）。0件でも「無い」と言い切らないため。</param>
    public record CandidatesProposed(
        IReadOnlyList<Resolution.ResolutionCandidate> Candidates, bool BoothUnreachable = false) : CommandResult;

    public record AvatarsDetected(Services.AvatarDetectResult Result) : CommandResult;

    public record Failed(string Message) : CommandResult;

    /// <summary>一時フォルダへ展開した。</summary>
    public record Unpacked(string Folder) : CommandResult;

    /// <summary>見つからないファイルを探した結果（G17）。</summary>
    public record MissingFilesSearched(Services.MissingFileSearchResult Result) : CommandResult;

    /// <summary>展開フォルダをzipへ切り替えた結果。</summary>
    public record ArchiveSwapped(Services.ArchiveSwapOutcome Outcome) : CommandResult;

    /// <summary>バックアップを書き出した。</summary>
    public record BackupExported(Storage.BackupResult Result) : CommandResult;

    /// <summary>バックアップを展開した。</summary>
    public record BackupRestored(int Files) : CommandResult;

    /// <summary>保存先を運んだ（引越し・置き換え）。失敗も結果の中に入っている。</summary>
    public record StoreMoved(Storage.StoreMoveResult Result) : CommandResult;
}
