// 試験はそれぞれ自分の一時フォルダの API を立てるが、控えの錠（名前付きの Mutex）と Console の差し替えはプロセスで1つなので、並べずに走らせる（数秒の一式）
[assembly: CollectionBehavior(DisableTestParallelization = true)]
