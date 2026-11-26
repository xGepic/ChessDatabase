import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type OwnerColor = 'white' | 'black';
export type GameResult = '1-0' | '0-1' | '1/2-1/2';

export interface Player {
  id: number;
  name: string;
}

export interface GameSummary {
  id: number;
  date: string | null;
  ownerColor: OwnerColor;
  opponent: Player;
  white: string;
  black: string;
  whiteElo: number | null;
  blackElo: number | null;
  result: GameResult;
}

export interface MoveNode {
  id: number;
  parentId: number | null;
  children: number[];
  ply: number;
  san: string;
  uci: string;
  /** Position after this move. */
  fen: string;
  commentBefore: string | null;
  commentAfter: string | null;
  nags: number[];
}

export interface Game extends GameSummary {
  startFen: string;
  firstMoves: number[];
  nodes: MoveNode[];
}

export interface GameDetails {
  ownerColor: OwnerColor;
  opponentId: number;
  whiteElo: number | null;
  blackElo: number | null;
  result: GameResult;
  date: string | null;
}

export interface Settings {
  ownerName: string;
}

export const RESULTS: { value: GameResult; label: string }[] = [
  { value: '1-0', label: '1-0' },
  { value: '0-1', label: '0-1' },
  { value: '1/2-1/2', label: '½-½' },
];

export function resultLabel(result: GameResult): string {
  return result === '1/2-1/2' ? '½-½' : result;
}

export function eloLabel(elo: number | null): string {
  return elo ? String(elo) : 'unrated';
}

/** The first validation message of a ProblemDetails response, or a generic one. */
export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const errors = error.error?.errors as Record<string, string[]> | undefined;
    const first = errors && Object.values(errors)[0]?.[0];
    if (first) return first;
    if (error.status === 0) return 'The app backend is not reachable.';
  }
  return 'Something went wrong.';
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  // Games
  listGames(): Observable<GameSummary[]> {
    return this.http.get<GameSummary[]>('/api/games');
  }
  getGame(id: number): Observable<Game> {
    return this.http.get<Game>(`/api/games/${id}`);
  }
  createGame(details: GameDetails): Observable<Game> {
    return this.http.post<Game>('/api/games', details);
  }
  updateGame(id: number, details: GameDetails): Observable<Game> {
    return this.http.put<Game>(`/api/games/${id}`, details);
  }
  deleteGame(id: number): Observable<void> {
    return this.http.delete<void>(`/api/games/${id}`);
  }

  // Move tree
  addMove(gameId: number, parentId: number | null, move: string): Observable<{ nodeId: number; game: Game }> {
    return this.http.post<{ nodeId: number; game: Game }>(`/api/games/${gameId}/moves`, { parentId, move });
  }

  // Players
  searchPlayers(search: string): Observable<Player[]> {
    return this.http.get<Player[]>('/api/players', { params: { search } });
  }
  createPlayer(name: string): Observable<Player> {
    return this.http.post<Player>('/api/players', { name });
  }

  // Settings
  getSettings(): Observable<Settings> {
    return this.http.get<Settings>('/api/settings');
  }
  saveSettings(settings: Settings): Observable<Settings> {
    return this.http.put<Settings>('/api/settings', settings);
  }
}
