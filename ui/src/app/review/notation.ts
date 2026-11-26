import { Game, MoveNode } from '../api/api';
import { nagSuffix } from '../shared/nags';

/**
 * Turns a Game's move tree into what the scoresheet shows: main-line rows with White and Black
 * columns, comments and Variation blocks under the move they belong to. Inside a block, a
 * Variation reads as one line of text, with deeper Variations in parentheses.
 */

export type Token =
  | { kind: 'move'; node: MoveNode; label: string }
  | { kind: 'comment'; text: string }
  | { kind: 'open' }
  | { kind: 'close' };

export interface Row {
  kind: 'row';
  key: string;
  number: number;
  white: MoveNode | null;
  black: MoveNode | null;
}

export interface CommentItem {
  kind: 'comment';
  key: string;
  text: string;
}

export interface VariationBlock {
  kind: 'variations';
  key: string;
  lines: Token[][];
}

export type SheetItem = Row | CommentItem | VariationBlock;

export function moveNumber(node: MoveNode): number {
  return Math.ceil(node.ply / 2);
}

export function isWhiteMove(node: MoveNode): boolean {
  return node.ply % 2 === 1;
}

/** SAN with its symbols: "Qh5?! ⩲". */
export function annotatedSan(node: MoveNode): string {
  const { attached, trailing } = nagSuffix(node.nags);
  return trailing ? `${node.san}${attached} ${trailing}` : `${node.san}${attached}`;
}

/** "12.e4", "12...e5" or "e5" depending on whether the number must be shown. */
export function moveLabel(node: MoveNode, withNumber: boolean): string {
  const san = annotatedSan(node);
  if (isWhiteMove(node)) return `${moveNumber(node)}.${san}`;
  return withNumber ? `${moveNumber(node)}...${san}` : san;
}

export function buildSheet(game: Game): SheetItem[] {
  const byId = new Map(game.nodes.map((n) => [n.id, n]));
  const children = (ids: number[]) => ids.map((id) => byId.get(id)!);
  const items: SheetItem[] = [];

  let row: Row | null = null;
  let siblings = children(game.firstMoves);
  while (siblings.length > 0) {
    const [main, ...alternatives] = siblings;

    if (main.commentBefore) {
      items.push({ kind: 'comment', key: `cb${main.id}`, text: main.commentBefore });
      row = null;
    }

    // White moves always open a new row; Black moves open one only after a break (shown as "…").
    if (isWhiteMove(main) || !row) {
      row = { kind: 'row', key: `r${main.id}`, number: moveNumber(main), white: null, black: null };
      items.push(row);
    }
    if (isWhiteMove(main)) row.white = main;
    else row.black = main;

    if (main.commentAfter) {
      items.push({ kind: 'comment', key: `ca${main.id}`, text: main.commentAfter });
      row = null;
    }
    if (alternatives.length > 0) {
      items.push({
        kind: 'variations',
        key: `v${main.id}`,
        lines: alternatives.map((alt) => lineTokens(alt, byId)),
      });
      row = null;
    }
    siblings = children(main.children);
  }
  return items;
}

/** A Variation from its first move, with comments inline and nested Variations in parentheses. */
export function lineTokens(start: MoveNode, byId: Map<number, MoveNode>): Token[] {
  const tokens: Token[] = [];
  let needNumber = true;
  const pushMove = (node: MoveNode) => {
    if (node.commentBefore) {
      tokens.push({ kind: 'comment', text: node.commentBefore });
      needNumber = true;
    }
    tokens.push({ kind: 'move', node, label: moveLabel(node, needNumber) });
    needNumber = false;
    if (node.commentAfter) {
      tokens.push({ kind: 'comment', text: node.commentAfter });
      needNumber = true;
    }
  };

  pushMove(start);
  let current = start;
  while (current.children.length > 0) {
    const [mainId, ...altIds] = current.children;
    const main = byId.get(mainId)!;
    pushMove(main);
    for (const altId of altIds) {
      tokens.push({ kind: 'open' }, ...lineTokens(byId.get(altId)!, byId), { kind: 'close' });
      needNumber = true;
    }
    current = main;
  }
  return tokens;
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
