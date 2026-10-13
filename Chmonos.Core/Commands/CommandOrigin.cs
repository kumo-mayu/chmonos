namespace Chmonos.Core.Commands;

/// <summary>命令がどこから来たか（<see cref="CommandHandler.ExecuteAsync"/>）。</summary>
public enum CommandOrigin
{
    /// <summary>アプリの画面・裏の作業。画面から来た命令は「人が押した」優先度で BOOTH へ問い合わせる。</summary>
    App,

    /// <summary>外から使う API（docs/spec/local-api.md）。人が画面の前で待っているわけではないので、「人が押した」優先度を掛けない。</summary>
    Api,
}
