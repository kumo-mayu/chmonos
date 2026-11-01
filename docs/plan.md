# 残りの作業

## 次にやること

1. psd・tga を実際の Unity で選べるかを確かめる（png・jpg は確かめた。実物の psd・tga を用意する必要がある）
2. `SixLabors.ImageSharp` は 3.1.12 のまま（ユーザ判断 2026-11-01）。知らせ6件（GHSA-gwg2-r3hj-4w44・wmxv-xphr-5c9g・j3p4-wp97-rph4・j9gm-c75j-xc9q・jjfr-hcj7-qf5w・v76p-62qx-wwq2）は、TIFF・ICC・ヒストグラム平坦化のどれかで、今の作りでは通らない。3.2.0・4.x は Release で組むのにライセンスキーが要る（3.2.0 で実際に止まった）。新しい知らせが JPEG・PNG・GIF・WebP・BMP の読み取りに当たるなら見直す
