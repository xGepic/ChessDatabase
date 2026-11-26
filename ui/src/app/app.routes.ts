import { Routes } from '@angular/router';
import { GameList } from './games/game-list';
import { NewGame } from './games/new-game';
import { GameReview } from './review/game-review';
import { ImportPage } from './import/import-page';
import { ReviewPage } from './import/review-page';
import { SettingsPage } from './settings/settings-page';
import { PositionSearch } from './research/position-search';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'games' },
  { path: 'games', component: GameList, title: 'Games · Chess Database' },
  { path: 'games/new', component: NewGame, title: 'New Game · Chess Database' },
  { path: 'games/:id', component: GameReview, title: 'Game · Chess Database' },
  { path: 'import', component: ImportPage, title: 'Import · Chess Database' },
  { path: 'import/review', component: ReviewPage, title: 'Review imports · Chess Database' },
  { path: 'search', component: PositionSearch, title: 'Position Search · Chess Database' },
  { path: 'settings', component: SettingsPage, title: 'Settings · Chess Database' },
  { path: '**', redirectTo: 'games' },
];
