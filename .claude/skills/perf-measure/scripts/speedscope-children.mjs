// 指定の関数の直下（N段まで）に呼ばれた関数の、含む時間。時刻の範囲で絞れる。
// node speedscope-children.mjs <speedscope.json> <関数の正規表現> [段=3] [開始-終了]
import { readFileSync } from 'node:fs';
const [file, pattern, depthArg, windowArg] = process.argv.slice(2);
const re = new RegExp(pattern);
const depth = Number(depthArg ?? 3);
const [from, to] = windowArg ? windowArg.split('-').map(Number) : [-Infinity, Infinity];
const data = JSON.parse(readFileSync(file, 'utf8'));
const frames = data.shared.frames.map((f) => f.name);
let best = null;
for (const p of data.profiles) {
  const n = p.events.filter((e) => /MediaContext\.RenderMessageHandler/.test(frames[e.frame])).length;
  if (!best || n > best.n) best = { p, n };
}
const totals = new Map();
const stack = [];
for (const e of best.p.events) {
  if (e.type === 'O') { stack.push({ frame: e.frame, at: e.at }); continue; }
  const open = stack.pop();
  if (!open || open.at < from || open.at > to) continue;
  const anchor = stack.findLastIndex((s) => re.test(frames[s.frame]));
  if (anchor < 0) continue;
  const level = stack.length - anchor;
  if (level > depth) continue;
  const key = `${'  '.repeat(level - 1)}${frames[open.frame].replace(/\(.*$/, '').slice(0, 150)}`;
  totals.set(key, (totals.get(key) ?? 0) + (e.at - open.at));
}
for (const [k, v] of [...totals].sort((a, b) => b[1] - a[1]).slice(0, 40)) console.log(`${String(Math.round(v)).padStart(6)}  ${k}`);
