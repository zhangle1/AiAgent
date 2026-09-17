export async function readAnalysisStream(response: Response, onDelta: (text: string) => void) {
  if (!response.headers.get('content-type')?.includes('text/event-stream')) throw new Error('后端尚未支持流式回复，请更新后端');
  const reader = response.body?.getReader();
  if (!reader) throw new Error('后端响应为空');
  const decoder = new TextDecoder();
  let buffer = '', length = 0;
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) throw new Error('回复连接中断，请重试');
      length += value.length;
      if (length > 2_000_000) throw new Error('后端响应超出大小限制');
      buffer += decoder.decode(value, { stream: true });
      let boundary: RegExpExecArray | null;
      while ((boundary = /\r?\n\r?\n/.exec(buffer))) {
        const frame = buffer.slice(0, boundary.index);
        buffer = buffer.slice(boundary.index + boundary[0].length);
        const data = frame.split(/\r?\n/).filter(line => line.startsWith('data:')).map(line => line.slice(5).trimStart()).join('\n');
        if (!data) continue;
        const event = JSON.parse(data);
        if (event.type === 'delta' && typeof event.text === 'string') onDelta(event.text);
        if (event.type === 'done' && typeof event.answer === 'string') return { answer: event.answer };
      }
    }
  } finally { await reader.cancel(); reader.releaseLock(); }
}
