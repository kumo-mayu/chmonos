import fs from 'node:fs';
import path from 'node:path';

// 画面に出る文字列を機械的に集める。
// XAML の Text= / Content= / ToolTip= / Header= / Placeholder= と、
// C# 側の MessageBox・Status・ユーザ向け文字列。
const roots = ['BoothAssetManager.App/Views', 'BoothAssetManager.App', 'BoothAssetManager.App/ViewModels'];

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'bin' || entry.name === 'obj') continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else if (/\.(xaml|cs)$/.test(entry.name)) out.push(full);
  }
  return out;
}

const files = walk('BoothAssetManager.App');
const rows = [];
const seen = new Set();

const hasJapanese = (s) => /[぀-ヿ一-鿿]/.test(s);

for (const file of files) {
  const text = fs.readFileSync(file, 'utf8');
  const lines = text.split(/\r?\n/);

  lines.forEach((line, i) => {
    if (file.endsWith('.xaml')) {
      for (const m of line.matchAll(/(Text|Content|ToolTip|Header|Placeholder|Label)="([^"{}]+)"/g)) {
        const value = m[2].trim();
        if (!hasJapanese(value)) continue;
        const key = value;
        if (seen.has(key)) return;
        seen.add(key);
        rows.push({ file, line: i + 1, kind: m[1], value });
      }
    } else {
      // C# は文字列リテラルのうち日本語を含むものだけ
      for (const m of line.matchAll(/"((?:[^"\\]|\\.)*)"/g)) {
        const value = m[1];
        if (!hasJapanese(value)) continue;
        if (value.length < 3) continue;
        if (seen.has(value)) return;
        seen.add(value);
        rows.push({ file, line: i + 1, kind: 'cs', value });
      }
    }
  });
}

// 6つの基準で印を付ける
const internalWords = ['item', 'appTag', 'BoothID', 'variation', 'ハッシュ', 'マスタ', 'JSON', 'ID ', 'h2', 'null'];
const negativeShapes = ['できません', 'ありません', 'されていません', 'えません', 'られません', '不可', '無効'];

function flags(v) {
  const f = [];

  // ① 否定に読める（何ができないかだけを言っていて、次に何をすればよいか書いていない）
  if (negativeShapes.some((w) => v.includes(w)) && !/してください|できます|します。|ます。$/.test(v.replace(/。$/, '。'))) {
    f.push('否定');
  }

  // ② 内部の言葉が漏れている
  if (internalWords.some((w) => v.includes(w))) f.push('内部語');

  // ③ 何が起きるかは書いてあるが、なぜかが無い（短い断定文）
  if (/します。$|されます。$/.test(v) && v.length < 26 && !/ため|ので|から/.test(v)) f.push('理由なし');

  // ④ 破壊的な操作なのに取り返しの記載が無い
  if (/消し|削除|外し|除外|捨て/.test(v) && !/戻せ|戻り|復元|元に戻|残り|残し/.test(v)) f.push('取り返し');

  // ⑤ 空表示・エラーで次の行動が無い
  if (/^(まだ|該当|見つかり|ありません)/.test(v) && !/してください|できます|から/.test(v)) f.push('次の行動');

  // ⑥ 説明が長い
  if (v.length >= 60) f.push('長い');

  return f;
}

for (const r of rows) r.flags = flags(r.value);

const marked = rows.filter((r) => r.flags.length > 0);

console.log(`抽出した文字列: ${rows.length} 件（重複を除く）`);
console.log(`印が付いた: ${marked.length} 件\n`);

const byFlag = {};
for (const r of marked) for (const f of r.flags) (byFlag[f] ??= []).push(r);
for (const [flag, list] of Object.entries(byFlag)) {
  console.log(`  ${flag}: ${list.length} 件`);
}

fs.writeFileSync(
  process.argv[2],
  JSON.stringify({ total: rows.length, rows }, null, 2),
  'utf8');
console.log(`\n全件を ${process.argv[2]} に書き出した`);
