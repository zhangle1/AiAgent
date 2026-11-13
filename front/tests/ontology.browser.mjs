import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import http from 'node:http';
const front=fileURLToPath(new URL('../',import.meta.url));
const require=createRequire(path.join(front,'package.json'));
const tools=createRequire(path.resolve(process.env.CHAT_LIBRARY_TEST_TOOLS || '../../goal-browser-tmp','package.json'));
const {chromium}=tools('playwright'); const {build}=tools('esbuild');
const bundle=await build({stdin:{contents:`import React,{useState} from 'react';import{createRoot}from'react-dom/client';import{OntologyWorkbench}from'./components/ontology/OntologyWorkbench';function App(){const[open,setOpen]=useState(true);const[value,setValue]=useState('');return <><button onClick={()=>setOpen(true)}>打开</button><output>{value}</output>{open&&<OntologyWorkbench projectId={7} onClose={()=>setOpen(false)} onInsert={setValue}/>}</>};createRoot(document.getElementById('root')).render(<App/>);`,resolveDir:front,loader:'tsx'},bundle:true,write:false,platform:'browser',format:'iife',jsx:'automatic',tsconfig:path.join(front,'tsconfig.json')});
let model={id:'a'.repeat(32),name:'合成企业',version:1,connection:{type:'mysql',host:'',port:3306,database:'',username:''},has_password:false,graph:{nodes:[],edges:[]}};
let captured;
const server=http.createServer(async(req,res)=>{
 if(req.url==='/bundle.js'){res.setHeader('Content-Type','text/javascript');return res.end(bundle.outputFiles[0].text);}
 if(req.url.startsWith('/api/')){
  let raw='';for await(const chunk of req) raw+=chunk; const body=raw?JSON.parse(raw):{};
  res.setHeader('Content-Type','application/json');
  if(req.url.endsWith('/plugins'))return res.end(JSON.stringify(['source','transform','output'].map((id,i)=>({id,label:['MES 数据源','字段映射','聊天输出'][i],version:1}))));
  if(req.url.endsWith('/models')&&req.method==='GET')return res.end(JSON.stringify([{id:model.id,name:model.name}]));
  if(req.url.endsWith('/context'))return res.end(JSON.stringify({text:'合成模型上下文；无密码',version:model.version}));
  if(req.method==='PUT'){captured=body;model={...model,connection:body.connection,graph:body.graph,has_password:!!body.password,version:model.version+1};}
  return res.end(JSON.stringify(model));
 }
 res.setHeader('Content-Type','text/html; charset=utf-8');res.end('<html><body><div id="root"></div><script src="/bundle.js"></script></body></html>');
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));let browser;
try{
 browser=await chromium.launch({headless:true,...(process.env.CHAT_LIBRARY_TEST_BROWSER?{executablePath:process.env.CHAT_LIBRARY_TEST_BROWSER}:{})});const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(`http://127.0.0.1:${server.address().port}`);
 await page.getByLabel('企业模型',{exact:true}).selectOption(model.id);
 await page.getByText('MES 数据库配置',{exact:true}).waitFor();
 await page.getByLabel('数据库地址',{exact:true}).fill('synthetic-host');
 await page.getByLabel('密码',{exact:true}).fill('synthetic-secret');
 await page.getByRole('button',{name:'＋ MES 数据源',exact:true}).click();
 await page.getByRole('button',{name:'＋ 聊天输出',exact:true}).click();
 await page.getByLabel('来源节点',{exact:true}).selectOption({label:'MES 数据源'});
 await page.getByLabel('目标节点',{exact:true}).selectOption({label:'聊天输出'});
 await page.getByRole('button',{name:'连接节点',exact:true}).click();
 await page.getByRole('button',{name:'保存配置与流程',exact:true}).click();
 await page.getByText('ETL 流程定义 · v2',{exact:true}).waitFor();
 assert.equal(captured.password,'synthetic-secret');assert.equal(captured.graph.edges.length,1);
 assert.equal(await page.locator('input[type=password]').inputValue(),'');
 await page.getByRole('button',{name:'获取聊天上下文',exact:true}).click();
 await page.getByLabel('上下文预览',{exact:true}).waitFor();
 assert.doesNotMatch(await page.getByLabel('上下文预览',{exact:true}).inputValue(),/synthetic-secret/);
 await page.getByRole('button',{name:'填入聊天草稿',exact:true}).click();
 assert.equal(await page.locator('output').textContent(),'合成模型上下文；无密码');
 await page.getByRole('button',{name:'打开',exact:true}).click();await page.locator('dialog[open]').waitFor();await page.keyboard.press('Escape');await page.locator('dialog').waitFor({state:'detached'});
 assert.deepEqual(errors,[]);console.log('PASS: model selection, connection and DAG save, password clearing, context preview and draft insertion, Escape');
}finally{await browser?.close();await new Promise(resolve=>server.close(resolve));}
