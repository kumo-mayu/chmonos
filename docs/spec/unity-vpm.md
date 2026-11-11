# Unity プロジェクトの VPM（今の決め事）

> **要点**：Unity プロジェクトに VPM（VCC・ALCOM）で入っているパッケージを**読むだけで控える**（入れる・消す・版を変えるはしない。plan 40・ユーザ判断 2026-11-11「作りましょう」）。
> きっかけは、VPM で入っている物と同じ GUID・違う中身の物を unitypackage で取り込むと、Unity が `Assets/` に作らず `Packages/` の中の VPM のファイルを黙って上書きすること（[実測]・`docs/research/vpm-duplicate-guid.md`）。
>
> **コード**：`Core/Services/VpmPackages.cs`（読む）・`UnityProjectScan.Vpm`・`Storage/UnityScanStore`（控え）、`App/ViewModels/ModificationHubRows.cs`（`HubProjectDetail.VpmText`）
>
> 送る前の決め事（Unity への受け渡しの全体）は [unity.md](unity.md)、改変の画面は [modifications.md](modifications.md)。

## 読む物

- `Packages/vpm-manifest.json` の `dependencies`（人が入れた物）と **`locked`**（それが連れてきた物）。版は `locked` が先（実際に解決した版）。
  `locked` も読むのは、確かめ用のプロジェクトで Modular Avatar が連れてきた NDMF が `locked` にだけ書かれていたため（`dependencies` だけ見ると手で置いた物と取り違える）。
- `Packages/*/package.json`（`name`・`displayName`・`version`）。manifest に無い、手で置いた埋め込みの物も拾う（unitypackage で上書きされるのは同じ）。
  名前は `package.json` の `name` が正（フォルダの名前と違うことがある）。`package.json` の無いフォルダ・隠しフォルダ（`.git` など）はパッケージと見ない（Unity も読まない）。
- 1つの行は「名前・表示名・版・フォルダ・manifest にあるか」（`VpmPackage`）。版はフォルダがあれば `package.json`、無ければ manifest の物。
- 壊れた JSON・欠けた欄・形の違う欄は無いのと同じにし、読めた分だけ返す。1MB を超える物は読まない（手元の manifest は 1.4KB）。
- 確かめ用のプロジェクトで9本・27ms（JIT を含む。2026-11-11）。

## いつ読み、どこに控えるか

- **プロジェクトの中を調べる時と同じ時**（`AppServiceContainer.ScanUnityProject`。検索の条件「Unityプロジェクト」・使っている商品の見張り）に読み、調べた結果の控え（`unityscans/`）の `vpm` に書く。
- **形式の版は上げない**：控えは消しても調べ直すだけの写しで、欄の無い前の控えは「まだ読んでいない」（`Vpm` が null）と読めば足りる。空の配列は「読んだが1つも無い」。
- 照合の結果ではないので、`FindsSameAs`（一覧を組み直すか）には入れない。場所の付け替えでは、控えごと新しい場所へ写る（[modifications.md](modifications.md)「場所を付け替える」）。

## 見せる所

- **改変の画面のプロジェクトの右の欄**の「VPM」の行。1つ1行で「表示名 版」（表示名が無ければ名前）。並びは見せる名前の順。
  manifest にあってフォルダが無い物には「：フォルダが見つかりません」、manifest に無い物には「：手で置いたもの」と添える（どちらも VCC・ALCOM の一覧と見比べたときに食い違って見える）。
  行は多くても十数本なので畳まない。見出しは値の1行目にそろえる。
- 控えが無い（改変に紐付けておらず、開いてもいないプロジェクトは調べない）ときは「Unityで開くと表示されます。」、1つも無ければ「ありません。」。
- 読むのは右の欄を出す時（控えの小さなファイルを画面のスレッドで読む）。出している間に調べ直しが終わっても差し替えない（選び直すと新しい物）。

## 外から使う API

- `GET /v1/unity-scans` の答えに `vpm: [{ name, displayName, version, folder, inManifest }] | null` を足した（[local-api-stored.md](local-api-stored.md)）。null は読んでいない。API は読み直さない。
