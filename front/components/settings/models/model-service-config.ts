import { AudioLines, Brain, Clapperboard, Database, Image as ImageIcon, Mic, Search, type LucideIcon } from "lucide-react";
import type { ServiceName } from "@/lib/settings-types";
import type { TranslationKey } from "@/i18n/dictionaries";

export type ModelServiceConfig = {
  key: ServiceName;
  title: string;
  titleKey: TranslationKey;
  shortTitle: string;
  subtitle: string;
  subtitleKey: TranslationKey;
  icon: LucideIcon;
  href: string;
  tileClass: string;
  modelFields: Array<"model" | "dimension" | "context_window" | "voice" | "response_format" | "language" | "size" | "quality" | "aspect_ratio" | "duration" | "resolution">;
};

export const MODEL_SERVICE_CONFIGS: ModelServiceConfig[] = [
  {
    key: "llm",
    title: "LLM",
    titleKey: "models.llm",
    shortTitle: "LLM",
    subtitle: "Language model providers and active profile.",
    subtitleKey: "models.llmSubtitle",
    href: "/settings/models/llm",
    tileClass: "bg-violet-50 text-violet-600",
    icon: Brain,
    modelFields: ["model", "context_window"],
  },
  {
    key: "embedding",
    title: "Embedding",
    titleKey: "models.embedding",
    shortTitle: "Embedding",
    subtitle: "Embedding model providers and dimensions.",
    subtitleKey: "models.embeddingSubtitle",
    href: "/settings/models/embedding",
    tileClass: "bg-emerald-50 text-emerald-600",
    icon: Database,
    modelFields: ["model", "dimension"],
  },
  {
    key: "search",
    title: "Search",
    titleKey: "models.search",
    shortTitle: "Search",
    subtitle: "Web search providers.",
    subtitleKey: "models.searchSubtitle",
    href: "/settings/models/search",
    tileClass: "bg-amber-50 text-amber-600",
    icon: Search,
    modelFields: [],
  },
  {
    key: "tts",
    title: "Text-to-Speech",
    titleKey: "models.tts",
    shortTitle: "TTS",
    subtitle: "Text-to-speech for reading replies aloud.",
    subtitleKey: "models.ttsSubtitle",
    href: "/settings/models/tts",
    tileClass: "bg-rose-50 text-rose-600",
    icon: AudioLines,
    modelFields: ["model", "voice", "response_format"],
  },
  {
    key: "stt",
    title: "Speech-to-Text",
    titleKey: "models.stt",
    shortTitle: "STT",
    subtitle: "Speech-to-text for the composer microphone.",
    subtitleKey: "models.sttSubtitle",
    href: "/settings/models/stt",
    tileClass: "bg-pink-50 text-pink-600",
    icon: Mic,
    modelFields: ["model", "language"],
  },
  {
    key: "imagegen",
    title: "Image Generation",
    titleKey: "models.imagegen",
    shortTitle: "Image",
    subtitle: "Text-to-image model for the chat imagegen tool.",
    subtitleKey: "models.imagegenSubtitle",
    href: "/settings/models/image",
    tileClass: "bg-fuchsia-50 text-fuchsia-600",
    icon: ImageIcon,
    modelFields: ["model", "size", "quality", "response_format"],
  },
  {
    key: "videogen",
    title: "Video Generation",
    titleKey: "models.videogen",
    shortTitle: "Video",
    subtitle: "Text-to-video model for the chat videogen tool.",
    subtitleKey: "models.videogenSubtitle",
    href: "/settings/models/video",
    tileClass: "bg-indigo-50 text-indigo-600",
    icon: Clapperboard,
    modelFields: ["model", "aspect_ratio", "duration", "resolution"],
  },
];

export function getModelServiceConfig(key: ServiceName) {
  return MODEL_SERVICE_CONFIGS.find((item) => item.key === key);
}
