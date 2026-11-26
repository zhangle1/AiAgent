// Offline prototype entry. Bundle with @xyflow/react 12, React 19 and the CSS text loader.
import React, { memo, useCallback, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Background, Controls, Handle, MarkerType, MiniMap, Position, ReactFlow,
  applyNodeChanges, useReactFlow, ReactFlowProvider } from '@xyflow/react';
import baseCss from '@xyflow/react/dist/style.css';

const style = document.createElement('style');
style.textContent = baseCss;
document.head.prepend(style);

const icons = {
  mes: <><path d="m3 9 6-3 6 3v7l-6 3-6-3z M3 9l6 3 6-3 M9 12v7 M8 5l4-2 6 3v7l-2 1"/></>,
  erp: <><path d="M5 3h9l3 3v13H5z M13 3v4h4 M8 10h6 M8 14h6"/></>,
  api: <><path d="m3 8 7-4 7 4-7 4z M3 8v7l7 4 7-4V8 M10 12v7"/></>,
  database: <><ellipse cx="10" cy="5" rx="7" ry="3"/><path d="M3 5v10c0 4 14 4 14 0V5 M3 10c0 4 14 4 14 0"/></>,
  http: <><path d="m7 6-4 4 4 4 M13 6l4 4-4 4 M12 3l-4 14"/></>
};
function Icon({name}) {return <svg width="21" height="21" viewBox="0 0 21 21" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" strokeLinecap="round" aria-hidden="true">{icons[name]}</svg>}

const DiagramNode = memo(function DiagramNode({data:d,selected}) {
  const source = d.role === 'source';
  return <div className={'diagram-node '+d.type+(source?' source':'')+(selected?' selected':'')} data-node={d.key}>
    <Handle type="target" position={d.below?Position.Top:Position.Left} id="in" isConnectable={!source}/>
    <div className="diagram-cap"><span className="diagram-icon"><Icon name={source?(d.type==='api'?'http':'database'):d.type}/></span>
      <span>{source?(d.type==='api'?'接口组件':'数据库连接'):'业务对象'}</span>
      {source&&<i className={'health '+(d.ok?'':'down')} title={d.ok?'已验证':'待检查'}/>}</div>
    <strong>{d.name}</strong>
    <div className="diagram-meta">{source?d.driver:d.entity}</div>
    {!source&&<><div className="diagram-key"><b>⌑</b> {d.keyName}<span>{d.count} 个字段</span></div>
      <Handle type="source" position={Position.Right} id="out"/>
      <Handle type="source" position={d.type==='mes'?Position.Bottom:Position.Right} id="reads" isConnectable={false} style={d.type==='mes'?{}:{top:'72%'}}/></>}
  </div>;
});
const nodeTypes = {diagram:DiagramNode};
function initialPosition(d,index,items) {
  const right=items.filter(i=>i.role==='object'&&i.type!=='mes');
  const left=items.filter(i=>i.role==='object'&&i.type==='mes');
  const leftIndex=left.findIndex(i=>i.key===d.key);
  if(d.role==='object')return d.type==='mes'?{x:0,y:146+leftIndex*400}:{x:340,y:36+right.findIndex(i=>i.key===d.key)*220};
  const leftOwner=left.findIndex(i=>i.source===d.key);
  if(leftOwner>=0)return {x:0,y:360+leftOwner*400};
  const owners=right.filter(i=>items.find(s=>s.key===i.source));
  const unique=[...new Set(owners.map(i=>i.source))];
  return {x:660,y:68+Math.max(0,unique.indexOf(d.key))*220};
}
function Graph({graph,events}) {
  const flow=useReactFlow();
  const [nodes,setNodes]=useState([]);
  const [selected,setSelected]=useState(null);
  const items=graph.items;
  useEffect(()=>{
    setNodes(old=>items.map((d,index)=>{
      const previous=old.find(n=>n.id===d.key);
      return {id:d.key,type:'diagram',position:previous?.position||events.positions.get(d.key)||initialPosition(d,index,items),
        selected:selected===d.key,data:d,ariaLabel:d.name+'，'+(d.role==='source'?'连接配置':'对象属性')};
    }));
  },[items,events,selected]);
  const fit=useCallback(()=>flow.fitView({padding:.16,maxZoom:1.18,duration:0}),[flow]);
  useEffect(()=>{
    if(!items.length)return;
    const frame=requestAnimationFrame(()=>requestAnimationFrame(fit));
    return ()=>cancelAnimationFrame(frame);
  },[graph.layoutKey,fit]);
  const edges=useMemo(()=>graph.edges.map(e=>{
    const color=e.sourceEdge?'#91a2b3':e.type==='api'?'#168476':'#5471d5';
    return {...e,type:'default',animated:false,selectable:!e.sourceEdge,
      className:e.sourceEdge?'source-edge':'relation-edge',ariaLabel:e.label,
      style:{stroke:color,strokeWidth:e.sourceEdge?1.6:2.3,strokeDasharray:e.sourceEdge?'5 5':undefined},
      markerEnd:{type:MarkerType.ArrowClosed,color,width:14,height:14},
      labelStyle:{fill:e.sourceEdge?'#64778a':color,fontSize:12,fontWeight:650},
      labelBgStyle:{fill:'#f7fafb'},labelBgPadding:[5,2],interactionWidth:22};
  }),[graph.edges]);
  return <ReactFlow nodes={nodes} edges={edges} nodeTypes={nodeTypes} minZoom={.18} maxZoom={2}
    onNodesChange={changes=>setNodes(ns=>applyNodeChanges(changes,ns))}
    onNodeDragStop={(_,n)=>events.positions.set(n.id,n.position)}
    onNodeClick={(_,n)=>{setSelected(n.id);events.inspect(n.data)}}
    onNodeKeyDown={undefined}
    onPaneClick={()=>{setSelected(null);events.close()}}
    onEdgeClick={(_,e)=>{if(!e.sourceEdge)events.edge(e.id)}}
    onConnect={c=>events.connect(c)} deleteKeyCode={null} nodesConnectable
    proOptions={{hideAttribution:true}} fitView fitViewOptions={{padding:.16,maxZoom:1.18}}>
      <Background color="#c5d2d1" gap={18} size={1.15}/>
      <Controls showInteractive={false} position="bottom-left" fitViewOptions={{padding:.16,maxZoom:1.18}}/>
      <MiniMap pannable zoomable nodeStrokeWidth={2} nodeColor={n=>n.data.role==='source'?'#c9d1db':({mes:'#6581e6',erp:'#ad87d7',api:'#4aa99a'}[n.data.type])} maskColor="rgba(211,220,223,.52)" />
  </ReactFlow>;
}
const root=createRoot(document.getElementById('flowRoot'));
window.drawOntologyFlow=(graph,events)=>root.render(<ReactFlowProvider key={graph.model}><Graph graph={graph} events={events}/></ReactFlowProvider>);
