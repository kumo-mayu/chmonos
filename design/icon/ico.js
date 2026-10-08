// 大きさごとの PNG を、1つの .ico にまとめる（各大きさを PNG のまま入れる。Windows Vista 以降はこれを読める）
// 使い方: node ico.js <png のフォルダ> <出す .ico>
const fs = require("fs");
const path = require("path");
const [dir, outFile] = process.argv.slice(2);
const sizes = [16, 20, 24, 32, 40, 48, 64, 256];
const images = sizes.map(s => ({ size: s, data: fs.readFileSync(path.join(dir, `icon-${s}.png`)) }));

const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0); // 予約
header.writeUInt16LE(1, 2); // 1 = アイコン
header.writeUInt16LE(images.length, 4);

const entries = [];
let offset = 6 + 16 * images.length;
for (const { size, data } of images) {
  const e = Buffer.alloc(16);
  e.writeUInt8(size >= 256 ? 0 : size, 0); // 幅（256 は 0 で表す）
  e.writeUInt8(size >= 256 ? 0 : size, 1); // 高さ
  e.writeUInt8(0, 2); // 色の数（使わない）
  e.writeUInt8(0, 3); // 予約
  e.writeUInt16LE(1, 4); // 面
  e.writeUInt16LE(32, 6); // 1画素のビット数
  e.writeUInt32LE(data.length, 8);
  e.writeUInt32LE(offset, 12);
  offset += data.length;
  entries.push(e);
}

fs.writeFileSync(outFile, Buffer.concat([header, ...entries, ...images.map(i => i.data)]));
console.log(`${outFile}: ${images.length} 枚・${fs.statSync(outFile).size} バイト`);
