from __future__ import annotations

import re
from pathlib import Path
from typing import Iterable

from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.shared import Inches, Pt, RGBColor
from docx.text.paragraph import Paragraph
from docx.oxml import OxmlElement
from docx.oxml.ns import qn


ROOT = Path(__file__).resolve().parent
ASSETS = ROOT / "assets"
DOCX = ROOT / "坤伴Agent协同研发平台介绍手册.docx"
HTML = ROOT / "坤伴Agent协同研发平台介绍手册.html"


FIGURES = [
    ("在项目演示或阶段评审中，团队可以展示任务如何拆解", "chat-project-docs.png", "图 1 会话侧组织任务上下文，右侧打开项目文档并查看实际内容。"),
    ("以上是可由团队配置的分工示例。配置连线后", "work-canvas.png", "图 2 工作画布用节点和连线表达会话分工与上下游关系。"),
    ("看板改码链路包含工作区检查", "code-repository-center.png", "图 3 项目与代码库页面集中管理项目目录、仓库关联和项目说明。"),
    ("上述审批控制适用于平台变更集交付流程。", "project-run-git-check.png", "图 4 项目程序运行面板显示仓库同步状态、运行入口和交付前检查。"),
    ("部署方案应把人员、项目、模型和执行权限一并明确", "settings-center.png", "图 5 设置中心按外观、网络、模型、知识库、代码库、聊天和管理配置分组，便于管理员逐项开通。"),
]


def insert_after(paragraph: Paragraph) -> Paragraph:
    element = OxmlElement("w:p")
    paragraph._p.addnext(element)
    return Paragraph(element, paragraph._parent)


def format_caption(paragraph: Paragraph) -> None:
    paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
    paragraph.paragraph_format.space_before = Pt(2)
    paragraph.paragraph_format.space_after = Pt(8)
    run = paragraph.add_run()
    run.font.size = Pt(9)
    run.font.color.rgb = RGBColor(96, 120, 137)


def add_figure_after(paragraph: Paragraph, filename: str, caption: str) -> None:
    figure = insert_after(paragraph)
    figure.alignment = WD_ALIGN_PARAGRAPH.CENTER
    figure.paragraph_format.space_before = Pt(7)
    figure.paragraph_format.space_after = Pt(2)
    figure.add_run().add_picture(str(ASSETS / filename), width=Inches(6.35))
    caption_paragraph = insert_after(figure)
    caption_paragraph.add_run(caption)
    format_caption(caption_paragraph)


def add_docx_figures() -> None:
    document = Document(DOCX)
    if document.inline_shapes:
        return
    for anchor, filename, caption in FIGURES:
        target = next((p for p in document.paragraphs if anchor in p.text), None)
        if target is None:
            raise RuntimeError(f"DOCX anchor not found: {anchor}")
        add_figure_after(target, filename, caption)
    document.save(DOCX)


def add_html_figures() -> None:
    html = HTML.read_text(encoding="utf-8")
    if "class=\"manual-figure\"" not in html:
        style = ".manual-figure{margin:22px auto 26px;text-align:center}.manual-figure img{display:block;width:100%;max-height:70mm;object-fit:contain;border:1px solid var(--line);border-radius:8px;background:#f7fafb}.manual-figure figcaption{margin-top:7px;color:var(--muted);font-size:11px;line-height:1.6}@media print{.manual-figure{break-inside:avoid;margin:3mm auto}.manual-figure img{max-height:42mm}.manual-figure figcaption{font-size:8.5pt}}"
        html = html.replace("</style>", style + "</style>", 1)
    else:
        # Keep repeated builds reproducible when the HTML already contains figures.
        html = html.replace("@media print{.manual-figure{break-inside:avoid;margin:5mm auto}.manual-figure img{max-height:70mm}", "@media print{.manual-figure{break-inside:avoid;margin:3mm auto}.manual-figure img{max-height:42mm}")
        html = html.replace("@media print{.manual-figure{break-inside:avoid;margin:4mm auto}.manual-figure img{max-height:52mm}", "@media print{.manual-figure{break-inside:avoid;margin:3mm auto}.manual-figure img{max-height:42mm}")
    html = html.replace(".pagefooter{font-size:7.5pt;margin-top:4mm}", ".pagefooter{font-size:7pt;margin-top:1mm;padding-top:4px}")
    html = html.replace("#chapter-10{font-size:10pt;line-height:1.6}", "#chapter-5 .pagefooter,#chapter-6 .pagefooter{display:none}#chapter-10{font-size:10pt;line-height:1.6}")
    html = html.replace("#chapter-5 .pagefooter,#chapter-6 .pagefooter{display:none}", "#chapter-5 .pagefooter,#chapter-6 .pagefooter{display:none}#chapter-5 .manual-figure img,#chapter-6 .manual-figure img{max-height:30mm}")
    html = html.replace("#chapter-5 .manual-figure img,#chapter-6 .manual-figure img{max-height:30mm}", "#chapter-5 .manual-figure img,#chapter-6 .manual-figure img{max-height:30mm}#chapter-5,#chapter-6{font-size:9.5pt;line-height:1.6}")
    # The settings figure belongs to the deployment chapter. Older generated
    # HTML placed it after the first paragraph of the next chapter because the
    # deployment conclusion is a blockquote rather than a paragraph.
    html = re.sub(r'<figure class="manual-figure" data-figure="settings-center\.png">.*?</figure>', "", html, count=1, flags=re.S)
    deployment_anchor = "<blockquote>部署方案应把人员、项目、模型和执行权限一并明确，形成可验证的使用范围。</blockquote>"
    settings_figure = '<figure class="manual-figure" data-figure="settings-center.png"><img src="assets/settings-center.png" alt="图 5 设置中心按外观、网络、模型、知识库、代码库、聊天和管理配置分组，便于管理员逐项开通" loading="lazy"><figcaption>图 5 设置中心按外观、网络、模型、知识库、代码库、聊天和管理配置分组，便于管理员逐项开通。</figcaption></figure>'
    if "界面导览与建议用法" not in html:
        guide = (
            "<h3>界面导览与建议用法</h3>"
            "<p>平台的常用入口按“聊天协作、知识中心、研发工作台、设置管理”组织。首次试点时，建议先从左侧选择项目，再在会话中说明目标、范围和验收条件；需要查看代码时打开项目文档或代码库，需要拆解多阶段工作时进入工作画布，需要复用业务资料时进入知识中心。</p>"
            + settings_figure
            + "<p>界面截图用于说明当前版本的入口和信息组织方式。不同部署版本可能因权限、启用模块和屏幕尺寸出现差异，正式使用前应以目标环境的验收结果为准。</p>"
        )
        if deployment_anchor not in html:
            raise RuntimeError("HTML deployment anchor not found")
        html = html.replace(deployment_anchor, guide + deployment_anchor, 1)
    elif 'data-figure="settings-center.png"' not in html:
        # Reinsert the figure after the guide paragraph on repeated builds.
        guide_heading = "<h3>界面导览与建议用法</h3>"
        start = html.find(guide_heading)
        if start < 0:
            raise RuntimeError("HTML guide heading not found")
        paragraph_end = html.find("</p>", start)
        if paragraph_end < 0:
            raise RuntimeError("HTML guide paragraph end not found")
        paragraph_end += len("</p>")
        html = html[:paragraph_end] + settings_figure + html[paragraph_end:]
    for anchor, filename, caption in FIGURES:
        if filename == "settings-center.png":
            continue
        marker = f'data-figure="{filename}"'
        if marker in html:
            continue
        index = html.find(anchor)
        if index < 0:
            raise RuntimeError(f"HTML anchor not found: {anchor}")
        end = html.find("</p>", index)
        if end < 0:
            raise RuntimeError(f"HTML paragraph end not found: {anchor}")
        end += len("</p>")
        figure = f'<figure class="manual-figure" data-figure="{filename}"><img src="assets/{filename}" alt="{caption.rstrip("。")}" loading="lazy"><figcaption>{caption}</figcaption></figure>'
        html = html[:end] + figure + html[end:]
    HTML.write_text(html, encoding="utf-8")


if __name__ == "__main__":
    missing = [p.name for p in (ASSETS / name for _, name, _ in FIGURES) if not p.exists()]
    if missing:
        raise SystemExit(f"Missing image assets: {', '.join(missing)}")
    add_docx_figures()
    add_html_figures()
