/**
 * Turns a QR matrix (`true` = dark module) into one SVG path. Each run of dark modules in a row
 * becomes one rectangle, so a typical otpauth code is a few hundred commands, not thousands.
 */
export function qrPath(matrix: readonly (readonly boolean[])[]): string {
  const parts: string[] = [];
  matrix.forEach((row, y) => {
    let x = 0;
    while (x < row.length) {
      if (!row[x]) {
        x++;
        continue;
      }
      const start = x;
      while (x < row.length && row[x]) x++;
      parts.push(`M${start} ${y}h${x - start}v1h${start - x}z`);
    }
  });
  return parts.join('');
}
