/*
 * Request and response shapes of the endpoints the web app uses today. Hand-written until task 0.11
 * generates the client from the OpenAPI document; keep them identical to the API.
 */

export interface SetupStatus {
  setupRequired: boolean;
}

export interface SetupRequest {
  setupToken: string;
  email: string;
  displayName: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  status: 'signedIn' | 'twoFactorRequired';
}

export interface TwoFactorLoginRequest {
  code: string;
  isRecoveryCode: boolean;
}

export interface RegisterRequest {
  invitationToken: string;
  email: string;
  displayName: string;
  password: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

export type OrganizationRole = string;

export interface MembershipSummary {
  id: string;
  slug: string;
  name: string;
  role: OrganizationRole;
}

export interface SessionUser {
  id: string;
  email: string;
  displayName: string;
  isInstanceAdmin: boolean;
  twoFactorEnabled: boolean;
}

/** `GET /api/v1/auth/session` (master plan 10.2); 401 when signed out. Also sets the XSRF-TOKEN cookie. */
export interface SessionResponse {
  user: SessionUser;
  organizations: MembershipSummary[];
}

/** The signed-in person as the pages use it: the user with their memberships. */
export type Me = SessionUser & { organizations: MembershipSummary[] };

export interface TwoFactorSetup {
  sharedKey: string;
  authenticatorUri: string;
}

export interface EnableTwoFactorRequest {
  code: string;
}

export interface EnableTwoFactorResponse {
  recoveryCodes: string[];
}
