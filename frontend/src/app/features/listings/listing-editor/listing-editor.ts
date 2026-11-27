import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, finalize, forkJoin, map, of, switchMap, tap, throwError } from 'rxjs';
import { DraftAnalysisPanel } from '../draft-analysis/draft-analysis-panel';
import { ListingsApi } from '../data-access/listings-api';
import { Category, ListingType } from '../data-access/listing.models';
import { apiError } from '../../../shared/api-error';

@Component({
  imports: [FormsModule, RouterLink, DraftAnalysisPanel],
  templateUrl: './listing-editor.html',
  styleUrl: './listing-editor.scss'
})
export class ListingEditor {
  private readonly api = inject(ListingsApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly analysis = viewChild(DraftAnalysisPanel);
  readonly analysisPhase = computed(() => this.analysis()?.phase() ?? 'analyzing');
  readonly existingId = inject(ActivatedRoute).snapshot.paramMap.get('id');
  readonly savedTraits = signal<string[]>([]);
  private originalCategory = '';
  private readonly categoryOverride = signal<string | null>(null);
  readonly customCategory = signal<string | null>(null);
  private readonly groupOverride = signal<string | null>(null);
  readonly suggestedObjects = computed(() => this.analysis()?.snapshot()?.suggestions?.objects.filter(item => item.objectType?.trim()) ?? []);
  readonly suggestedCategory = computed(() => this.suggestedObjects()[0]?.objectType?.trim().toLowerCase() ?? '');
  readonly selectedCategory = computed(() => this.customCategory() !== null ? '' :
    (this.categoryOverride() ?? this.suggestedCategory()).trim().toLowerCase());
  readonly categoryName = computed(() => this.customCategory() ?? this.selectedCategory());
  readonly categoryNames = computed(() => [...new Set([
    ...this.categories().map(category => category.name),
    ...this.suggestedObjects().map(item => item.objectType!.trim()),
    this.categoryOverride() ?? ''
  ].map(name => name.trim().toLowerCase()).filter(Boolean))].sort());
  readonly catalogGroup = computed(() => this.categories().find(category => category.name === this.categoryName().trim().toLowerCase())?.broadClass);
  readonly broadClass = computed(() => this.catalogGroup() ?? this.groupOverride() ??
    this.suggestedObjects().find(item => item.objectType?.trim().toLowerCase() === this.categoryName().trim().toLowerCase())?.category ?? '');
  private savedDate: string | null = null;
  readonly draft = signal<{ listingId: string; revision: number; photoUrl: string | null } | null>(null);
  readonly selectedTraits = signal<string[]>([]);
  readonly categories = signal<Category[]>([]);
  readonly error = signal('');
  readonly busy = signal(false);
  type: ListingType = 'Lost';
  photo: File | null = null;
  title = '';
  description = '';
  location = '';
  occurredOn = '';
  chooseCategory(name: string) {
    if (!name) {
      this.customCategory.set(this.categoryName());
      return;
    }
    this.customCategory.set(null);
    this.categoryOverride.set(name);
    this.groupOverride.set(null);
  }

  editCategory(name: string) {
    this.customCategory.set(name);
    this.groupOverride.set(null);
  }

  setBroadClass(group: string) { this.groupOverride.set(group); }

  constructor() {
    this.destroyRef.onDestroy(() => {
      const draft = this.draft();
      if (draft?.photoUrl) URL.revokeObjectURL(draft.photoUrl);
    });
    if (this.existingId) this.loadDraft(this.existingId);
  }

  loadDraft(id: string) {
    if (this.busy()) return;
    this.error.set('');
    this.busy.set(true);
    forkJoin({ details: this.api.draft(id), categories: this.api.categories() }).pipe(
      switchMap(({ details, categories }) => (details.photo ? this.api.draftPhoto(id).pipe(
        catchError(e => e instanceof HttpErrorResponse && e.status === 404 ? of(null) : throwError(() => e))
      ) : of(null)).pipe(
        map(photo => ({ details, categories, photo }))
      )),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.busy.set(false))
    ).subscribe({
      next: ({ details, categories, photo }) => {
        this.categories.set(categories);
        this.type = details.type;
        this.title = details.title;
        this.description = details.description;
        this.location = details.location;
        this.savedDate = details.occurredOn;
        this.occurredOn = details.occurredOn ?? '';
        this.originalCategory = details.categories[0]?.name ?? '';
        this.categoryOverride.set(details.draftCategoryName ?? (this.originalCategory || null));
        this.groupOverride.set(details.draftBroadClass);
        this.savedTraits.set(details.traits);
        this.selectedTraits.set(details.traits);
        this.draft.set({ listingId: id, revision: details.revision, photoUrl: photo && URL.createObjectURL(photo) });
      },
      error: e => this.error.set(apiError(e))
    });
  }

  pickPhoto(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.photo = file && file.size > 0 && file.size <= 10 * 1024 * 1024 ? file : null;
    this.error.set(file && !this.photo ? 'Choose a nonempty image of at most 10 MiB.' : '');
  }

  create() {
    const photo = this.photo;
    if (!photo || this.busy() || this.draft()) return;
    this.busy.set(true);
    this.error.set('');
    this.api.categories().pipe(switchMap(categories => {
      this.categories.set(categories);
      return this.api.createDraft(this.type, photo);
    }), takeUntilDestroyed(this.destroyRef), finalize(() => this.busy.set(false))).subscribe({
      next: draft => this.draft.set({ ...draft, photoUrl: URL.createObjectURL(photo) }),
      error: e => this.error.set(apiError(e))
    });
  }

  save(action: 'draft' | 'publish') {
    const draft = this.draft();
    if (!draft || this.busy() || this.analysisPhase() !== 'ready') return;
    if (this.savedDate && !this.occurredOn) {
      this.error.set('Choose a date to replace the saved date.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    const categoryName = this.categoryName().trim();
    const keepAssignments = this.originalCategory !== '' && categoryName === this.originalCategory && this.categoryOverride() === this.originalCategory && !this.groupOverride();
    const fields = { title: this.title, description: this.description, location: this.location,
      occurredOn: this.occurredOn || null,
      categoryName: keepAssignments ? undefined : categoryName,
      broadClass: keepAssignments || !categoryName ? undefined : this.broadClass().trim() || undefined,
      traits: this.selectedTraits() };
    this.api.updateDraft(draft.listingId, draft.revision, fields).pipe(
      tap(updated => {
        if (!keepAssignments) this.originalCategory = '';
        this.savedDate = this.occurredOn || null;
        this.draft.set({ ...draft, revision: updated.revision });
      }),
      switchMap(updated => action === 'publish' ? this.api.publish(draft.listingId, updated.revision) : of(null)),
      finalize(() => this.busy.set(false))
    ).subscribe({ next: () => void this.router.navigate(action === 'publish' ? ['/listings', draft.listingId] : ['/profile']), error: e => this.error.set(apiError(e)) });
  }
}
