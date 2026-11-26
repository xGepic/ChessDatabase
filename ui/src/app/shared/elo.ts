/** Reads an Elo field: empty or 0 means unrated (null); undefined means the text is not a number. */
export function parseElo(text: string | number | null): number | null | undefined {
  const trimmed = String(text ?? '').trim();
  if (trimmed === '' || trimmed === '0') return null;
  return /^\d{1,4}$/.test(trimmed) ? Number(trimmed) : undefined;
}
