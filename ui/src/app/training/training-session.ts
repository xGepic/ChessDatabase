import { DestroyRef, Directive, computed, inject, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { TrainingSummary, problemMessage } from '../api/api';
import { INITIAL_FEN } from '../shared/chess';

/**
 * What every training session shares (Repertoire Training, Mistake Training): one item at a time
 * on the board, an answer played on it, the next item on Enter. A move counts as an answer only
 * while the item's own position is on the board and nothing is on its way.
 */
@Directive()
export abstract class TrainingSession<TItem extends { itemId: number }, TAnswer extends { shown: string; fenAfter: string }> {
  protected readonly summary = signal<TrainingSummary | null>(null);
  protected readonly item = signal<TItem | null>(null);
  protected readonly answer = signal<TAnswer | null>(null);
  protected readonly loading = signal(true);
  /** An answer is on its way (the Engine may be judging it). */
  protected readonly checking = signal(false);
  protected readonly error = signal('');
  protected readonly fen = signal(INITIAL_FEN);
  protected readonly lastMove = signal<string | null>(null);
  /** The item's position is on the board: a move made now is the answer. */
  protected readonly ready = signal(false);
  protected readonly canAnswer = computed(() => this.ready() && !!this.item() && !this.answer() && !this.checking());
  protected readonly dueLabel = computed(() => {
    const s = this.summary();
    return s ? `${s.due} due today` : '';
  });
  private request: Subscription | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.request?.unsubscribe();
      this.stop();
    });
  }

  protected abstract summaryRequest(): Observable<TrainingSummary>;
  protected abstract nextRequest(): Observable<TItem | null>;
  protected abstract answerRequest(itemId: number, uci: string): Observable<TAnswer>;
  /** Puts the item on the board and sets <code>ready</code> once a move may be answered. */
  protected abstract present(item: TItem): void;
  /** The board after a refused answer. */
  protected abstract restore(item: TItem): void;
  /** Stops whatever the session animates (when moving on or leaving). */
  protected stop(): void {}

  /** Loads the next due item; only the latest request shows its item. */
  protected next() {
    this.request?.unsubscribe();
    this.stop();
    this.answer.set(null);
    this.ready.set(false);
    this.error.set('');
    this.summaryRequest().subscribe((s) => this.summary.set(s));
    this.request = this.nextRequest().subscribe({
      next: (item) => {
        this.loading.set(false);
        this.item.set(item);
        if (item) this.present(item);
      },
      error: (e) => {
        this.loading.set(false);
        this.error.set(problemMessage(e));
      },
    });
  }

  protected onMove(uci: string) {
    const item = this.item();
    if (!item || !this.canAnswer()) return;
    this.checking.set(true);
    this.error.set('');
    this.answerRequest(item.itemId, uci).subscribe({
      next: (answer) => {
        this.checking.set(false);
        this.answer.set(answer);
        this.show(answer.fenAfter, answer.shown); // the Owner's move when right, the right one when wrong
      },
      error: (e) => {
        this.checking.set(false);
        this.error.set(problemMessage(e));
        this.restore(item);
      },
    });
  }

  /** Enter goes on; on a focused button the button's own click does, so it isn't done twice. */
  protected onEnter(event: Event) {
    if ((event.target as HTMLElement | null)?.closest('button')) return;
    if (this.answer()) this.next();
  }

  protected show(fen: string, lastMove: string | null) {
    this.fen.set(fen);
    this.lastMove.set(lastMove);
  }
}
