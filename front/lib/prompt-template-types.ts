export type PromptTemplateStage = "requirements" | "design" | "development" | "code-understanding" | "testing" | "delivery";
export type PromptTemplateVisibility = "personal" | "project" | "team";
export type PromptTemplateVariableType = "text" | "textarea" | "select";

export type PromptTemplateVariable = {
  key: string;
  label: string;
  type: PromptTemplateVariableType;
  required: boolean;
  default_value?: string | null;
  description?: string | null;
  options: string[];
};

export type PromptTemplate = {
  id: number;
  name: string;
  description: string;
  stage: PromptTemplateStage;
  tags: string[];
  body: string;
  variables: PromptTemplateVariable[];
  project_id?: number | null;
  visibility: PromptTemplateVisibility;
  author_name: string;
  created_by_me: boolean;
  liked_by_me: boolean;
  favorited_by_me: boolean;
  like_count: number;
  use_count: number;
  created_at: string;
  updated_at: string;
};

export type PromptTemplateSaveRequest = {
  name: string;
  description: string;
  stage: PromptTemplateStage;
  tags: string[];
  body: string;
  variables: PromptTemplateVariable[];
  project_id?: number | null;
  visibility: PromptTemplateVisibility;
};

export type PromptTemplateUseResult = {
  template: PromptTemplate;
  project_id?: number | null;
  rendered_content: string;
};
