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

/** A move tree: a Game's moves, or a Repertoire. firstMoves are the continuations of the starting position. */
export interface MoveTreeView {
  firstMoves: number[];
  nodes: MoveNode[];
}

/** One of the Owner's Repertoires (comments are commentAfter on its nodes). */
export interface Repertoire extends MoveTreeView {
  side: OwnerColour;
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
  /** Whether a Lichess API token is stored (the token itself is never sent back). */
  hasLichessToken: boolean;
}

export interface SaveSettings {
  ownerName: string;
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
  /** The position after the move (worked out by the backend). */
  fen: string;
  gameCount: number;
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
  gameCount: number;
  moves: TreeMove[];
  /** The newest of those Games (at most 50). */
  games: PositionGame[];
}

/** A move to show on the board: from the Opening Tree (either source). fen is the position after it. */
export interface BoardMove {
  uci: string;
  san: string;
  fen: string;
}

/** A player of a Master Game, with the Elo Lichess has for them. */
export interface MasterPlayer {
  name: string;
  elo: number | null;
}

/** How games ended, in percent: White won, drawn, Black won. */
export interface ResultShare {
  white: number;
  draw: number;
  black: number;
}

/** A move played from the position in Master Games (Lichess masters explorer). */
export interface MastersMove extends BoardMove {
  gameCount: number;
  results: ResultShare;
  averageElo: number | null;
}

/** A top Master Game for a position. Never stored in the Collection. */
export interface MasterGameRef {
  id: string;
  white: MasterPlayer;
  black: MasterPlayer;
  year: number | null;
  result: GameResult;
}

/** The Masters source for a position; when not available, message says why. */
export interface Masters {
  available: boolean;
  message: string | null;
  gameCount: number;
  /** The most played moves (at most 12). */
  moves: MastersMove[];
  /** Master Games that went on with a move not listed. */
  otherMovesGameCount: number;
  topGames: MasterGameRef[];
}

/** A Master Game to replay read-only. ply: where the asked position occurs, if it does. */
export interface MasterGame {
  id: string;
  white: MasterPlayer;
  black: MasterPlayer;
  result: string;
  event: string | null;
  site: string | null;
  date: string | null;
  moves: { san: string; uci: string; fen: string }[];
  ply: number | null;
}

/** A move with its Tablebase verdict, from the side that plays it. Distances are in half-moves, positive when winning. */
export interface TablebaseMove extends BoardMove {
  category: string;
  dtz: number | null;
  dtm: number | null;
  zeroing: boolean;
}

/** The Tablebase verdict for a position (category for the side to move); applicable only with up to 7 pieces. */
export interface Tablebase {
  applicable: boolean;
  available: boolean;
  message: string | null;
  category: string | null;
  outcome: string | null;
  dtz: number | null;
  dtm: number | null;
  /** Best first. */
  moves: TablebaseMove[];
}

export interface PositionSearchResult {
  /** Games that reached the position. */
  gameCount: number;
  /** The newest of them (at most 200). */
  games: PositionGame[];
}

/** The Owner's score over some Games, from the Owner's side. percent: a draw counts half. */
export interface Score {
  games: number;
  wins: number;
  draws: number;
  losses: number;
  percent: number;
}

export interface PlayerDetails {
  id: number;
  name: string;
  fideId: string | null;
  club: string | null;
}

/** A row of the Players list. lastDate: the newest dated Game against them. */
export interface PlayerRow extends PlayerDetails {
  score: Score;
  lastDate: string | null;
}

/** A rating at a (possibly partial) date, from one Game. */
export interface RatingPoint {
  date: string;
  elo: number;
  gameId: number;
}

export interface OpeningScore {
  eco: string | null;
  name: string | null;
  score: Score;
}

/** A Player's Dossier. ratingHistory leaves out unrated and undated Games; games are newest first. */
export interface Dossier {
  player: PlayerDetails;
  score: Score;
  ratingHistory: RatingPoint[];
  openings: OpeningScore[];
  games: GameSummary[];
}

/** The Owner's statistics over the Games the filters keep. */
export interface OwnerStatistics {
  score: Score;
  white: Score;
  black: Score;
  openings: (OpeningScore & { ownerColour: OwnerColour })[];
  /** The Owner's own Elo per Game; unrated and undated Games left out. */
  ratingHistory: RatingPoint[];
  /** Over rated Opponents only; null when there are none. */
  averageOpponentElo: number | null;
  ratedOpponentGames: number;
  /** Only Games with an Engine Assessment and a date. */
  accuracy: { date: string; accuracy: number; gameId: number }[];
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
  /** Every Player (matching the search) with the Owner's score against them. */
  playerOverview(search = ''): Observable<PlayerRow[]> {
    return this.http.get<PlayerRow[]>('/api/players/overview', { params: { search } });
  }
  /** filters: from, to (partial dates) and timeControl, as in the game list. */
  ownerStatistics(filters: Record<string, string>): Observable<OwnerStatistics> {
    return this.http.get<OwnerStatistics>('/api/statistics/owner', { params: filters });
  }
  dossier(playerId: number): Observable<Dossier> {
    return this.http.get<Dossier>(`/api/players/${playerId}/dossier`);
  }
  updatePlayer(playerId: number, details: { name: string; fideId: string; club: string }): Observable<Player> {
    return this.http.put<Player>(`/api/players/${playerId}`, details);
  }
  /** Moves every Game of mergeId to keptId, which takes the given details; mergeId is removed. */
  mergePlayers(keptId: number, merge: { mergeId: number; name: string; fideId: string; club: string }): Observable<Player> {
    return this.http.post<Player>(`/api/players/${keptId}/merge`, merge);
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
  searchPosition(fen: string): Observable<PositionSearchResult> {
    return this.http.get<PositionSearchResult>('/api/research/search', { params: { fen } });
  }

  /** The Masters source (Lichess) for a position. Unavailability comes back as data, not an error. */
  masters(fen: string): Observable<Masters> {
    return this.http.get<Masters>('/api/research/masters', { params: { fen } });
  }
  /** The Tablebase verdict for a position (Lichess). Unavailability comes back as data, not an error. */
  tablebase(fen: string): Observable<Tablebase> {
    return this.http.get<Tablebase>('/api/research/tablebase', { params: { fen } });
  }
  /** A Master Game from Lichess; with a FEN, ply says where that position occurs in it. */
  masterGame(id: string, fen?: string): Observable<MasterGame> {
    return this.http.get<MasterGame>(`/api/research/masters/games/${encodeURIComponent(id)}`, { params: fen ? { fen } : {} });
  }

  // Repertoire
  repertoire(side: OwnerColour): Observable<Repertoire> {
    return this.http.get<Repertoire>(`/api/repertoire/${side}`);
  }
  /** Plays the moves (UCI) from a node (null: the start); moves already there are followed, so lines merge. */
  addRepertoireLine(side: OwnerColour, parentId: number | null, moves: string[]): Observable<{ nodeId: number; repertoire: Repertoire }> {
    return this.http.post<{ nodeId: number; repertoire: Repertoire }>(`/api/repertoire/${side}/lines`, { parentId, moves });
  }
  deleteRepertoireFrom(side: OwnerColour, nodeId: number): Observable<Repertoire> {
    return this.http.delete<Repertoire>(`/api/repertoire/${side}/nodes/${nodeId}`);
  }
  deleteRepertoireVariation(side: OwnerColour, nodeId: number): Observable<Repertoire> {
    return this.http.delete<Repertoire>(`/api/repertoire/${side}/nodes/${nodeId}/variation`);
  }
  promoteRepertoire(side: OwnerColour, nodeId: number): Observable<Repertoire> {
    return this.http.post<Repertoire>(`/api/repertoire/${side}/nodes/${nodeId}/promote`, null);
  }
  setRepertoireComment(side: OwnerColour, nodeId: number, text: string): Observable<Repertoire> {
    return this.http.put<Repertoire>(`/api/repertoire/${side}/nodes/${nodeId}/comment`, { text });
  }

  // Settings
  getSettings(): Observable<Settings> {
    return this.http.get<Settings>('/api/settings');
  }
  saveSettings(settings: SaveSettings): Observable<Settings> {
    return this.http.put<Settings>('/api/settings', settings);
  }
  /** Stores the Lichess token (encrypted, outside the Collection); an empty one removes it. */
  saveLichessToken(token: string): Observable<{ hasLichessToken: boolean }> {
    return this.http.put<{ hasLichessToken: boolean }>('/api/settings/lichess-token', { token });
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
