export type Integer = number | string;
export type RefillState = 'Open' | 'Reserved' | 'Purchasing' | 'NeedsReview' | 'Completed';
export type AttemptState =
  | 'Invited'
  | 'Declined'
  | 'Ignored'
  | 'Reserved'
  | 'Released'
  | 'Expired'
  | 'Purchasing'
  | 'Completed';
export type ClaimState =
  | 'Draft'
  | 'SubmissionPending'
  | 'PendingHR'
  | 'SubmissionFailed'
  | 'SubmissionUncertain'
  | 'Approved'
  | 'Rejected'
  | 'Reimbursed';
export interface Office {
  id: number;
  name: string;
}
export interface Employee {
  id: number;
  officeId: number;
  name: string;
  volunteers: boolean;
}
export interface Refill {
  id: number;
  officeId: number;
  state: RefillState;
  createdAt: Integer;
  completedAt?: Integer | null;
  outcome?: string | null;
  beingHandled?: boolean;
}
export interface Invitation {
  refillRequestId: number;
  state: AttemptState;
  invitedAt: Integer;
  invitationExpiresAt: Integer;
  reservationExpiresAt: Integer | null;
}
export interface Budget {
  officeId: number;
  month: string;
  currency: string;
  limitMinor: Integer;
  releasedMinor: Integer;
  spentMinor: Integer;
  reservedMinor: Integer;
  availableMinor: Integer;
}
export interface BudgetResult {
  success: boolean;
  error: string | null;
  allowanceWarning: boolean;
  requiresHrReview: boolean;
}
export interface ActionBody {
  employeeId: number;
  estimatedAmountMinor?: number;
  actualAmountMinor?: number;
  currency?: string;
}
export interface Purchase {
  id: number;
  refillAssignmentId: number;
  officeId: number;
  month: string;
  estimateMinor: Integer;
  actualMinor: Integer | null;
  cancelled: boolean;
  requiresHrReview: boolean;
  authorizedAt: Integer;
  resolvedAt: Integer | null;
}
export interface Claim {
  id: number;
  purchaseCommitmentId: number;
  employeeId: number;
  amountMinor: Integer;
  currency: string;
  description: string;
  receiptReference: string;
  state: ClaimState;
  requiresHrReview: boolean;
  createdAt: Integer;
  updatedAt: Integer;
}
export interface Spending {
  officeId: number;
  office: string;
  month: string;
  currency: string;
  spentMinor: Integer;
  outstandingMinor: Integer;
}
export interface Rate {
  month: string;
  numerator: Integer;
  denominator: Integer;
  sourceCurrency: string;
  targetCurrency: string;
  recordedAt: Integer;
}
export interface Consolidated {
  month: string;
  currency: string;
  spentMinor: Integer;
  outstandingMinor: Integer;
  exchangeRate: Rate;
  nativeTotals: Spending[];
}
export interface Contribution {
  id: number;
  refillRequestId: number;
  employeeId: number;
  kind: string;
  points: number;
  earnedAt: Integer;
}
export interface ContributionTotal {
  employeeId: number;
  name: string;
  points: number;
  reports: number;
  inspections: number;
  refills: number;
}
