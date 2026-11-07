// speedscope（dotnet-trace convert）の画面のスレッドを、関数ごとの含む時間・自分の時間で並べる。
// 使い方: node speedscope-top.mjs <file.speedscope.json> [含む時間で並べる数=40] [名前の絞り込みの正規表現]
import { readFileSync } from 'node:fs';

const [file, topArg, filterArg] = process.argv.slice(2);
const top = Number(topArg ?? 40);
const filter = filterArg ? new RegExp(filterArg) : null;
const data = JSON.parse(readFileSync(file, 'utf8'));
const frames = data.shared.frames.map((frame) => frame.name);

function walk(profile) {
  const inclusive = new Map();
  const self = new Map();
  const stack = [];
  let last = profile.startValue;
  for (const event of profile.events) {
    if (stack.length > 0) {
      const topFrame = stack[stack.length - 1].frame;
      self.set(topFrame, (self.get(topFrame) ?? 0) + (event.at - last));
    }
    last = event.at;
    if (event.type === 'O') {
      stack.push({ frame: event.frame, at: event.at });
    } else {
      const open = stack.pop();
      if (!open) continue;
      // 再帰で同じ関数が下にもあるときは、いちばん外の分だけ数える
      if (!stack.some((entry) => entry.frame === open.frame)) {
        inclusive.set(open.frame, (inclusive.get(open.frame) ?? 0) + (event.at - open.at));
      }
    }
  }
  return { inclusive, self };
}

// 画面のスレッド：描画の処理（MediaContext）を含む時間がいちばん長いスレッド
let best = null;
for (const profile of data.profiles) {
  const result = walk(profile);
  const render = [...result.inclusive].filter(([frame]) => /MediaContext\.RenderMessageHandler|Dispatcher\.PushFrame/.test(frames[frame]))
    .reduce((sum, [, value]) => Math.max(sum, value), 0);
  if (!best || render > best.render) best = { profile, result, render };
}

// 1回ごとの長さ：画面のスレッドの、いちばん外側の処理（Dispatcher の1回の仕事）の長い順。
// SPANS=1 で出す。PushFrame の直下の1段（ProcessQueue など）を1回の仕事とみなす
if (process.env.SPANS) {
  const spans = [];
  const stack = [];
  for (const event of best.profile.events) {
    if (event.type === 'O') {
      stack.push({ frame: event.frame, at: event.at });
    } else {
      const open = stack.pop();
      if (open && /Dispatcher\.ProcessQueue|Dispatcher\.WndProcHook|HwndSubclass\.SubclassWndProc/.test(frames[open.frame])
        && !stack.some((entry) => /Dispatcher\.ProcessQueue|Dispatcher\.WndProcHook|HwndSubclass\.SubclassWndProc/.test(frames[entry.frame]))) {
        spans.push({ at: open.at, length: event.at - open.at });
      }
    }
  }
  spans.sort((a, b) => b.length - a.length);
  console.log('長い仕事（開始 ms・長さ ms）:', spans.slice(0, Number(process.env.SPANS)).map((span) => `${Math.round(span.at)}:${Math.round(span.length)}`).join('  '));
}

// LONG=<ms>：その長さ以上の仕事の中だけで、関数ごとの含む時間を数え直す（開いたときの固まりの中身）
if (process.env.LONG) {
  const threshold = Number(process.env.LONG);
  const outer = /Dispatcher\.ProcessQueue|Dispatcher\.WndProcHook|HwndSubclass\.SubclassWndProc/;
  // 1回目：長い仕事の始まりの時刻を集める
  const longStarts = new Set();
  const stack1 = [];
  for (const event of best.profile.events) {
    if (event.type === 'O') stack1.push({ frame: event.frame, at: event.at });
    else {
      const open = stack1.pop();
      if (open && outer.test(frames[open.frame]) && !stack1.some((entry) => outer.test(frames[entry.frame])) && event.at - open.at >= threshold) {
        longStarts.add(open.at);
      }
    }
  }
  // 2回目：長い仕事の中にいる間だけ数える
  const inclusive = new Map();
  const stack = [];
  let inLong = 0;
  let spanCount = 0;
  for (const event of best.profile.events) {
    if (event.type === 'O') {
      const isOuter = outer.test(frames[event.frame]) && !stack.some((entry) => outer.test(frames[entry.frame]));
      const inWindow = !process.env.WINDOW || (event.at >= Number(process.env.WINDOW.split("-")[0]) && event.at <= Number(process.env.WINDOW.split("-")[1]));
      if (isOuter && longStarts.has(event.at) && inWindow) { inLong++; spanCount++; }
      stack.push({ frame: event.frame, at: event.at, isOuterLong: isOuter && longStarts.has(event.at) && inWindow });
    } else {
      const open = stack.pop();
      if (!open) continue;
      if (inLong > 0 && !stack.some((entry) => entry.frame === open.frame)) {
        inclusive.set(open.frame, (inclusive.get(open.frame) ?? 0) + (event.at - open.at));
      }
      if (open.isOuterLong) inLong--;
    }
  }
  console.log(`${threshold}ms 以上の仕事 ${spanCount} 回の中身`);
  const rows = [...inclusive].map(([frame, value]) => ({ name: frames[frame], value }))
    .filter((row) => !filter || filter.test(row.name))
    .sort((a, b) => b.value - a.value).slice(0, top);
  for (const row of rows) console.log(`${String(Math.round(row.value)).padStart(7)}  ${row.name.slice(0, 170)}`);
  process.exit(0);
}

const unit = best.profile.unit;
const fmt = (value) => `${Math.round(value)}`.padStart(7);
console.log(`スレッド: ${best.profile.name}（単位 ${unit}）`);
const rows = [...best.result.inclusive]
  .map(([frame, value]) => ({ name: frames[frame], inclusive: value, self: best.result.self.get(frame) ?? 0 }))
  .filter((row) => !filter || filter.test(row.name))
  .sort((a, b) => b.inclusive - a.inclusive)
  .slice(0, top);
console.log('含む時間  自分の時間  関数');
for (const row of rows) console.log(`${fmt(row.inclusive)} ${fmt(row.self)}  ${row.name.slice(0, 160)}`);
