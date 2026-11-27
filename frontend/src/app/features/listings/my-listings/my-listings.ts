import { AsyncPipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { BehaviorSubject, catchError, finalize, map, of, startWith, switchMap } from 'rxjs';
import { apiError } from '../../../shared/api-error';
import { OwnedListing } from '../data-access/listing.models';
import { ListingsApi } from '../data-access/listings-api';

const filters = {
  All: {},
  Draft: { state: 'Draft' },
  Open: { state: 'Open' },
  Found: { type: 'Found' },
  Closed: { state: 'Closed' },
  Recovered: { closureReason: 'Recovered' }
} as const;
type Filter = keyof typeof filters;

@Component({
  selector: 'sl-my-listings',
  imports: [AsyncPipe, RouterLink],
  templateUrl: './my-listings.html',
  styleUrl: './my-listings.scss'
})
export class MyListings {
  private readonly api = inject(ListingsApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly deletingId = signal<string | null>(null);
  readonly deleteError = signal('');
  readonly labels = Object.keys(filters) as Filter[];
  private readonly request = new BehaviorSubject<{ filter: Filter; index: number }>({ filter: 'All', index: 0 });
  readonly results = this.request.pipe(switchMap(({ filter, index }) =>
    this.api.mine(filters[filter], index).pipe(
      map(page => ({ page, error: '' })),
      catchError(e => of({ page: null, error: apiError(e) })),
      startWith({ page: null, error: '' })
    )
  ));

  get selected(): Filter { return this.request.value.filter; }
  selectFilter(value: string) {
    const filter = this.labels.find(label => label === value);
    if (filter) this.show(filter);
  }
  show(filter: Filter, index = 0) { this.request.next({ filter, index }); }
  deleteDraft(item: OwnedListing, itemsOnPage: number) {
    if (item.state !== 'Draft' || this.deletingId() || !window.confirm('Delete “' + (item.title || 'Untitled draft') + '” and its photo?')) return;
    const pageAtDelete = this.request.value;
    this.deletingId.set(item.id);
    this.deleteError.set('');
    this.api.deleteDraft(item.id).pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.deletingId.set(null))
    ).subscribe({
      next: () => {
        if (this.request.value === pageAtDelete && itemsOnPage === 1 && pageAtDelete.index > 0) {
          this.show(pageAtDelete.filter, pageAtDelete.index - 1);
        } else {
          this.retry();
        }
      },
      error: e => this.deleteError.set(apiError(e))
    });
  }

  retry() { this.request.next(this.request.value); }
}
