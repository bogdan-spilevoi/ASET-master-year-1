export type AnalysisKind = 'traits' | 'embedding';

export interface AnalysisOperation {
  state: 'NotRequested' | 'Pending' | 'Processing' | 'Ready' | 'Failed';
  errorCode: string | null;
}

export interface DetectedObject {
  category: string | null;
  objectType: string | null;
  colors: string[];
  material: string | null;
  shape: string | null;
  pattern: string | null;
  visibleText: string[];
  distinctiveFeatures: string[];
  tags: string[];
}

export interface DraftAnalysis {
  traits: AnalysisOperation;
  embedding: AnalysisOperation;
  suggestions: { objects: DetectedObject[]; uncertainties: string[] } | null;
}
