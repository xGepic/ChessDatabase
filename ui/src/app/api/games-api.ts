import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface GameSummary {
  id: number;
}

@Injectable({ providedIn: 'root' })
export class GamesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<GameSummary[]> {
    return this.http.get<GameSummary[]>('/api/games');
  }
}
