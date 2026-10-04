// 画面に出る文を App と Core から集めて、書き方の決まりに外れていそうな所に印を付ける。
// 決まりは docs/spec/ui-writing.md、語の表は docs/spec/ui-terms.md（表はここから読む）。
// 使い方は .claude/skills/ui-wording/SKILL.md。素の Node で動かす（依存を足さない）。
//
//   node tools/wording.mjs                 画面ごと・印ごとの数
//   node tools/wording.mjs --list          一覧も出す（画面ごと。file:line・印・文）
//   node tools/wording.mjs --list --screen 検索 --flag 括弧の補足
//   node tools/wording.mjs --weak          弱い印も一覧に入れる
//   node tools/wording.mjs --json out.json 全件を JSON で書き出す（直す前後を比べるとき）
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const opt = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const has = (name) => args.includes(name);

// ---- 集める ----------------------------------------------------------------

// 辞書・検出の語の表は画面の文ではない（アバター名・読み・同義語が何千件も入っている）
const notScreenFile = /AvatarDetector\.cs|AvatarBaseSeed\.cs|RomajiReading\.cs|KanjiReadings\.cs|JapaneseDictionary\.cs|ReadingMatch\.cs|AppSettings\.cs|Synonym|Kana/;
// 画面ではなく記録のファイルへ書く文
const notScreenFileLog = /UiTrace\.cs/;

function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'bin' || e.name === 'obj') continue;
    const full = path.join(dir, e.name);
    if (e.isDirectory()) walk(full, out);
    else if (/\.(xaml|cs)$/.test(e.name) && !notScreenFile.test(e.name) && !notScreenFileLog.test(e.name)) out.push(full);
  }
  return out;
}

const files = ['Chmonos.App', 'Chmonos.Core'].flatMap((d) => walk(path.join(repo, d)));
const jp = (s) => /[぀-ヿ一-鿿]/.test(s);
const rel = (f) => path.relative(repo, f).split(path.sep).join('/').replace(/^Chmonos\./, '');

// 吹き出しに結ばれた ViewModel の名前（ToolTip="{Binding OpenHint}" の OpenHint）。
// C# の側ではこの名前の中の文字列を吹き出しとして扱う。Name・Path のような
// 汎用の名前は本文にも結ばれているので、Tip・Hint などで終わる物だけにする
const tipNames = new Set();
for (const f of files.filter((f) => f.endsWith('.xaml'))) {
  for (const m of fs.readFileSync(f, 'utf8').matchAll(/ToolTip="\{Binding (?:Path=)?([\w.]+)/g)) {
    const name = m[1].split('.').pop();
    if (/(Tip|ToolTip|Tooltip|Hint|Reason|Alert)$/.test(name)) tipNames.add(name);
  }
}

const rows = [];
const add = (file, line, kind, value) => {
  value = value.replace(/&#x0*[aA];|&#10;/g, '\n').replace(/\\n/g, '\n').replace(/&quot;/g, '"').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
  if (jp(value)) rows.push({ file: rel(file), line, kind, value });
};

for (const file of files) {
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  if (file.endsWith('.xaml')) {
    let inComment = false;
    let tipDepth = 0; // <Button.ToolTip> の中の文は吹き出し
    lines.forEach((line, i) => {
      let t = line;
      if (inComment) { if (!t.includes('-->')) return; t = t.slice(t.indexOf('-->') + 3); inComment = false; }
      t = t.replace(/<!--.*?-->/g, '');
      if (t.includes('<!--')) { t = t.slice(0, t.indexOf('<!--')); inComment = true; }
      if (/<[\w:]+\.ToolTip>/.test(t)) tipDepth++;
      const inTip = tipDepth > 0;
      if (/<\/[\w:]+\.ToolTip>/.test(t)) tipDepth = Math.max(0, tipDepth - 1);
      for (const m of t.matchAll(/\b([\w.:]*(?:Text|Content|ToolTip|Header|Placeholder|Label|Title|Hint|Name))="([^"]*)"/g)) {
        let v = m[2];
        if (!jp(v)) continue;
        if (/x:Name|AutomationProperties/.test(m[1]) && !/AutomationProperties\.(Name|HelpText)/.test(m[1])) continue;
        const sf = v.match(/StringFormat='([^']*)'/) || v.match(/StringFormat=([^,}]*)/);
        const tv = v.match(/(?:TargetNullValue|FallbackValue)='([^']*)'/);
        if (sf) v = sf[1]; else if (v.startsWith('{')) { if (!tv) continue; v = tv[1]; }
        const kind = /ToolTip/.test(m[1]) || inTip ? '吹き出し' : m[1].replace(/^.*\./, '');
        add(file, i + 1, kind, v);
      }
      // 要素の中身に直に書いた文（<TextBlock>検索画面と同じ書き方で…<LineBreak/>）
      const bodyKind = inTip ? '吹き出し' : 'Text';
      for (const m of t.matchAll(/>([^<>]+)</g)) if (!/="/.test(m[1])) add(file, i + 1, bodyKind, m[1].trim());
      const lead = t.trim().startsWith('<') ? '' : t.split('<')[0].trim();
      if (lead && !lead.includes('="')) add(file, i + 1, bodyKind, lead);
    });
  } else {
    let member = '';
    let logUntilSemicolon = false;
    let pending = null; // 行をまたいで + でつないだ文字列
    lines.forEach((line, i) => {
      const t = line.trim();
      if (t.startsWith('//') || t.startsWith('*') || t.startsWith('/*') || t.startsWith('[')) return;
      const code = line.replace(/\/\/[^"]*$/, '');
      const decl = code.match(/\b(?:public|private|internal|protected)\b[^;="]*?\b([A-Z]\w*)\s*(?:=>|\{|\(|$|=(?!=))/);
      if (decl) member = decl[1];
      // ログ・例外・デバッグの文は画面に出ない（例外の文を画面に出さない決まりは ui-empty-and-errors.md）
      const isLog = logUntilSemicolon || /AppLog\.|\bLog(Info|Warn|Error|Debug)?\(|Debug\.|Trace\.|throw new|Exception\(|Console\./.test(code);
      logUntilSemicolon = isLog && !code.includes(';');
      const literals = [...code.matchAll(/\$?@?"((?:[^"\\]|\\.)*)"/g)];
      if (literals.length === 0) { if (pending) { add(file, pending.line, pending.kind, pending.value); pending = null; } return; }
      if (isLog) { pending = null; return; }
      const kind = tipNames.has(member) || /ToolTip\s*=/.test(code) ? '吹き出し' : '文';
      const values = literals.map((m) => m[1]);
      if (pending && /^\s*\+?\s*\$?@?"/.test(code)) { values[0] = pending.value + values[0]; }
      else if (pending) add(file, pending.line, pending.kind, pending.value);
      const startLine = pending && /^\s*\+?\s*\$?@?"/.test(code) ? pending.line : i + 1;
      const startKind = pending && /^\s*\+?\s*\$?@?"/.test(code) ? pending.kind : kind;
      pending = null;
      const after = code.slice(literals.at(-1).index + literals.at(-1)[0].length);
      const continues = /^\s*\+\s*$/.test(after) || (i + 1 < lines.length && /^\s*\+\s*\$?@?"/.test(lines[i + 1]));
      values.forEach((v, k) => {
        const isFirst = k === 0, isLast = k === values.length - 1;
        if (isLast && continues) { pending = { line: isFirst ? startLine : i + 1, kind: isFirst ? startKind : kind, value: v }; return; }
        if (v.length < 2) return;
        add(file, isFirst ? startLine : i + 1, isFirst ? startKind : kind, v);
      });
    });
    if (pending) add(file, pending.line, pending.kind, pending.value);
  }
}

// ---- 用語表を読む --------------------------------------------------------------

const termsFile = path.join(repo, 'docs/spec/ui-terms.md');
const terms = fs.readFileSync(termsFile, 'utf8').split(/\r?\n/);
function table(heading) {
  const start = terms.findIndex((l) => l.trim() === `## ${heading}`);
  if (start < 0) throw new Error(`${termsFile} に「## ${heading}」がありません`);
  const out = [];
  for (const l of terms.slice(start + 1)) {
    if (l.startsWith('## ')) break;
    if (!l.startsWith('|') || /^\|[-| ]+\|$/.test(l)) continue;
    const cells = l.slice(1, -1).split(/(?<!\\)\|/).map((c) => c.trim());
    const cell = cells.findLast((c) => c.includes('`')) ?? '';
    const patterns = [...cell.matchAll(/`([^`]+)`/g)].map((m) => m[1].replace(/\\\|/g, '|'));
    if (patterns.length) out.push({ label: cells[0], use: cells[1], patterns: patterns.map((p) => new RegExp(p, 'u')) });
  }
  return out;
}
const banned = table('使わない語');
const unify = table('揃える表記');
const undecided = table('まだ決めていない揺れ');

// ---- 印を付ける ------------------------------------------------------------

// 置き換える値（{Item.Name}・{0}）は2文字として数える。中の式の長さで長く見えないように
const shown = (v) => v.replace(/\{[^{}]*\}/g, '●●');
const sentences = (v) => shown(v).split(/(?<=[。！？])|\n/).map((s) => s.trim()).filter(Boolean);
const internal = /\bitem\b|appTag|BoothID|variation|ハッシュ|マスタ|\bnull\b|exception/;

// 強い印：まず直す所。弱い印：読んで決める所（見当違いも多い）
const weakFlags = new Set(['一文50字超', '理由（本文）', '否定', '取り返し', '次の行動']);
function flagsOf(r) {
  const v = r.value, f = [];
  const tip = r.kind === '吹き出し';
  // 文の終わりの括弧の補足。数やキーだけの括弧（「未確定（{n}）」「（3 件）」「（←）」）は補足ではないので数えない
  const tailParens = [...v.matchAll(/（([^（）]*)）[。]?(?=\s*$|\n)/g)].map((m) => m[1]);
  const onlyCount = (c) => !jp(c.replace(/\{[^{}]*\}|[件枚体つ個日秒分]/g, ''));
  if (tailParens.some((c) => !onlyCount(c)) && !/^（[^（）]*）$/.test(v.trim())) f.push('括弧の補足');
  for (const b of banned) if (b.patterns.some((p) => p.test(shown(v)))) f.push(`使わない語:${b.label}`);
  for (const u of unify) if (u.patterns.some((p) => p.test(v))) f.push(`揺れ:${u.use}`);
  if (tip && shown(v).replace(/\n/g, '').length > 40) f.push('吹き出し40字超');
  if (!tip) {
    const longest = Math.max(0, ...sentences(v).map((s) => s.length));
    if (longest > 80) f.push('一文80字超'); else if (longest > 50) f.push('一文50字超');
  }
  if (/\*\*|`|(^|\n)#{1,3} |__/.test(v)) f.push('Markdown');
  if (/\}件/.test(v)) f.push('件の空白');
  const reason = /ので[、。]|ため[、。に]|ためです|前は|以前は/;
  if (tip && reason.test(v)) f.push('吹き出しに理由');
  if (!tip && reason.test(v)) f.push('理由（本文）');
  if (internal.test(shown(v))) f.push('内部語');
  if (/できません|ありません|されていません|られません|不可|無効/.test(v) && !/ください|できます|すると|ると、/.test(v)) f.push('否定');
  if (/削除|消し|消え/.test(v) && /します|しますか|ますか/.test(v) && !/戻せ|戻り|元に戻|残り|残し|残ります|消しません/.test(v)) f.push('取り返し');
  if (/^(まだ|該当|見つかり)/.test(v) && !/ください|できます|すると|と、/.test(v)) f.push('次の行動');
  return f;
}

// 画面の名前は、ファイルの名前から決める（どの画面の文かを見て回るため）
const screens = [
  ['設定・初回', /Settings|FirstRun|StoreMover|Shortcut/],
  ['検索', /Search/],
  ['取り込み', /Import(View|ViewModel)|ImportNotice/],
  ['改変', /Modification|UnityMember|PickPackages/],
  ['アバターの管理', /Avatar(s|Base|Group)|ItemAvatars/],
  ['タグの管理', /TagManage|RenameTag|MoveSub|MergeTag/],
  ['属性の管理', /Attribute/],
  ['編集', /Edit(View|ViewModel)|ChangeItemId/],
  ['未確定', /Resolve/],
  ['商品ページ', /Item(View|ViewModel|Files|Rows|Card|Selection|Unity|File)/],
  ['統計', /Stats/],
  ['通知', /Inbox|Notification/],
  ['フォルダ', /Folder/],
  ['ショップ', /Shop/],
  ['主画面・共通', /MainWindow|MainViewModel|ChoiceDialog|ListChoice|Notices|App\.xaml/],
  ['Unity連携', /Unity/],
  ['Core の失敗の文', /^Core\/(Commands|Services\/FailureText|Services\/ItemService)/],
];
const screenOf = (f) => (screens.find(([, re]) => re.test(f)) || [f.startsWith('Core') ? 'Core その他' : 'App その他'])[0];

for (const r of rows) { r.screen = screenOf(r.file); r.flags = flagsOf(r); }

// ---- 出力 --------------------------------------------------------------------

const strong = (r) => r.flags.filter((f) => !weakFlags.has(f));
const weak = (r) => r.flags.filter((f) => weakFlags.has(f));
const flagKey = (f) => f.replace(/:.*/, '');
const count = (list, fn) => { const o = {}; for (const r of list) for (const k of new Set(fn(r))) o[k] = (o[k] || 0) + 1; return o; };
const pad = (s, n) => s + ' '.repeat(Math.max(0, n - [...s].reduce((w, c) => w + (c.charCodeAt(0) > 0xff ? 2 : 1), 0)));

const app = rows.filter((r) => r.file.startsWith('App')).length;
const marked = rows.filter((r) => strong(r).length);
console.log(`画面の文 ${rows.length} 箇所（App ${app}・Core ${rows.length - app}。重複を除くと ${new Set(rows.map((r) => r.value)).size} 通り）`);
console.log(`強い印の付いた文 ${marked.length} 箇所・弱い印だけの文 ${rows.filter((r) => !strong(r).length && weak(r).length).length} 箇所\n`);

console.log('印ごと（1つの文に同じ印は1回と数える）');
const byFlag = count(rows, (r) => r.flags.map(flagKey));
for (const [k, n] of Object.entries(byFlag).sort((a, b) => b[1] - a[1])) console.log(`  ${pad(k, 16)} ${String(n).padStart(4)}${weakFlags.has(k) ? '  （弱い印）' : ''}`);

console.log('\n使わない語・揃える表記の内訳');
const byWord = count(rows, (r) => r.flags.filter((f) => f.includes(':')));
for (const [k, n] of Object.entries(byWord).sort((a, b) => b[1] - a[1])) console.log(`  ${pad(k, 30)} ${String(n).padStart(4)}`);

console.log('\nまだ決めていない揺れ（数えるだけ）');
for (const u of undecided) console.log(`  ${pad(u.label, 34)} ${u.patterns.map((p) => `${rows.filter((r) => p.test(r.value)).length}`).join(' ／ ')}`);

console.log('\n画面ごと（文の数・強い印の付いた文・弱い印だけの文・吹き出し40字超・一文80字超・括弧の補足・使わない語）');
const byScreen = {};
for (const r of rows) {
  const s = (byScreen[r.screen] ??= { n: 0, s: 0, w: 0, tip: 0, long: 0, paren: 0, word: 0 });
  s.n++; if (strong(r).length) s.s++; else if (weak(r).length) s.w++;
  if (r.flags.includes('吹き出し40字超')) s.tip++;
  if (r.flags.includes('一文80字超')) s.long++;
  if (r.flags.includes('括弧の補足')) s.paren++;
  if (r.flags.some((f) => f.startsWith('使わない語'))) s.word++;
}
console.log(`  ${pad('画面', 18)}   文  強  弱 吹40 文80 括弧 語`);
for (const [k, s] of Object.entries(byScreen).sort((a, b) => b[1].s - a[1].s)) {
  console.log(`  ${pad(k, 18)} ${[s.n, s.s, s.w, s.tip, s.long, s.paren, s.word].map((x) => String(x).padStart(4)).join('')}`);
}

if (has('--list')) {
  const screen = opt('--screen'), flag = opt('--flag');
  const pick = rows.filter((r) => (!screen || r.screen === screen)
    && (has('--weak') ? r.flags.length : strong(r).length)
    && (!flag || r.flags.some((f) => f.startsWith(flag))));
  let current = '';
  for (const r of pick.sort((a, b) => a.screen.localeCompare(b.screen) || a.file.localeCompare(b.file) || a.line - b.line)) {
    if (r.screen !== current) { current = r.screen; console.log(`\n## ${current}`); }
    const shownFlags = has('--weak') ? r.flags : strong(r);
    console.log(`${r.file}:${r.line} [${r.kind}] ${shownFlags.join(', ')}\n    ${r.value.replace(/\n/g, '⏎')}`);
  }
}

const jsonOut = opt('--json');
if (jsonOut) {
  fs.writeFileSync(jsonOut, JSON.stringify({ total: rows.length, rows }, null, 1), 'utf8');
  console.log(`\n全件を ${jsonOut} に書き出した`);
}
