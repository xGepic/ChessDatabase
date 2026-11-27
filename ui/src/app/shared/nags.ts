/** Numeric Annotation Glyphs as the Owner sees them. One move symbol and one position symbol per move. */
export interface NagSymbol {
  nag: number;
  glyph: string;
  name: string;
}

export const MOVE_SYMBOLS: NagSymbol[] = [
  { nag: 1, glyph: '!', name: 'good move' },
  { nag: 2, glyph: '?', name: 'mistake' },
  { nag: 3, glyph: '!!', name: 'brilliant move' },
  { nag: 4, glyph: '??', name: 'blunder' },
  { nag: 5, glyph: '!?', name: 'interesting move' },
  { nag: 6, glyph: '?!', name: 'dubious move' },
];

export const POSITION_SYMBOLS: NagSymbol[] = [
  { nag: 10, glyph: '=', name: 'equal position' },
  { nag: 13, glyph: '∞', name: 'unclear position' },
  { nag: 14, glyph: '⩲', name: 'White is slightly better' },
  { nag: 15, glyph: '⩱', name: 'Black is slightly better' },
  { nag: 16, glyph: '±', name: 'White is better' },
  { nag: 17, glyph: '∓', name: 'Black is better' },
  { nag: 18, glyph: '+-', name: 'White is winning' },
  { nag: 19, glyph: '-+', name: 'Black is winning' },
];

/** Marks a move for a board diagram in PDF export (NAG 220, as ChessBase writes it). */
export const DIAGRAM_NAG = 220;

/** Other common signs that arrive with imported PGN; shown, but not offered in the picker. Matches the PDF. */
const OTHER_GLYPHS: [number, string][] = [
  [22, '⨀'], [23, '⨀'], [32, '⟳'], [33, '⟳'], [36, '↑'], [37, '↑'], [40, '→'], [41, '→'], [44, '=∞'], [45, '=∞'],
  [132, '⇆'], [133, '⇆'], [138, '⊕'], [139, '⊕'], [140, '∆'], [146, 'N'], [DIAGRAM_NAG, '▦'],
];

const GLYPHS = new Map<number, string>([...[...MOVE_SYMBOLS, ...POSITION_SYMBOLS].map((s): [number, string] => [s.nag, s.glyph]), ...OTHER_GLYPHS]);
const MOVE_NAGS = new Set(MOVE_SYMBOLS.map((s) => s.nag));

/** Move symbols attach to the move ("Qh5?!"); everything else follows after a space ("Qh5?! ⩲"). */
export function nagSuffix(nags: number[]): { attached: string; trailing: string } {
  const attached = nags.filter((n) => MOVE_NAGS.has(n)).map((n) => GLYPHS.get(n)).join('');
  const trailing = nags
    .filter((n) => !MOVE_NAGS.has(n))
    .map((n) => GLYPHS.get(n) ?? `$${n}`)
    .join(' ');
  return { attached, trailing };
}

/** Toggles a symbol, keeping at most one from its group (move symbols or position symbols). */
export function toggleNag(nags: number[], nag: number): number[] {
  if (nags.includes(nag)) return nags.filter((n) => n !== nag);
  const group = MOVE_NAGS.has(nag) ? MOVE_SYMBOLS : POSITION_SYMBOLS.some((s) => s.nag === nag) ? POSITION_SYMBOLS : [];
  const sameGroup = new Set(group.map((s) => s.nag));
  return [...nags.filter((n) => !sameGroup.has(n)), nag].sort((a, b) => a - b);
}
