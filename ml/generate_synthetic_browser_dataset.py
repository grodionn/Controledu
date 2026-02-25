from __future__ import annotations

import argparse
import html
import json
import os
import random
import tempfile
import time
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from io import BytesIO
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

try:
    from selenium import webdriver
    from selenium.common.exceptions import TimeoutException, WebDriverException
    from selenium.webdriver.chrome.options import Options as ChromeOptions
    from selenium.webdriver.chrome.service import Service as ChromeService
    from selenium.webdriver.edge.options import Options as EdgeOptions
    from selenium.webdriver.edge.service import Service as EdgeService
except ImportError as exc:
    raise SystemExit("Install selenium first: pip install selenium") from exc


AI_LABELS = [
    "chatgpt_ui",
    "claude_ui",
    "gemini_ui",
    "copilot_ui",
    "perplexity_ui",
    "deepseek_ui",
    "poe_ui",
    "grok_ui",
    "qwen_ui",
    "mistral_ui",
    "meta_ai_ui",
    "ai_ui",
]
NOT_AI_LABEL = "not_ai_ui"
BRANDS = {
    "chatgpt_ui": ("ChatGPT", "OpenAI", "chatgpt.com"),
    "claude_ui": ("Claude", "Anthropic", "claude.ai"),
    "gemini_ui": ("Gemini", "Google", "gemini.google.com"),
    "copilot_ui": ("Copilot", "Microsoft", "copilot.microsoft.com"),
    "perplexity_ui": ("Perplexity", "Perplexity", "perplexity.ai"),
    "deepseek_ui": ("DeepSeek", "DeepSeek", "chat.deepseek.com"),
    "poe_ui": ("Poe", "Quora", "poe.com"),
    "grok_ui": ("Grok", "xAI", "grok.com"),
    "qwen_ui": ("Qwen", "Alibaba", "chat.qwen.ai"),
    "mistral_ui": ("Le Chat", "Mistral", "chat.mistral.ai"),
    "meta_ai_ui": ("Meta AI", "Meta", "meta.ai"),
    "ai_ui": ("Assistant", "Generic", "assistant.local"),
}
DETECTION_CLASS_BY_LABEL = {
    "chatgpt_ui": "ChatGpt",
    "claude_ui": "Claude",
    "gemini_ui": "Gemini",
    "copilot_ui": "Copilot",
    "perplexity_ui": "Perplexity",
    "deepseek_ui": "DeepSeek",
    "poe_ui": "Poe",
    "grok_ui": "UnknownAi",
    "qwen_ui": "UnknownAi",
    "mistral_ui": "UnknownAi",
    "meta_ai_ui": "UnknownAi",
    "ai_ui": "UnknownAi",
    "not_ai_ui": "None",
}
TOPICS = [
    "Explain this bug",
    "Summarize chapter 5",
    "Write SQL query",
    "Generate test cases",
    "Optimize this function",
    "Explain async and await",
]

DEFAULT_REAL_CATALOG: dict[str, Any] = {
    "ai": {
        "chatgpt_ui": [
            {"url": "https://chatgpt.com/", "title": "ChatGPT"},
            {"url": "https://openai.com/chatgpt/", "title": "OpenAI ChatGPT"},
        ],
        "claude_ui": [
            {"url": "https://claude.ai/", "title": "Claude"},
            {"url": "https://www.anthropic.com/claude", "title": "Anthropic Claude"},
        ],
        "gemini_ui": [
            {"url": "https://gemini.google.com/", "title": "Gemini"},
            {"url": "https://deepmind.google/technologies/gemini/", "title": "Google Gemini"},
        ],
        "copilot_ui": [
            {"url": "https://copilot.microsoft.com/", "title": "Microsoft Copilot"},
            {"url": "https://www.microsoft.com/microsoft-copilot", "title": "Copilot"},
        ],
        "perplexity_ui": [
            {"url": "https://www.perplexity.ai/", "title": "Perplexity"},
        ],
        "deepseek_ui": [
            {"url": "https://chat.deepseek.com/", "title": "DeepSeek"},
            {"url": "https://www.deepseek.com/", "title": "DeepSeek"},
        ],
        "poe_ui": [
            {"url": "https://poe.com/", "title": "Poe"},
        ],
        "grok_ui": [
            {"url": "https://grok.com/", "title": "Grok"},
            {"url": "https://x.ai/", "title": "xAI"},
        ],
        "qwen_ui": [
            {"url": "https://chat.qwen.ai/", "title": "Qwen"},
            {"url": "https://www.alibabacloud.com/help/en/model-studio/developer-reference/what-is-qwen-llm", "title": "Qwen"},
        ],
        "mistral_ui": [
            {"url": "https://chat.mistral.ai/", "title": "Le Chat"},
            {"url": "https://mistral.ai/", "title": "Mistral AI"},
        ],
        "meta_ai_ui": [
            {"url": "https://www.meta.ai/", "title": "Meta AI"},
            {"url": "https://www.meta.ai/ai-assistant", "title": "Meta AI Assistant"},
        ],
        "ai_ui": [
            {"url": "https://huggingface.co/chat/", "title": "HuggingFace Chat"},
            {"url": "https://platform.openai.com/playground", "title": "OpenAI Playground"},
        ],
    },
    "not_ai": [
        {"url": "https://www.wikipedia.org/", "title": "Wikipedia"},
        {"url": "https://docs.python.org/3/", "title": "Python Docs"},
        {"url": "https://learn.microsoft.com/en-us/", "title": "Microsoft Learn"},
        {"url": "https://news.ycombinator.com/", "title": "Hacker News"},
        {"url": "https://github.com/trending", "title": "GitHub Trending"},
        {"url": "https://stackoverflow.com/questions", "title": "Stack Overflow"},
        {"url": "https://www.nasa.gov/", "title": "NASA"},
    ],
    "hard_negative": [
        {"url": "https://en.wikipedia.org/wiki/ChatGPT", "title": "ChatGPT - Wikipedia"},
        {"url": "https://en.wikipedia.org/wiki/Claude_(language_model)", "title": "Claude - Wikipedia"},
        {"url": "https://en.wikipedia.org/wiki/Gemini_(chatbot)", "title": "Gemini - Wikipedia"},
        {"url": "https://learn.microsoft.com/en-us/azure/ai-services/openai/", "title": "Azure OpenAI docs"},
        {"url": "https://openai.com/research/", "title": "OpenAI Research"},
        {"url": "https://www.anthropic.com/news", "title": "Anthropic News"},
        {"url": "https://deepmind.google/technologies/gemini/", "title": "DeepMind Gemini"},
    ],
}


@dataclass(frozen=True)
class SyntheticSample:
    image_rel: str
    label: str
    metadata_rel: str
    source: str
    notes: str


@dataclass(frozen=True)
class RealTarget:
    url: str
    title_hint: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate browser synthetic dataset from live pages and/or HTML templates.")
    parser.add_argument("--dataset-root", default="dataset")
    parser.add_argument("--count", type=int, default=2000)
    parser.add_argument("--ai-ratio", type=float, default=0.5)
    parser.add_argument("--hard-negative-ratio", type=float, default=0.35)
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--prefix", default="synthetic-browser-v2")
    parser.add_argument("--browser", choices=["auto", "edge", "chrome"], default="auto")
    parser.add_argument("--wait-ms", type=int, default=120)
    parser.add_argument("--restart-every", type=int, default=250)
    parser.add_argument("--page-source", choices=["template", "live", "hybrid"], default="hybrid")
    parser.add_argument("--real-pages-config", default="ml/real_pages_catalog.json")
    parser.add_argument("--page-load-timeout-ms", type=int, default=18000)
    parser.add_argument("--max-attempts-multiplier", type=int, default=6)
    parser.add_argument("--user-agent", default="")
    parser.add_argument(
        "--ai-label-policy",
        choices=["uniform", "dataset", "inverse-frequency"],
        default="uniform",
        help="How to sample AI labels. 'dataset' follows current dataset distribution; 'inverse-frequency' boosts rare labels.",
    )
    parser.add_argument(
        "--dataset-labels-csv",
        default="",
        help="Optional labels CSV path for ai-label-policy=dataset|inverse-frequency. Default: <dataset-root>/labels/classification.csv",
    )
    parser.add_argument("--label-column", default="label", help="Label column name in labels CSV.")
    parser.add_argument("--dataset-label-floor", type=float, default=1.0, help="Pseudo-count floor for dataset-based AI label sampling.")
    parser.add_argument("--no-browser-chrome-overlay", action="store_true", help="Do not draw pseudo browser chrome over screenshots.")
    parser.add_argument("--debug-traceback", action="store_true", help="Print full Python traceback on errors.")
    return parser.parse_args()


def resolve_labels_csv(dataset_root: Path, dataset_labels_csv: str) -> Path:
    raw = str(dataset_labels_csv).strip()
    if not raw:
        return dataset_root / "labels" / "classification.csv"
    path = Path(raw)
    if path.is_absolute():
        return path
    return path.resolve()


def load_ai_label_counts(labels_csv: Path, label_column: str) -> dict[str, int]:
    if not labels_csv.exists():
        return {}
    frame = pd.read_csv(labels_csv, dtype=str).fillna("")
    if label_column not in frame.columns:
        return {}
    series = frame[label_column].astype(str).str.strip()
    counts = series.value_counts().to_dict()
    return {str(key): int(value) for key, value in counts.items()}


def compute_ai_sampling_weights(
    policy: str,
    label_counts: dict[str, int],
    floor: float,
) -> list[float]:
    floor = max(0.0, float(floor))
    if policy == "dataset":
        weights = [float(label_counts.get(label, 0)) + floor for label in AI_LABELS]
    elif policy == "inverse-frequency":
        weights = [1.0 / ((float(label_counts.get(label, 0)) + 1.0) ** 0.7) + floor for label in AI_LABELS]
    else:
        weights = [1.0 for _ in AI_LABELS]
    if sum(weights) <= 0:
        return [1.0 for _ in AI_LABELS]
    return weights


def format_ai_weight_report(weights: list[float]) -> str:
    total = max(1e-9, float(sum(weights)))
    parts = []
    for label, weight in zip(AI_LABELS, weights):
        parts.append(f"{label}={weight / total:.3f}")
    return ", ".join(parts)


def create_driver(kind: str, width: int, height: int, page_load_timeout_ms: int, user_agent: str):
    choices = ["edge", "chrome"] if kind == "auto" else [kind]
    errors: list[str] = []
    for candidate in choices:
        try:
            if candidate == "edge":
                options = EdgeOptions()
                options.use_chromium = True
            else:
                options = ChromeOptions()
            options.add_argument("--headless=new")
            options.add_argument("--disable-gpu")
            options.add_argument("--hide-scrollbars")
            options.add_argument("--disable-dev-shm-usage")
            options.add_argument("--disable-blink-features=AutomationControlled")
            options.add_argument("--window-position=0,0")
            options.add_argument("--log-level=3")
            options.add_argument("--silent")
            options.add_argument("--remote-debugging-pipe")
            # Suppress Chromium startup diagnostics in stderr.
            options.add_experimental_option("excludeSwitches", ["enable-logging"])
            if user_agent.strip():
                options.add_argument(f"--user-agent={user_agent.strip()}")
            if candidate == "edge":
                service = EdgeService(log_output=os.devnull)
                driver = webdriver.Edge(options=options, service=service)
            else:
                service = ChromeService(log_output=os.devnull)
                driver = webdriver.Chrome(options=options, service=service)
            driver.set_window_size(width, height)
            driver.set_page_load_timeout(max(5, int(page_load_timeout_ms / 1000)))
            driver.set_script_timeout(max(5, int(page_load_timeout_ms / 1000)))
            return driver, candidate
        except WebDriverException as ex:
            errors.append(f"{candidate}: {ex.__class__.__name__}")
    raise RuntimeError(
        "Failed to start browser driver. "
        + "; ".join(errors)
        + ". Ensure Google Chrome or Microsoft Edge is installed and up to date."
    )


def load_catalog(path: Path) -> dict[str, Any]:
    if not path.exists():
        return DEFAULT_REAL_CATALOG
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError("real pages config must be a JSON object")
    merged = dict(DEFAULT_REAL_CATALOG)
    for key, value in data.items():
        if key == "ai" and isinstance(value, dict) and isinstance(merged.get("ai"), dict):
            ai_map = dict(merged["ai"])
            ai_map.update(value)
            merged["ai"] = ai_map
        else:
            merged[key] = value
    return merged


def parse_targets(items: Any) -> list[RealTarget]:
    targets: list[RealTarget] = []
    if not isinstance(items, list):
        return targets
    for item in items:
        if isinstance(item, str):
            value = item.strip()
            if value:
                targets.append(RealTarget(url=value, title_hint=value))
        elif isinstance(item, dict):
            url = str(item.get("url", "")).strip()
            title = str(item.get("title", "")).strip() or url
            if url:
                targets.append(RealTarget(url=url, title_hint=title))
    return targets


def catalog_targets_for_label(catalog: dict[str, Any], label: str, hard_negative: bool) -> list[RealTarget]:
    if label != NOT_AI_LABEL:
        ai_section = catalog.get("ai", {})
        if isinstance(ai_section, dict):
            own = parse_targets(ai_section.get(label))
            if own:
                return own
            fallback = parse_targets(ai_section.get("ai_ui"))
            if fallback:
                return fallback
        return []
    section_name = "hard_negative" if hard_negative else "not_ai"
    section = parse_targets(catalog.get(section_name))
    if section:
        return section
    return parse_targets(catalog.get("not_ai"))


def rand_palette(rng: random.Random) -> dict[str, str]:
    dark = rng.random() < 0.55
    if dark:
        return {
            "bg": f"hsl({rng.randint(210,240)} 26% {rng.randint(8,13)}%)",
            "surface": f"hsl({rng.randint(210,240)} 19% {rng.randint(14,20)}%)",
            "text": "hsl(220 16% 92%)",
            "muted": "hsl(220 9% 72%)",
            "border": "hsl(220 14% 28%)",
            "accent": f"hsl({rng.randint(165,230)} 85% 56%)",
        }
    return {
        "bg": f"hsl({rng.randint(210,240)} 35% {rng.randint(95,98)}%)",
        "surface": "hsl(0 0% 100%)",
        "text": "hsl(220 25% 15%)",
        "muted": "hsl(220 10% 43%)",
        "border": "hsl(220 14% 84%)",
        "accent": f"hsl({rng.randint(165,230)} 85% 52%)",
    }


def shell_html(title: str, url: str, body: str, palette: dict[str, str], rng: random.Random) -> str:
    font = rng.choice(["'Inter','Segoe UI',system-ui,sans-serif", "'Segoe UI','Inter',system-ui,sans-serif"])
    return f"""
<!doctype html><html><head><meta charset='utf-8'/>
<style>
*{{box-sizing:border-box}}html,body{{height:100%;margin:0;font-family:{font};background:
radial-gradient(circle at 15% 0%, color-mix(in oklab,var(--accent) 14%,transparent), transparent 36%),var(--bg);}}
:root{{--bg:{palette['bg']};--surface:{palette['surface']};--text:{palette['text']};--muted:{palette['muted']};--border:{palette['border']};--accent:{palette['accent']};}}
body{{color:var(--text)}}.app{{height:100%;display:grid;grid-template-rows:40px 34px 1fr}}
.bar,.nav{{display:flex;align-items:center;padding:0 10px;border-bottom:1px solid var(--border);background:color-mix(in oklab,var(--surface) 92%, #000 8%)}}
.bar{{justify-content:space-between;font-size:12px}}.dots{{display:flex;gap:7px}}.dots i{{display:block;width:9px;height:9px;border-radius:999px;background:#f87171}}
.dots i:nth-child(2){{background:#fbbf24}} .dots i:nth-child(3){{background:#34d399}}
.pill{{font-size:11px;border:1px solid var(--border);border-radius:999px;padding:2px 8px;color:var(--muted)}}
.url{{margin:0 10px;flex:1;height:24px;border:1px solid var(--border);border-radius:999px;padding:0 10px;color:var(--muted);display:flex;align-items:center;overflow:hidden;white-space:nowrap;text-overflow:ellipsis}}
.btn{{width:24px;height:22px;border:1px solid var(--border);border-radius:8px;background:var(--surface)}}
.main{{min-height:0;overflow:hidden}}.surface{{background:var(--surface);border:1px solid var(--border);border-radius:12px}}
.line{{height:9px;border-radius:999px;background:color-mix(in oklab,var(--muted) 30%,transparent);margin-bottom:8px}}
.muted{{color:var(--muted)}}
</style></head><body>
<div class='app'>
 <div class='bar'><div class='dots'><i></i><i></i><i></i></div><div>{html.escape(title)}</div><div class='pill'>Secure</div></div>
 <div class='nav'><button class='btn'></button><button class='btn' style='margin-left:6px'></button><div class='url'>{html.escape(url)}</div><button class='btn'></button></div>
 <div class='main'>{body}</div>
</div></body></html>
"""


def ai_layout(label: str, rng: random.Random) -> tuple[str, str, str, str]:
    brand, vendor, host = BRANDS[label]
    title = f"{brand} - {vendor}"
    topic = rng.choice(TOPICS)
    url = f"https://{host}/?q={topic.replace(' ', '+')}"
    process = rng.choice(["chrome", "msedge", "firefox"])
    side = "".join(f"<div class='surface' style='padding:9px;font-size:12px;margin-bottom:7px'>{rng.choice(TOPICS)}</div>" for _ in range(rng.randint(8, 12)))
    bubbles: list[str] = []
    for i in range(rng.randint(8, 13)):
        lines = "".join(f"<div class='line' style='width:{rng.randint(52,96)}%'></div>" for _ in range(rng.randint(2, 5)))
        txt = rng.choice(TOPICS if i % 2 == 0 else ["Sure, here is the structured answer", "Step-by-step plan", "Proposed solution with risks"])
        bubbles.append(f"<div class='surface' style='padding:10px;margin-bottom:10px'><div style='font-size:12px;margin-bottom:6px'>{html.escape(txt)}</div>{lines}</div>")
    with_sources = rng.random() < 0.45
    sources = ""
    if with_sources:
        cards = "".join(
            f"<div class='surface' style='padding:8px;margin-bottom:7px'><div style='font-size:11px;font-weight:600'>Source {i + 1}</div><div class='muted' style='font-size:11px'>Reference snippet</div></div>"
            for i in range(rng.randint(4, 7))
        )
        sources = f"<aside style='width:290px;padding:12px 12px 12px 0'><div class='surface' style='height:100%;padding:10px;overflow:auto'>{cards}</div></aside>"
    body = (
        "<div style='display:flex;height:100%'>"
        f"<aside style='width:240px;padding:12px'><div class='surface' style='height:100%;padding:10px;overflow:auto'><div style='font-weight:700;margin-bottom:10px'>{brand}</div>{side}</div></aside>"
        "<section style='flex:1;min-width:0;padding:12px 0 12px 0;display:flex;flex-direction:column'>"
        f"<div class='surface' style='padding:10px;margin:0 12px 10px 0;font-size:13px;font-weight:600;color:var(--accent)'>{brand} conversation</div>"
        f"<div class='surface' style='padding:12px;overflow:auto;flex:1;margin:0 12px 0 0'>{''.join(bubbles)}</div>"
        "<div class='surface' style='padding:10px;margin:10px 12px 0 0;display:flex;align-items:center'><span class='pill'>Attach</span><span class='muted' style='margin-left:8px;font-size:12px'>Type your message...</span></div>"
        "</section>"
        f"{sources}</div>"
    )
    return body, process, title, url


def not_ai_layout(rng: random.Random, hard_negative: bool) -> tuple[str, str, str, str]:
    mode = rng.choice(["wiki", "docs", "dashboard", "mail", "sheet"])
    process = rng.choice(["chrome", "msedge", "outlook", "excel"])
    if mode == "wiki":
        topic = rng.choice(["Machine learning", "Computer network", "Calculus", "Project management"])
        subtitle = "ChatGPT and AI tools overview" if hard_negative else "Knowledge article"
        title = f"{topic} - Wikipedia"
        url = f"https://en.wikipedia.org/wiki/{topic.replace(' ', '_')}"
        sections = "".join(
            "<div class='surface' style='padding:10px;margin-bottom:9px'>"
            + "".join(f"<div class='line' style='width:{rng.randint(58,98)}%'></div>" for _ in range(rng.randint(4, 8)))
            + "</div>"
            for _ in range(rng.randint(5, 8))
        )
        body = (
            "<div style='display:flex;height:100%'>"
            "<aside style='width:250px;padding:12px'><div class='surface' style='height:100%;padding:10px;overflow:auto'>"
            + "".join(f"<div class='muted' style='font-size:12px;margin-bottom:7px'>{i + 1}. Section</div>" for i in range(14))
            + "</div></aside>"
            f"<section style='flex:1;padding:12px 12px 12px 0;min-width:0'><div class='surface' style='height:100%;padding:14px;overflow:auto'><h2 style='margin:0'>{topic}</h2><div class='muted' style='font-size:12px;margin-bottom:10px'>{subtitle}</div>{sections}</div></section></div>"
        )
        return body, process, title, url
    if mode == "docs":
        title = "Documentation"
        url = "https://docs.python.org/3/"
        nav = "".join(f"<div class='surface' style='padding:8px;font-size:12px;margin-bottom:7px'>API section {i + 1}</div>" for i in range(14))
        page = "".join(f"<div class='line' style='width:{rng.randint(54,98)}%'></div>" for _ in range(30))
        body = f"<div style='display:grid;grid-template-columns:270px 1fr;height:100%'><aside style='padding:12px'><div class='surface' style='height:100%;padding:10px;overflow:auto'>{nav}</div></aside><section style='padding:12px 12px 12px 0'><div class='surface' style='height:100%;padding:14px;overflow:auto'><h2 style='margin:0 0 8px 0'>Documentation</h2>{page}</div></section></div>"
        return body, process, title, url
    if mode == "dashboard":
        title = "Operations dashboard"
        url = "https://portal.example.com/monitoring"
        cards = "".join(f"<div class='surface' style='padding:10px'><div class='muted' style='font-size:11px'>Metric</div><div style='font-size:24px;font-weight:700'>{rng.randint(20,999)}</div><div style='height:34px;background:linear-gradient(180deg,var(--accent),transparent);border-radius:8px'></div></div>" for _ in range(6))
        rows = "".join(f"<tr><td>device-{i}</td><td>{rng.randint(1,900)}</td><td>{rng.choice(['ok','warn','down'])}</td></tr>" for i in range(18))
        body = f"<div style='padding:12px;height:100%;overflow:auto'><div style='display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin-bottom:8px'>{cards}</div><div class='surface' style='padding:10px'><table style='width:100%;border-collapse:collapse;font-size:12px'>{rows}</table></div></div><style>td{{padding:6px;border-bottom:1px solid var(--border)}}</style>"
        return body, process, title, url
    if mode == "mail":
        title = "Inbox"
        url = "https://mail.example.com/inbox"
        letters = "".join(f"<div class='surface' style='padding:9px;margin-bottom:8px;display:grid;grid-template-columns:180px 1fr 80px;gap:8px'><div style='font-size:12px;font-weight:600'>Sender {i}</div><div class='muted' style='font-size:12px'>Message preview {i}</div><div class='muted' style='font-size:11px;text-align:right'>{rng.randint(7,22)}:{rng.randint(0,59):02d}</div></div>" for i in range(20))
        body = f"<div style='display:grid;grid-template-columns:260px 1fr;height:100%'><aside style='padding:12px'><div class='surface' style='height:100%;padding:10px'><span class='pill'>Compose</span></div></aside><section style='padding:12px 12px 12px 0;overflow:auto'>{letters}</section></div>"
        return body, process, title, url
    title = "Budget spreadsheet"
    url = "https://office.example.com/sheet"
    rows = "".join("<tr>" + "".join(f"<td>{rng.randint(0,9999)}</td>" for _ in range(10)) + "</tr>" for _ in range(30))
    body = f"<div style='padding:12px;height:100%'><div class='surface' style='height:100%;padding:10px;overflow:auto'><table style='width:100%;border-collapse:collapse;font-size:12px'>{rows}</table></div></div><style>td{{border:1px solid var(--border);padding:6px}}</style>"
    return body, process, title, url


def render_template_page(driver, html_path: Path, page_html: str, wait_ms: int, rng: random.Random) -> Image.Image:
    html_path.write_text(page_html, encoding="utf-8")
    driver.get(html_path.as_uri())
    maybe_scroll(driver, rng)
    if wait_ms > 0:
        time.sleep(wait_ms / 1000.0)
    return Image.open(BytesIO(driver.get_screenshot_as_png())).convert("RGB")


def render_live_page(driver, url: str, wait_ms: int, rng: random.Random) -> tuple[Image.Image, str, str]:
    driver.get(url)
    maybe_scroll(driver, rng)
    if wait_ms > 0:
        time.sleep(wait_ms / 1000.0)
    image = Image.open(BytesIO(driver.get_screenshot_as_png())).convert("RGB")
    title = str(driver.title or "").strip()
    current_url = str(driver.current_url or url)
    return image, title, current_url


def maybe_scroll(driver, rng: random.Random) -> None:
    if rng.random() >= 0.7:
        return
    try:
        max_scroll = driver.execute_script(
            "return Math.max(document.body.scrollHeight, document.documentElement.scrollHeight) - window.innerHeight;"
        )
        if isinstance(max_scroll, (int, float)) and max_scroll > 100:
            driver.execute_script("window.scrollTo(0, arguments[0]);", rng.randint(0, int(max_scroll)))
    except Exception:
        return


def overlay_browser_chrome(image: Image.Image, title: str, url: str, rng: random.Random) -> Image.Image:
    if image.width < 500 or image.height < 300:
        return image
    if rng.random() < 0.28:
        return image
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    top_h = int(max(54, min(74, image.height * 0.095)))
    nav_h = int(max(30, top_h * 0.56))
    draw.rectangle((0, 0, image.width, top_h), fill=(18, 21, 28, 205))
    draw.rectangle((0, top_h - nav_h, image.width, top_h), fill=(24, 28, 36, 212))
    dot_y = int(top_h * 0.32)
    dot_r = 5
    colors = [(255, 96, 92, 210), (255, 189, 68, 210), (0, 202, 78, 210)]
    x = 18
    for color in colors:
        draw.ellipse((x, dot_y - dot_r, x + dot_r * 2, dot_y + dot_r), fill=color)
        x += 16
    pill_left = int(image.width * 0.19)
    pill_right = int(image.width * 0.9)
    pill_top = top_h - nav_h + 6
    pill_bottom = top_h - 6
    draw.rounded_rectangle((pill_left, pill_top, pill_right, pill_bottom), radius=12, fill=(52, 58, 68, 235), outline=(79, 86, 98, 220), width=1)
    title_text = (title or "Browser").strip()[:80]
    url_text = (url or "").strip()[:105]
    draw.text((int(image.width * 0.08), 8), title_text, fill=(228, 232, 241, 225))
    draw.text((pill_left + 10, pill_top + 5), url_text, fill=(198, 205, 217, 225))
    return Image.alpha_composite(image.convert("RGBA"), overlay).convert("RGB")


def jpeg_roundtrip(image: Image.Image, quality: int) -> Image.Image:
    buffer = BytesIO()
    image.save(buffer, format="JPEG", quality=max(20, min(95, quality)), optimize=True)
    buffer.seek(0)
    return Image.open(buffer).convert("RGB")


def add_chromatic_shift(image: Image.Image, rng: random.Random) -> Image.Image:
    arr = np.asarray(image).astype(np.int16)
    shift_x = rng.randint(-2, 2)
    shift_y = rng.randint(-2, 2)
    if shift_x == 0 and shift_y == 0:
        return image
    red = np.roll(arr[:, :, 0], shift=(shift_y, shift_x), axis=(0, 1))
    blue = np.roll(arr[:, :, 2], shift=(-shift_y, -shift_x), axis=(0, 1))
    arr[:, :, 0] = red
    arr[:, :, 2] = blue
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))


def add_vignette(image: Image.Image, rng: random.Random) -> Image.Image:
    strength = rng.uniform(0.06, 0.24)
    arr = np.asarray(image).astype(np.float32)
    height, width = arr.shape[0], arr.shape[1]
    xs = np.linspace(-1.0, 1.0, width, dtype=np.float32)
    ys = np.linspace(-1.0, 1.0, height, dtype=np.float32)
    x_grid, y_grid = np.meshgrid(xs, ys)
    radius = np.sqrt(x_grid * x_grid + y_grid * y_grid)
    mask = 1.0 - np.clip(radius, 0, 1.42) * strength
    arr *= mask[:, :, None]
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))


def add_scanlines(image: Image.Image, rng: random.Random) -> Image.Image:
    arr = np.asarray(image).astype(np.int16)
    step = rng.randint(2, 5)
    darken = rng.randint(4, 12)
    arr[::step, :, :] = np.clip(arr[::step, :, :] - darken, 0, 255)
    return Image.fromarray(arr.astype(np.uint8))


def draw_mouse_cursor(image: Image.Image, rng: random.Random) -> Image.Image:
    if rng.random() >= 0.35:
        return image
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    x = rng.randint(20, image.width - 40)
    y = rng.randint(20, image.height - 40)
    pts = [(x, y), (x + 12, y + 28), (x + 16, y + 18), (x + 28, y + 25)]
    draw.polygon(pts, fill=(255, 255, 255, 230), outline=(30, 30, 30, 255))
    return Image.alpha_composite(image.convert("RGBA"), overlay).convert("RGB")


def draw_popup_overlay(image: Image.Image, rng: random.Random) -> Image.Image:
    if rng.random() >= 0.28:
        return image
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    w = rng.randint(int(image.width * 0.22), int(image.width * 0.42))
    h = rng.randint(44, 120)
    x0 = rng.randint(8, max(8, image.width - w - 8))
    y0 = rng.randint(8, max(8, image.height - h - 8))
    x1 = x0 + w
    y1 = y0 + h
    draw.rounded_rectangle((x0, y0, x1, y1), radius=10, fill=(245, 245, 245, rng.randint(20, 70)), outline=(255, 255, 255, 90), width=1)
    return Image.alpha_composite(image.convert("RGBA"), overlay).convert("RGB")


def apply_artifacts(image: Image.Image, rng: random.Random, np_rng: np.random.Generator) -> Image.Image:
    out = image
    if rng.random() < 0.92:
        out = ImageEnhance.Brightness(out).enhance(rng.uniform(0.84, 1.14))
    if rng.random() < 0.90:
        out = ImageEnhance.Contrast(out).enhance(rng.uniform(0.82, 1.18))
    if rng.random() < 0.86:
        out = ImageEnhance.Color(out).enhance(rng.uniform(0.80, 1.20))
    if rng.random() < 0.48:
        out = out.filter(ImageFilter.GaussianBlur(radius=rng.uniform(0.2, 1.7)))
    if rng.random() < 0.64:
        w, h = out.size
        scale = rng.uniform(0.66, 0.97)
        out = out.resize((max(320, int(w * scale)), max(220, int(h * scale))), Image.Resampling.BILINEAR)
        out = out.resize((w, h), Image.Resampling.BICUBIC)

    arr = np.asarray(out).astype(np.int16)
    if rng.random() < 0.88:
        arr = arr + np_rng.normal(0.0, rng.uniform(2.0, 12.0), size=arr.shape).astype(np.int16)
    if rng.random() < 0.3:
        arr = arr + np_rng.integers(-16, 17, size=(arr.shape[0], 1, arr.shape[2]), dtype=np.int16)
    out = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))

    if rng.random() < 0.54:
        out = jpeg_roundtrip(out, quality=rng.randint(28, 88))
    if rng.random() < 0.36:
        out = add_chromatic_shift(out, rng)
    if rng.random() < 0.42:
        out = add_scanlines(out, rng)
    if rng.random() < 0.64:
        out = add_vignette(out, rng)
    out = draw_popup_overlay(out, rng)
    out = draw_mouse_cursor(out, rng)
    if rng.random() < 0.18:
        out = out.filter(ImageFilter.SHARPEN)
    return out


def a_hash_hex(image: Image.Image) -> str:
    gray = image.convert("L").resize((8, 8), Image.Resampling.LANCZOS)
    pixels = np.asarray(gray)
    bits = (pixels > pixels.mean()).flatten()
    value = 0
    for bit in bits:
        value = (value << 1) | int(bit)
    return f"{value:016X}"


def append_labels_csv(labels_csv: Path, samples: list[SyntheticSample]) -> None:
    frame = pd.read_csv(labels_csv, dtype=str).fillna("") if labels_csv.exists() else pd.DataFrame(columns=["image_path", "label", "source", "notes"])
    for column in ("image_path", "label", "source", "notes"):
        if column not in frame.columns:
            frame[column] = ""
    existing = {str(value).replace("\\", "/") for value in frame["image_path"].astype(str).tolist()}
    rows: list[dict[str, str]] = []
    for sample in samples:
        if sample.image_rel in existing:
            continue
        rows.append(
            {
                "image_path": sample.image_rel,
                "label": sample.label,
                "source": sample.source,
                "notes": sample.notes,
            }
        )
    if rows:
        frame = pd.concat([frame, pd.DataFrame(rows)], ignore_index=True)
        frame = frame.drop_duplicates(subset=["image_path"], keep="last").sort_values("image_path").reset_index(drop=True)
    labels_csv.parent.mkdir(parents=True, exist_ok=True)
    frame.to_csv(labels_csv, index=False, encoding="utf-8")


def build_metadata(label: str, process: str, title: str, url: str, timestamp: datetime, frame_hash: str, rng: random.Random, source: str) -> dict[str, Any]:
    is_ai = label != NOT_AI_LABEL
    confidence = round(rng.uniform(0.76, 0.98), 3) if is_ai else round(rng.uniform(0.01, 0.26), 3)
    return {
        "studentId": "synthetic-browser-generator",
        "timestampUtc": timestamp.isoformat(),
        "screenFrameHash": frame_hash,
        "frameChanged": True,
        "activeProcessName": process,
        "activeWindowTitle": title,
        "browserHintUrl": url,
        "detection": {
            "isAiUiDetected": is_ai,
            "confidence": confidence,
            "class": DETECTION_CLASS_BY_LABEL.get(label, "UnknownAi"),
            "stageSource": "OnnxMulticlass" if is_ai else "MetadataRule",
            "reason": f"Synthetic sample ({source})",
            "modelVersion": "synthetic-browser-v2",
            "isStable": is_ai,
            "triggeredKeywords": None,
        },
    }


def choose_process_name(engine: str) -> str:
    if engine == "edge":
        return "msedge"
    if engine == "chrome":
        return "chrome"
    return "browser"


def generate(args: argparse.Namespace) -> None:
    if args.count < 1:
        raise ValueError("--count must be > 0")
    if args.width < 640 or args.height < 360:
        raise ValueError("Resolution too low")
    if args.max_attempts_multiplier < 1:
        raise ValueError("--max-attempts-multiplier must be >= 1")

    dataset_root = Path(args.dataset_root).resolve()
    raw_root = dataset_root / "raw" / args.prefix
    labels_root = dataset_root / "labels"
    raw_root.mkdir(parents=True, exist_ok=True)
    labels_root.mkdir(parents=True, exist_ok=True)
    labels_csv = labels_root / "classification.csv"

    catalog = load_catalog(Path(args.real_pages_config))

    rng = random.Random(args.seed)
    np_rng = np.random.default_rng(args.seed)
    ai_ratio = min(max(args.ai_ratio, 0.0), 1.0)
    hard_ratio = min(max(args.hard_negative_ratio, 0.0), 1.0)
    ai_count_target = int(round(args.count * ai_ratio))
    labels_csv_for_policy = resolve_labels_csv(dataset_root, str(args.dataset_labels_csv))
    label_counts = load_ai_label_counts(labels_csv_for_policy, str(args.label_column))
    ai_weights = compute_ai_sampling_weights(
        policy=str(args.ai_label_policy),
        label_counts=label_counts,
        floor=float(args.dataset_label_floor),
    )
    start_time = datetime.now(timezone.utc) - timedelta(days=1)

    samples: list[SyntheticSample] = []
    stats = {
        "live_success": 0,
        "live_failures": 0,
        "template_success": 0,
        "fallback_to_template": 0,
    }
    driver = None
    engine = ""
    generated_ai_counts: dict[str, int] = {label: 0 for label in AI_LABELS}

    print(f"[synth] ai_label_policy={args.ai_label_policy}")
    if args.ai_label_policy != "uniform":
        print(f"[synth] labels_csv_for_policy={labels_csv_for_policy}")
        if label_counts:
            subset_counts = {label: int(label_counts.get(label, 0)) for label in AI_LABELS}
            print(f"[synth] dataset label counts: {subset_counts}")
        else:
            print("[synth] dataset label counts are empty; using floor/uniform fallback.")
    print(f"[synth] ai sampling weights: {format_ai_weight_report(ai_weights)}")

    max_attempts = args.count * args.max_attempts_multiplier
    produced = 0
    attempts = 0

    with tempfile.TemporaryDirectory(prefix="controledu-synth-browser-") as temp_dir:
        html_path = Path(temp_dir) / "page.html"
        try:
            driver, engine = create_driver(args.browser, args.width, args.height, args.page_load_timeout_ms, args.user_agent)
            while produced < args.count and attempts < max_attempts:
                attempts += 1
                if args.restart_every > 0 and attempts > 1 and attempts % args.restart_every == 0:
                    driver.quit()
                    driver, engine = create_driver(args.browser, args.width, args.height, args.page_load_timeout_ms, args.user_agent)

                make_ai = produced < ai_count_target
                label = rng.choices(AI_LABELS, weights=ai_weights, k=1)[0] if make_ai else NOT_AI_LABEL
                hard_negative = (label == NOT_AI_LABEL) and (rng.random() < hard_ratio)
                if label in generated_ai_counts:
                    generated_ai_counts[label] += 1
                process = choose_process_name(engine)
                title = ""
                url = ""
                source = "template_renderer"
                note_parts: list[str] = []

                image: Image.Image | None = None
                live_error = ""
                use_live = args.page_source in ("live", "hybrid")
                use_template = args.page_source in ("template", "hybrid")

                if use_live:
                    targets = catalog_targets_for_label(catalog, label, hard_negative=hard_negative)
                    if targets:
                        target = rng.choice(targets)
                        try:
                            driver.set_window_size(
                                max(1024, int(args.width * rng.uniform(0.84, 1.18))),
                                max(640, int(args.height * rng.uniform(0.84, 1.18))),
                            )
                            image, page_title, current_url = render_live_page(driver, target.url, args.wait_ms, rng)
                            title = page_title or target.title_hint
                            url = current_url or target.url
                            source = "live_url_capture"
                            note_parts.append(f"live_url={url}")
                            stats["live_success"] += 1
                        except (TimeoutException, WebDriverException, RuntimeError) as ex:
                            live_error = f"{ex.__class__.__name__}: {ex}"
                            stats["live_failures"] += 1
                            note_parts.append(f"live_error={ex.__class__.__name__}")
                    else:
                        live_error = f"No live targets for label={label}"
                        stats["live_failures"] += 1

                if image is None and use_template:
                    palette = rand_palette(rng)
                    if label == NOT_AI_LABEL:
                        body, process_template, title_template, url_template = not_ai_layout(rng, hard_negative=hard_negative)
                    else:
                        body, process_template, title_template, url_template = ai_layout(label, rng)
                    process = process_template
                    title = title_template
                    url = url_template
                    driver.set_window_size(
                        max(1024, int(args.width * rng.uniform(0.88, 1.16))),
                        max(640, int(args.height * rng.uniform(0.88, 1.16))),
                    )
                    page = shell_html(title, url, body, palette, rng)
                    image = render_template_page(driver, html_path, page, args.wait_ms, rng)
                    source = "template_renderer"
                    stats["template_success"] += 1
                    if live_error:
                        stats["fallback_to_template"] += 1
                        note_parts.append("fallback=template")

                if image is None:
                    if args.page_source == "live":
                        continue
                    raise RuntimeError("Failed to render sample in selected mode")

                image = image.resize((args.width, args.height), Image.Resampling.LANCZOS)
                if not args.no_browser_chrome_overlay:
                    image = overlay_browser_chrome(image, title, url, rng)
                image = apply_artifacts(image, rng, np_rng)

                ts = start_time + timedelta(seconds=produced * rng.randint(2, 6) + rng.randint(0, 3))
                stamp = ts.strftime("%Y-%m-%dT%H-%M-%SZ")
                suffix = f"{rng.randint(0, 16**8 - 1):08x}"
                image_name = f"{stamp}-{produced:05d}-{label}-{suffix}.jpg"
                meta_name = image_name.replace(".jpg", ".json")
                image_path = raw_root / image_name
                meta_path = raw_root / meta_name

                image.save(image_path, format="JPEG", quality=rng.randint(54, 92), optimize=True)
                metadata = build_metadata(label, process, title, url, ts, a_hash_hex(image), rng, source)
                meta_path.write_text(json.dumps(metadata, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

                rel_img = str(image_path.relative_to(dataset_root)).replace("\\", "/")
                rel_meta = str(meta_path.relative_to(dataset_root)).replace("\\", "/")
                notes = f"{source}; metadata={rel_meta}"
                if note_parts:
                    notes = notes + "; " + "; ".join(note_parts)
                samples.append(SyntheticSample(rel_img, label, rel_meta, source, notes))
                produced += 1
        finally:
            if driver is not None:
                driver.quit()

    if produced < args.count:
        raise RuntimeError(f"Only generated {produced}/{args.count} samples. Increase --max-attempts-multiplier or use --page-source hybrid.")

    append_labels_csv(labels_csv, samples)
    ai_count = sum(1 for s in samples if s.label != NOT_AI_LABEL)
    print("Browser synthetic dataset generation completed.")
    print(f"Dataset root: {dataset_root}")
    print(f"Output folder: {raw_root}")
    print(f"Generated images: {len(samples)}")
    print(f"AI samples: {ai_count}")
    print(f"not_ai samples: {len(samples) - ai_count}")
    print(f"Render engine: {engine}")
    print(f"Mode: {args.page_source}")
    print(f"Live captures: {stats['live_success']} success, {stats['live_failures']} failures")
    print(f"Template captures: {stats['template_success']}, fallback_to_template={stats['fallback_to_template']}")
    print(f"Generated AI label distribution: {generated_ai_counts}")
    print(f"Labels CSV updated: {labels_csv}")


if __name__ == "__main__":
    args = parse_args()
    try:
        generate(args)
    except Exception as exc:
        print(f"\nERROR: {exc}")
        if args.debug_traceback:
            raise
        raise SystemExit(1)
