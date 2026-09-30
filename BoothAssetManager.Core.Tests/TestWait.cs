namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 試験で <c>BoothClient</c> に渡す「待たない待ち」。
///
/// <c>FetchIntervalMs = 0</c> では待ちは消えない。間隔の床（1.5秒）を <c>BoothClient</c> 自身が踏むので
/// （設定を通さずに組み立てる道でも守るため。2026-09-20）、作り物の BOOTH に問い合わせるたびに実際に1.5秒待つ。
/// 取り込みの試験は1件で4〜12回問い合わせるので1件 6〜18秒になり、一式の90秒はこの待ちで決まっていた（2026-09-30 に測った）。
///
/// 待ちを差し替えても、順番・回数・優先度は変わらない（門は1本のままで、待ちの長さだけが0になる）。
/// 「どれだけ待つと言ったか」を確かめる試験は、待ちを記録する関数を自分で渡す（<c>BoothClientTests</c>）。
///
/// **待ちを差し替えた <c>BoothClient</c> は、PC で1つの門（<c>BoothMachineGate</c>）に入らない**（2026-09-30）。
/// 入ると、待たない試験が本物の門のファイルを書き換えて隣で動いている本物のアプリを待たせ、
/// 試験の結果もその PC でアプリが動いているかで変わる。**相手が作り物のときだけ使う**——本物の BOOTH を相手に使うと、
/// 間隔が無くなり、門にも入らない（絶対に破らない決め事1）。
/// </summary>
internal static class TestWait
{
    public static readonly Func<TimeSpan, CancellationToken, Task> None = (_, _) => Task.CompletedTask;
}
