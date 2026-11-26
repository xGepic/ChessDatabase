/** Display helpers for Engine scores and lines. Scores are from White's side. */

export interface Score {
  cp: number | null;
  mate: number | null;
}

/** "+0.35", "−1.20", "#3" (White mates), "#−3" (Black mates). */
export function scoreLabel(s: Score): string {
  if (s.mate !== null) return s.mate >= 0 ? `#${s.mate}` : `#−${-s.mate}`;
  const pawns = (s.cp ?? 0) / 100;
  if (pawns === 0) return '0.00';
  return (pawns > 0 ? '+' : '−') + Math.abs(pawns).toFixed(2);
}

/** The ply (1 = White's first move) of the next move in the position described by a FEN. */
export function nextPly(fen: string): number {
  const [, side, , , , full] = fen.split(' ');
  return ((Number(full) || 1) - 1) * 2 + (side === 'b' ? 2 : 1);
}

/** Moves numbered as the scoresheet writes them, the first at the given ply: "12...e5 13.Nf3 Nc6". */
export function numberedLine(firstPly: number, san: string[]): string {
  return san
    .map((move, i) => {
      const ply = firstPly + i;
      const number = Math.ceil(ply / 2);
      if (ply % 2 === 1) return `${number}.${move}`;
      return i === 0 ? `${number}...${move}` : move;
    })
    .join(' ');
}

/** Alternative moves at one ply, numbered: "2.Nf3 or 2.Nc3". */
export function numberedChoices(ply: number, san: string[]): string {
  return san.map((move) => numberedLine(ply, [move])).join(' or ');
}
