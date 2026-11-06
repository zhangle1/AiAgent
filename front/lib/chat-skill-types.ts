export type ChatSkillReference = { id: string; version: string };
export type ChatSkill = ChatSkillReference & {
  name: string; category: string; description: string; example: string;
  multi_turn: boolean; invocation_policy: "explicit_only"; prompt: string;
  source: { repository: string; commit: string; path: string; url: string; license: string };
};
