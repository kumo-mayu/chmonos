// 試験は並べて走らせない。画面のスレッドは1本で（Support/UiThread.cs）、アプリ全体に効く静的な状態
// （ログの書き先・知らせの窓の受け口・一覧の大きさ・表示の倍率）も試験ごとに入れ替えるため。
// 並べると、隣の試験の保存先へログが書かれ、隣の試験が出した知らせを拾う
[assembly: CollectionBehavior(DisableTestParallelization = true)]
