import { Router } from '@angular/router';

/** The standard starting position. */
export const INITIAL_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/** Opens a Game showing the main-line position after this many half-moves (0 = the start). */
export function openGameAt(router: Router, gameId: number, ply: number) {
  return router.navigate(['/games', gameId], { queryParams: { ply } });
}
