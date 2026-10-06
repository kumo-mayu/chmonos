# NullPriceFix：購入記録の額の空欄を 0円にそろえる

2026-09-29 より前の版の Chmonos は、編集画面の額の欄を空欄にすると「未入力」として記録していました。
今の版は空欄を 0円として記録するので、前の版で 0円のつもりで空欄にした記録は、今の版では「額の分からない記録」に見えます
（統計の「払った額の記録が無い」に数えられ、払った額の範囲の絞り込みから外れます）。この道具は、その空欄を 0円に書き換えます。

## 使い方

1. **Chmonos を閉じる**（開いていると、道具は止まります）
2. 数えるだけ（何も書き換えません）：

   ```
   NullPriceFix.exe
   ```

   保存先は、Chmonos と同じ決め方で決まります（環境変数 `CHMONOS_HOME` → 設定で選んだ場所（`%LOCALAPPDATA%\Chmonos\location.json`）→ 既定の場所）。
   表示された「保存先」が、いつも使っている場所か確かめてください。違う場所を見たいときは `--store <場所>` を付けます。
3. 書き換える：

   ```
   NullPriceFix.exe --apply
   ```

   件数を出して「よろしいですか？ [y/N]」と聞くので、`y` で書き換えます。

## 何が変わるか

- 商品の記録（`items\{商品ID}.json`）の購入記録のうち、額が無い物にだけ `"price": 0` を足します。ほかの欄は1文字も変えません
- 書き換える前の記録は、保存先の中の `backup-null-price-日時` フォルダに写してから書きます
- 何度走らせても同じです（2回目からは「書き換える物はありません」）
- 読めない（壊れた）記録には触りません

## 元に戻すとき

Chmonos を閉じて、`backup-null-price-日時` の中の `.json` を、保存先の `items` フォルダへ上書きで写してください。

## 作る人へ

中身は `Chmonos.Core/Storage/NullPriceFixer.cs`（試験は `Chmonos.Core.Tests/NullPriceFixerTests.cs`）。配る形は
`dotnet publish tools/NullPriceFix -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`（.NET の入っていない PC でも動く）。
