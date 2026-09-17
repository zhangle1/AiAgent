import { describe, expect, it } from 'vitest';
import { readAnalysisStream } from './analysis-stream';

function response(text: string, size = 1) {
  const bytes = new TextEncoder().encode(text);
  return new Response(new ReadableStream({ start(controller) {
    for (let i = 0; i < bytes.length; i += size) controller.enqueue(bytes.slice(i, i + size));
    controller.close();
  } }), { headers: { 'content-type': 'text/event-stream' } });
}
describe('analysis stream', () => {
  it('decodes Chinese split across bytes and emits deltas before the final answer', async () => {
    const chunks: string[] = [];
    const result = await readAnalysisStream(response('data: {"type":"started"}\n\ndata: {"type":"delta","text":"你好"}\r\n\r\ndata: {"type":"delta","text":"世界"}\n\ndata: {"type":"done","answer":"你好世界"}\n\n'), text => chunks.push(text));
    expect(chunks).toEqual(['你好', '世界']);
    expect(result.answer).toBe('你好世界');
  });
  it('does not treat a dropped stream as successful completion', async () => {
    const chunks: string[] = [];
    await expect(readAnalysisStream(response('data: {"type":"delta","text":"partial"}\n\n'), text => chunks.push(text))).rejects.toThrow('中断');
    expect(chunks).toEqual(['partial']);
  });
  it('rejects legacy JSON responses instead of simulating streaming', async () => {
    await expect(readAnalysisStream(Response.json({ answer: 'legacy' }), () => {})).rejects.toThrow('更新后端');
  });
  it('cancels the response reader once the done event arrives', async () => {
    let cancelled = false;
    const stream = new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode('data: {"type":"done","answer":"ok"}\n\n')); }, cancel() { cancelled = true; } });
    await readAnalysisStream(new Response(stream, { headers: { 'content-type': 'text/event-stream' } }), () => {});
    expect(cancelled).toBe(true);
  });
});
