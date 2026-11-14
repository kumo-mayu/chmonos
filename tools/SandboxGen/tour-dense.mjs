// 通しで画面を見るための「多い」写しの一覧を、showcase.json から作る（2026-11-14）。
//
// なぜ要るか：showcase.json は 24 件で、BOOTH のページ用に見栄えの良い数にしてある。公開の前に全部の画面を見るときは、
// 実際に使う人の量（数百件・長い名前・対応アバターの多い衣装・ファイルの多い商品・溜まった通知）で、
// 一覧の伸び方・折り返し・はみ出しを見たい。名前は一般名詞と作り物の飾りだけ。画像は showcase と同じ CC0 の写真を使い回す
// （アバターは色相をずらして見分ける）。乱数は種を固定して、作り直しても同じ写しになるようにする。
//
// 使い方:
//   node tools/SandboxGen/tour-dense.mjs > <作業用>/tour-dense.json
//   SandboxGen <写し> init
//   SandboxGen <写し> showcase <作業用>/tour-dense.json <画像のフォルダ>
// 画像のフォルダは showcase と同じ（%LOCALAPPDATA%\Chmonos-fixtures\showcase-images）。
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const base = JSON.parse(readFileSync(join(here, 'showcase.json'), 'utf8'));

// 種を固定した乱数（mulberry32）。Math.random だと作り直すたびに画面が変わり、前後の画像を比べられない
let seed = 20261114;
function rand() {
  seed |= 0; seed = (seed + 0x6D2B79F5) | 0;
  let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}
const pick = (list) => list[Math.floor(rand() * list.length)];
const between = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));
const sample = (list, n) => [...list].sort(() => rand() - 0.5).slice(0, Math.min(n, list.length));

// ショップ：元の5つに 25 を足す。1つは長い名前（カード・一覧の欄で切れ方を見る）
const shops = [...base.shops];
const shopWords = ['工房', '堂', 'アトリエ', '雑貨店', '製作所', '屋', 'スタジオ', '商店', '舎', 'ラボ'];
const shopHeads = ['ひだまり', 'こもれび', 'あおぞら', 'ゆうぐれ', 'ほしぞら', 'しずく', 'わたぐも', 'つきあかり', 'さくら', 'もみじ',
  'こなゆき', 'なないろ', 'ぽかぽか', 'きらきら', 'ふわふわ', 'まどろみ', 'あさやけ', 'こはる', 'ひなた', 'そよかぜ', 'みずたま', 'しろくま', 'よつば', 'あまやどり'];
shopHeads.forEach((head, i) => shops.push({ key: `s${i}`, name: `サンプル${head}${shopWords[i % shopWords.length]}`, subdomain: `chmonos-sample-s${i}` }));
shops.push({ key: 'long', name: 'サンプル服飾工房 ～ゆめかわいい毎日のための小さなお店～ 公式ショップ', subdomain: 'chmonos-sample-long' });
const shopKeys = shops.map((shop) => shop.key);

// アバター：元の3体に 22 体を足す。絵は元の3体の写真の色相をずらす（同じ絵が並ぶと見分けにくい）
const animals = ['いぬ', 'くま', 'しか', 'たぬき', 'ひつじ', 'りす', 'ぺんぎん', 'ぱんだ', 'こあら', 'はむすたー', 'ふくろう',
  'いるか', 'うま', 'やぎ', 'かめ', 'ことり', 'もぐら', 'らいおん', 'とら', 'おおかみ', 'あざらし'];
const avatarSources = base.items.filter((item) => item.category === '3Dキャラクター');
const avatars = [...avatarSources];
animals.forEach((name, i) => {
  const source = avatarSources[i % avatarSources.length];
  avatars.push({
    ...source, name, shop: pick(shopKeys), price: between(30, 90) * 100, days: between(10, 900), favorite: rand() < 0.2,
    hue: 40 + (i * 47) % 300, file: `${name}_v${between(1, 3)}.${between(0, 9)}.zip`, unitypackage: `Avatar${i}_v1.unitypackage`,
    userTags: ['アバター'], attributes: { 'かわいい': between(40, 100) }, description: `VRChat 向けのオリジナル3Dモデル「${name}」です。\n\n■ 同梱物\nunitypackage・テクスチャ・PSD`,
  });
});
// 名前の長いアバター（対応アバターの札・アバターの画面の欄で、どこで切れるかを見る）
avatars.push({
  ...avatarSources[0], name: 'ながいなまえのアバター・スペシャルエディション（夏の思い出）', shop: 'long', price: 7200, days: 40, hue: 200,
  file: 'ながいなまえのアバター_v1.0.zip', unitypackage: 'LongAvatar_v1.unitypackage', userTags: ['アバター'], attributes: {}, description: '名前の長いアバターです。',
});
const avatarNames = avatars.map((avatar) => avatar.name);

// 衣装・小物・テクスチャなど：元の 21 件を飾りの語で増やす
const things = base.items.filter((item) => item.category !== '3Dキャラクター');
const modifiers = ['夏の', '冬の', 'レースの', 'チェックの', 'ゆったり', 'ぴったり', 'もこもこ', 'しっとり', 'きらきら', 'シンプルな', 'クラシック',
  'ガーリー', 'ボーイッシュ', 'パステル', 'モノトーン', 'ストライプ', 'ドット柄', '花柄', 'ふんわり', 'おでかけ'];
const tagTops = {
  '衣装': ['ワンピース', 'トップス', 'ボトムス', 'アウター', '和服', 'セット'],
  '季節': ['春', '夏', '秋', '冬'],
  '色': ['白', '黒', '赤', '青', '緑', 'ピンク', '紫'],
  '用途': ['写真撮影', 'イベント', '普段使い', 'ワールド巡り'],
  '小物': ['靴', '帽子', 'アクセサリ', 'かばん'],
  'あとで試す': [],
  'お気に入りの組み合わせ（冬のおでかけ・写真撮影用）': [],
};
const tagPool = Object.entries(tagTops).flatMap(([top, subs]) => (subs.length ? subs.map((sub) => `${top}/${sub}`) : [top]));
const attributeNames = ['かわいい', 'かっこいい', 'よく使う', '軽さ', '質感', '色味', '揺れもの', '改変のしやすさ', '説明の丁寧さ', '値段の満足'];
const derived = [];
const used = new Set([...avatarNames, ...things.map((item) => item.name)]);
let serial = 0;
for (let round = 0; round < 11; round++) {
  for (const source of things) {
    serial++;
    let name = `${pick(modifiers)}${source.name}`;
    // 7件に1件は長い名前（BOOTH でよくある、対応アバターと特徴を名前に詰めた形）
    if (serial % 7 === 0) {
      name = `【${between(3, 12)}アバター対応】${name}【PhysBone・軽量版・Quest対応】 ver${between(1, 3)}.${between(0, 9)}.${between(0, 9)}`;
    }
    while (used.has(name)) {
      name = `${name}${between(2, 9)}`;
    }
    used.add(name);
    const wears = source.category === '3D衣装' || source.category === '3D装飾品';
    // 対応アバターは 0〜8 体が多く、たまに多い物（16〜26 体）。対応の多い衣装で札がどう畳まれるかを見る
    const supportCount = !wears ? 0 : serial % 23 === 0 ? between(16, avatarNames.length) : between(0, 8);
    const tagCount = serial % 13 === 0 ? 9 : between(0, 4);
    const attributes = {};
    sample(attributeNames, serial % 11 === 0 ? attributeNames.length : between(0, 3)).forEach((attribute) => { attributes[attribute] = between(0, 100); });
    const folder = pick(['衣装', '小物', 'テクスチャ', 'ギミック', '未整理/2025', '未整理/2026', 'BOOTH/衣装/まとめ買い']);
    const file = rand() < 0.85 ? `${name.replace(/[【】\s・.]/g, '').slice(0, 24)}_${serial}.zip` : undefined;
    const item = {
      name, image: source.image, images: source.images ?? [], shop: pick(shopKeys), category: source.category,
      price: rand() < 0.12 ? 0 : between(3, 60) * 100, days: between(1, 900), favorite: rand() < 0.12,
      folder, file, unitypackage: file && wears ? `Item${serial}_v1.unitypackage` : undefined,
      supports: sample(avatarNames, supportCount), userTags: sample(tagPool, tagCount), attributes,
      description: `${source.description ?? ''}\n\n■ 内容\n${name} の作り物です。\n\n■ 更新履歴\nver1.0 公開`,
    };
    // 13件に1件はファイルを何本も持つ（版違い・色違いの zip。商品ページのファイルの欄の伸び方を見る）
    if (file && serial % 13 === 5) {
      item.extraFiles = Array.from({ length: 6 }, (_, n) => ({ file: `${file.replace('.zip', '')}_色違い${n + 1}.zip`, unitypackage: `Item${serial}_color${n + 1}.unitypackage` }));
    }
    derived.push(item);
  }
}

// 説明の長い商品（見出しの多い説明・商品ページを流した下の方を見る）
const longDescription = Array.from({ length: 12 }, (_, n) =>
  `■ 項目${n + 1}\n${'この商品の説明の作り物の文です。'.repeat(between(2, 6))}\n・注意書き${n + 1}-1\n・注意書き${n + 1}-2`).join('\n\n');
derived[3].description = longDescription;

const items = [...avatars, ...things.map((item) => ({ ...item })), ...derived];

// 未確定（商品を特定できなかったファイル）：40 本
const unresolved = [...base.unresolved];
for (let i = 0; i < 38; i++) {
  const source = pick(things);
  unresolved.push({
    folder: pick(['未整理', '未整理/2025', 'ダウンロード', 'BOOTH/まとめ']),
    file: i % 9 === 0 ? `とても長いファイル名の作り物_${pick(modifiers)}${source.name}_全アバター対応版_最終_${i}.zip` : `${pick(modifiers)}${source.name}_${i}.zip`,
    unitypackage: `Unknown${i}.unitypackage`, image: source.image, shop: pick(shopKeys),
  });
}

// 改変：30 件。使ったものは 2〜12 件
const clothes = [...things, ...derived].filter((item) => item.file && (item.category === '3D衣装' || item.category === '3D装飾品'));
const modifications = [...base.modifications];
const photos = [...new Set(things.map((item) => item.image))];
for (let i = 0; i < 27; i++) {
  modifications.push({
    avatar: pick(avatarNames), name: i % 8 === 0 ? `とても長い名前の改変（夏祭りのイベント用・浴衣と小物のセット）${i}` : `${pick(modifiers)}コーデ ${i + 1}`,
    members: sample(clothes, between(2, 12)).map((item) => item.name), photo: pick(photos),
  });
}

// 通知：150 件（価格・説明文・バリエーション・販売終了）
const notifications = [...base.notifications];
const priced = items.filter((item) => item.price > 0);
for (let i = 0; i < 147; i++) {
  const item = pick(priced);
  const roll = rand();
  const diffs = roll < 0.4 ? [['価格', `¥ ${item.price.toLocaleString('en-US')}`, `¥ ${(item.price - 100).toLocaleString('en-US')}`]]
    : roll < 0.7 ? [['説明文', null, null]]
      : roll < 0.9 ? [['説明文', null, null], ['バリエーション', `${between(2, 5)}色`, `${between(6, 9)}色`]]
        : [['販売状態', '販売中', '販売終了']];
  notifications.push({ item: item.name, diffs });
}

process.stdout.write(JSON.stringify({
  note: 'tools/SandboxGen/tour-dense.mjs が showcase.json から作った「多い」写しの一覧（作り直せば同じ物になる）',
  imageSize: 512,
  shops, items, unresolved, modifications, notifications,
}, null, 1));
