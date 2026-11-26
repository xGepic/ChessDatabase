import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type OwnerColour = 'white' | 'black';
export type GameResult = '1-0' | '0-1' | '1/2-1/2';

export interface Player {
  id: number;
  name: string;
}

export interface GameSummary {
  id: number;
  date: string | null;
  ownerColour: OwnerColour;
  opponent: Player;
  white: string;
  black: string;
  whiteElo: number | null;
  blackElo: number | null;
  result: GameResult;
  event: Named | null;
  eco: string | null;
  openingName: string | null;
}

/** An Event or a Tag. */
export interface Named {
  id: number;
  name: string;
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
  site: string | null;
  round: string | null;
  timeControl: string | null;
  notes: string | null;
  tags: string[];
  startFen: string;
  firstMoves: number[];
  nodes: MoveNode[];
}

export interface GameDetails {
  ownerColour: OwnerColour;
  opponentId: number;
  whiteElo: number | null;
  blackElo: number | null;
  result: GameResult;
  date: string | null;
  /** Event name; an existing Event with that name is linked. */
  event?: string | null;
  site?: string | null;
  round?: string | null;
  timeControl?: string | null;
  notes?: string | null;
  tags?: string[];
}

export interface Settings {
  ownerName: string;
  /** Other spellings of the Owner's name, used to recognise the Owner on import. */
  aliases: string[];
}

export type ImportStatus = 'imported' | 'needsReview' | 'failed';

export interface ImportedGame {
  index: number;
  status: ImportStatus;
  reason: string | null;
  white: string | null;
  black: string | null;
  gameId: number | null;
}

export interface ImportSummary {
  imported: number;
  needsReview: number;
  failed: number;
  games: ImportedGame[];
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
  promote(gameId: number, nodeId: number): Observable<Game> {
    return this.http.post<Game>(`/api/games/${gameId}/moves/${nodeId}/promote`, null);
  }
  deleteVariation(gameId: number, nodeId: number): Observable<Game> {
    return this.http.delete<Game>(`/api/games/${gameId}/moves/${nodeId}/variation`);
  }
  deleteFromHere(gameId: number, nodeId: number): Observable<Game> {
    return this.http.delete<Game>(`/api/games/${gameId}/moves/${nodeId}`);
  }
  setComment(gameId: number, nodeId: number, text: string): Observable<Game> {
    return this.http.put<Game>(`/api/games/${gameId}/moves/${nodeId}/comment`, { text });
  }
  setNags(gameId: number, nodeId: number, nags: number[]): Observable<Game> {
    return this.http.put<Game>(`/api/games/${gameId}/moves/${nodeId}/nags`, { nags });
  }

  // Autocomplete
  searchEvents(search: string): Observable<Named[]> {
    return this.http.get<Named[]>('/api/events', { params: { search } });
  }
  searchSites(search: string): Observable<string[]> {
    return this.http.get<string[]>('/api/sites', { params: { search } });
  }
  searchTags(search: string): Observable<Named[]> {
    return this.http.get<Named[]>('/api/tags', { params: { search } });
  }

  // Players
  searchPlayers(search: string): Observable<Player[]> {
    return this.http.get<Player[]>('/api/players', { params: { search } });
  }
  createPlayer(name: string): Observable<Player> {
    return this.http.post<Player>('/api/players', { name });
  }

  // Import
  importText(pgn: string): Observable<ImportSummary> {
    return this.http.post<ImportSummary>('/api/import/text', { pgn });
  }
  importFile(file: File): Observable<ImportSummary> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ImportSummary>('/api/import/file', form);
  }

  // Settings
  getSettings(): Observable<Settings> {
    return this.http.get<Settings>('/api/settings');
  }
  saveSettings(settings: Settings): Observable<Settings> {
    return this.http.put<Settings>('/api/settings', settings);
  }
}
