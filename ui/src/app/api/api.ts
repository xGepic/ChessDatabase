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
  /** Has an Engine Assessment. */
  analysed: boolean;
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

export interface BackupStatus {
  folder: string;
  /** True when no folder is set and backups go to "Backups" beside the Collection. */
  isDefault: boolean;
  lastBackupAt: string | null;
}

/** The Stockfish definition's id (the only Engine that ships). */
export const STOCKFISH_ID = 1;

/** Game Analysis time per position, in seconds: the backend's allowed range. */
export const ANALYSIS_SECONDS = { min: 0.1, max: 600 };

/** An Engine definition (only Stockfish ships). */
export interface Engine {
  id: number;
  name: string;
  /** Empty until set. */
  path: string;
  threads: number;
  hashMb: number;
  /** Lines shown in Interactive Analysis (1–3). */
  multiPv: number;
  /** Interactive Analysis search time in seconds; null means no limit. */
  interactiveSecondsLimit: number | null;
  /** Game Analysis time per position. */
  analysisSecondsPerMove: number;
  /** What the program called itself, e.g. "Stockfish 17.1". */
  reportedName: string | null;
}

/** One Engine line. Scores are from White's side: positive favours White. */
export interface AnalysisLine {
  rank: number;
  depth: number;
  cp: number | null;
  /** Moves to mate; negative when Black mates. */
  mate: number | null;
  /** White's winning chance in percent (Lichess formula, worked out by the backend). */
  whitePercent: number;
  san: string[];
  uci: string[];
}

export interface AnalysisUpdate {
  requestId: number;
  /** Empty when the position has no legal moves. */
  lines: AnalysisLine[];
}

export type Classification = 'inaccuracy' | 'mistake' | 'blunder';

/** A position's evaluation from White's side. mate 0: the side to move is checkmated. */
export interface AssessedPosition {
  ply: number;
  whitePercent: number;
  cp: number | null;
  mate: number | null;
}

export interface AssessedMove {
  ply: number;
  /** The move in the Game; null if the main line has changed there since the analysis. */
  nodeId: number | null;
  san: string;
  classification: Classification | null;
  /** Winning chance given away, in points (Lichess scale). */
  loss: number;
  accuracy: number;
  /** The Engine's move in the position before, and its line. */
  bestSan: string | null;
  bestLineSan: string[];
  bestLineUci: string[];
}

/** Game Analysis output, kept apart from the Owner's Annotations. */
export interface EngineAssessment {
  gameId: number;
  engineName: string;
  threads: number;
  hashMb: number;
  secondsPerMove: number;
  analysedAt: string;
  /** The main line changed since the analysis. */
  stale: boolean;
  whiteAccuracy: number | null;
  blackAccuracy: number | null;
  positions: AssessedPosition[];
  moves: AssessedMove[];
}

export interface AnalysisQueue {
  running: { gameId: number; done: number; total: number } | null;
  queued: number[];
  batchDone: number;
  batchTotal: number;
}

/** copied: how many moves gained a symbol or a Variation. */
export interface CopyResult {
  copied: number;
  game: Game;
}

export interface AnalysisFinished {
  gameId: number;
  error: string | null;
}

/** A move played from a position in the Owner's Games. score: the Owner's, in percent. */
export interface TreeMove {
  uci: string;
  san: string;
  games: number;
  wins: number;
  draws: number;
  losses: number;
  score: number;
}

/** A Game whose main line reached a position: ply is when it first did (0 = the start). */
export interface PositionGame {
  game: GameSummary;
  ply: number;
  /** The number of the move played next from the position. */
  moveNumber: number;
}

export interface OpeningTree {
  /** Games that reached the position. */
  games: number;
  moves: TreeMove[];
  gameList: PositionGame[];
}

export type ImportStatus = 'imported' | 'needsReview' | 'duplicate' | 'failed';

export interface ImportedGame {
  index: number;
  status: ImportStatus;
  reason: string | null;
  white: string | null;
  black: string | null;
  /** The imported Game, or for a duplicate the existing one. */
  gameId: number | null;
  /** For a duplicate: its PGN, to import it anyway. */
  pgn: string | null;
}

/** A held-back imported Game. ownerColour and result are null while they still need choosing. */
export interface ReviewItem {
  id: number;
  white: string | null;
  black: string | null;
  date: string | null;
  reason: string;
  ownerColour: OwnerColour | null;
  result: GameResult | null;
  whiteSuggestion: Player | null;
  blackSuggestion: Player | null;
}

export interface ImportSummary {
  imported: number;
  needsReview: number;
  duplicates: number;
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
  /** The game list; filters and sort as query parameters (see GameListQuery in the backend). */
  listGames(filters: Record<string, string> = {}): Observable<GameSummary[]> {
    return this.http.get<GameSummary[]>('/api/games', { params: filters });
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
  /** Plays several moves from a node; a move already recorded there is followed, not doubled. */
  addLine(gameId: number, parentId: number | null, moves: string[]): Observable<{ nodeId: number; game: Game }> {
    return this.http.post<{ nodeId: number; game: Game }>(`/api/games/${gameId}/moves/line`, { parentId, moves });
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
  importText(pgn: string, allowDuplicates = false): Observable<ImportSummary> {
    return this.http.post<ImportSummary>('/api/import/text', { pgn, allowDuplicates });
  }
  reviewList(): Observable<ReviewItem[]> {
    return this.http.get<ReviewItem[]>('/api/import/review');
  }
  /** Fails with 409 when the Collection already has this Game, unless allowDuplicate is set. */
  resolveReview(
    id: number,
    ownerColour: OwnerColour,
    result: GameResult,
    opponentPlayerId: number | null,
    allowDuplicate = false,
  ): Observable<Game> {
    return this.http.post<Game>(`/api/import/review/${id}/resolve`, { ownerColour, result, opponentPlayerId, allowDuplicate });
  }
  discardReview(id: number): Observable<void> {
    return this.http.delete<void>(`/api/import/review/${id}`);
  }
  importFile(file: File): Observable<ImportSummary> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ImportSummary>('/api/import/file', form);
  }

  // Research
  openingTree(fen: string): Observable<OpeningTree> {
    return this.http.get<OpeningTree>('/api/research/tree', { params: { fen } });
  }
  /** Games whose main line reached the position. */
  searchPosition(fen: string): Observable<PositionGame[]> {
    return this.http.get<PositionGame[]>('/api/research/search', { params: { fen } });
  }

  // Settings
  getSettings(): Observable<Settings> {
    return this.http.get<Settings>('/api/settings');
  }
  saveSettings(settings: Settings): Observable<Settings> {
    return this.http.put<Settings>('/api/settings', settings);
  }

  // Engines
  getEngines(): Observable<Engine[]> {
    return this.http.get<Engine[]>('/api/engines');
  }
  /** Queues Game Analysis of one Game; secondsPerMove overrides the time per position from Settings. */
  analyseGame(gameId: number, secondsPerMove?: number): Observable<AnalysisQueue> {
    return this.http.post<AnalysisQueue>(`/api/games/${gameId}/analysis`, { secondsPerMove: secondsPerMove ?? null });
  }
  queueAnalysis(gameIds: number[], secondsPerMove?: number): Observable<AnalysisQueue> {
    return this.http.post<AnalysisQueue>('/api/analysis/queue', { gameIds, secondsPerMove: secondsPerMove ?? null });
  }
  analysisQueue(): Observable<AnalysisQueue> {
    return this.http.get<AnalysisQueue>('/api/analysis/queue');
  }
  cancelAnalysis(): Observable<AnalysisQueue> {
    return this.http.delete<AnalysisQueue>('/api/analysis/queue');
  }
  /** Copies a classified move into the Game: its symbol (unless one is set) and the Engine line as a Variation. */
  copyAssessedMove(gameId: number, ply: number): Observable<CopyResult> {
    return this.http.post<CopyResult>(`/api/games/${gameId}/assessment/moves/${ply}/copy`, null);
  }
  /** Copies every mistake and blunder the same way. */
  copyMistakesAndBlunders(gameId: number): Observable<CopyResult> {
    return this.http.post<CopyResult>(`/api/games/${gameId}/assessment/copy`, null);
  }
  /** 404 when the Game has no Engine Assessment. */
  getAssessment(gameId: number): Observable<EngineAssessment> {
    return this.http.get<EngineAssessment>(`/api/games/${gameId}/assessment`);
  }
  /** Saving a path starts the program once to check it is a UCI engine. */
  saveEngine(
    engine: Pick<Engine, 'id' | 'path' | 'threads' | 'hashMb' | 'multiPv' | 'interactiveSecondsLimit' | 'analysisSecondsPerMove'>,
  ): Observable<Engine> {
    const { id, ...body } = engine;
    return this.http.put<Engine>(`/api/engines/${id}`, body);
  }

  // Backups
  backupStatus(): Observable<BackupStatus> {
    return this.http.get<BackupStatus>('/api/backups');
  }
  /** An empty folder means the default. */
  setBackupFolder(folder: string): Observable<BackupStatus> {
    return this.http.put<BackupStatus>('/api/backups/folder', { folder });
  }
  backUpNow(): Observable<BackupStatus> {
    return this.http.post<BackupStatus>('/api/backups', null);
  }
}
