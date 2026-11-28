import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api, OUTCOMES, Outcome, OwnerColour, Player, problemMessage, resultFor } from '../api/api';
import { PlayerPicker } from '../shared/player-picker';
import { SuggestInput } from '../shared/suggest-input';
import { map } from 'rxjs';
import { parseElo } from '../shared/elo';

/** Starts a Game: the Owner's colour, the Opponent, both Elos, the result, and optionally the Event and Round. Moves follow on the board. */
@Component({
  selector: 'app-new-game',
  imports: [PlayerPicker, SuggestInput],
  templateUrl: './new-game.html',
})
export class NewGame {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly outcomes = OUTCOMES;
  protected readonly ownerColour = signal<OwnerColour>('white');
  protected readonly opponent = signal<Player | null>(null);
  protected readonly ownerElo = signal('');
  protected readonly opponentElo = signal('');
  protected readonly outcome = signal<Outcome | null>(null);
  protected readonly date = signal(new Date().toISOString().slice(0, 10));
  protected readonly event = signal('');
  protected readonly round = signal('');
  protected readonly searchEvents = (term: string) => this.api.searchEvents(term).pipe(map((l) => l.map((e) => e.name)));
  protected readonly error = signal('');
  protected readonly saving = signal(false);

  protected readonly ready = computed(() => !!this.opponent() && !!this.outcome() && !this.saving());

  protected start() {
    const opponent = this.opponent();
    const outcome = this.outcome();
    if (!opponent || !outcome) return;
    const ownerElo = parseElo(this.ownerElo());
    const opponentElo = parseElo(this.opponentElo());
    if (ownerElo === undefined || opponentElo === undefined) {
      this.error.set('An Elo is a number. Leave it empty or enter 0 for unrated.');
      return;
    }
    const white = this.ownerColour() === 'white';
    this.saving.set(true);
    this.api
      .createGame({
        ownerColour: this.ownerColour(),
        opponentId: opponent.id,
        whiteElo: white ? ownerElo : opponentElo,
        blackElo: white ? opponentElo : ownerElo,
        result: resultFor(outcome, this.ownerColour()),
        date: this.date() || null,
        event: this.event().trim() || null,
        round: this.round().trim() || null,
      })
      .subscribe({
        next: (game) => this.router.navigate(['/games', game.id]),
        error: (e) => {
          this.saving.set(false);
          this.error.set(problemMessage(e));
        },
      });
  }
}
