import { Game, MoveNode } from '../api/api';

/**
 * Turns a Game's move tree into what the scoresheet shows: main-line rows with White and Black
 * columns, and Variation blocks under the move they branch from. Inside a block, a Variation reads
 * as one line of text, with deeper Variations in parentheses.
 */

export type Token =
  | { kind: 'move'; node: MoveNode; label: string }
  | { kind: 'open' }
  | { kind: 'close' };

export interface Row {
  kind: 'row';
  key: string;
  number: number;
  white: MoveNode | null;
  black: MoveNode | null;
}

export interface VariationBlock {
  kind: 'variations';
  key: string;
  lines: Token[][];
}

export type SheetItem = Row | VariationBlock;

export function moveNumber(node: MoveNode): number {
  return Math.ceil(node.ply / 2);
}

export function isWhiteMove(node: MoveNode): boolean {
  return node.ply % 2 === 1;
}

/** "12.e4", "12...e5" or "e5" depending on whether the number must be shown. */
export function moveLabel(node: MoveNode, withNumber: boolean): string {
  if (isWhiteMove(node)) return `${moveNumber(node)}.${node.san}`;
  return withNumber ? `${moveNumber(node)}...${node.san}` : node.san;
}

export function buildSheet(game: Game): SheetItem[] {
  const byId = new Map(game.nodes.map((n) => [n.id, n]));
  const children = (ids: number[]) => ids.map((id) => byId.get(id)!);
  const items: SheetItem[] = [];

  let row: Row | null = null;
  let siblings = children(game.firstMoves);
  while (siblings.length > 0) {
    const [main, ...alternatives] = siblings;
    // White moves always open a new row; Black moves open one only after a break (shown as "…").
    if (isWhiteMove(main) || !row) {
      row = { kind: 'row', key: `r${main.id}`, number: moveNumber(main), white: null, black: null };
      items.push(row);
    }
    if (isWhiteMove(main)) row.white = main;
    else row.black = main;

    if (alternatives.length > 0) {
      items.push({
        kind: 'variations',
        key: `v${main.id}`,
        lines: alternatives.map((alt) => lineTokens(alt, byId)),
      });
      // After a break, Black's reply starts a fresh row showing "…" for White.
      row = null;
    }
    siblings = children(main.children);
  }
  return items;
}

/** A Variation from its first move, with nested Variations in parentheses. */
export function lineTokens(start: MoveNode, byId: Map<number, MoveNode>): Token[] {
  const tokens: Token[] = [{ kind: 'move', node: start, label: moveLabel(start, true) }];
  let current = start;
  let needNumber = false;
  while (current.children.length > 0) {
    const [mainId, ...altIds] = current.children;
    const main = byId.get(mainId)!;
    tokens.push({ kind: 'move', node: main, label: moveLabel(main, needNumber) });
    needNumber = false;
    for (const altId of altIds) {
      tokens.push({ kind: 'open' }, ...lineTokens(byId.get(altId)!, byId), { kind: 'close' });
      needNumber = true;
    }
    current = main;
  }
  return tokens;
}

/** The main line, following first continuations from the start. */
export function mainLine(game: Game): MoveNode[] {
  const byId = new Map(game.nodes.map((n) => [n.id, n]));
  const line: MoveNode[] = [];
  let node = byId.get(game.firstMoves[0]);
  while (node) {
    line.push(node);
    node = byId.get(node.children[0]);
  }
  return line;
}

/** True when every move from the start to this node is a first continuation. */
export function isOnMainLine(game: Game, node: MoveNode): boolean {
  const byId = new Map(game.nodes.map((n) => [n.id, n]));
  let current: MoveNode | undefined = node;
  while (current) {
    const siblings: number[] = current.parentId === null ? game.firstMoves : byId.get(current.parentId)!.children;
    if (siblings[0] !== current.id) return false;
    current = current.parentId === null ? undefined : byId.get(current.parentId);
  }
  return true;
}
