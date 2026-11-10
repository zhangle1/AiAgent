import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import path from 'node:path';
const require=createRequire(path.resolve(process.env.GOAL_TEST_TOOLS ?? '../goal-browser-tmp','package.json'));
const {chromium}=require('playwright');
const browser=await chromium.launch({headless:true});
const page=await browser.newPage({viewport:{width:1440,height:1000}});
let status='available', requests=0;
await page.route('**/api/**',async route=>{
 const name=new URL(route.request().url()).pathname;let body=[];
 if(name.endsWith('/auth/status'))body={authenticated:true,user_id:'fixture',username:'fixture',role:'admin'};
 else if(name.endsWith('/settings/ui'))body={theme:'light',language:'zh-CN'};
 else if(name.endsWith('/sessions/list'))body={sessions:[]};
 else if(name.endsWith('/sessions/project-preferences'))body={preferences:[]};
 else if(name.endsWith('/sessions/sidebar-preference'))body={project_sort_mode:'recent'};
 else if(name.endsWith('/codex/quota')){requests++;body={status,plan:'plus',account_hint:'p***@example.invalid',primary:{used_percent:42,window_minutes:300,resets_at:1800000000},secondary:null,updated_at:new Date().toISOString(),next_refresh_at:new Date(Date.now()+1800000).toISOString()};}
 await route.fulfill({json:body});
});
try{
 await page.goto(process.env.QUOTA_TEST_URL ?? 'http://127.0.0.1:3782/settings', {timeout:120000,waitUntil:'domcontentloaded'});
 const button=page.getByRole('button',{name:'Codex 额度',exact:true});await button.waitFor();
 assert.equal(await page.getByRole('dialog',{name:'3 步开始使用坤伴'}).count(),0);
 assert.equal(requests,0);
 await button.click();const dialog=page.getByRole('dialog',{name:'Codex 额度',exact:true});
 await dialog.getByText('已用 42%',{exact:true}).waitFor();
 assert.match(await dialog.innerText(),/p\*\*\*@example.invalid/);
 await page.waitForTimeout(500);assert.equal(requests,1);
 await page.keyboard.press('Escape');await dialog.waitFor({state:'hidden'});
 status='not_logged_in';await button.click();await dialog.getByText('后端本地 Codex 尚未登录。',{exact:true}).waitFor();
 await page.keyboard.press('Escape');
 await page.setViewportSize({width:390,height:844});
 await page.evaluate(()=>window.dispatchEvent(new Event('aiagent:mobile-drawer-toggle')));
 await button.click();await dialog.waitFor();
 const box=await dialog.boundingBox();assert.ok(box.x>=0&&box.x+box.width<=390);
 console.log('PASS: quota icon, onboarding removed, quota rendering, no eager/repeated reads, signed-out state, Escape and mobile layout');
}finally{await browser.close();}
