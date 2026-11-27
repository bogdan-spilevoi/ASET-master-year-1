export type ListingType = 'Lost' | 'Found';

export interface Category {
  id: string;
  name: string;
  broadClass?: string | null;
}

interface ListingPhoto {
  id: string;
  revision: number;
}

export interface Listing {
  id: string;
  type: ListingType;
  title: string;
  location: string;
  occurredOn: string;
  categories: Category[];
  photo: ListingPhoto | null;
}

export interface ListingDetails extends Listing {
  description: string;
  traits: string[];
  colour: string | null;
}

export interface DraftDetails extends Omit<ListingDetails, 'occurredOn'> {
  draftCategoryName: string | null;
  draftBroadClass: string | null;
  revision: number;
  occurredOn: string | null;
}

export interface OwnedListing {
  id: string;
  title: string;
  location: string;
  occurredOn: string | null;
  type: ListingType;
  state: 'Draft' | 'Open' | 'Closed';
  closureReason: 'Withdrawn' | 'Recovered' | null;
}

export interface ListingPage<T = Listing> {
  items: T[];
  currentPage: number;
  pageSize: number;
  totalCount: number;
}
