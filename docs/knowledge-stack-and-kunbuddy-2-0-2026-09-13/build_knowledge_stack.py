from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = Path(__file__).parent
ASSETS = ROOT / "assets"
ASSETS.mkdir(parents=True, exist_ok=True)
DOCX = ROOT / "知识库体系与坤伴 2.0 接入方案 2026-09-13.docx"
HTML = ROOT / "知识库体系与坤伴 2.0 接入方案 2026-09-13.html"

NAVY, BLUE, INK, MUTED, LINE = "153B67", "2563EB", "111827", "475569", "D7E1EE"

def f(size, bold=False):
    choices = ["C:/Windows/Fonts/msyhbd.ttc" if bold else "C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simhei.ttf"]
    for item in choices:
        if Path(item).exists(): return ImageFont.truetype(item, size)
    return ImageFont.load_default()

def rbox(d, box, fill, outline="#CBD5E1", radius=24, width=2):
    d.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)

def ctext(d, box, text, font, fill="#1E3A5F", spacing=6):
    x1,y1,x2,y2=box
    b=d.multiline_textbbox((0,0), text, font=font, spacing=spacing, align="center")
    d.multiline_text(((x1+x2-(b[2]-b[0]))/2,(y1+y2-(b[3]-b[1]))/2), text, font=font, fill=fill, spacing=spacing, align="center")

def make_stack(path):
    im=Image.new("RGB",(1800,1120),"#F8FBFF"); d=ImageDraw.Draw(im)
    d.text((80,50),"四类知识能力的职责边界与协同",font=f(43,True),fill="#10365E")
    d.text((82,112),"它们不是相互替代的产品，而是面向不同问题、输入形态和更新节奏的知识层。",font=f(22),fill="#52677F")
    layers=[
      (120,220,"LLM Wiki", "沉淀层", "把原始资料编译为\n可读、可链接、可版本化的\n项目知识页面", "#F5F3FF", "#6D28D9"),
      (520,220,"LlamaIndex", "检索编排层", "连接数据源、切块、向量/关键词\n检索、重排与路由；\n适合日常事实问答", "#EFF6FF", "#2563EB"),
      (920,220,"PageIndex", "长文理解层", "按文档结构建立上下文树，\n让模型逐层推理定位；\n适合长 PDF、方案和规范", "#ECFDF5", "#047857"),
      (1320,220,"GraphRAG", "关系推理层", "提取实体、关系、主张与社群，\n回答跨文档关系、影响范围\n与全局归纳问题", "#FFF7ED", "#C2410C"),
    ]
    for x,y,name,kind,desc,bg,color in layers:
        rbox(d,(x,y,x+360,y+365),bg)
        rbox(d,(x+30,y+28,x+160,y+78),color,color,15,1); ctext(d,(x+30,y+28,x+160,y+78),kind,f(17,True),"#FFFFFF")
        d.text((x+30,y+115),name,font=f(29,True),fill="#173B67")
        d.multiline_text((x+30,y+180),desc,font=f(20),fill="#334155",spacing=11)
    # Common facts and router
    rbox(d,(260,720,1540,830),"#FFFFFF","#93C5FD",25,2)
    ctext(d,(280,735,1520,815),"统一事实与治理层：项目 / 代码 / 需求 / 工单 / 文件 / Git 证据 / 权限 / 版本 / 失效时间",f(26,True),"#174EA6")
    for x in (300,700,1100,1500): d.line((x,585,x if x<700 else x-20,720),fill="#94A3B8",width=3)
    rbox(d,(400,900,1400,1030),"#EAF2FF","#60A5FA",25,2)
    ctext(d,(420,915,1380,1015),"Agent 路由层：根据问题类型选择 Wiki、普通 RAG、长文检索、图谱检索或互联网搜索工具，并统一返回引用与证据。",f(23,True),"#153B67")
    im.save(path)

def make_flow(path):
    im=Image.new("RGB",(1800,1020),"#FBFDFF"); d=ImageDraw.Draw(im)
    d.text((80,50),"坤伴 2.0 知识库接入流程",font=f(43,True),fill="#10365E")
    d.text((82,112),"先保证来源与权限，再扩展检索方式；所有答案最终回到可验证的事实与证据。",font=f(22),fill="#52677F")
    steps=[
      (90,"1", "接入与登记", "代码库、需求、文档、\n工单、对话、附件\n记录来源和权限", "#EFF6FF", "#2563EB"),
      (435,"2", "清洗与切分", "解析结构、OCR、去重、\n元数据、版本、\n敏感信息处理", "#ECFDF5", "#047857"),
      (780,"3", "多索引构建", "Wiki 页面、向量索引、\n长文上下文树、\n实体关系与主张", "#FFF7ED", "#C2410C"),
      (1125,"4", "Agent 路由", "判断问题意图和风险，\n选择检索器与工具，\n需要时要求澄清", "#F5F3FF", "#6D28D9"),
      (1470,"5", "回答与沉淀", "返回来源、差异、证据；\n人工确认后把高价值\n结论写回 Wiki/记忆", "#FFF1F2", "#BE123C"),
    ]
    for x,num,title,desc,bg,color in steps:
        rbox(d,(x,260,x+250,650),bg)
        rbox(d,(x+25,285,x+78,337),color,color,14,1); ctext(d,(x+25,285,x+78,337),num,f(19,True),"#FFFFFF")
        d.text((x+25,375),title,font=f(26,True),fill="#173B67")
        d.multiline_text((x+25,445),desc,font=f(19),fill="#334155",spacing=12)
        if x<1470:
            d.line((x+250,455,x+330,455),fill="#94A3B8",width=5); d.polygon([(x+330,455),(x+310,442),(x+310,468)],fill="#94A3B8")
    rbox(d,(190,770,1610,900),"#FFFFFF","#CBD5E1",22,2)
    ctext(d,(220,790,1580,880),"贯穿全程的护栏：租户和项目隔离 · 来源与版本 · 最小权限 · 人工确认 · 评测集 · 执行轨迹 · 反馈闭环",f(25,True),"#153B67")
    im.save(path)

def shade(cell, fill):
    p=cell._tc.get_or_add_tcPr(); e=OxmlElement("w:shd"); e.set(qn("w:fill"),fill); p.append(e)
def borders(cell):
    p=cell._tc.get_or_add_tcPr(); b=OxmlElement("w:tcBorders")
    for edge in ("top","left","bottom","right"):
        e=OxmlElement(f"w:{edge}"); e.set(qn("w:val"),"single"); e.set(qn("w:sz"),"6"); e.set(qn("w:color"),LINE); b.append(e)
    p.append(b)
def margins(cell):
    p=cell._tc.get_or_add_tcPr(); m=OxmlElement("w:tcMar")
    for side in ("top","start","bottom","end"):
        e=OxmlElement(f"w:{side}"); e.set(qn("w:w"),"120"); e.set(qn("w:type"),"dxa"); m.append(e)
    p.append(m)
def repeat(row):
    p=row._tr.get_or_add_trPr(); e=OxmlElement("w:tblHeader"); e.set(qn("w:val"),"true"); p.append(e)
def no_split(row):
    p=row._tr.get_or_add_trPr(); e=OxmlElement("w:cantSplit"); p.append(e)

def configure(doc):
    s=doc.sections[0]; s.page_width,s.page_height=Inches(8.5),Inches(11); s.top_margin,s.bottom_margin=Inches(.72),Inches(.7);s.left_margin,s.right_margin=Inches(.72),Inches(.72)
    n=doc.styles["Normal"]; n.font.name="Microsoft YaHei"; n._element.rPr.rFonts.set(qn("w:eastAsia"),"Microsoft YaHei"); n.font.size=Pt(10.5); n.font.color.rgb=RGBColor.from_string(INK); n.paragraph_format.space_after=Pt(7); n.paragraph_format.line_spacing=1.32
    for name,size in (("Title",25),("Heading 1",16),("Heading 2",12.5)):
        st=doc.styles[name];st.font.name="Microsoft YaHei";st._element.rPr.rFonts.set(qn("w:eastAsia"),"Microsoft YaHei");st.font.size=Pt(size);st.font.bold=True;st.font.color.rgb=RGBColor(0,0,0)
    doc.styles["Heading 1"].paragraph_format.space_before=Pt(18);doc.styles["Heading 1"].paragraph_format.space_after=Pt(8)
    doc.styles["Heading 2"].paragraph_format.space_before=Pt(12);doc.styles["Heading 2"].paragraph_format.space_after=Pt(5)
    p=s.footer.paragraphs[0];p.alignment=WD_ALIGN_PARAGRAPH.CENTER;r=p.add_run("知识库体系与坤伴 2.0 接入方案  |  2026-09-13");r.font.size=Pt(8.5);r.font.color.rgb=RGBColor.from_string("64748B")

def para(doc,text,lead=None):
    p=doc.add_paragraph()
    if lead and text.startswith(lead):r=p.add_run(lead);r.bold=True;p.add_run(text[len(lead):])
    else:p.add_run(text)
    return p
def bullets(doc,items):
    for x in items:
        p=doc.add_paragraph(style="List Bullet");p.paragraph_format.space_after=Pt(4);p.add_run(x)
def table(doc,heads,rows,widths):
    t=doc.add_table(rows=1,cols=len(heads));t.alignment=WD_TABLE_ALIGNMENT.CENTER;t.autofit=False;repeat(t.rows[0])
    for i,h in enumerate(heads):
        c=t.rows[0].cells[i];c.width=Inches(widths[i]);c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER;shade(c,NAVY);borders(c);margins(c);r=c.paragraphs[0].add_run(h);r.bold=True;r.font.size=Pt(9.5);r.font.color.rgb=RGBColor(255,255,255)
    for ri,row in enumerate(rows):
        row_obj=t.add_row();no_split(row_obj);cs=row_obj.cells
        for i,v in enumerate(row):
            c=cs[i];c.width=Inches(widths[i]);c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER;shade(c,"F7FAFE" if ri%2==0 else "FFFFFF");borders(c);margins(c);p=c.paragraphs[0];p.paragraph_format.space_after=Pt(0);p.add_run(v)
    doc.add_paragraph().paragraph_format.space_after=Pt(2)
def fig(doc,path,caption):
    p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.add_run().add_picture(str(path),width=Inches(7.0));c=doc.add_paragraph();c.alignment=WD_ALIGN_PARAGRAPH.CENTER;r=c.add_run(caption);r.italic=True;r.font.size=Pt(9);r.font.color.rgb=RGBColor.from_string(MUTED);c.paragraph_format.space_after=Pt(11)

def docx():
    map_png=ASSETS/"knowledge-stack-map.png";flow_png=ASSETS/"kunbuddy-knowledge-flow.png";make_stack(map_png);make_flow(flow_png)
    d=Document();configure(d)
    p=d.add_paragraph(style="Title");p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.add_run("知识库体系与坤伴 2.0 接入方案")
    p=d.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;r=p.add_run("LlamaIndex、PageIndex、GraphRAG 与 LLM Wiki 的定位、协同与落地路径  |  2026-09-13");r.font.size=Pt(11);r.font.color.rgb=RGBColor.from_string(MUTED)
    d.add_paragraph()
    para(d,"结论：这四类方案并不处在同一层。LlamaIndex 是数据接入和检索编排框架；PageIndex 面向复杂长文档的树状推理检索；GraphRAG 面向实体关系和跨文档全局推理；LLM Wiki 则把知识编译为可读、可链接、可维护的长期资产。坤伴 2.0 应以统一事实与权限层为底座，用 Agent 路由层把它们组合起来。")
    para(d,"术语校正：PageIndex 的官方定位是无向量、基于推理的长文档 RAG，不是互联网搜索。互联网搜索建议作为独立的外部工具，在有时效或公共信息需求时由 Agent 按策略调用。")
    d.add_heading("1. 四类方案分别解决什么问题",1)
    table(d,["方案","核心机制","最适合的问题","不应承担的职责"],[
      ("LlamaIndex", "以 Document / Node / Index / Retriever 为核心，连接数据源并构建向量、关键词、摘要、SQL 等索引。", "项目资料的日常事实问答、混合检索、数据连接和检索路由。", "单独解决复杂长文结构理解、全局关系推理或知识治理。"),
      ("PageIndex", "将长文档转换为上下文树，由 LLM 逐层推理定位；官方强调无向量、可解释检索。", "长 PDF、设计方案、合同、规范、审计报告中的章节级理解与引用。", "互联网搜索；跨大量异构资料的实体关系分析。"),
      ("GraphRAG", "从 TextUnit 提取实体、关系和主张，形成社区及社区摘要，再进行全局/局部/DRIFT 查询。", "“哪些系统受某改动影响”“历史上某问题与谁相关”“多个文档共同说明什么”。", "实时小问题的最低成本检索；未经治理的事实写回。"),
      ("LLM Wiki", "把原始材料编译为互相链接、可阅读、可版本化的 Markdown Wiki，并以日志维持更新。", "长期项目知识、术语、决策、架构、故障经验和技能沉淀。", "替代原始证据、实时业务数据或权限校验。"),
    ],[1.05,2.2,2.25,1.52])
    fig(d,map_png,"图 1  四类知识能力的职责边界与协同关系")
    d.add_heading("2. 相关概念速览",1)
    table(d,["概念","说明","在坤伴 2.0 中的含义"],[
      ("RAG", "在生成前检索与问题相关的外部上下文，连同问题一起交给模型。", "让回答基于项目文档、代码、工单和已批准的知识，而不是只凭模型参数。"),
      ("Chunk / Node", "将原始文档划为可检索、可引用的最小文本单元。", "保留文件、段落、页码、提交版本、项目与权限等元数据。"),
      ("Embedding / Vector", "把文本表示成向量，用语义相似度召回相关片段。", "适合快速找“意思相近”的事实；不等同于逻辑关系推理。"),
      ("知识图谱", "用实体、关系、主张和来源表达事实网络。", "可将模块、接口、需求、缺陷、提交、人和环境关联起来。"),
      ("上下文树", "按文档标题、段落和层级形成树，让检索先定位章节再深入内容。", "避免长规范被简单切块后失去目录、章节与引用语义。"),
      ("知识编译", "将原始资料提炼、归并和链接成稳定知识页，同时保留来源。", "把重复问答转为可复用的项目知识，而非每次从原文重新推导。"),
      ("Provenance 来源追溯", "能够从答案或实体回到原始文件、页码、行、提交或业务记录。", "每个可执行结论都要能回答“依据是什么、何时有效、谁确认过”。"),
    ],[1.4,2.35,3.27])
    d.add_heading("3. 它们如何配合使用",1)
    para(d,"建议使用“事实先行、路由检索、回答带证据、确认后沉淀”的组合，而不是让任何一种索引包办全部问题。典型路由如下：")
    table(d,["用户问题类型","优先能力","返回内容"],[
      ("“这个接口怎么调用？当前配置是什么？”", "LlamaIndex 混合检索 + 代码/文档来源", "相关片段、文件位置、版本和简洁说明。"),
      ("“这份 200 页规范对交付有什么要求？”", "PageIndex 长文上下文树", "涉及章节、页级或行级引用、结构化约束清单。"),
      ("“这次字段改动会影响哪些系统和历史缺陷？”", "GraphRAG + 代码与工单事实", "实体关系路径、影响范围、主张来源及不确定项。"),
      ("“我们为什么选这个方案？以后怎么做？”", "LLM Wiki 优先，再回链原始资料", "已确认决策、适用边界、相关页面和原始证据。"),
      ("“今天外部平台/政策/公开文档有何变化？”", "互联网搜索工具 + 来源评估", "带发布时间、链接、抓取时间的外部事实；不直接写入长期知识。"),
    ],[2.3,2.25,2.47])
    d.add_heading("4. 坤伴 2.0 的目标架构",1)
    para(d,"坤伴 2.0 的关键不是同时部署四套产品，而是建立一条统一的数据与治理通路。每种检索器都是 Agent 可以选择的受控能力；最终答案和写操作均要被项目、用户、权限、来源和版本约束。")
    fig(d,flow_png,"图 2  坤伴 2.0 从资料接入到知识沉淀的建议流程")
    bullets(d,[
      "事实对象层：统一管理 Project、Repository、File、Document、Requirement、Issue、GitCommit、Conversation、Attachment 等对象，并为每个对象保存来源、版本、权限与有效时间。",
      "索引服务层：在同一事实对象之上构建多种派生索引。索引可以重建，但原始事实与审计记录不可被模型静默覆盖。",
      "知识治理层：用 Wiki 页面承载经过确认的架构、决策、术语、技能和复盘；每页指向原始资料，并记录编译时间和审核状态。",
      "Agent 路由层：基于任务意图、数据敏感性、时效性、成本与置信度选择普通 RAG、长文树检索、图检索、Wiki 或外网搜索。",
      "证据与反馈层：所有答案返回引文；高价值结论仅在规则或人工确认后写入 Wiki / 长期记忆；把用户纠正反哺评测集。",
    ])
    d.add_heading("5. 建议的分阶段接入步骤",1)
    table(d,["阶段","交付内容","验收标准"],[
      ("P0 事实底座", "建立统一文档元数据、项目隔离、版本、权限、上传/同步队列及引用格式。", "任意检索结果能回到项目、文件、版本和段落；无权限资料不被召回。"),
      ("P1 普通 RAG", "用 LlamaIndex 或等效服务接入代码、Markdown、工单和常用文档，配置混合检索与重排。", "常见项目问答有来源；召回、时延、空答案与引用完整性可度量。"),
      ("P2 长文与 Wiki", "引入 PageIndex 处理重点长 PDF / 方案，同时建立 Markdown Wiki、变更日志和审核流。", "长文答案可定位到章节；已确认决策可在 Wiki 中复用并回链原文。"),
      ("P3 图谱试点", "选择一个高价值域构建 GraphRAG，例如模块-接口-需求-缺陷-Git 提交关系。", "能回答跨文档影响范围问题，且关系和主张可回溯到 TextUnit / 原始资料。"),
      ("P4 Agent 协同", "将检索器封装为工具，按策略路由至 CPS、APS 或代码 Agent，保留任务交接和执行轨迹。", "每次委派、工具调用、审批和最终结果可回放；失败可恢复或人工接管。"),
    ],[1.25,3.3,2.47])
    d.add_heading("6. 首批建议的知识模型",1)
    table(d,["对象","关键字段","关系示例"],[
      ("原始资料 Source", "来源类型、项目、作者、采集时间、版本、权限、内容哈希。", "Source -> Document / File / Conversation。"),
      ("知识页 WikiPage", "标题、类型、摘要、标签、状态、编译时间、审核人、来源集合。", "WikiPage -> Source；WikiPage <-> WikiPage。"),
      ("实体 Entity", "类型、标准名称、别名、项目范围、置信度、来源。", "模块 -> 接口；需求 -> 缺陷；提交 -> 文件。"),
      ("主张 Claim", "陈述、时间范围、证据、状态、确认人、冲突标记。", "Claim -> Entity / Source / GitCommit。"),
      ("任务轨迹 Trace", "会话、模型、工具、输入摘要、输出、耗时、成本、结果。", "Trace -> Source / WikiPage / ToolCall / Approval。"),
    ],[1.25,3.15,2.62])
    d.add_heading("7. 关键风险与实施原则",1)
    bullets(d,[
      "先治理再放大：没有来源、版本、权限和删除策略的知识库，会快速变成难以纠正的“模型记忆”。",
      "区分原始事实与 AI 结论：原始资料保持不可变；Wiki、摘要、实体和关系都要标记为派生内容并可重建。",
      "GraphRAG 成本较高：实体和关系抽取、社区摘要会消耗模型调用，应从一个业务域试点并衡量增益。",
      "长文索引不是万能：结构差、扫描件、图表密集文档需要 OCR、版面解析与人工抽检；不要假定文本提取已完整。",
      "外网搜索必须隔离：把它视作临时、低可信度上下文；经验证后才可进入项目知识，且应保留来源和抓取时间。",
      "评测先于规模化：为代码问答、需求追溯、影响分析、交付复盘建立基准任务集，持续比较各路由策略的准确性、引用率、时延和成本。",
    ])
    d.add_heading("附录 A 主要资料与项目定位",1)
    for name,url in [
      ("LlamaIndex 文档 - Indexing", "https://docs.llamaindex.ai/en/stable/module_guides/indexing/"),
      ("PageIndex 文档 - What is PageIndex", "https://docs.pageindex.ai/"),
      ("PageIndex 文档 - Getting Started", "https://docs.pageindex.ai/getting-started"),
      ("Microsoft GraphRAG 文档", "https://github.com/microsoft/graphrag/tree/main/docs"),
      ("Microsoft LLM Wiki 项目", "https://github.com/microsoft/llmwiki"),
      ("LLM Wiki 的 Agentic Knowledge Base Template", "https://github.com/elbalderas/llmwiki"),
    ]:
        p=d.add_paragraph(style="List Bullet");r=p.add_run(name+"\n");r.bold=True;u=p.add_run(url);u.font.size=Pt(9);u.font.color.rgb=RGBColor.from_string(BLUE)
    d.save(DOCX)

def html():
    content='''<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>知识库体系与坤伴 2.0 接入方案</title><style>:root{--n:#153b67;--b:#2563eb;--i:#111827;--m:#475569;--l:#d7e1ee;--bg:#f7faff}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--i);font:16px/1.7 "Microsoft YaHei",Arial,sans-serif}.hero{padding:60px max(5vw,24px);color:white;background:linear-gradient(120deg,#102c4e,#2563eb)}h1{font-size:clamp(28px,4vw,46px);margin:0}.hero p{color:#dbeafe;max-width:900px}.wrap{max-width:1180px;margin:auto;padding:30px 20px 70px}.layout{display:grid;grid-template-columns:230px 1fr;gap:24px}.nav{position:sticky;top:18px;height:max-content;background:#fff;border:1px solid var(--l);border-radius:16px;padding:12px}.nav a{display:block;text-decoration:none;color:var(--m);padding:8px;border-radius:8px}.nav a:hover{background:#eff6ff;color:var(--b)}section{background:#fff;border:1px solid var(--l);border-radius:18px;padding:26px;margin-bottom:22px;box-shadow:0 10px 28px #153b6708}h2{color:var(--n);margin:0 0 9px;font-size:24px}.lead{font-size:18px}.fig{width:100%;border:1px solid var(--l);border-radius:14px;margin:16px 0 4px}.cap{text-align:center;color:var(--m);font-size:13px;margin:0 0 18px}table{width:100%;border-spacing:0;border-collapse:separate;border:1px solid var(--l);border-radius:10px;overflow:hidden;margin:14px 0}th{background:var(--n);color:#fff;text-align:left}th,td{padding:12px;border-right:1px solid var(--l);border-bottom:1px solid var(--l);vertical-align:top}td:last-child,th:last-child{border-right:0}tr:last-child td{border-bottom:0}tbody tr:nth-child(odd){background:#f8fbff}details{border:1px solid var(--l);border-radius:10px;padding:12px;margin:10px 0}summary{font-weight:700;color:var(--n);cursor:pointer}li{margin:6px 0}a{color:#1d4ed8}.tag{display:inline-block;border-radius:999px;background:#dbeafe;color:#174ea6;font-size:12px;font-weight:700;padding:3px 9px;margin:3px}@media(max-width:800px){.layout{display:block}.nav{display:none}.wrap{padding:18px 12px}.hero{padding:42px 22px}section{padding:20px;overflow:auto}table{min-width:700px}}</style></head><body><header class="hero"><h1>知识库体系与坤伴 2.0 接入方案</h1><p>LlamaIndex、PageIndex、GraphRAG 与 LLM Wiki 的定位、协同与落地路径 · 2026-09-13</p></header><div class="wrap"><div class="layout"><nav class="nav"><a href="#difference">四类方案</a><a href="#concept">概念速览</a><a href="#combine">协同路由</a><a href="#architecture">2.0 架构</a><a href="#steps">接入步骤</a><a href="#model">知识模型</a><a href="#risk">风险原则</a><a href="#source">资料来源</a></nav><main><section><p class="lead"><b>结论：</b>四类方案不在同一层。LlamaIndex 是数据接入和检索编排框架；PageIndex 是复杂长文档的树状推理检索；GraphRAG 用于实体关系和跨文档全局推理；LLM Wiki 用于长期、可读、可维护的知识沉淀。坤伴 2.0 应以统一事实与权限层为底座，由 Agent 路由层组合它们。</p><p><b>术语校正：</b>PageIndex 的官方定位是无向量、基于推理的长文档 RAG，不是互联网搜索。互联网搜索应作为独立外部工具层，按时效性和可信度策略调用。</p></section><section id="difference"><h2>1. 四类方案分别解决什么问题</h2><img class="fig" src="assets/knowledge-stack-map.png" alt="四类知识能力协同图"><p class="cap">图 1 · 职责边界与协同关系</p><table><thead><tr><th>方案</th><th>核心机制</th><th>适合的问题</th><th>不应承担的职责</th></tr></thead><tbody><tr><td>LlamaIndex</td><td>Document / Node / Index / Retriever，连接数据与混合检索。</td><td>日常事实问答、数据连接、检索路由。</td><td>单独解决复杂长文结构或全局图谱推理。</td></tr><tr><td>PageIndex</td><td>构建上下文树，LLM 逐层推理定位，无向量且可解释。</td><td>长 PDF、方案、规范、合同与审计报告。</td><td>互联网搜索、跨大量异构资料关系分析。</td></tr><tr><td>GraphRAG</td><td>实体、关系、主张、社区与社区摘要。</td><td>影响范围、跨文档关系、整体归纳。</td><td>实时小问题的低成本检索。</td></tr><tr><td>LLM Wiki</td><td>将资料编译为互链、版本化、可维护的 Markdown Wiki。</td><td>架构、决策、术语、经验和技能沉淀。</td><td>替代原始证据、实时数据或权限校验。</td></tr></tbody></table></section><section id="concept"><h2>2. 相关概念</h2><details open><summary>RAG</summary>在生成前检索外部上下文，把问题与可靠资料一起交给模型。</details><details><summary>Chunk / Node 与向量</summary>Chunk / Node 是可引用的最小片段；Embedding / Vector 用语义相似度快速召回“意思相近”的内容。</details><details><summary>知识图谱与主张</summary>图谱用实体、关系和带来源的主张表达事实网络；适合追问谁、什么、何时、影响了谁。</details><details><summary>上下文树与知识编译</summary>上下文树保留章节层次以理解长文；知识编译将原始资料提炼为稳定、可链接、可审核的 Wiki 页面。</details><details><summary>Provenance</summary>来源追溯：答案、实体或结论必须能回到原始文件、页码、段落、提交或业务记录。</details></section><section id="combine"><h2>3. 如何协同使用</h2><table><thead><tr><th>问题类型</th><th>优先能力</th><th>应返回</th></tr></thead><tbody><tr><td>接口怎么调用？当前配置是什么？</td><td>LlamaIndex 混合检索</td><td>片段、文件位置、版本与简洁说明</td></tr><tr><td>长规范对交付有什么要求？</td><td>PageIndex 上下文树</td><td>章节、页级/行级引用、约束清单</td></tr><tr><td>字段改动影响哪些系统和历史缺陷？</td><td>GraphRAG</td><td>实体关系路径、影响范围、主张来源</td></tr><tr><td>为什么选这个方案？以后怎么做？</td><td>LLM Wiki 优先</td><td>已确认决策、适用边界和原始证据</td></tr><tr><td>外部平台或政策今日有什么变化？</td><td>互联网搜索工具</td><td>链接、发布时间、抓取时间；不直接写入长期知识</td></tr></tbody></table></section><section id="architecture"><h2>4. 坤伴 2.0 目标架构</h2><img class="fig" src="assets/kunbuddy-knowledge-flow.png" alt="坤伴 2.0 知识库接入流程"><p class="cap">图 2 · 从资料接入到知识沉淀</p><ul><li>统一事实对象：项目、代码、需求、工单、文件、Git 提交、对话、附件及其来源、版本、权限和有效期。</li><li>索引服务：在同一事实对象上生成可重建的 Wiki、向量、长文树和图谱索引。</li><li>Agent 路由：按问题意图、敏感性、时效性、成本与置信度选择能力。</li><li>证据与反馈：回答带引用；高价值结论经规则或人工确认后写入 Wiki / 长期记忆。</li></ul></section><section id="steps"><h2>5. 分阶段接入步骤</h2><p><span class="tag">P0 事实底座</span><span class="tag">P1 普通 RAG</span><span class="tag">P2 长文与 Wiki</span><span class="tag">P3 图谱试点</span><span class="tag">P4 Agent 协同</span></p><table><thead><tr><th>阶段</th><th>交付内容</th><th>验收标准</th></tr></thead><tbody><tr><td>P0</td><td>统一元数据、项目隔离、版本、权限、同步队列和引用格式。</td><td>每条结果可回到项目、文件、版本和段落。</td></tr><tr><td>P1</td><td>LlamaIndex 或等效服务接入代码、Markdown、工单和常用文档。</td><td>常见问答有来源，召回、时延和引用完整性可度量。</td></tr><tr><td>P2</td><td>PageIndex 处理重点长文，建立 Markdown Wiki、日志和审核流。</td><td>长文答案可定位章节；决策可复用且回链原文。</td></tr><tr><td>P3</td><td>选择高价值域试点 GraphRAG，如模块-接口-需求-缺陷-Git 提交。</td><td>能回答影响范围，且关系与主张可追溯。</td></tr><tr><td>P4</td><td>检索器工具化，由 CPS、APS 或代码 Agent 按策略调用。</td><td>委派、工具、审批和结果均可回放并可人工接管。</td></tr></tbody></table></section><section id="model"><h2>6. 首批建议知识模型</h2><ul><li><b>Source：</b>原始资料、来源类型、项目、版本、权限、内容哈希。</li><li><b>WikiPage：</b>标题、类型、摘要、标签、状态、编译时间、审核人、来源集合。</li><li><b>Entity / Relationship / Claim：</b>标准名称、别名、关系、时间范围、置信度、证据与冲突标记。</li><li><b>Trace：</b>会话、模型、工具、输入摘要、输出、耗时、成本、结果与审批。</li></ul></section><section id="risk"><h2>7. 风险与原则</h2><ul><li>先治理再放大：来源、版本、权限和删除策略先于知识规模。</li><li>原始事实与 AI 派生内容必须分开；摘要、实体和关系可重建、可撤销。</li><li>GraphRAG 成本较高，应从业务域试点并衡量增益。</li><li>扫描件和图表密集文档需要 OCR、版面解析与人工抽检。</li><li>外网搜索是临时低可信上下文，验证后才可进入项目知识。</li><li>先建立评测集，持续比较正确性、引用率、时延、成本和人工介入。</li></ul></section><section id="source"><h2>附录 · 主要资料</h2><ul><li><a href="https://docs.llamaindex.ai/en/stable/module_guides/indexing/">LlamaIndex · Indexing</a></li><li><a href="https://docs.pageindex.ai/">PageIndex · 官方文档</a></li><li><a href="https://docs.pageindex.ai/getting-started">PageIndex · Getting Started</a></li><li><a href="https://github.com/microsoft/graphrag/tree/main/docs">Microsoft GraphRAG · 文档</a></li><li><a href="https://github.com/microsoft/llmwiki">Microsoft LLM Wiki</a></li><li><a href="https://github.com/elbalderas/llmwiki">LLM Wiki · Agentic Knowledge Base Template</a></li></ul></section></main></div></div></body></html>'''
    HTML.write_text(content,encoding="utf-8")

if __name__=="__main__":
    docx();html();print(DOCX);print(HTML)
