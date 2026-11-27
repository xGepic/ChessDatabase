import { Observable, Subject, of, throwError } from 'rxjs';
import { MoveNode, MoveTreeView } from '../api/api';
import { INITIAL_FEN } from '../shared/chess';
import { TreePort, TreeSession } from './tree-session';

/** A tree from [id, parentId, san, uci] rows; children follow row order (first = main continuation). */
function tree(rows: [number, number | null, string, string][]): MoveTreeView {
  const nodes: MoveNode[] = rows.map(([id, parentId, san, uci]) => ({
    id, parentId, san, uci, children: rows.filter((r) => r[1] === id).map((r) => r[0]),
    ply: 0, fen: `fen-${id}`, commentBefore: null, commentAfter: null, nags: [],
  }));
  return { firstMoves: rows.filter((r) => r[1] === null).map((r) => r[0]), nodes };
}

/** 1.e4 e5 2.Nf3, with 1...c5 2.Nc3 as a Variation to 1...e5. */
const GAME = tree([
  [1, null, 'e4', 'e2e4'],
  [2, 1, 'e5', 'e7e5'],
  [3, 2, 'Nf3', 'g1f3'],
  [4, 1, 'c5', 'c7c5'],
  [5, 4, 'Nc3', 'b1c3'],
]);

/** Records calls and answers with whatever the test hands it. */
class FakePort implements TreePort<MoveTreeView> {
  readonly calls: string[] = [];
  answer: () => Observable<MoveTreeView> = () => of(GAME);
  lineAnswer: () => Observable<{ nodeId: number; tree: MoveTreeView }> = () => of({ nodeId: 1, tree: GAME });

  addLine(parentId: number | null, moves: string[]) {
    this.calls.push(`addLine ${parentId} ${moves.join(' ')}`);
    return this.lineAnswer();
  }
  promote(nodeId: number) {
    this.calls.push(`promote ${nodeId}`);
    return this.answer();
  }
  deleteVariation(nodeId: number) {
    this.calls.push(`deleteVariation ${nodeId}`);
    return this.answer();
  }
  deleteFrom(nodeId: number) {
    this.calls.push(`deleteFrom ${nodeId}`);
    return this.answer();
  }
}

function session(at: 'start' | 'end' | number = 'end', hooks = {}) {
  const port = new FakePort();
  const s = new TreeSession<MoveTreeView>(hooks);
  s.load(GAME, port, at);
  return { s, port };
}

describe('TreeSession', () => {
  describe('opening a tree', () => {
    it('shows the end of the main line by default', () => {
      expect(session().s.currentId()).toBe(3);
    });

    it('can open at the start or at a main-line ply', () => {
      expect(session('start').s.currentId()).toBeNull();
      expect(session(2).s.currentId()).toBe(2);
      expect(session(9).s.currentId()).toBe(3); // past the end: the last move
    });

    it('knows the main line and the moves to the board position', () => {
      const { s } = session(2);
      expect(s.mainLine()).toEqual(['e2e4', 'e7e5', 'g1f3']);
      expect(s.path()).toEqual(['e2e4', 'e7e5']);
      expect(s.fen()).toBe('fen-2');
    });
  });

  describe('navigation', () => {
    it('steps back, forward along the main continuation, to the start and to the end', () => {
      const { s } = session('start');
      s.forward();
      expect(s.currentId()).toBe(1);
      s.forward();
      expect(s.currentId()).toBe(2); // the main continuation, not the Variation
      s.back();
      expect(s.currentId()).toBe(1);
      s.goTo(4);
      s.toEnd();
      expect(s.currentId()).toBe(5); // the end of this line, inside the Variation
      s.toStart();
      expect(s.currentId()).toBeNull();
      expect(s.fen()).toBe(INITIAL_FEN);
    });

    it('answers the navigation keys and leaves others alone', () => {
      const { s } = session('start');
      expect(s.handleKey('End')).toBe(true);
      expect(s.currentId()).toBe(3);
      expect(s.handleKey('ArrowLeft')).toBe(true);
      expect(s.currentId()).toBe(2);
      expect(s.handleKey('ArrowUp')).toBe(true);
      expect(s.currentId()).toBeNull();
      expect(s.handleKey('x')).toBe(false);
    });

    it('tells the view before the board leaves a move', () => {
      const leaving: (number | null)[] = [];
      const { s } = session('end', { beforeChange: () => leaving.push(s.currentId()) });
      s.back();
      expect(leaving).toEqual([3]);
    });
  });

  describe('exploring', () => {
    it('steps into a recorded move instead of exploring it', () => {
      const { s } = session(1);
      s.treeMove({ uci: 'c7c5', san: 'c5', fen: 'x' });
      expect(s.currentId()).toBe(4);
      expect(s.explore()).toEqual([]);
    });

    it('shows a move the tree does not have without saving it, and steps back out', () => {
      const { s, port } = session(1);
      s.treeMove({ uci: 'e7e6', san: 'e6', fen: 'fen-e6' });
      expect(s.explore().map((step) => step.san)).toEqual(['e6']);
      expect(s.fen()).toBe('fen-e6');
      expect(s.path()).toEqual(['e2e4', 'e7e6']);
      expect(port.calls).toEqual([]);
      s.handleKey('ArrowLeft');
      expect(s.explore()).toEqual([]);
      expect(s.currentId()).toBe(1);
    });

    it('goes on exploring with moves played on the board', () => {
      const { s, port } = session('start');
      s.treeMove({ uci: 'd2d4', san: 'd4', fen: 'rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR b KQkq - 0 1' });
      s.playMove('d7d5');
      expect(s.explore().map((step) => step.san)).toEqual(['d4', 'd5']);
      expect(port.calls).toEqual([]);
    });

    it('adds the exploration from where it started and steps to its first move', () => {
      const { s, port } = session(1);
      const added = tree([...GAME.nodes.map((n) => [n.id, n.parentId, n.san, n.uci] as [number, number | null, string, string]), [6, 1, 'e6', 'e7e6']]);
      port.lineAnswer = () => of({ nodeId: 6, tree: added });
      s.treeMove({ uci: 'e7e6', san: 'e6', fen: 'fen-e6' });
      s.addExploration();
      expect(port.calls).toEqual(['addLine 1 e7e6']);
      expect(s.currentId()).toBe(6);
      expect(s.explore()).toEqual([]);
    });
  });

  describe('editing', () => {
    it('adds a move played on the board and shows it', () => {
      const { s, port } = session(3);
      port.lineAnswer = () => of({ nodeId: 7, tree: tree([[1, null, 'e4', 'e2e4'], [7, 1, 'd5', 'd7d5']]) });
      s.playMove('d7d5');
      expect(port.calls).toEqual(['addLine 3 d7d5']);
      expect(s.currentId()).toBe(7);
    });

    it('says why a move was refused and lets the view put the board back', () => {
      let rejected = 0;
      const { s, port } = session(3, { rejectedMove: () => rejected++ });
      port.lineAnswer = () => throwError(() => new Error('That move is not legal here.'));
      s.playMove('a1a8');
      expect(s.error()).not.toBe('');
      expect(rejected).toBe(1);
      expect(s.currentId()).toBe(3);
    });

    it('steps back to the nearest move that is left when the shown move is deleted', () => {
      const { s, port } = session('start');
      s.goTo(5);
      port.answer = () => of(tree([[1, null, 'e4', 'e2e4'], [2, 1, 'e5', 'e7e5'], [3, 2, 'Nf3', 'g1f3']]));
      s.treeAction('delete-variation', s.nodes().get(4)!);
      expect(port.calls).toEqual(['deleteVariation 4']);
      expect(s.currentId()).toBe(1);
    });

    it('keeps the shown move when an answer still has it', () => {
      const { s } = session(2);
      s.replace(GAME);
      expect(s.currentId()).toBe(2);
    });

    it('shows the first added move from where the line was sent, even if the board moved meanwhile', () => {
      const { s, port } = session(1);
      const late = new Subject<{ nodeId: number; tree: MoveTreeView }>();
      port.lineAnswer = () => late;
      s.treeMove({ uci: 'e7e6', san: 'e6', fen: 'fen-e6' });
      s.playLine(['d2d4']);                       // the exploration, then an Engine line
      expect(port.calls).toEqual(['addLine 1 e7e6 d2d4']);
      s.goTo(3);                                  // the Owner moves on before the answer arrives
      const rows = GAME.nodes.map((n) => [n.id, n.parentId, n.san, n.uci] as [number, number | null, string, string]);
      late.next({ nodeId: 7, tree: tree([...rows, [6, 1, 'e6', 'e7e6'], [7, 6, 'd4', 'd2d4']]) });
      expect(s.currentId()).toBe(6);
    });

    it('deletes from a move on and keeps the board on a move that is left', () => {
      const { s, port } = session(2);
      port.answer = () => of(tree([[1, null, 'e4', 'e2e4'], [4, 1, 'c5', 'c7c5'], [5, 4, 'Nc3', 'b1c3']]));
      s.treeAction('delete-from-here', s.nodes().get(2)!);
      expect(port.calls).toEqual(['deleteFrom 2']);
      expect(s.currentId()).toBe(1);
    });

    it('drops the exploration when the tree changes', () => {
      const { s } = session(1);
      s.treeMove({ uci: 'e7e6', san: 'e6', fen: 'fen-e6' });
      s.treeAction('promote', s.nodes().get(4)!);
      expect(s.explore()).toEqual([]);
    });

    it('lets the view save what it has before every change, not only before moving', () => {
      let saves = 0;
      const { s } = session(3, { beforeChange: () => saves++ });
      s.playMove('d7d5');
      s.treeAction('promote', s.nodes().get(4)!);
      expect(saves).toBe(2);
    });

    it('puts the board back when a move is played while exploring that chess.js refuses, or before the tree has loaded', () => {
      let rejected = 0;
      const s = new TreeSession<MoveTreeView>({ rejectedMove: () => rejected++ });
      s.load(null, null, 'start');
      s.playMove('e2e4');                         // nothing to add it to yet
      s.load(GAME, new FakePort(), 'start');
      s.treeMove({ uci: 'd2d4', san: 'd4', fen: 'rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR b KQkq - 0 1' });
      s.playMove('a1a8');                         // not legal in the explored position
      expect(rejected).toBe(2);
      expect(s.explore().length).toBe(1);
    });

    it('starts from the position the tree starts from', () => {
      const s = new TreeSession<MoveTreeView>();
      const custom = '8/8/8/8/8/8/4K3/4k3 w - - 0 1';
      s.load(tree([]), new FakePort(), 'start', custom);
      expect(s.fen()).toBe(custom);
    });

    it('ignores a failure for a tree that is no longer shown', () => {
      const { s, port } = session(3);
      const late = new Subject<MoveTreeView>();
      port.answer = () => late;
      s.treeAction('promote', s.nodes().get(4)!);
      s.load(GAME, port, 'end');
      late.error(new Error('gone'));
      expect(s.error()).toBe('');
    });

    it('drops an answer for a tree that is no longer shown', () => {
      const { s, port } = session(3);
      const late = new Subject<MoveTreeView>();
      port.answer = () => late;
      s.treeAction('promote', s.nodes().get(4)!);
      const other = tree([[9, null, 'd4', 'd2d4']]);
      s.load(other, port, 'end');
      late.next(GAME);
      expect(s.tree()).toBe(other);
      expect(s.currentId()).toBe(9);
    });
  });
});
