export type PushChannel = {
  id: number;
  name: string;
  provider_type: "dingtalk_custom_webhook";
  enabled: boolean;
  has_access_token: boolean;
  has_sign_secret: boolean;
  robot_code?: string | null;
  stream_client_id?: string | null;
  has_stream_client_secret: boolean;
  stream_enabled: boolean;
  stream_status?: string | null;
  stream_last_error_code?: string | null;
  stream_connected_at?: string | null;
  stream_last_frame_at?: string | null;
  stream_last_callback_at?: string | null;
  stream_reconnect_attempt?: number | null;
  stream_next_reconnect_at?: string | null;
  project_ids: number[];
  project_names: string[];
  last_tested_at?: string | null;
  last_test_status?: string | null;
  last_error_code?: string | null;
  updated_at?: string | null;
};

export type PushChannelInput = {
  name: string; webhook_url?: string; sign_secret?: string; clear_sign_secret?: boolean;
  robot_code?: string; stream_client_id?: string; stream_client_secret?: string; clear_stream_client_secret?: boolean;
  stream_enabled: boolean; project_ids: number[]; enabled: boolean;
};
export type PushChannelTestResult = { status: "success" | "failed"; summary: string; tested_at: string };

export type ProjectPushBinding = {
  id: number;
  project_id: number;
  project_name: string;
  push_channel_id: number;
  push_channel_name: string;
  provider_type: "dingtalk_custom_webhook";
  trigger_type: "git_push_succeeded";
  enabled: boolean;
  last_sent_at?: string | null;
  last_status?: string | null;
  last_error_code?: string | null;
};

export type PushAudit = { id: number; occurred_at?: string | null; action: string; outcome: string; project_id?: number | null; push_channel_id?: number | null; metadata?: string | null };
