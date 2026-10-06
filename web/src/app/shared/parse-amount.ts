/** Accepts "1.234,56", "1234.56", "45,9" and "€ 45.90". */
export function parseAmount(text: string): number | null {
  const value = parseDecimal(text);
  return value === null ? null : Math.round(value * 10000) / 10000;
}

/** Same formats as {@link parseAmount}, without rounding (crypto quantities need many decimals). */
export function parseDecimal(text: string): number | null {
  const cleaned = text.replace(/[€$£\s]/g, '');
  if (!cleaned) return null;
  const lastComma = cleaned.lastIndexOf(',');
  const lastDot = cleaned.lastIndexOf('.');
  const decimalSep = lastComma > lastDot ? ',' : '.';
  const thousandSep = decimalSep === ',' ? '.' : ',';
  const normalised = cleaned.split(thousandSep).join('').replace(decimalSep, '.');
  const value = Number(normalised);
  return Number.isFinite(value) ? Math.round(value * 1e10) / 1e10 : null;
}
