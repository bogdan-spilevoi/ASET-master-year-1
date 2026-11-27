import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { AnalysisKind, DraftAnalysis } from './draft-analysis.models';
import { Category, DraftDetails, ListingDetails, ListingPage, ListingType, OwnedListing } from './listing.models';

@Injectable({ providedIn: 'root' })
export class ListingsApi {
  private readonly http = inject(HttpClient);

  feed(type: ListingType | '', pageIndex: number, categoryId: string) {
    return this.http.get<ListingPage>('/api/listings', {
      params: { pageIndex, pageSize: 12, ...(type && { type }), ...(categoryId && { categoryId }) }
    });
  }

  mine(filters: { state?: OwnedListing['state']; type?: ListingType; closureReason?: 'Recovered' | 'Withdrawn' }, pageIndex: number) {
    return this.http.get<ListingPage<OwnedListing>>('/api/listings/mine', {
      params: { ...filters, pageIndex, pageSize: 12 }
    });
  }

  categories() {
    return this.http.get<Category[]>('/api/categories');
  }

  details(id: string) {
    return this.http.get<ListingDetails>('/api/listings/' + encodeURIComponent(id));
  }

  draft(id: string) {
    return this.http.get<DraftDetails>('/api/listings/drafts/' + encodeURIComponent(id));
  }

  draftPhoto(id: string) {
    return this.http.get('/api/listings/drafts/' + encodeURIComponent(id) + '/photo', { responseType: 'blob' });
  }

  deleteDraft(id: string) {
    return this.http.delete<void>('/api/listings/drafts/' + encodeURIComponent(id));
  }

  requestAnalysis(id: string, kind: AnalysisKind) {
    return this.http.post<unknown>('/api/listings/drafts/' + encodeURIComponent(id) + '/' + kind, null);
  }

  analysis(id: string) {
    return this.http.get<DraftAnalysis>('/api/listings/drafts/' + encodeURIComponent(id) + '/analysis');
  }

  updateDraft(id: string, expectedRevision: number, fields: { title: string; description: string; location: string; occurredOn: string | null; categoryName?: string; broadClass?: string; traits: string[] }) {
    return this.http.patch<{ revision: number }>('/api/listings/drafts/' + encodeURIComponent(id), { expectedRevision, ...fields });
  }

  publish(id: string, expectedRevision: number) {
    return this.http.post<void>('/api/listings/drafts/' + encodeURIComponent(id) + '/publish', { expectedRevision });
  }

  createDraft(type: ListingType, photo: File) {
    const form = new FormData();
    form.set('type', type);
    form.set('photo', photo);
    return this.http.post<{ listingId: string; revision: number }>('/api/listings/drafts', form);
  }
}
