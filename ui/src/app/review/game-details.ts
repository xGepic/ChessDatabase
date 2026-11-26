import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { Api, Game, GameDetails, GameResult, OwnerColour, Player, RESULTS, problemMessage } from '../api/api';
import { PlayerPicker } from '../shared/player-picker';
import { parseElo } from '../shared/elo';

/** Edits a Game's details. Every change is saved at once; there is no Save button. */
@Component({
  selector: 'app-game-details',
  imports: [PlayerPicker],
  template: `
    <section class="card side-card">
      <header class="card-head"><h2>Game</h2></header>
      <div class="form-grid compact">
        <span class="label">I played</span>
        <div class="segmented" role="radiogroup" aria-label="Your colour">
          <button type="button" role="radio" [attr.aria-checked]="game().ownerColour === 'white'"
                  [class.on]="game().ownerColour === 'white'" (click)="setColour('white')">White</button>
          <button type="button" role="radio" [attr.aria-checked]="game().ownerColour === 'black'"
                  [class.on]="game().ownerColour === 'black'" (click)="setColour('black')">Black</button>
        </div>

        <label for="details-opponent">Opponent</label>
        <app-player-picker inputId="details-opponent" [player]="game().opponent" (playerChange)="setOpponent($event)" />

        <label for="white-elo">White Elo</label>
        <input id="white-elo" class="elo" inputmode="numeric" placeholder="unrated" [value]="whiteElo()"
               (input)="whiteElo.set($any($event.target).value)" (change)="saveElos()" />

        <label for="black-elo">Black Elo</label>
        <input id="black-elo" class="elo" inputmode="numeric" placeholder="unrated" [value]="blackElo()"
               (input)="blackElo.set($any($event.target).value)" (change)="saveElos()" />

        <span class="label">Result</span>
        <div class="segmented" role="radiogroup" aria-label="Result">
          @for (r of results; track r.value) {
            <button type="button" role="radio" [attr.aria-checked]="game().result === r.value"
                    [class.on]="game().result === r.value" (click)="setResult(r.value)">{{ r.label }}</button>
          }
        </div>

        <label for="details-date">Date</label>
        <input id="details-date" [value]="game().date ?? ''" placeholder="YYYY-MM-DD"
               (change)="setDate($any($event.target).value)" />
      </div>
      <footer class="card-foot">
        @if (error()) {
          <span class="field-error">{{ error() }}</span>
        } @else {
          <span class="hint">Changes save automatically.</span>
        }
        <span class="spacer"></span>
        @if (confirmingDelete()) {
          <span class="confirm">Delete this Game?</span>
          <button type="button" class="danger" data-testid="confirm-delete" (click)="deleted.emit()">Delete</button>
          <button type="button" (click)="confirmingDelete.set(false)">Keep</button>
        } @else {
          <button type="button" class="danger" data-testid="delete-game" (click)="confirmingDelete.set(true)">Delete Game</button>
        }
      </footer>
    </section>
  `,
})
export class GameDetailsCard {
  private readonly api = inject(Api);

  readonly game = input.required<Game>();
  readonly saved = output<Game>();
  readonly deleted = output<void>();

  protected readonly results = RESULTS;
  protected readonly whiteElo = signal('');
  protected readonly blackElo = signal('');
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);

  private readonly details = computed<GameDetails>(() => {
    const g = this.game();
    return {
      ownerColour: g.ownerColour,
      opponentId: g.opponent.id,
      whiteElo: g.whiteElo,
      blackElo: g.blackElo,
      result: g.result,
      date: g.date,
    };
  });

  constructor() {
    // Only a change of the saved Elos resets the fields, so other saves (e.g. a comment) never
    // overwrite an Elo the Owner is typing.
    effect(() => this.whiteElo.set(this.savedWhiteElo() ? String(this.savedWhiteElo()) : ''));
    effect(() => this.blackElo.set(this.savedBlackElo() ? String(this.savedBlackElo()) : ''));
  }

  private readonly savedWhiteElo = computed(() => this.game().whiteElo);
  private readonly savedBlackElo = computed(() => this.game().blackElo);

  protected setColour(ownerColour: OwnerColour) {
    if (ownerColour !== this.game().ownerColour) this.save({ ownerColour });
  }

  protected setOpponent(player: Player) {
    if (player.id !== this.game().opponent.id) this.save({ opponentId: player.id });
  }

  protected setResult(result: GameResult) {
    if (result !== this.game().result) this.save({ result });
  }

  protected setDate(date: string) {
    this.save({ date: date.trim() || null });
  }

  protected saveElos() {
    const whiteElo = parseElo(this.whiteElo());
    const blackElo = parseElo(this.blackElo());
    if (whiteElo === undefined || blackElo === undefined) {
      this.error.set('An Elo is a number. Leave it empty or enter 0 for unrated.');
      return;
    }
    this.save({ whiteElo, blackElo });
  }

  private save(change: Partial<GameDetails>) {
    this.error.set('');
    this.api.updateGame(this.game().id, { ...this.details(), ...change }).subscribe({
      next: (game) => this.saved.emit(game),
      error: (e) => this.error.set(problemMessage(e)),
    });
  }
}
