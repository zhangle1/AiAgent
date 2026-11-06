import type { ChatSkill } from "./chat-skill-types";

export function skillCommand(value: string, skills: ChatSkill[]): ChatSkill | undefined {
  const match = /^\/([a-z][a-z0-9-]*)(?=\s|$)/.exec(value);
  return match ? skills.find((skill) => skill.id === match[1]) : undefined;
}

export function filterChatSkills(skills: ChatSkill[], query: string): ChatSkill[] {
  const key = query.replace(/^skills?\s*/, "").toLocaleLowerCase();
  return skills.filter((skill) => `${skill.id} ${skill.name} ${skill.category} ${skill.description}`.toLocaleLowerCase().includes(key));
}
