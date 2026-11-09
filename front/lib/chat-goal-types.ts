export type ChatGoalRequest = {
  action: "start" | "resume";
  objective?: string;
  token_budget?: number;
  expected_version?: number;
};

export type ChatGoal = {
  id: string;
  objective: string;
  status: string;
  tokenBudget: number | null;
  tokensUsed: number;
  timeUsedSeconds: number;
  version: number;
  stopReason: string | null;
  running: boolean;
};
