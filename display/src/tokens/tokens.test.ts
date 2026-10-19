import { describe, expect, it } from 'vitest';
import css from './tokens.css?raw';
import type { FormatKey } from '../types/protocol';

const FORMATS: FormatKey[] = ['1920x1080', '1366x768', '1280x800', '1024x768', '1080x1920'];

describe('tokens.css', () => {
  it.each(FORMATS)('ha la tabella tipografica per %s', (format) => {
    expect(css).toContain(`:root[data-format='${format}']`);
  });

  it('niente vw/vh: la tipografia viene dalla tabella per formato (CLAUDE.md)', () => {
    expect(css).not.toMatch(/\d(vw|vh)\b/);
  });

  it('i keyframes animano solo transform e opacity', () => {
    const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, '');
    const keyframes = withoutComments.match(/@keyframes[\s\S]*?\n\}/g) ?? [];
    expect(keyframes.length).toBeGreaterThan(0);
    for (const block of keyframes) {
      const properties = [...block.matchAll(/([a-z-]+)\s*:/g)].map((m) => m[1]);
      for (const property of properties) expect(['transform', 'opacity']).toContain(property);
    }
  });
});
