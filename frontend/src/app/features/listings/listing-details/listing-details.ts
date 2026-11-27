import { Component, inject } from '@angular/core';
import { AsyncPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { ListingPhoto } from '../listing-photo/listing-photo';
import { ListingsApi } from '../data-access/listings-api';
import { apiError } from '../../../shared/api-error';

@Component({
  imports: [RouterLink, AsyncPipe, ListingPhoto],
  template: `
    <article class="form-page"><a routerLink="/">← Item board</a>
      @if (result | async; as state) {
        @if (state.item; as item) { <p class="tag" [class.found]="item.type === 'Found'">{{ item.type }}</p>
          <h1>{{ item.title }}</h1><p>{{ item.location }} · {{ item.occurredOn }}</p>
          @if (item.photo) { <sl-listing-photo [listingId]="item.id" [title]="item.title" /> }
          <p>{{ item.description }}</p>
          @if (item.colour) { <p>Colour · {{ item.colour }}</p> }
          <div class="chips">@for (category of item.categories; track category.id) { <span>{{ category.name }}</span> }
            @for (trait of item.traits; track $index) { <span>{{ trait }}</span> }</div>
        } @else { <p role="alert">{{ state.error }}</p> }
      } @else { <p role="status">Loading report…</p> }
    </article>
  `
})
export class ListingDetails {
  private readonly api = inject(ListingsApi);
  readonly result = inject(ActivatedRoute).paramMap.pipe(switchMap(params =>
    this.api.details(params.get('id')!).pipe(map(item => ({ item, error: '' })),
      catchError(error => of({ item: null, error: apiError(error) })))));
}
