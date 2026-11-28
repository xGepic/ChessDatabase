/** Names of Players, Events and Tags are the same when they differ only in letter case. */
export function sameName(a: string | null | undefined, b: string | null | undefined): boolean {
  return (a ?? '').trim().toLocaleLowerCase() === (b ?? '').trim().toLocaleLowerCase();
}
