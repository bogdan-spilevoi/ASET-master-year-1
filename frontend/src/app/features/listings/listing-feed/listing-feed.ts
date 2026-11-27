import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { BehaviorSubject, catchError, of, switchMap } from 'rxjs';
import { ListingPhoto } from '../listing-photo/listing-photo';
import { ListingsApi } from '../data-access/listings-api';
import { Category, ListingPage, ListingType } from '../data-access/listing.models';
import { apiError } from '../../../shared/api-error';

@Component({
  imports: [RouterLink, ListingPhoto],
  template: `
    <section class="board"><div class="heading"><h1>Campus lost &amp; found</h1><p>Browse reports from your campus.</p></div>
      <div class="tools"><div class="tabs" aria-label="Filter by report type">
        @for (filter of filters; track filter.value) {
          <button [class.selected]="filter.value === type" [attr.aria-pressed]="filter.value === type" (click)="select(filter.value)">{{ filter.label }}</button>
        }
      </div><label class="category">Category<select #category (change)="filterCategory(category.value)">
        <option value="">All categories</option>@for (c of categories(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
      </select></label></div>
      @if (categoryError()) { <p role="alert">{{ categoryError() }}</p> }
      @if (error()) { <p role="alert">{{ error() }} <button (click)="load(index)">Retry</button></p> }
      @else if (page(); as data) {
        <p class="count">{{ data.totalCount }} {{ data.totalCount === 1 ? 'report' : 'reports' }}</p>
        <div class="grid">@for (item of data.items; track item.id) {
          <a class="card" [routerLink]="['/listings', item.id]">
          @if (item.photo) { <sl-listing-photo [listingId]="item.id" [title]="item.title" [preview]="true" /> }
          <span class="tag" [class.found]="item.type === 'Found'">{{ item.type }}</span>
          <h2>{{ item.title }}</h2><p>{{ item.location }}</p><time>{{ item.occurredOn }}</time></a>
        }</div>
        @if (!data.items.length) { <div class="empty"><h2>No reports here yet</h2><p>Try a different filter or report an item.</p></div> }
        @if (data.totalCount > data.pageSize) { <div class="pager"><button [disabled]="index === 0" (click)="load(index - 1)">Previous</button>
          <span>Page {{ index + 1 }}</span><button [disabled]="(index + 1) * data.pageSize >= data.totalCount" (click)="load(index + 1)">Next</button></div> }
      } @else { <p role="status">Loading reports…</p> }
    </section>
  `
})
export class ListingFeed {
  private readonly api = inject(ListingsApi);
  private readonly requests = new BehaviorSubject<void>(undefined);
  readonly filters = [{ value: '', label: 'All reports' }, { value: 'Lost', label: 'Lost' }, { value: 'Found', label: 'Found' }] as const;
  readonly page = signal<ListingPage | null>(null);
  readonly categories = signal<Category[]>([]);
  readonly error = signal('');
  readonly categoryError = signal('');
  type: ListingType | '' = '';
  categoryId = '';
  index = 0;

  constructor() {
    this.api.categories().subscribe({ next: value => this.categories.set(value), error: e => this.categoryError.set(apiError(e)) });
    this.requests.pipe(switchMap(() => this.api.feed(this.type, this.index, this.categoryId)
      .pipe(catchError(e => { this.error.set(apiError(e)); return of(null); }))), takeUntilDestroyed())
      .subscribe(value => this.page.set(value));
  }

  select(type: ListingType | '') { this.type = type; this.load(0); }
  filterCategory(id: string) { this.categoryId = id; this.load(0); }
  load(index: number) {
    this.index = index;
    this.error.set('');
    this.page.set(null);
    this.requests.next();
  }
}
