import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'works' },
  {
    path: 'works',
    loadComponent: () =>
      import('./pages/works-list/works-list.component').then((m) => m.WorksListComponent),
  },
  {
    path: 'jobs',
    loadComponent: () =>
      import('./pages/jobs-list/jobs-list.component').then((m) => m.JobsListComponent),
  },
  {
    path: 'works/:id',
    loadComponent: () =>
      import('./pages/work-layout/work-layout.component').then((m) => m.WorkLayoutComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'analysis' },
      {
        path: 'ingest',
        pathMatch: 'full',
        redirectTo: 'analysis',
      },
      {
        path: 'analysis',
        loadComponent: () =>
          import('./pages/work-ingest/work-ingest.component').then((m) => m.WorkIngestComponent),
      },
      {
        path: 'timeline',
        loadComponent: () =>
          import('./pages/work-timeline/work-timeline.component').then((m) => m.WorkTimelineComponent),
      },
      {
        path: 'characters',
        loadComponent: () =>
          import('./pages/work-characters/work-characters.component').then((m) => m.WorkCharactersComponent),
      },
      {
        path: 'audio',
        loadComponent: () =>
          import('./pages/work-audio/work-audio.component').then((m) => m.WorkAudioComponent),
      },
      {
        path: 'profiles',
        loadComponent: () =>
          import('./pages/work-profiles/work-profiles.component').then((m) => m.WorkProfilesComponent),
      },
    ],
  },
  { path: '**', redirectTo: 'works' },
];
