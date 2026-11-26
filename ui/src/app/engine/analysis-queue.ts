import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AnalysisFinished, AnalysisQueue, Api } from '../api/api';

/**
 * The Game Analysis queue as the backend reports it over the analysis hub. Shared by every screen,
 * so progress stays visible wherever the Owner is working.
 */
@Injectable({ providedIn: 'root' })
export class AnalysisQueueService {
  private readonly api = inject(Api);
  readonly status = signal<AnalysisQueue>({ running: null, queued: [], batchDone: 0, batchTotal: 0 });
  /** One event per Game that finished (or failed, or was cancelled). */
  readonly finished = new Subject<AnalysisFinished>();
  private connection: HubConnection | null = null;

  /** Starts listening (once) and reads the current queue. */
  watch() {
    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        .withUrl('/hubs/analysis')
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();
      this.connection.on('analysisQueue', (status: AnalysisQueue) => this.status.set(status));
      this.connection.on('analysisFinished', (event: AnalysisFinished) => this.finished.next(event));
      this.connection.onreconnected(() => this.refresh());
    }
    if (this.connection.state === HubConnectionState.Disconnected) this.connection.start().catch(() => {});
    this.refresh();
  }

  /** The queue as returned by a call that changed it, in case the hub message is still on its way. */
  set(status: AnalysisQueue) {
    this.status.set(status);
  }

  private refresh() {
    this.api.analysisQueue().subscribe((status) => this.status.set(status));
  }
}
