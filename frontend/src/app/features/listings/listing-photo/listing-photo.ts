import { Component, computed, input, signal } from '@angular/core';

@Component({
  selector: 'sl-listing-photo',
  template: `
    @if (failedUrl() !== url()) {
      <img [src]="url()" [alt]="preview() ? '' : title()" [class.preview]="preview()"
           [attr.loading]="preview() ? 'lazy' : 'eager'" decoding="async" (error)="failedUrl.set(url())">
    } @else if (!preview()) { <p>Photo unavailable.</p> }
  `,
  styles: `
    :host { display: block; }
    img { display: block; width: 100%; height: auto; max-height: 75vh; object-fit: contain;
          border-radius: 8px; background: #f2f5f0; margin-bottom: 1rem; }
    img.preview { aspect-ratio: 4 / 3; max-height: 14rem; object-fit: cover; }
  `
})
export class ListingPhoto {
  readonly listingId = input.required<string>();
  readonly title = input.required<string>();
  readonly preview = input(false);
  readonly url = computed(() => '/api/listings/' + encodeURIComponent(this.listingId()) + '/photo' +
    (this.preview() ? '?preview=true' : ''));
  readonly failedUrl = signal('');
}
