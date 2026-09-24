// 本番の location.json（%LOCALAPPDATA%\Chmonos\location.json）を守る。CLAUDE.md「本番のデータを触らない」。
// 本番の location.json はユーザが普段使う friendtest を指していて、書き換えるとユーザの作業場所が変わってしまう。
// 決め事だけでは長い会話で抜けることがあるので、書き込みの手前で止める。
// 写しの保存先は BoothAssetManager-<名前> なので、この形には当たらない
import fs from "fs";

const target = /(^|[\\/])chmonos[\\/]+location\.json/i;

let input;
try {
  input = JSON.parse(fs.readFileSync(0, "utf8"));
} catch {
  process.exit(0); // 読めない入力で道具を止めない
}

const tool = input.tool_name ?? "";
const args = input.tool_input ?? {};

// ファイルを書く道具は、本番の location.json なら止める
const path = args.file_path ?? args.notebook_path ?? "";
if (path && target.test(path)) {
  answer("deny", "本番の location.json は書き換えない（CLAUDE.md「本番のデータを触らない」）。確かめは写しの保存先で行う。");
}

// シェルは読むだけのこともあるので、止めずにユーザに確かめてもらう
if ((tool === "Bash" || tool === "PowerShell") && target.test(args.command ?? "")) {
  answer("ask", "本番の location.json に触れるコマンドです。読むだけか確かめてください（CLAUDE.md「本番のデータを触らない」）。");
}

process.exit(0);

function answer(decision, reason) {
  // Windows の文字コードで化けないよう、日本語は \u 形式にして出す
  const json = JSON.stringify({
    hookSpecificOutput: { hookEventName: "PreToolUse", permissionDecision: decision, permissionDecisionReason: reason },
  }).replace(/[^\x00-\x7f]/g, (c) => "\\u" + c.charCodeAt(0).toString(16).padStart(4, "0"));
  process.stdout.write(json);
  process.exit(0);
}
