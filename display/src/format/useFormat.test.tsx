// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { render, resizeTo } from '../test/render';
import { useFormat } from './useFormat';

function Probe() {
  const { layout, format } = useFormat();
  return <output>{`${layout} ${format}`}</output>;
}

describe('useFormat', () => {
  let cleanup: (() => void) | undefined;
  afterEach(() => cleanup?.());

  it('narrow per 1024×768, portrait per 1080×1920, e segue il resize', () => {
    resizeTo(1024, 768);
    const view = render(<Probe />);
    cleanup = view.unmount;
    expect(view.container.textContent).toBe('narrow 1024x768');

    resizeTo(1080, 1920);
    expect(view.container.textContent).toBe('portrait 1080x1920');

    resizeTo(1920, 1080);
    expect(view.container.textContent).toBe('wide 1920x1080');
  });
});
