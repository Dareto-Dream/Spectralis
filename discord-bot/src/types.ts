export interface GuildLink {
  guildId: string;
  channelId: string;
  roomId: string;
  botToken: string | null;
  linkedBy: string;
  linkedAt: string;
}

export interface SqFeeSettings {
  enabled: boolean;
  amount: number;
  currency: string;
}

export interface SqPublicSettings {
  requireApproval: boolean;
  allowDuplicates: boolean;
  allowLinkSubmissions: boolean;
  maxQueueLength: number;
  maxSubmissionsPerPerson: number;
  skipBypassesLimit: boolean;
  queueEntryFee: SqFeeSettings;
  skip: SqFeeSettings;
  superSkip: SqFeeSettings;
}

/** Shape returned by the public (non-owner) GET /rooms/{id}. */
export interface SqPublicRoom {
  roomId: string;
  enabled: boolean;
  acceptingSubmissions: boolean;
  settings: SqPublicSettings;
  stripePublishableKey: string | null;
  activeCount: number;
  queueLength: number;
  nowPlayingId: string | null;
  nowPlayingTier: string | null;
  nowPlayingTitle: string | null;
  nowPlayingArtist: string | null;
}

export interface SqSubmitResponse {
  submissionId: string;
  status: string;
  position: number | null;
  waitEstimateLowMins: number | null;
  waitEstimateHighMins: number | null;
  clientSecret?: string;
}

export interface SqPromoteResponse {
  submissionId: string;
  tier: string;
  position: number | null;
  waitEstimateLowMins: number | null;
  waitEstimateHighMins: number | null;
  clientSecret?: string;
}
