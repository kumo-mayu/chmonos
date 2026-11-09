// docs/feedback/done-*.md の見出し（## と ###）から、1件1行の索引 docs/feedback/done-index.md を作り直す。
// 探す時は索引を Grep し、当たった行の「ファイル:行」を Read の offset で開く。直した記録を足した後に走らせる。
// 使い方：node .claude/skills/feedback-log/scripts/done-index.mjs（リポジトリの直下で）
import fs from "fs";
import path from "path";

const dir = path.join(process.cwd(), "docs", "feedback");
const files = fs.readdirSync(dir).filter((f) => /^done-\d{4}-\d{2}\.md$/.test(f)).sort();
const out = [
  "# 直した記録の索引（自動で作る。手で直さない）",
  "",
  "`done-YYYY-MM.md` の見出しの一覧。`.claude/skills/feedback-log/scripts/done-index.mjs` が作り直す。",
  "探す時はこのファイルを Grep し、当たった行の `ファイル:行` を Read の offset で開く（done は大きいので丸ごと読まない）。",
  "`##` の行は日ごとのまとまり、その下の `-` がまとまりの中の節。",
  "",
];
let count = 0;
for (const f of files) {
  const lines = fs.readFileSync(path.join(dir, f), "utf8").split(/\r?\n/);
  out.push(`## ${f}`, "");
  let inCode = false;
  lines.forEach((line, i) => {
    if (/^\s*(```|~~~)/.test(line)) return void (inCode = !inCode);
    if (inCode) return;
    const m = /^(#{2,3}) (.+)$/.exec(line);
    if (!m) return;
    const where = `${f}:${i + 1}`;
    out.push(m[1] === "##" ? `- **${m[2].trim()}** ${where}` : `  - ${m[2].trim()} ${where}`);
    count++;
  });
  out.push("");
}
fs.writeFileSync(path.join(dir, "done-index.md"), out.join("\n"));
console.log(`done-index.md：${files.length} ファイル・${count} 件`);
