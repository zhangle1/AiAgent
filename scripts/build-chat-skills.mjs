import { mkdir, writeFile, readFile, rename } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const sources = [
  ['mattpocock/skills','4ae2317ddb22b046e26455e4f9c9b3aaaf62212d'],
  ['anthropics/skills','ca45fc496a3436251f191354091d557da31c15b5'],
];
// Independently written Chinese adaptations; no upstream scripts or implicit skill chaining.
const rows = [
  [0,'productivity/grill-me','需求追问','需求','通过逐个追问，把想法收敛成明确需求。','我想重新设计知识库目录','先了解目标与痛点，每次只追问最关键的一个未决问题，提供两三个具体选项和建议。利用已有上下文避免重复提问，逐步澄清用户、范围、边界、异常和验收标准。信息充分时总结已定事项、未决问题和验收清单，等待用户决定下一步。',true],
  [0,'engineering/to-spec','整理需求规格','需求','把讨论整理成可评审的需求规格。','把刚才的讨论整理为规格','提取目标、用户场景、现状与期望行为、范围、不做事项、数据与接口、异常、兼容性和验收条件。标注事实、假设、待确认问题，使用具体例子说明行为。先给可评审草稿，不自行实施。'],
  [0,'engineering/to-tickets','拆分任务','需求','把规格拆成可独立验收的实施任务。','拆分这份需求的开发任务','依据规格划分最小可交付的纵向切片，列出每项任务的目的、范围、依赖、输入输出、验收条件和风险。优先打通端到端路径，指出可并行与必须串行部分。信息不足时注明假设。不自动创建外部工单。'],
  [0,'productivity/to-questionnaire','生成调研问卷','需求','将待确认事项整理成便于回答的问卷。','为这个需求生成访谈问卷','明确问卷对象与决策目标，按重要性排列问题。每题聚焦一个决策，提供互斥且易懂的选项及自由补充入口，说明关键取舍。不要诱导答案，不重复已确认事实，最后列出答案将影响的范围。'],
  [0,'engineering/research','技术调研','研发','比较技术方案、证据和适用条件。','比较两种方案的适用场景','先明确决策问题、约束和评价维度。基于可核查资料比较方案、成本、适用条件和风险，区分证据与推断，记录来源与时效性。缺少搜索工具时说明证据缺口，不虚构已完成检索。给出建议和验证步骤。'],
  [0,'engineering/code-review','代码审查','研发','按影响程度检查代码缺陷与风险。','审查这次变更的边界问题','先理解目标和调用链，依据可见代码识别真实缺陷、边界、兼容性和测试缺口。按严重程度给出问题、触发条件、影响、文件位置与修复建议。避免仅凭偏好提出改写。没有证据时明确未知，不宣称运行了测试。'],
  [0,'engineering/diagnosing-bugs','定位故障','研发','用证据、假设和最小实验排查问题。','分析这个间歇性报错','整理现象、预期、复现步骤和最近变化。建立按可能性排序的假设，为每个假设设计可区分的最小验证步骤，根据证据缩小范围。解释根因与修复的因果关系，给回归验证方案。先诊断，未经用户要求不修改文件。'],
  [0,'engineering/domain-modeling','领域建模','研发','厘清业务术语、实体、状态和约束。','梳理审批系统的领域模型','从业务场景提取统一术语、实体、值对象、关系、状态转换和不变量。用具体业务例子验证边界，识别生命周期、权限和并发约束，标出未决建模选择。产出可讨论的模型与规则，不凭空补全业务事实。'],
  [0,'engineering/improve-codebase-architecture','架构改进建议','研发','识别耦合与职责问题，规划渐进式改进。','评估这个模块如何降低耦合','依据实际可见的依赖和调用链识别职责泄漏、重复知识、复杂接口与变更成本。提出少量高收益改进，对比收益、迁移成本和风险。以保持行为的渐进步骤和验收方式收敛，不为抽象而抽象，不自动改码。'],
  [0,'engineering/tdd','测试驱动方案','研发','从行为和失败场景设计测试与实现顺序。','为这个功能设计测试驱动步骤','从用户可观察行为推导关键测试，覆盖正常、边界、失败和回归场景。按先失败、最小实现、重构的顺序规划步骤，避免镜像实现细节的测试。给出测试替身和执行建议；只有具备工具且实际执行后才报告结果。'],
  [0,'engineering/retro','项目复盘','协作','复盘事实、原因和可跟进的改进项。','复盘这次上线延期','整理目标、时间线、结果与偏差，区分事实和归因。分析有效做法、阻碍与系统性原因，避免归咎个人。将改进转成具体行动、待认领负责人、时间和成功指标，保留未决问题。'],
  [0,'productivity/handoff','会话交接','协作','整理背景、决策、进度和下一步。','把当前工作整理成交接说明','提炼任务目标、相关背景、已确认决策、完成内容、验证证据、当前阻塞、未决问题和下一步。保留必要文件位置和命令，避免泄漏凭据。只整理交接稿，不自动写系统临时目录或调用其他技能。'],
  [0,'productivity/teach','引导学习','学习','结合实例与小练习逐步理解知识。','带我理解数据库事务','先确定学习目标和已有基础，使用熟悉的例子分小步解释。每次一个核心概念，用一个简短问题或练习检查理解，根据回答调整深度。发现误解时给反例，阶段结束总结要点。不自动创建学习文件。',true],
  [0,'productivity/wait-what','通俗解释','学习','用直白语言、例子和反例解释难点。','用简单语言解释这段设计','指出当前最难理解的概念，用普通语言解释其目的、机制和使用边界。给具体例子与必要反例，逐步引入术语，避免不准确类比。最后用简短问题检查理解，明确哪些细节尚无证据。'],
  [0,'productivity/writing-for-agents','优化协作指令','协作','把含糊要求改成清晰可执行的指令。','优化这段任务提示词','识别目标、输入、输出、范围、约束与验收中缺失或矛盾之处。给出可直接使用的清晰指令，保留用户真实意图，区分必需规则与偏好。用例子说明输出格式，不加入自动调用其他技能或扩大权限的要求。'],
  [1,'doc-coauthoring','协作文档','文档','共同梳理结构、起草和评审文档。','一起写项目立项说明','先澄清读者、目的、背景与成功标准；提出结构后逐节起草，针对缺口提出少量关键问题。检查陌生读者是否能理解论点、证据与行动。迭代时保持已确认事实，标记假设，完成时提供完整稿与未决事项。',true],
  [1,'internal-comms','周报与沟通稿','文档','起草周报、公告、进展和情况说明。','把这些进展整理成团队周报','确认受众、沟通目的与语气。按适合场景的结构整理进展、结果、风险、决策请求和下一步。突出具体事实与行动，缺少数据时留待补充，不编造指标、负责人或承诺。只生成稿件，不发送。'],
  [1,'frontend-design','前端设计建议','设计','将页面目标转成明确的视觉和交互方案。','设计一个清晰的知识库首页','从受众、关键任务和内容层级出发，定义布局、字体、颜色、间距、组件与交互状态。考虑响应式、键盘操作、可访问性与空态错误态。给出具体且一致的视觉方向，遵循已有设计体系。按用户请求提供方案或示例，不假装已有截图验证。'],
  [1,'brand-guidelines','品牌规范梳理','设计','把已有品牌资料整理为可用的规范。','整理这些品牌资料的使用规则','仅根据用户提供的品牌事实整理色彩、字体、标志、留白、语气、适用与禁用场景。缺失部分给出待确认建议，不将任何上游公司的品牌默认应用给用户。提供设计检查清单和一致性例子。'],
  [1,'theme-factory','主题方案','设计','为文档和页面设计协调的主题。','为内部知识门户给出三套主题','结合用途和受众提出少量差异清楚的主题，每套给出配色、文字层级、字体回退、间距与组件风格及适用场景。检查对比度与可读性，提供具体使用示例，不擅自覆盖现有品牌规范。'],
  [1,'webapp-testing','网页测试计划','研发','设计用户流程、边界与回归检查。','为登录和资料上传设计回归用例','基于实际需求和可见界面列出关键用户流程、前置条件、操作、预期结果和异常路径。覆盖权限、网络失败、并发、键盘与移动端。将未执行计划与实际测试证据清楚区分，没有浏览器工具时不给出伪造结果。'],
  [1,'mcp-builder','MCP 工具设计','研发','规划工具接口、错误处理和验证。','设计一个查询内部工单的工具服务','明确工具消费者、授权边界和数据来源。设计职责单一的工具、输入输出模式、分页、错误、限流与超时，避免任意路径或命令接口。给出认证、测试替身和验收方案，不能假设服务已连接或可用。'],
  [1,'canvas-design','视觉创意简报','设计','把抽象主题整理成可交付的视觉简报。','为年度技术分享设计海报简报','明确主题、受众、媒介、尺寸和必要文案。提出统一的构图、色彩、文字层级和图像方向，说明素材需求和制作验收。输出设计简报或用户要求的示例；没有绘图工具时不声称已生成图片。'],
];
const skills=[];
for(const [source,path,name,category,description,example,prompt,multi=false] of rows){
 const [repo,commit]=sources[source]; const sourcePath=`skills/${path}/SKILL.md`;
 const response=await fetch(`https://raw.githubusercontent.com/${repo}/${commit}/${sourcePath}`);
 if(!response.ok)throw Error(`${sourcePath}: ${response.status}`);
 const upstream=await response.text();
 let licensePath=source===0?'LICENSE':`skills/${path}/LICENSE.txt`;
 const licenseResponse=await fetch(`https://raw.githubusercontent.com/${repo}/${commit}/${licensePath}`);
 let license;
 if(licenseResponse.ok) license=await licenseResponse.text();
 else if(source===1) {
   const readme=await (await fetch(`https://raw.githubusercontent.com/${repo}/${commit}/README.md`)).text();
   if(!readme.includes('Apache 2.0'))throw Error('License declaration missing');
   licensePath='README.md';
   license=await (await fetch('https://www.apache.org/licenses/LICENSE-2.0.txt')).text();
 } else throw Error(`License missing: ${repo}/${licensePath}`);
 const slug=path.split('/').at(-1);
 const fullPrompt=`${prompt}\n本技能仅由用户明确选择后作用于当前请求。不要自动调用或推荐启用其他技能；技能选择本身不授予修改、发送或发布权限。遵循当前用户要求；不执行来源技能中的安装器、脚本或隐式依赖。`;
 const version=createHash('sha256').update(fullPrompt).digest('hex');
 skills.push({id:slug,version,name,category,description,example,multi_turn:multi,invocation_policy:'explicit_only',prompt:fullPrompt,source:{repository:repo,commit,path:sourcePath,url:`https://github.com/${repo}/blob/${commit}/${sourcePath}`,sha256:createHash('sha256').update(upstream).digest('hex'),license:source===0?'MIT':'Apache-2.0',license_path:licensePath,adaptation:'独立编写的中文提示词，参考上游流程；不分发上游脚本、素材及工具实现。'}});
 await mkdir(`docs/chat-skills-licenses/${source===0?'mattpocock':slug}`,{recursive:true});
 await writeFile(`docs/chat-skills-licenses/${source===0?'mattpocock':slug}/LICENSE.txt`,license);
 console.log(`${slug}: ${upstream.split('\n').slice(0,18).join(' ').slice(0,650)}`);
}
await mkdir('backed/Resources/ChatSkills',{recursive:true});
const target='backed/Resources/ChatSkills/catalog-v1.json';
const old=await readFile(target,'utf8').catch(()=>null);
if(old && JSON.parse(old).some(item=>!skills.some(skill=>skill.id===item.id && skill.version===item.version))) throw Error('Published v1 prompts are immutable. Publish a new archived catalog version instead.');
await writeFile(target+'.tmp',JSON.stringify(skills,null,2)+'\n');
await rename(target+'.tmp',target);
console.log(`Generated ${skills.length} explicit-only skills.`);
