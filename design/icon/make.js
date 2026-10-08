// アイコンの案を SVG で作り、大きさ・背景ごとに並べた見本の HTML を書く。
// 大きい大きさ（48px 以上）用と、小さい大きさ（32px 以下）用を別に描く（小さい方は輪と放射の線を減らし、線を太くする）
const fs = require("fs");
const path = require("path");
const out = __dirname;

const ACCENT = "#3767a6";
const ACCENT_DARK = "#2a5289";
const WHITE = "#ffffff";
const f = n => Number(n.toFixed(2));
const rad = a => (a * Math.PI) / 180;

// 巣：中心から angles の向きへ放射の線。輪は隣の放射の線どうしを、中心へ少したわませた曲線でつなぐ
function web({ cx, cy, angles, radii, spoke, stroke, closed, sag = 0.12 }) {
  const pt = (a, r) => [cx + r * Math.cos(rad(a)), cy + r * Math.sin(rad(a))];
  let s = "";
  for (const a of angles) {
    const [x, y] = pt(a, spoke);
    s += `<line x1="${cx}" y1="${cy}" x2="${f(x)}" y2="${f(y)}"/>`;
  }
  for (const r of radii) {
    const seq = closed ? [...angles, angles[0]] : angles;
    let d = "";
    for (let i = 0; i < seq.length; i++) {
      const [x, y] = pt(seq[i], r);
      if (i === 0) { d += `M${f(x)} ${f(y)}`; continue; }
      const prev = seq[i - 1];
      const diff = (((seq[i] - prev) % 360) + 360) % 360;
      const mid = prev + diff / 2;
      const [qx, qy] = pt(mid, r * (Math.cos(rad(diff / 2)) - sag));
      d += ` Q${f(qx)} ${f(qy)} ${f(x)} ${f(y)}`;
    }
    s += `<path d="${d}"/>`;
  }
  return `<g fill="none" stroke="${WHITE}" stroke-width="${stroke}" stroke-linecap="round" stroke-linejoin="round">${s}</g>`;
}

const folderBody = `<rect x="16" y="68" width="224" height="160" rx="22"/>`;
const folderTab = `<path d="M16 64 Q16 44 36 44 L92 44 Q104 44 112 54 L122 68 L16 68 Z"/>`;
const eight = [0, 45, 90, 135, 180, 225, 270, 315];
const six = [30, 90, 150, 210, 270, 330];

const folder = webSvg => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <defs><clipPath id="body">${folderBody}</clipPath></defs>
  <g fill="${ACCENT_DARK}">${folderTab}</g>
  <g fill="${ACCENT}">${folderBody}</g>
  <g clip-path="url(#body)">${webSvg}</g>
</svg>`;
const tile = webSvg => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <defs><clipPath id="tile"><rect x="16" y="16" width="224" height="224" rx="52"/></clipPath></defs>
  <rect x="16" y="16" width="224" height="224" rx="52" fill="${ACCENT}"/>
  <g clip-path="url(#tile)">${webSvg}</g>
</svg>`;

// 小さい方の線の太さ 16（256 の中で）＝ 16px で 1px・32px で 2px
const designs = {
  A: {
    big: folder(web({ cx: 128, cy: 150, angles: eight, radii: [30, 60, 92], spoke: 140, stroke: 8, closed: true })),
    small: folder(web({ cx: 128, cy: 150, angles: six, radii: [62], spoke: 140, stroke: 16, closed: true, sag: 0.06 })),
  },
  B: {
    big: tile(web({ cx: 128, cy: 128, angles: eight, radii: [32, 64, 98], spoke: 160, stroke: 9, closed: true })),
    small: tile(web({ cx: 128, cy: 128, angles: six, radii: [70], spoke: 160, stroke: 18, closed: true, sag: 0.06 })),
  },
  C: {
    big: folder(web({ cx: 30, cy: 82, angles: [0, 22.5, 45, 67.5, 90], radii: [50, 100, 150], spoke: 260, stroke: 8, closed: false, sag: 0.1 })),
    small: folder(web({ cx: 30, cy: 82, angles: [0, 45, 90], radii: [70, 140], spoke: 260, stroke: 16, closed: false, sag: 0.06 })),
  },
};

for (const [name, d] of Object.entries(designs)) {
  fs.writeFileSync(path.join(out, `icon-${name}.svg`), d.big);
  fs.writeFileSync(path.join(out, `icon-${name}-small.svg`), d.small);
}

const sizes = [256, 64, 48, 32, 24, 16];
const cell = (name, s) => `<div class="cell"><img src="icon-${name}${s <= 32 ? "-small" : ""}.svg" width="${s}" height="${s}"><div class="size">${s}</div></div>`;
const block = (name, bg, fg) => `<div class="row" style="background:${bg};color:${fg}"><div class="label">${name}</div>${sizes.map(s => cell(name, s)).join("")}</div>`;
const html = `<!doctype html><meta charset="utf-8"><style>
  body{margin:0;font:13px "Yu Gothic UI",sans-serif;display:grid;grid-template-columns:1fr 1fr}
  .row{display:flex;align-items:flex-end;gap:16px;padding:12px 14px}
  .label{width:18px;font-weight:bold;font-size:18px;align-self:center}
  .cell{display:flex;flex-direction:column;align-items:center;gap:4px}
  .cell img[width="256"]{width:160px;height:160px}
  .size{font-size:11px;opacity:.7}
</style>
${Object.keys(designs).map(n => block(n, "#f3f4f6", "#222") + block(n, "#1e2227", "#ddd")).join("")}`;
fs.writeFileSync(path.join(out, "sheet.html"), html);

// 小さい大きさだけを、拡大して並べた見本（16・24・32 の潰れ方を見る。画素のまま拡大）
const zoom = `<!doctype html><meta charset="utf-8"><style>
  body{margin:0;font:13px "Yu Gothic UI",sans-serif}
  .row{display:flex;gap:24px;align-items:center;padding:10px 14px}
  .label{width:18px;font-weight:bold;font-size:18px}
  canvas{image-rendering:pixelated}
</style>
<div id="root"></div>
<script>
const names = ${JSON.stringify(Object.keys(designs))};
const sizes = [16, 24, 32];
const root = document.getElementById("root");
for (const [bg, fg] of [["#f3f4f6", "#222"], ["#1e2227", "#ddd"]]) {
  for (const name of names) {
    const row = document.createElement("div"); row.className = "row"; row.style.background = bg; row.style.color = fg;
    row.innerHTML = '<div class="label">' + name + '</div>';
    for (const s of sizes) {
      const img = new Image(); img.src = "icon-" + name + "-small.svg";
      const c = document.createElement("canvas"); c.width = s; c.height = s; c.style.width = (s * 4) + "px"; c.style.height = (s * 4) + "px";
      img.onload = () => c.getContext("2d").drawImage(img, 0, 0, s, s);
      row.appendChild(c);
    }
    root.appendChild(row);
  }
}
</script>`;
fs.writeFileSync(path.join(out, "zoom.html"), zoom);
console.log("書いた");
