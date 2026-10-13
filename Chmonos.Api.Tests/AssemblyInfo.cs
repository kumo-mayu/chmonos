// 保存先を運ぶ間の門（StoreWriteGate）はプロセスに1つなので、門を閉じる試験がほかの試験の書き込みを待たせないよう、試験を並べずに走らせる。
// 組（Collection）の並べない指定はその組の中にしか効かない。34件・数秒なので、全部を並べない代償は小さい（2026-10-13）
[assembly: CollectionBehavior(DisableTestParallelization = true)]
