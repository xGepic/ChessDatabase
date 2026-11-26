/** Display helpers for Engine scores. Scores are from White's side. */

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

/** White's winning chance in percent (Lichess formula; mate counts as ±1000 cp). */
export function whitePercent(s: Score): number {
  const cp = s.mate !== null ? (s.mate >= 0 ? 1000 : -1000) : Math.max(-1000, Math.min(1000, s.cp ?? 0));
  return 50 + 50 * (2 / (1 + Math.exp(-0.00368208 * cp)) - 1);
}

/** SAN moves with move numbers, as the scoresheet writes them, from the position in the FEN: "12...e5 13.Nf3". */
export function numberedLine(fen: string, san: string[]): string {
  const [, side, , , , full] = fen.split(' ');
  let number = Number(full) || 1;
  let white = side !== 'b';
  const parts: string[] = [];
  san.forEach((move, i) => {
    if (white) parts.push(`${number}.${move}`);
    else parts.push(i === 0 ? `${number}...${move}` : move);
    if (!white) number++;
    white = !white;
  });
  return parts.join(' ');
}
