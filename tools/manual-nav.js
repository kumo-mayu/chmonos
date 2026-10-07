// 使い方：manual フォルダの中で node ../tools/manual-nav.js（章を足したら下の parts にも足す）
// 章の冒頭と末尾に、目次・前・次の記事へのリンクを入れる。前と次は部の中でたどる。
// 印のコメントで囲むので、章を足したら同じ道具で入れ直せる
const fs = require("fs");
const parts = [
  ["01-start", "02-import", "03-find", "04-unity", "05-record"],
  ["10-search", "11-item", "12-edit", "13-resolve", "14-import", "15-folders", "16-modifications", "17-avatars", "18-shops", "19-stats", "20-inbox", "21-tags-attributes", "22-settings", "23-keys"],
  ["30-data", "31-faq"],
];
const title = n => fs.readFileSync(n + ".md", "utf8").split("\n")[0].replace(/^# /, "").trim();
const strip = t => t.replace(/<!-- nav:top -->[\s\S]*?<!-- \/nav:top -->\n\n/, "").replace(/\n\n<!-- nav:bottom -->[\s\S]*?<!-- \/nav:bottom -->/, "");
for (const part of parts) {
  part.forEach((name, i) => {
    let t = strip(fs.readFileSync(name + ".md", "utf8"));
    const prev = i > 0 ? part[i - 1] : null, next = i < part.length - 1 ? part[i + 1] : null;
    const top = "<!-- nav:top -->\n[← 目次へ](README.md)" + (prev ? `　｜　[← 前の記事：${title(prev)}](${prev}.md)` : "") + "\n<!-- /nav:top -->\n\n";
    const lines = t.split("\n");
    // 見出しの直後に置く
    lines.splice(1, 0, "", top.trimEnd());
    t = lines.join("\n").replace(/^(# [^\n]+)\n\n\n/, "$1\n\n");
    if (next) {
      const bottom = `\n\n<!-- nav:bottom -->\n\n---\n\n[次の記事：${title(next)} →](${next}.md)\n<!-- /nav:bottom -->`;
      // 画像のメモのコメントが末尾にあれば、その前に置く
      const m = t.match(/\n*(<!-- 画像：[\s\S]*-->)\s*$/);
      t = m ? t.slice(0, m.index).trimEnd() + bottom + "\n\n" + m[1] + "\n" : t.trimEnd() + bottom + "\n";
    }
    fs.writeFileSync(name + ".md", t);
  });
}
