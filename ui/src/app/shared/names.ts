/** Names of Players, Events, Tags and Aliases are the same when they differ only in letter case. */
export function sameName(a: string | null | undefined, b: string | null | undefined): boolean {
  return (a ?? '').trim().toLocaleLowerCase() === (b ?? '').trim().toLocaleLowerCase();
}
