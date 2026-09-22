// 按功能生成图标：icon.png + icon.ico（纯 node stdlib：zlib + 手写 PNG/ICO 编码）
// 图标 = 本软件在做的事：深色圆角底 + 电源符号（断口朝上的环 + 中柱），琥珀色中柱呼应界面里的橙色「取消」。
import { readFileSync, writeFileSync } from 'node:fs';
import { deflateSync, inflateSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import assert from 'node:assert/strict';

const N = 256, R = 48, C = N / 2;
const R1 = 74, R2 = 54, GAP = 0.66;                  // 环外/内半径；GAP = 断口半角（弧度，从正上往两边）
const BAR_HALF = 15, BAR_TOP = C - R1 - 16, BAR_BOT = C - 6;
const BG = [30, 41, 59], RING = [241, 245, 249], BAR = [255, 171, 64];

const inBadge = (x, y) => {                          // 满幅圆角方形：只削四角
  if (x >= R && x <= N - R || y >= R && y <= N - R) return true;
  return Math.hypot(x - (x < R ? R : N - R), y - (y < R ? R : N - R)) <= R;
};

function px(x, y) {
  if (!inBadge(x, y)) return [0, 0, 0, 0];
  const dx = x - C + .5, dy = y - C + .5;
  const d = Math.hypot(dx, dy);
  let top = Math.atan2(dy, dx) + Math.PI / 2;        // 距正上方向的偏角
  if (top > Math.PI) top -= 2 * Math.PI;
  if (top < -Math.PI) top += 2 * Math.PI;
  if (Math.abs(dx) <= BAR_HALF && y >= BAR_TOP && y <= BAR_BOT) return [...BAR, 255];      // 中柱压在断口上
  if (d >= R2 && d <= R1 && Math.abs(top) > GAP) return [...RING, 255];                    // 断口朝上的环
  return [...BG, 255];
}

const raw = Buffer.alloc(N * (N * 4 + 1));
for (let y = 0; y < N; y++) {
  raw[y * (N * 4 + 1)] = 0;
  for (let x = 0; x < N; x++) Buffer.from(px(x, y)).copy(raw, y * (N * 4 + 1) + 1 + x * 4);
}
const T = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; return c >>> 0 });
const crc = b => { let c = ~0; for (const x of b) c = T[(c ^ x) & 255] ^ (c >>> 8); return ~c >>> 0 };
const chunk = (type, data) => {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type), data]);
  const sum = Buffer.alloc(4); sum.writeUInt32BE(crc(body));
  return Buffer.concat([len, body, sum]);
};
const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(N, 0); ihdr.writeUInt32BE(N, 4); ihdr[8] = 8; ihdr[9] = 6;
const png = Buffer.concat([Buffer.from('\x89PNG\r\n\x1a\n', 'latin1'), chunk('IHDR', ihdr),
  chunk('IDAT', deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
const ico = Buffer.alloc(22);                        // 单条 256px PNG 内嵌（Vista+ 标准 ICO）
ico.writeUInt16LE(1, 2); ico.writeUInt16LE(1, 4);
ico[6] = 0; ico[7] = 0; ico[10] = 1; ico[12] = 32;
ico.writeUInt32LE(png.length, 14); ico.writeUInt32LE(22, 18);

const dir = join(dirname(fileURLToPath(import.meta.url)));
writeFileSync(join(dir, 'icon.png'), png);
writeFileSync(join(dir, 'icon.ico'), Buffer.concat([ico, png]));

// 自检 1：回读 .ico 内嵌的 PNG，解压后逐像素比对（手写编码器错了这里就炸）
const buf = readFileSync(join(dir, 'icon.ico')).subarray(22), idat = [];
for (let p = 8; p + 8 <= buf.length;) {
  const len = buf.readUInt32BE(p), type = buf.toString('ascii', p + 4, p + 8);
  if (type === 'IDAT') idat.push(buf.subarray(p + 8, p + 8 + len));
  p += 12 + len;
}
assert.ok(Buffer.compare(inflateSync(Buffer.concat(idat)), raw) === 0, 'PNG 回读与像素源不一致');
// 自检 2：图标语义 == 电源符号（环、断口、中柱、圆角外透明）
const rgb = c => c.slice(0, 3).join();
let filled = 0;
for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) if (px(x, y)[3]) filled++;
assert.ok(filled > 0.9 * N * N, '圆角方形应占满大部分画布');
assert.equal(px(2, 2)[3], 0, '圆角外必须透明');
assert.equal(rgb(px(C - (R1 + R2) / 2, C)), rgb(RING), '环最左应是白色环带');
assert.equal(rgb(px(103, 69)), rgb(BG), '断口区（左上）应透出底色');
assert.equal(rgb(px(153, 69)), rgb(BG), '断口区（右上）应透出底色');
assert.equal(rgb(px(C, C - 70)), rgb(BAR), '中柱上段应是琥珀色');
assert.equal(rgb(px(C, C + 40)), rgb(BG), '环中心应是底色');
console.log('icon.png %d / icon.ico %d bytes / PASS 7/7', png.length, png.length + 22);
