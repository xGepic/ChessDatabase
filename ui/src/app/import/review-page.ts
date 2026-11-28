import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PlayerPicker } from '../shared/player-picker';
import { Api, GameResult, OUTCOMES, Outcome, OwnerColour, Player, ReviewItem, outcomeOf, problemMessage, resultFor } from '../api/api';

/** What the Owner has chosen so far for one held-back import. */
interface Draft {
  ownerColour: OwnerColour | null;
  result: GameResult | null;
  /** True: same Player as the suggestion. False: a different (new) Player. Null: not decided. */
  sameAsSuggestion: boolean | null;
  /** The Opponent the Owner chose, when the PGN has no name for that side. */
  opponent: Player | null;
}

/**
 * The import review list: imported entries held back because no side matched the Owner, the result or a
 * player's name was missing, or the Opponent only resembles an existing Player. Nothing is linked without a choice.
 */
@Component({
  selector: 'app-review-page',
  imports: [RouterLink, PlayerPicker],
  templateUrl: './review-page.html',
})
export class ReviewPage {
  private readonly api = inject(Api);

  protected readonly outcomes = OUTCOMES;
  protected readonly items = signal<ReviewItem[] | null>(null);
  protected readonly drafts = signal<Record<number, Draft>>({});
  protected readonly errors = signal<Record<number, string>>({});
  /** Items the Collection already has, with the existing Game, so the Owner can open it or add anyway. */
  protected readonly duplicates = signal<Record<number, number | null>>({});

  constructor() {
    this.load();
  }

  private load() {
    this.api.reviewList().subscribe((items) => {
      this.items.set(items);
      this.drafts.set(
        Object.fromEntries(
          items.map((i) => [i.id, { ownerColour: i.ownerColour, result: i.result, sameAsSuggestion: null, opponent: null }]),
        ),
      );
    });
  }

  protected draft(item: ReviewItem): Draft {
    return this.drafts()[item.id];
  }

  protected update(item: ReviewItem, change: Partial<Draft>) {
    this.drafts.update((d) => ({ ...d, [item.id]: { ...d[item.id], ...change } }));
  }

  /** The result as the Owner sees it. A result from the PGN stays the board result when the side changes. */
  protected outcome(item: ReviewItem): Outcome | null {
    const d = this.draft(item);
    return outcomeOf(d.result, d.ownerColour);
  }

  protected setOutcome(item: ReviewItem, outcome: Outcome) {
    const colour = this.draft(item).ownerColour;
    if (colour) this.update(item, { result: resultFor(outcome, colour) });
  }

  /** The existing Player the Opponent may be, once the Owner's side is known. */
  protected suggestion(item: ReviewItem): Player | null {
    const colour = this.draft(item).ownerColour;
    if (!colour) return null;
    return colour === 'white' ? item.blackSuggestion : item.whiteSuggestion;
  }

  /** The PGN has no name on the Opponent's side, so the Owner picks the Player. */
  protected opponentMissing(item: ReviewItem): boolean {
    const colour = this.draft(item).ownerColour;
    if (!colour) return false;
    return !(colour === 'white' ? item.black : item.white)?.trim();
  }

  protected opponentName(item: ReviewItem): string {
    return (this.draft(item).ownerColour === 'white' ? item.black : item.white) ?? '?';
  }

  protected ready(item: ReviewItem): boolean {
    const d = this.draft(item);
    return (
      !!d.ownerColour &&
      !!d.result &&
      (!this.opponentMissing(item) || !!d.opponent) &&
      (!this.suggestion(item) || d.sameAsSuggestion !== null)
    );
  }

  protected resolve(item: ReviewItem, allowDuplicate = false) {
    const d = this.draft(item);
    if (!d.ownerColour || !d.result) return;
    const link = this.opponentMissing(item)
      ? (d.opponent?.id ?? null)
      : this.suggestion(item) && d.sameAsSuggestion
        ? this.suggestion(item)!.id
        : null;
    this.api.resolveReview(item.id, d.ownerColour, d.result, link, allowDuplicate).subscribe({
      next: () => this.remove(item),
      error: (e: HttpErrorResponse) => {
        if (e.status === 409) {
          this.duplicates.update((x) => ({ ...x, [item.id]: e.error?.existingGameId ?? null }));
          this.errors.update((x) => ({ ...x, [item.id]: 'This Game is already in your Collection.' }));
        } else {
          this.errors.update((x) => ({ ...x, [item.id]: problemMessage(e) }));
        }
      },
    });
  }

  protected discard(item: ReviewItem) {
    this.api.discardReview(item.id).subscribe(() => this.remove(item));
  }

  private remove(item: ReviewItem) {
    this.items.update((list) => (list ?? []).filter((i) => i.id !== item.id));
  }
}
