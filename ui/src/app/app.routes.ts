import { Routes } from '@angular/router';
import { GameList } from './games/game-list';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'games' },
  { path: 'games', component: GameList },
  { path: '**', redirectTo: 'games' },
];
