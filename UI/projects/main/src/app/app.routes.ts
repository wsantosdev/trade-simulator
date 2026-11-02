import { Routes } from '@angular/router';

const suffix = ' | Trade Simulator';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'account' },
  {
    path: 'account',
    title: `Conta Corrente${suffix}`,
    loadComponent: () => import('./account/account-page').then((m) => m.AccountPage),
  },
  {
    path: 'orders',
    title: `Ordens${suffix}`,
    loadComponent: () => import('./orders/orders-page').then((m) => m.OrdersPage),
  },
  {
    path: 'portfolio',
    title: `Portfólio${suffix}`,
    loadComponent: () => import('./portfolio/portfolio-page').then((m) => m.PortfolioPage),
  },
  {
    path: 'quotes',
    title: `Cotações${suffix}`,
    loadComponent: () => import('./quotes/quotes-page').then((m) => m.QuotesPage),
  },
  {
    path: 'offerbook',
    title: `Livro de Ofertas${suffix}`,
    loadComponent: () => import('./offerbook/offerbook-page').then((m) => m.OfferBookPage),
  },
  { path: '**', redirectTo: 'account' },
];
