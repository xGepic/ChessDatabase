import { Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, switchMap } from 'rxjs';
import { Api, PlayerDetails, PlayerRow, problemMessage } from '../api/api';

/**
 * Merges another Player into this one: the Owner picks the other Player, chooses which name,
 * FIDE ID and club to keep, and confirms. Nothing happens before the confirmation.
 */
@Component({
  selector: 'app-merge-panel',
  template: `
    <section class="card merge-card" aria-label="Merge Players" data-testid="merge-panel">
      <header class="card-head"><h2>Merge with another Player</h2><span class="spacer"></span>
        <button type="button" class="link-button" (click)="closed.emit()">Close</button></header>
      @if (!other()) {
        <div class="card-body">
          <input type="search" class="player-search" placeholder="Find the other Player" aria-label="Find the other Player"
                 data-testid="merge-search" (input)="search.next($any($event.target).value)" />
          <ul class="merge-candidates">
            @for (p of candidates(); track p.id) {
              <li><button type="button" class="link-button" data-testid="merge-candidate" (click)="choose(p)">
                {{ p.name }} <span class="hint">{{ p.score.games }} {{ p.score.games === 1 ? 'Game' : 'Games' }}{{ p.club ? ' · ' + p.club : '' }}</span>
              </button></li>
            }
          </ul>
        </div>
      } @else {
        @let o = other()!;
        <div class="form-grid compact">
          <span class="label">Name</span>
          <div class="segmented" role="radiogroup" aria-label="Name to keep">
            @for (n of options('name'); track n) {
              <button type="button" role="radio" data-testid="merge-name" [class.on]="name() === n" [attr.aria-checked]="name() === n"
                      (click)="name.set(n)">{{ n }}</button>
            }
          </div>
          @if (options('fideId').length > 1) {
            <span class="label">FIDE ID</span>
            <div class="segmented" role="radiogroup" aria-label="FIDE ID to keep">
              @for (v of options('fideId'); track v) {
                <button type="button" role="radio" [class.on]="fideId() === v" [attr.aria-checked]="fideId() === v" (click)="fideId.set(v)">{{ v || 'none' }}</button>
              }
            </div>
          }
          @if (options('club').length > 1) {
            <span class="label">Club</span>
            <div class="segmented" role="radiogroup" aria-label="Club to keep">
              @for (v of options('club'); track v) {
                <button type="button" role="radio" [class.on]="club() === v" [attr.aria-checked]="club() === v" (click)="club.set(v)">{{ v || 'none' }}</button>
              }
            </div>
          }
        </div>
        <p class="merge-summary">All {{ o.score.games }} {{ o.score.games === 1 ? 'Game' : 'Games' }} against <strong>{{ o.name }}</strong> become
          Games against <strong>{{ name() }}</strong>, and {{ o.name === name() ? 'the other record' : o.name }} is removed. This can't be undone.</p>
        <footer class="form-actions merge-actions">
          <button type="button" class="primary" data-testid="merge-confirm" (click)="merge()">Merge</button>
          <button type="button" (click)="other.set(null)">Choose another</button>
          @if (error()) { <span class="field-error" role="alert">{{ error() }}</span> }
        </footer>
      }
    </section>
  `,
})
export class MergePanel {
  private readonly api = inject(Api);

  /** The Player whose Dossier this is: the one kept. */
  readonly player = input.required<PlayerDetails>();
  readonly merged = output<void>();
  readonly closed = output<void>();

  protected readonly search = new Subject<string>();
  protected readonly found = signal<PlayerRow[]>([]);
  protected readonly other = signal<PlayerRow | null>(null);
  protected readonly name = signal('');
  protected readonly fideId = signal('');
  protected readonly club = signal('');
  protected readonly error = signal('');
  protected readonly candidates = computed(() => this.found().filter((p) => p.id !== this.player().id).slice(0, 12));

  constructor() {
    this.search
      .pipe(debounceTime(150), switchMap((term) => this.api.playerOverview(term)), takeUntilDestroyed())
      .subscribe((list) => this.found.set(list));
  }

  protected choose(other: PlayerRow) {
    const kept = this.player();
    this.other.set(other);
    this.name.set(kept.name);
    this.fideId.set(kept.fideId ?? other.fideId ?? '');
    this.club.set(kept.club ?? other.club ?? '');
    this.error.set('');
  }

  /** The distinct values of a detail across the two Players; for FIDE ID and club, "none" too when one has none. */
  protected options(field: 'name' | 'fideId' | 'club'): string[] {
    const other = this.other();
    if (!other) return [];
    const values = [this.player()[field] ?? '', other[field] ?? ''];
    return [...new Set(values)];
  }

  protected merge() {
    const other = this.other();
    if (!other) return;
    this.api.mergePlayers(this.player().id, { mergeId: other.id, name: this.name(), fideId: this.fideId(), club: this.club() }).subscribe({
      next: () => this.merged.emit(),
      error: (e) => this.error.set(problemMessage(e)),
    });
  }
}
