import { Routes } from '@angular/router';
import { GameList } from './games/game-list';
import { NewGame } from './games/new-game';
import { GameReview } from './review/game-review';
import { ImportPage } from './import/import-page';
import { ReviewPage } from './import/review-page';
import { SettingsPage } from './settings/settings-page';
import { PositionSearch } from './research/position-search';
import { MasterGamePage } from './research/master-game';
import { PlayersPage } from './people/players-page';
import { DossierPage } from './people/dossier-page';
import { OwnerStatisticsPage } from './people/owner-statistics-page';
import { RepertoirePage } from './training/repertoire-page';
import { DeviationsPage } from './training/deviations-page';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'games' },
  { path: 'games', component: GameList, title: 'Games · Chess Database' },
  { path: 'games/new', component: NewGame, title: 'New Game · Chess Database' },
  { path: 'games/:id', component: GameReview, title: 'Game · Chess Database' },
  { path: 'import', component: ImportPage, title: 'Import · Chess Database' },
  { path: 'import/review', component: ReviewPage, title: 'Review imports · Chess Database' },
  { path: 'repertoire', pathMatch: 'full', redirectTo: 'repertoire/white' },
  { path: 'repertoire/deviations', component: DeviationsPage, title: 'My Deviations · Chess Database' },
  { path: 'repertoire/:side', component: RepertoirePage, title: 'Repertoire · Chess Database' },
  { path: 'statistics', component: OwnerStatisticsPage, title: 'My statistics · Chess Database' },
  { path: 'players', component: PlayersPage, title: 'Players · Chess Database' },
  { path: 'players/:id', component: DossierPage, title: 'Dossier · Chess Database' },
  { path: 'masters/:id', component: MasterGamePage, title: 'Master Game · Chess Database' },
  { path: 'search', component: PositionSearch, title: 'Position Search · Chess Database' },
  { path: 'settings', component: SettingsPage, title: 'Settings · Chess Database' },
  { path: '**', redirectTo: 'games' },
];
