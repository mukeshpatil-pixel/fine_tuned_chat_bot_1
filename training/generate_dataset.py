#!/usr/bin/env python3
"""
Industrial Sensor Telemetry Chatbot - Dataset Generator for LLM Fine-Tuning.
Generates 1,500+ structured examples for Intent Extraction and Slot Filling.

Target Models:
- HuggingFaceTB/SmolLM2-135M-Instruct
- Qwen/Qwen2.5-0.5B-Instruct / Qwen2.5-1.5B-Instruct

Formats Output:
1. data/train_chatml.jsonl (Hugging Face / Unsloth ChatML format)
2. data/val_chatml.jsonl   (Validation split)
3. data/train_alpaca.jsonl (Instruction / Input / Output format)
4. data/val_alpaca.jsonl
"""

import json
import random
import os
from datetime import datetime, timedelta

# Set seed for reproducible diversity
random.seed(42)

ASSETS = [
    {
        "id": 1,
        "name": "Boiler Feed Pump Motor",
        "aliases": [
            "Boiler Feed Pump Motor", "boiler feed pump motor", "boiler feed pump",
            "boiler pump", "feed pump", "bfp motor", "BFP", "bfp", "boiler motor",
            "Boiler Pump", "boiler feed", "feed pump motor", "the boiler pump"
        ]
    },
    {
        "id": 2,
        "name": "Air Compressor Motor",
        "aliases": [
            "Air Compressor Motor", "air compressor motor", "air compressor",
            "compressor motor", "compressor", "air comp", "AC motor", "ac motor",
            "Air Compressor", "air compressor unit", "the compressor", "comp motor"
        ]
    },
    {
        "id": 3,
        "name": "Main Conveyor Drive Motor",
        "aliases": [
            "Main Conveyor Drive Motor", "main conveyor drive motor", "main conveyor",
            "conveyor drive motor", "conveyor motor", "conveyor", "belt conveyor",
            "Main Conveyor", "conveyor drive", "main belt", "the conveyor", "conveyor belt motor"
        ]
    },
    {
        "id": 4,
        "name": "Cooling Tower Fan Motor",
        "aliases": [
            "Cooling Tower Fan Motor", "cooling tower fan motor", "cooling tower fan",
            "cooling tower", "tower fan motor", "tower fan", "ct fan", "CT fan motor",
            "Cooling Tower", "cooling fan", "the cooling tower", "cooling fan motor"
        ]
    },
    {
        "id": 5,
        "name": "Primary Crusher Motor",
        "aliases": [
            "Primary Crusher Motor", "primary crusher motor", "primary crusher",
            "crusher motor", "crusher", "primary crusher drive", "crusher drive",
            "Primary Crusher", "jaw crusher motor", "the crusher", "rock crusher motor"
        ]
    }
]

ASSETS_CATALOG_STR = "1: Boiler Feed Pump Motor | 2: Air Compressor Motor | 3: Main Conveyor Drive Motor | 4: Cooling Tower Fan Motor | 5: Primary Crusher Motor"

SYSTEM_PROMPT = f"""You extract report intent for an offline industrial asset PDF bot. Return strict JSON only.

ASSETS: {ASSETS_CATALOG_STR}

Valid task: choose an asset and timeframe for a telemetry PDF report.
Off-topic: sports, weather, news, jokes, general chat, coding, personal questions.

Rules:
- Prefer CURRENT_STATE when latest input omits asset or timeframe.
- If latest input names a different asset, use the new asset.
- Normalize time to "24h" or "Xd". Weeks = days*7. Today/yesterday = "24h".
- For dates, use fromDate/toDate as ISO UTC when clear.
- Do not invent asset IDs. Use only ASSETS.
- Keep replyMessage empty; the app writes replies.

JSON:
{{"isOnTopic":bool,"isComplete":bool,"replyMessage":"","suggestedAction":"select_asset"|"select_timeframe"|"confirm_queue"|"none","suggestedOptions":[],"extractedParameters":{{"assetId":int|null,"assetName":"str"|null,"timeRange":"str"|null,"fromDate":"str"|null,"toDate":"str"|null,"mode":"raw"}}}}"""

TIMEFRAME_PATTERNS = [
    # (normalized, list of text patterns)
    ("24h", [
        "last 24 hours", "24h", "24 hours", "past 24 hours", "past 24h", "for 24 hours",
        "past 1 day", "last 1 day", "last day", "1 day", "yesterday", "today",
        "last 24 hr", "past day", "24 hrs", "over the last 24 hours"
    ]),
    ("48h", [
        "last 48 hours", "48h", "48 hours", "past 48 hours", "past 2 days", "last 2 days",
        "2 days", "for 2 days", "over the last 48 hours"
    ]),
    ("3d", [
        "last 3 days", "3d", "3 days", "past 3 days", "for 3 days", "past 72 hours", "72h"
    ]),
    ("5d", [
        "last 5 days", "5d", "5 days", "past 5 days", "for 5 days", "over the past 5 days"
    ]),
    ("7d", [
        "last 7 days", "7d", "7 days", "past 7 days", "1 week", "last 1 week", "past week",
        "last week", "one week", "for the past week", "7 days window"
    ]),
    ("10d", [
        "last 10 days", "10d", "10 days", "past 10 days", "for 10 days"
    ]),
    ("14d", [
        "last 2 weeks", "2 weeks", "14d", "14 days", "past 2 weeks", "two weeks",
        "past 14 days", "for the past 2 weeks", "last fortnight", "past 14d"
    ]),
    ("30d", [
        "last 30 days", "30d", "30 days", "past 30 days", "past month", "last 1 month",
        "1 month", "last month", "one month", "over the last month"
    ])
]

REQUEST_TEMPLATES = [
    "generate a report for {asset} for {time}",
    "give me a report for {asset} for {time}",
    "i want report for {asset} for {time}",
    "can you generate a pdf for {asset} covering {time}?",
    "prepare telemetry report of {asset} for {time}",
    "create pdf report for {asset} for {time}",
    "pull report for {asset} covering {time}",
    "generate pdf for {asset} {time}",
    "report for {asset} {time}",
    "{asset} {time}",
    "{asset} for {time}",
    "{asset} covering {time}",
    "i need data for {asset} for {time}",
    "show report of {asset} for {time}",
    "inspect {asset} over {time}",
    "telemetry audit for {asset} in {time}",
    "please create a report for {asset} {time}",
    "give telemetry pdf for {asset} {time}",
    "can i have {asset} report for {time} please"
]

ASSET_ONLY_TEMPLATES = [
    "{asset}",
    "i want a report for {asset}",
    "give me a report for {asset}",
    "select {asset}",
    "let's inspect {asset}",
    "check {asset}",
    "{asset} please",
    "can you pull up {asset}?",
    "i need data for {asset}",
    "report on {asset}",
    "show me {asset}",
    "telemetry for {asset}",
    "configure report for {asset}"
]

OFF_TOPIC_EXAMPLES = [
    "what is the weather today?",
    "who won the football game yesterday?",
    "write a python function to sort a list",
    "tell me a funny joke",
    "what is the capital of France?",
    "how to bake chocolate cookies?",
    "who is the prime minister of India?",
    "explain quantum physics in simple terms",
    "write an essay on climate change",
    "can you write a poem about the sea?",
    "what is 45 * 87?",
    "solve x^2 + 5x + 6 = 0",
    "what stock should i buy today?",
    "who is Lionel Messi?",
    "recommend some good sci-fi movies",
    "what is your name?",
    "are you chatgpt?",
    "can you hack a website for me?",
    "how do i fix a flat tire on my bicycle?",
    "how to repair a diesel generator engine?",  # industrial but not in catalog
    "show me gas turbine temperature data",     # equipment not in catalog
    "generate report for steam turbine",       # asset not in catalog
    "hydraulic press sensor data",             # asset not in catalog
    "what is the latest news today?",
    "tell me about yourself",
    "what can you do besides reports?"
]


def make_json_output(is_on_topic, is_complete, suggested_action, asset_id=None, asset_name=None, time_range=None, from_date=None, to_date=None):
    return json.dumps({
        "isOnTopic": is_on_topic,
        "isComplete": is_complete,
        "replyMessage": "",
        "suggestedAction": suggested_action,
        "suggestedOptions": [],
        "extractedParameters": {
            "assetId": asset_id,
            "assetName": asset_name,
            "timeRange": time_range,
            "fromDate": from_date,
            "toDate": to_date,
            "mode": "raw"
        }
    }, separators=(',', ':'))


def generate_all_samples():
    samples = []
    
    # -------------------------------------------------------------
    # 1. Complete One-Shot Queries (Asset + Timeframe in one message)
    # -------------------------------------------------------------
    for asset in ASSETS:
        for norm_time, time_aliases in TIMEFRAME_PATTERNS:
            for tmpl in REQUEST_TEMPLATES:
                chosen_alias = random.choice(asset["aliases"])
                chosen_time = random.choice(time_aliases)
                
                # Apply random casing
                casing = random.choice(["as_is", "lower", "title"])
                user_text = tmpl.format(asset=chosen_alias, time=chosen_time)
                if casing == "lower":
                    user_text = user_text.lower()
                elif casing == "title":
                    user_text = user_text.capitalize()
                    
                context = "CURRENT_STATE: asset=null,time=null"
                user_msg = f"{context}\nINPUT: \"{user_text}\""
                target = make_json_output(
                    is_on_topic=True,
                    is_complete=True,
                    suggested_action="confirm_queue",
                    asset_id=asset["id"],
                    asset_name=asset["name"],
                    time_range=norm_time
                )
                samples.append((user_msg, target))

    # -------------------------------------------------------------
    # 2. Asset Only Selection (State: asset=null, time=null)
    # -------------------------------------------------------------
    for asset in ASSETS:
        for alias in asset["aliases"]:
            for tmpl in ASSET_ONLY_TEMPLATES[:7]:
                user_text = tmpl.format(asset=alias)
                context = "CURRENT_STATE: asset=null,time=null"
                user_msg = f"{context}\nINPUT: \"{user_text}\""
                target = make_json_output(
                    is_on_topic=True,
                    is_complete=False,
                    suggested_action="select_timeframe",
                    asset_id=asset["id"],
                    asset_name=asset["name"],
                    time_range=None
                )
                samples.append((user_msg, target))

    # -------------------------------------------------------------
    # 3. Timeframe Given with Asset Already Selected in State
    # -------------------------------------------------------------
    for asset in ASSETS:
        for norm_time, time_aliases in TIMEFRAME_PATTERNS:
            for t_alias in time_aliases:
                prefix = random.choice(["", "for ", "inspect ", "past ", "show ", "over "])
                user_text = f"{prefix}{t_alias}".strip()
                context = f"CURRENT_STATE: asset={asset['name']},time=null"
                user_msg = f"{context}\nINPUT: \"{user_text}\""
                target = make_json_output(
                    is_on_topic=True,
                    is_complete=True,
                    suggested_action="confirm_queue",
                    asset_id=asset["id"],
                    asset_name=asset["name"],
                    time_range=norm_time
                )
                samples.append((user_msg, target))

    # -------------------------------------------------------------
    # 4. Context Switching: Change Asset
    # -------------------------------------------------------------
    for asset_old in ASSETS:
        for asset_new in ASSETS:
            if asset_old["id"] == asset_new["id"]:
                continue
            for norm_time, _ in TIMEFRAME_PATTERNS[:4]:
                context = f"CURRENT_STATE: asset={asset_old['name']},time={norm_time}"
                switch_inputs = [
                    f"change asset to {asset_new['name']}",
                    f"switch to {random.choice(asset_new['aliases'])}",
                    f"change machine to {random.choice(asset_new['aliases'])}",
                    f"instead show {random.choice(asset_new['aliases'])}",
                    f"no, for {random.choice(asset_new['aliases'])}",
                    f"change to {asset_new['name']}"
                ]
                for inp in switch_inputs:
                    user_msg = f"{context}\nINPUT: \"{inp}\""
                    target = make_json_output(
                        is_on_topic=True,
                        is_complete=False,
                        suggested_action="select_timeframe",
                        asset_id=asset_new["id"],
                        asset_name=asset_new["name"],
                        time_range=None
                    )
                    samples.append((user_msg, target))

    # -------------------------------------------------------------
    # 5. Context Switching: Change Timeframe
    # -------------------------------------------------------------
    for asset in ASSETS:
        for norm_old, _ in TIMEFRAME_PATTERNS[::2]:
            for norm_new, time_aliases in TIMEFRAME_PATTERNS:
                if norm_old == norm_new:
                    continue
                context = f"CURRENT_STATE: asset={asset['name']},time={norm_old}"
                chosen_new = random.choice(time_aliases)
                change_inputs = [
                    f"change timeframe to {chosen_new}",
                    f"change time to {chosen_new}",
                    f"make it {chosen_new}",
                    f"switch to {chosen_new}",
                    f"instead do {chosen_new}",
                    f"change to {chosen_new}"
                ]
                for inp in change_inputs[:3]:
                    user_msg = f"{context}\nINPUT: \"{inp}\""
                    target = make_json_output(
                        is_on_topic=True,
                        is_complete=True,
                        suggested_action="confirm_queue",
                        asset_id=asset["id"],
                        asset_name=asset["name"],
                        time_range=norm_new
                    )
                    samples.append((user_msg, target))

    # -------------------------------------------------------------
    # 6. Intent: "I want to change asset" / "change timeframe" (No parameter named yet)
    # -------------------------------------------------------------
    for asset in ASSETS:
        context = f"CURRENT_STATE: asset={asset['name']},time=5d"
        # Change asset prompt
        for phr in ["i want to change asset", "change asset", "switch asset", "different machine", "change machine", "choose another"]:
            user_msg = f"{context}\nINPUT: \"{phr}\""
            target = make_json_output(
                is_on_topic=True,
                is_complete=False,
                suggested_action="select_asset",
                asset_id=None,
                asset_name=None,
                time_range=None
            )
            samples.append((user_msg, target))
            
        # Change timeframe prompt
        for phr in ["i want to change timeframe", "change timeframe", "change time", "different timeframe", "new timeframe"]:
            user_msg = f"{context}\nINPUT: \"{phr}\""
            target = make_json_output(
                is_on_topic=True,
                is_complete=False,
                suggested_action="select_timeframe",
                asset_id=asset["id"],
                asset_name=asset["name"],
                time_range=None
            )
            samples.append((user_msg, target))

    # -------------------------------------------------------------
    # 7. Confirmation Phrases (Queue Trigger)
    # -------------------------------------------------------------
    confirm_phrases = [
        "yes", "yes please", "yes, queue pdf report", "queue report", "generate report",
        "ok lets do it", "let's do it", "proceed", "go ahead", "confirm", "sure", "ok",
        "okay", "generate now", "start generation", "looks good", "queue pdf", "yes do it"
    ]
    for asset in ASSETS:
        for norm_time in ["24h", "5d", "14d", "30d"]:
            context = f"CURRENT_STATE: asset={asset['name']},time={norm_time}"
            for phr in confirm_phrases:
                user_msg = f"{context}\nINPUT: \"{phr}\""
                target = make_json_output(
                    is_on_topic=True,
                    is_complete=True,
                    suggested_action="confirm_queue",
                    asset_id=asset["id"],
                    asset_name=asset["name"],
                    time_range=norm_time
                )
                samples.append((user_msg, target))

    # -------------------------------------------------------------
    # 8. Explicit Date Range Queries (ISO / YYYY-MM-DD)
    # -------------------------------------------------------------
    date_ranges = [
        ("2026-09-01", "2026-09-15"),
        ("2026-08-10", "2026-08-25"),
        ("2026-07-01", "2026-07-31"),
        ("2026-09-10", "2026-09-20"),
        ("2026-08-01", "2026-08-07")
    ]
    for asset in ASSETS:
        for from_d, to_d in date_ranges:
            date_phrasings = [
                f"from {from_d} to {to_d}",
                f"between {from_d} and {to_d}",
                f"{from_d} to {to_d}",
                f"date range {from_d} - {to_d}"
            ]
            for d_text in date_phrasings:
                # One-shot
                user_msg = f"CURRENT_STATE: asset=null,time=null\nINPUT: \"report for {asset['name']} {d_text}\""
                target = make_json_output(
                    is_on_topic=True,
                    is_complete=True,
                    suggested_action="confirm_queue",
                    asset_id=asset["id"],
                    asset_name=asset["name"],
                    time_range=f"{from_d} to {to_d}",
                    from_date=f"{from_d}T00:00:00Z",
                    to_date=f"{to_d}T23:59:59Z"
                )
                samples.append((user_msg, target))
                
                # Turn with asset already selected
                user_msg2 = f"CURRENT_STATE: asset={asset['name']},time=null\nINPUT: \"{d_text}\""
                samples.append((user_msg2, target))

    # -------------------------------------------------------------
    # 9. General Machine Catalog & Greeting Inquiries
    # -------------------------------------------------------------
    catalog_queries = [
        "what machines are there?", "what assets do you have?", "list machines", "show assets",
        "which machines can i report on?", "what can you monitor?", "show catalog",
        "what are the available assets?", "hi", "hello", "hey", "help", "start",
        "i want report", "i want a report"
    ]
    for q in catalog_queries:
        user_msg = f"CURRENT_STATE: asset=null,time=null\nINPUT: \"{q}\""
        target = make_json_output(
            is_on_topic=True,
            is_complete=False,
            suggested_action="select_asset",
            asset_id=None,
            asset_name=None,
            time_range=None
        )
        samples.append((user_msg, target))

    # -------------------------------------------------------------
    # 10. Off-Topic & Safety Guardrails Refusals
    # -------------------------------------------------------------
    for off in OFF_TOPIC_EXAMPLES:
        # Test under both empty state and partially filled state
        states = [
            "CURRENT_STATE: asset=null,time=null",
            "CURRENT_STATE: asset=Air Compressor Motor,time=null",
            "CURRENT_STATE: asset=Primary Crusher Motor,time=14d"
        ]
        for st in states:
            user_msg = f"{st}\nINPUT: \"{off}\""
            target = make_json_output(
                is_on_topic=False,
                is_complete=False,
                suggested_action="none",
                asset_id=None,
                asset_name=None,
                time_range=None
            )
            samples.append((user_msg, target))

    return samples


def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    data_dir = os.path.join(script_dir, "data")
    os.makedirs(data_dir, exist_ok=True)

    print("Generating comprehensive industrial telemetry dataset...")
    raw_samples = generate_all_samples()
    print(f"Total raw generated samples: {len(raw_samples)}")

    # Deduplicate while preserving order
    seen = set()
    unique_samples = []
    for user_msg, target in raw_samples:
        key = (user_msg, target)
        if key not in seen:
            seen.add(key)
            unique_samples.append((user_msg, target))

    print(f"Total unique samples after deduplication: {len(unique_samples)}")

    # Shuffle for training
    random.shuffle(unique_samples)

    # 90 / 10 Train / Val Split
    split_idx = int(len(unique_samples) * 0.90)
    train_samples = unique_samples[:split_idx]
    val_samples = unique_samples[split_idx:]

    print(f"Train samples: {len(train_samples)} | Validation samples: {len(val_samples)}")

    # 1. Write ChatML format (Messages format)
    train_chatml_path = os.path.join(data_dir, "train_chatml.jsonl")
    val_chatml_path = os.path.join(data_dir, "val_chatml.jsonl")

    with open(train_chatml_path, "w", encoding="utf-8") as f:
        for user_msg, target in train_samples:
            record = {
                "messages": [
                    {"role": "system", "content": SYSTEM_PROMPT},
                    {"role": "user", "content": user_msg},
                    {"role": "assistant", "content": target}
                ]
            }
            f.write(json.dumps(record, ensure_ascii=False) + "\n")

    with open(val_chatml_path, "w", encoding="utf-8") as f:
        for user_msg, target in val_samples:
            record = {
                "messages": [
                    {"role": "system", "content": SYSTEM_PROMPT},
                    {"role": "user", "content": user_msg},
                    {"role": "assistant", "content": target}
                ]
            }
            f.write(json.dumps(record, ensure_ascii=False) + "\n")

    # 2. Write Alpaca format (instruction / input / output)
    train_alpaca_path = os.path.join(data_dir, "train_alpaca.jsonl")
    val_alpaca_path = os.path.join(data_dir, "val_alpaca.jsonl")

    with open(train_alpaca_path, "w", encoding="utf-8") as f:
        for user_msg, target in train_samples:
            record = {
                "instruction": SYSTEM_PROMPT,
                "input": user_msg,
                "output": target
            }
            f.write(json.dumps(record, ensure_ascii=False) + "\n")

    with open(val_alpaca_path, "w", encoding="utf-8") as f:
        for user_msg, target in val_samples:
            record = {
                "instruction": SYSTEM_PROMPT,
                "input": user_msg,
                "output": target
            }
            f.write(json.dumps(record, ensure_ascii=False) + "\n")

    # 3. Dataset Summary
    summary = {
        "generated_at": datetime.utcnow().isoformat() + "Z",
        "total_samples": len(unique_samples),
        "train_samples": len(train_samples),
        "val_samples": len(val_samples),
        "files": {
            "train_chatml": train_chatml_path,
            "val_chatml": val_chatml_path,
            "train_alpaca": train_alpaca_path,
            "val_alpaca": val_alpaca_path
        },
        "target_models": [
            "HuggingFaceTB/SmolLM2-135M-Instruct",
            "Qwen/Qwen2.5-0.5B-Instruct",
            "Qwen/Qwen2.5-1.5B-Instruct"
        ]
    }
    with open(os.path.join(data_dir, "dataset_summary.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    print("\nDataset generation complete!")
    print(f"Files saved in: {data_dir}")
    print(f" - {os.path.basename(train_chatml_path)}: {len(train_samples)} lines")
    print(f" - {os.path.basename(val_chatml_path)}: {len(val_samples)} lines")
    print(f" - {os.path.basename(train_alpaca_path)}: {len(train_samples)} lines")
    print(f" - {os.path.basename(val_alpaca_path)}: {len(val_samples)} lines")


if __name__ == "__main__":
    main()
