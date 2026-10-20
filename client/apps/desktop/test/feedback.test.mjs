import test from 'node:test';
import assert from 'node:assert/strict';
import { GITHUB_REPO, buildBugReportUrl, assertFeedbackIssueUrl } from '@kunbuddy/shared';
test('client feedback belongs to AiAgent rather than the imported upstream repository',()=>{
 assert.equal(GITHUB_REPO,'zhangle1/AiAgent');
 const url=buildBugReportUrl({version:'0.20.1',platform:'win32',arch:'x64',protocolVersion:11});
 assert.equal(new URL(url).pathname,'/zhangle1/AiAgent/issues/new');
 assertFeedbackIssueUrl(url);
 assert.throws(()=>assertFeedbackIssueUrl('https://github.com/vastsa/PI-Desktop/issues/new?template=bug_report.yml'));
});
