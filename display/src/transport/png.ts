/**
 * PNG in scala di grigi a 8 bit, senza compressione (blocchi zlib "stored"): basta per il QR finto
 * della cassa, che deve arrivare come il vero, un PNG in Base64. Niente DOM, quindi gira anche nei test.
 */
export function grayPngDataUri(
  width: number,
  height: number,
  pixel: (x: number, y: number) => number,
): string {
  const stride = width + 1;
  const raw = new Uint8Array(stride * height);
  for (let y = 0; y < height; y++) {
    raw[y * stride] = 0; // filtro "none"
    for (let x = 0; x < width; x++) raw[y * stride + 1 + x] = pixel(x, y);
  }

  const header = new Uint8Array(13);
  const view = new DataView(header.buffer);
  view.setUint32(0, width);
  view.setUint32(4, height);
  header[8] = 8; // bit per pixel; colore 0 = grigi; compressione, filtro e interlacciamento 0

  const png = concat([
    Uint8Array.of(0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a),
    chunk('IHDR', header),
    chunk('IDAT', zlibStored(raw)),
    chunk('IEND', new Uint8Array(0)),
  ]);
  let binary = '';
  for (const byte of png) binary += String.fromCharCode(byte);
  return `data:image/png;base64,${btoa(binary)}`;
}

function zlibStored(data: Uint8Array): Uint8Array {
  const parts: Uint8Array[] = [Uint8Array.of(0x78, 0x01)];
  for (let offset = 0; offset < data.length; offset += 0xffff) {
    const block = data.subarray(offset, offset + 0xffff);
    const last = offset + 0xffff >= data.length ? 1 : 0;
    const size = block.length;
    parts.push(
      Uint8Array.of(last, size & 0xff, size >> 8, ~size & 0xff, (~size >> 8) & 0xff),
      block,
    );
  }
  parts.push(uint32(adler32(data)));
  return concat(parts);
}

function chunk(type: string, data: Uint8Array): Uint8Array {
  const typed = concat([Uint8Array.from(type, (c) => c.charCodeAt(0)), data]);
  return concat([uint32(data.length), typed, uint32(crc32(typed))]);
}

function uint32(value: number): Uint8Array {
  const bytes = new Uint8Array(4);
  new DataView(bytes.buffer).setUint32(0, value >>> 0);
  return bytes;
}

function concat(parts: Uint8Array[]): Uint8Array {
  const out = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
  let offset = 0;
  for (const part of parts) {
    out.set(part, offset);
    offset += part.length;
  }
  return out;
}

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});

function crc32(data: Uint8Array): number {
  let crc = 0xffffffff;
  for (const byte of data) crc = CRC_TABLE[(crc ^ byte) & 0xff]! ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function adler32(data: Uint8Array): number {
  let a = 1;
  let b = 0;
  for (const byte of data) {
    a = (a + byte) % 65521;
    b = (b + a) % 65521;
  }
  return ((b << 16) | a) >>> 0;
}
