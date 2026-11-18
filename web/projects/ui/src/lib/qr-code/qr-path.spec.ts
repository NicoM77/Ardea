import { encode } from 'uqr';
import { qrPath } from './qr-path';

describe('qrPath', () => {
  it('draws one rectangle per run of dark modules in a row', () => {
    const path = qrPath([
      [true, true, false, true],
      [false, false, false, false],
      [false, true, true, true],
    ]);
    expect(path).toBe('M0 0h2v1h-2zM3 0h1v1h-1zM1 2h3v1h-3z');
  });

  it('draws nothing for an all-light matrix', () => {
    expect(
      qrPath([
        [false, false],
        [false, false],
      ]),
    ).toBe('');
  });

  it('covers exactly the dark modules of a real code', () => {
    const { data } = encode('otpauth://totp/Ardea:sample%40example.org?secret=GEZDGNBVGY3TQOJQ', {
      border: 4,
    });
    const dark = data.flat().filter(Boolean).length;
    const covered = [...qrPath(data).matchAll(/h(\d+)v1/g)].reduce(
      (sum, m) => sum + Number(m[1]),
      0,
    );
    expect(covered).toBe(dark);
  });
});
