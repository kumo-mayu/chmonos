# 残りの作業

## 次にやること

0. 済み：v1.2.0 の前に release を `workflow_dispatch` で試しに組んだ（2026-11-05・7分38秒で通った）
1. 済み：v1.2.0 を公開した（2026-11-05。Releases・BOOTH）
2. `SixLabors.ImageSharp` は 3.1.12 のまま（ユーザ判断 2026-11-01）。知らせ6件（GHSA-gwg2-r3hj-4w44・wmxv-xphr-5c9g・j3p4-wp97-rph4・j9gm-c75j-xc9q・jjfr-hcj7-qf5w・v76p-62qx-wwq2）は、TIFF・ICC・ヒストグラム平坦化のどれかで、今の作りでは通らない。3.2.0・4.x は Release で組むのにライセンスキーが要る（3.2.0 で実際に止まった）。新しい知らせが JPEG・PNG・GIF・WebP・BMP の読み取りに当たるなら見直す
3. v1.2.1 で、画像の設定の文言を「容量が小さくなり、取り込みも速くなる」の両方を書く形に直す（ユーザ判断 2026-11-06。マニュアル 01-start は直した）。今は「かなり速くなり」だけで、初回の画面 `FirstRunWindow.xaml`・設定 `SettingsView.xaml`・切り替えたときの確認 `SettingsViewModel.cs` の3か所
