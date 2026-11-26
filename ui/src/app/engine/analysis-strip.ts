import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../api/api';
import { AnalysisQueueService } from './analysis-queue';

/** Game Analysis progress under the top bar, on every screen, while the queue is busy. */
@Component({
  selector: 'app-analysis-strip',
  imports: [RouterLink],
  template: `
    @if (busy()) {
      <div class="analysis-strip" role="status" data-testid="analysis-strip">
        <span>Game Analysis: Game <strong data-testid="strip-game">{{ gameNumber() }} of {{ status().batchTotal }}</strong></span>
        @if (status().running; as r) {
          <progress [value]="r.done" [max]="r.total || 1" aria-label="This Game"></progress>
          <span>{{ r.done }}/{{ r.total || '?' }} positions</span>
          <a [routerLink]="['/games', r.gameId]">Open this Game</a>
        }
        <span class="spacer"></span>
        <button type="button" data-testid="cancel-analysis" (click)="cancel()">Cancel</button>
      </div>
    }
  `,
})
export class AnalysisStrip {
  private readonly api = inject(Api);
  private readonly queue = inject(AnalysisQueueService);
  protected readonly status = this.queue.status;
  protected readonly busy = computed(() => !!this.status().running || this.status().queued.length > 0);
  protected readonly gameNumber = computed(() => Math.min(this.status().batchDone + 1, this.status().batchTotal));

  constructor() {
    this.queue.watch();
  }

  /** Stops the running analysis and empties the queue; finished Engine Assessments stay. */
  protected cancel() {
    this.api.cancelAnalysis().subscribe((status) => this.queue.set(status));
  }
}
