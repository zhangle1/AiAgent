declare module '@deepseek-ai/cordis' {
  export interface Context {
    tools: {
      register(tool: unknown): void
    }
  }
}

declare module '@deepseek-ai/schemastery' {
  interface SchemaBuilder<T> {
    required(): SchemaBuilder<T>
    default(value: T): SchemaBuilder<T>
    min(value: number): SchemaBuilder<T>
    max(value: number): SchemaBuilder<T>
  }

  interface SchemaFactory {
    object<T>(shape: Record<string, unknown>): SchemaBuilder<T>
    string(): SchemaBuilder<string>
    boolean(): SchemaBuilder<boolean>
    number(): SchemaBuilder<number>
  }

  type Schema<T> = SchemaBuilder<T>
  const Schema: SchemaFactory
  export default Schema
}

declare module '@deepseek-ai/dsh-tools' {
  interface ToolExecution {
    signal?: AbortSignal
  }

  interface ToolDefinition {
    execute(args: Record<string, string | undefined>, execution: ToolExecution): Promise<string>
    [key: string]: unknown
  }

  export function defineTool(definition: ToolDefinition): unknown
}
