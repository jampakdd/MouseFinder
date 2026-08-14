import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const svg = path.join(root, 'MouseFinder.svg');
const output = path.join(root, 'MouseFinder.ico');
const sizes = [16, 20, 24, 32, 40, 48, 64, 256];
const images = await Promise.all(sizes.map(size =>
  sharp(svg).resize(size, size).png().toBuffer()
));

const header = Buffer.alloc(6 + (16 * images.length));
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(images.length, 4);
let offset = header.length;
images.forEach((image, index) => {
  const entry = 6 + (index * 16);
  header.writeUInt8(sizes[index] === 256 ? 0 : sizes[index], entry);
  header.writeUInt8(sizes[index] === 256 ? 0 : sizes[index], entry + 1);
  header.writeUInt8(0, entry + 2);
  header.writeUInt8(0, entry + 3);
  header.writeUInt16LE(1, entry + 4);
  header.writeUInt16LE(32, entry + 6);
  header.writeUInt32LE(image.length, entry + 8);
  header.writeUInt32LE(offset, entry + 12);
  offset += image.length;
});

fs.writeFileSync(output, Buffer.concat([header, ...images]));
fs.writeFileSync(path.join(root, 'MouseFinder-preview.png'), images.at(-1));
console.log(`Generated ${output}`);
