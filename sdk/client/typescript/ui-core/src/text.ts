// Text and number helpers that reproduce the .NET behavior the portable language relies on
// (char.IsWhiteSpace, invariant casing, double "R" formatting, decimal "G" formatting).

/** `char.IsWhiteSpace` of .NET: Unicode separators (Zs, Zl, Zp) plus \t \n \v \f \r, U+0085 and U+00A0. */
const WHITE_SPACE = /^[\t\n\v\f\r\x85\xA0\p{Zs}\p{Zl}\p{Zp}]$/u;

export function isWhiteSpace(c: string): boolean {
  return c.length === 1 && WHITE_SPACE.test(c);
}

/** `string.IsNullOrWhiteSpace` of .NET. */
export function isNullOrWhiteSpace(text: string | null | undefined): boolean {
  if (text === null || text === undefined) {
    return true;
  }

  for (let i = 0; i < text.length; i++) {
    if (!isWhiteSpace(text[i]!)) {
      return false;
    }
  }

  return true;
}

/** `string.Trim()` of .NET (trims `char.IsWhiteSpace` characters; JS `trim()` differs for U+0085 and U+FEFF). */
export function trimWhiteSpace(text: string): string {
  let start = 0;
  let end = text.length;
  while (start < end && isWhiteSpace(text[start]!)) {
    start++;
  }

  while (end > start && isWhiteSpace(text[end - 1]!)) {
    end--;
  }

  return text.slice(start, end);
}

/**
 * Invariant (simple, one-to-one) case mapping like .NET `ToUpperInvariant` / `ToLowerInvariant`: characters whose full
 * Unicode mapping has a different length (ß → SS, İ → i̇) are kept, and the Turkish dotted/dotless i are not mapped
 * (.NET maps U+0131 and U+0130 to themselves in the invariant culture).
 */
export function changeCaseInvariant(text: string, upper: boolean): string {
  let result = '';
  for (const c of text) {
    if (c === 'ı' || c === 'İ') {
      result += c;
      continue;
    }

    const mapped = upper ? c.toUpperCase() : c.toLowerCase();
    result += mapped.length === c.length && [...mapped].length === 1 ? mapped : c;
  }

  return result;
}

/** Shortest round-trip digits of a finite, non-zero |value| and its scale (value = 0.d1d2... × 10^scale). */
function shortestDigits(value: number): { digits: string; scale: number } {
  const [mantissa, exponent] = Math.abs(value).toExponential().split('e') as [string, string];
  return { digits: mantissa.replace('.', ''), scale: Number(exponent) + 1 };
}

function positional(digits: string, scale: number, decimalSeparator: string): string {
  if (scale <= 0) {
    return `0${decimalSeparator}${'0'.repeat(-scale)}${digits}`;
  }

  if (scale >= digits.length) {
    return digits + '0'.repeat(scale - digits.length);
  }

  return `${digits.slice(0, scale)}${decimalSeparator}${digits.slice(scale)}`;
}

/**
 * Text of a number in the portable expression language: ECMAScript `Number.prototype.toString` (shortest round-trip
 * digits; scientific notation below 1e-6 and from 1e21, e.g. `1e+21`, `1e-7`); the .NET evaluator implements the same
 * rule. NaN and infinities are empty; -0 is `0`.
 */
export function formatNumberInvariant(value: number): string {
  return Number.isFinite(value) ? String(value) : '';
}

/** Largest .NET decimal; larger numbers are not converted to decimal by the C# formatter. */
export const DECIMAL_MAX = 79228162514264337593543950335;

/**
 * Text of a number like `decimal.ToString(culture)` of .NET (general format of the shortest round-trip value: no
 * grouping, no exponent, the culture's decimal separator).
 */
export function formatDecimalGeneral(value: number, decimalSeparator: string): string {
  if (!Number.isFinite(value)) {
    return '';
  }

  if (value === 0) {
    return '0';
  }

  const { digits, scale } = shortestDigits(value);
  return (value < 0 ? '-' : '') + positional(digits, scale, decimalSeparator);
}

/** Shortest round-trip text of a number without exponent (exact decimal input for `Intl.NumberFormat`). */
export function decimalString(value: number): string {
  return formatDecimalGeneral(value, '.');
}

const separators = new Map<string, string>();

/** Decimal separator of a locale (`,` for tr-TR). */
export function decimalSeparator(locale: string): string {
  let separator = separators.get(locale);
  if (separator === undefined) {
    separator = new Intl.NumberFormat(locale).formatToParts(1.5).find(p => p.type === 'decimal')?.value ?? '.';
    separators.set(locale, separator);
  }

  return separator;
}
