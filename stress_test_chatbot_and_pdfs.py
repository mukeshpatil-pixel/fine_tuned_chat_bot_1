#!/usr/bin/env python3
"""
Comprehensive Stress Test for Industrial Telemetry Chatbot and PDF Generation.
Tests:
1. Relevant & Standard Multi-turn Flows
2. Rapid One-Shot Queries
3. Confusing Context-Switching (changing machines & timeframes mid-flight)
4. Irrelevant, Adversarial & Off-Topic Guardrails
5. Shorthand & Industrial Slang
6. Verification & Visual Inspection of Generated PDFs
"""

import asyncio
import json
import os
import shutil
import time
import urllib.request
import websockets
import sys
sys.stdout.reconfigure(encoding='utf-8')
import pymupdf as fitz

HUB_URL = "http://localhost:5000/hubs/chat"
REPORTS_DIR = "C:/Users/Mukesh Patil/Documents/chat_bot_1/tmp/test_reports"
os.makedirs(REPORTS_DIR, exist_ok=True)

class ChatbotTester:
    def __init__(self, session_id):
        self.session_id = session_id
        self.ws = None

    async def connect(self):
        req = urllib.request.Request(f"{HUB_URL}/negotiate?negotiateVersion=1", method="POST")
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode())
        conn_token = data.get("connectionToken") or data.get("connectionId")
        ws_url = f"ws://localhost:5000/hubs/chat?id={conn_token}"
        self.ws = await websockets.connect(ws_url)
        await self.ws.send('{"protocol":"json","version":1}\x1e')
        res = await self.ws.recv()
        # Handshake complete

    async def send_message(self, message):
        payload = {
            "type": 1,
            "target": "SendMessage",
            "arguments": [self.session_id, message]
        }
        t0 = time.time()
        await self.ws.send(json.dumps(payload) + "\x1e")

        while True:
            raw = await self.ws.recv()
            for part in raw.split("\x1e"):
                if not part.strip():
                    continue
                parsed = json.loads(part)
                if parsed.get("type") == 1 and parsed.get("target") == "ReceiveChatResponse":
                    elapsed = time.time() - t0
                    res_obj = parsed.get("arguments", [{}])[0]
                    res_obj["_latency_sec"] = elapsed
                    return res_obj
                elif parsed.get("type") == 1 and parsed.get("target") == "ReceiveError":
                    return {"error": parsed.get("arguments", [""])[0]}

    async def close(self):
        if self.ws:
            await self.ws.close()

async def run_stress_tests():
    print("=" * 80)
    print("🚀 STARTING INDUSTRIAL CHATBOT STRESS TESTS (SMOLLM2-135M)")
    print("=" * 80)

    job_ids = []

    # -------------------------------------------------------------------------
    # TEST SUITE 1: Standard Multi-Turn Flow -> PDF Generation
    # -------------------------------------------------------------------------
    print("\n[SUITE 1] Standard Multi-Turn Flow (Primary Crusher -> 2 Weeks -> Queue)")
    client1 = ChatbotTester(f"stress-suite-1-{int(time.time())}")
    await client1.connect()

    r1 = await client1.send_message("Hello, I need an operational report")
    print(f"Turn 1 Input: 'Hello, I need an operational report'")
    print(f"Turn 1 Response: '{r1.get('replyMessage')}' | Action: {r1.get('suggestedAction')} | Latency: {r1.get('_latency_sec', 0):.2f}s")
    assert r1.get("suggestedAction") == "select_asset", "Expected select_asset"

    r2 = await client1.send_message("Primary Crusher Motor")
    print(f"Turn 2 Input: 'Primary Crusher Motor'")
    print(f"Turn 2 Response: '{r2.get('replyMessage')}' | Action: {r2.get('suggestedAction')} | Asset: {r2.get('extractedParameters', {}).get('assetName')}")
    assert r2.get("extractedParameters", {}).get("assetId") is not None, "Expected assetId to be set"

    r3 = await client1.send_message("past 2 weeks")
    print(f"Turn 3 Input: 'past 2 weeks'")
    print(f"Turn 3 Response: '{r3.get('replyMessage')}' | Action: {r3.get('suggestedAction')} | Time: {r3.get('extractedParameters', {}).get('timeRange')}")
    assert r3.get("suggestedAction") == "confirm_queue", "Expected confirm_queue"

    r4 = await client1.send_message("ok lets do it")
    print(f"Turn 4 Input: 'ok lets do it'")
    print(f"Turn 4 Response: '{r4.get('replyMessage')}' | JobId: {r4.get('jobId')}")
    assert r4.get("jobId"), "Expected JobId to be returned"
    job_ids.append(r4.get("jobId"))
    await client1.close()

    # -------------------------------------------------------------------------
    # TEST SUITE 2: Rapid One-Shot Direct Request -> PDF Generation
    # -------------------------------------------------------------------------
    print("\n[SUITE 2] One-Shot Request (Air Compressor Motor -> 24 Hours)")
    client2 = ChatbotTester(f"stress-suite-2-{int(time.time())}")
    await client2.connect()

    r_oneshot = await client2.send_message("give me a report for air compressor motor for last 24 hours")
    print(f"One-Shot Input: 'give me a report for air compressor motor for last 24 hours'")
    print(f"One-Shot Response: '{r_oneshot.get('replyMessage')}' | Action: {r_oneshot.get('suggestedAction')}")
    assert r_oneshot.get("suggestedAction") == "confirm_queue", "Expected confirm_queue"

    r_conf = await client2.send_message("yes, queue pdf report")
    print(f"Confirm Input: 'yes, queue pdf report'")
    print(f"Confirm Response: '{r_conf.get('replyMessage')}' | JobId: {r_conf.get('jobId')}")
    assert r_conf.get("jobId"), "Expected JobId to be returned"
    job_ids.append(r_conf.get("jobId"))
    await client2.close()

    # -------------------------------------------------------------------------
    # TEST SUITE 3: Confusing & Context-Switching Queries
    # -------------------------------------------------------------------------
    print("\n[SUITE 3] Confusing & Context-Switching Flow (Changing Machines & Times Mid-Flight)")
    client3 = ChatbotTester(f"stress-suite-3-{int(time.time())}")
    await client3.connect()

    # Step 1: Start with Cooling Tower
    s1 = await client3.send_message("Cooling Tower Fan Motor")
    print(f"Step 1: 'Cooling Tower Fan Motor' -> Selected: {s1.get('extractedParameters', {}).get('assetName')}")

    # Step 2: Mid-flight switch machine
    s2 = await client3.send_message("Actually wait, change machine to Main Conveyor Drive Motor")
    print(f"Step 2: 'Actually wait, change machine to Main Conveyor Drive Motor' -> Switched to: {s2.get('extractedParameters', {}).get('assetName')}")
    assert "conveyor" in s2.get('extractedParameters', {}).get('assetName', '').lower(), "Expected switch to conveyor"

    # Step 3: Pick a time
    s3 = await client3.send_message("make it 30 days")
    print(f"Step 3: 'make it 30 days' -> Time: {s3.get('extractedParameters', {}).get('timeRange')}")

    # Step 4: Mid-flight change time
    s4 = await client3.send_message("no wait, change timeframe to last 5 days instead")
    print(f"Step 4: 'no wait, change timeframe to last 5 days instead' -> Updated Time: {s4.get('extractedParameters', {}).get('timeRange')}")
    assert "5d" in str(s4.get('extractedParameters', {}).get('timeRange')), "Expected 5d timeframe"

    # Step 5: Confirm and Queue
    s5 = await client3.send_message("proceed")
    print(f"Step 5: 'proceed' -> Enqueued JobId: {s5.get('jobId')}")
    assert s5.get("jobId"), "Expected JobId for conveyor"
    job_ids.append(s5.get("jobId"))
    await client3.close()

    # -------------------------------------------------------------------------
    # TEST SUITE 4: Irrelevant, Adversarial & Guardrails
    # -------------------------------------------------------------------------
    print("\n[SUITE 4] Irrelevant, Adversarial & Guardrails Testing")
    client4 = ChatbotTester(f"stress-suite-4-{int(time.time())}")
    await client4.connect()

    adversarial_prompts = [
        "what is the weather forecast in Mumbai today?",
        "who won the cricket match yesterday?",
        "write a python script to reverse a string",
        "how to repair a diesel generator engine?",  # Equipment not in catalog
        "tell me a funny joke",
        "solve 2x + 5 = 15"
    ]

    for adv in adversarial_prompts:
        adv_res = await client4.send_message(adv)
        is_on_topic = adv_res.get("isOnTopic")
        action = adv_res.get("suggestedAction")
        print(f"Adversarial Input: '{adv}'")
        print(f"  -> OnTopic: {is_on_topic} | Action: {action} | Response: '{adv_res.get('replyMessage')[:60]}...'")
        assert is_on_topic is False, f"Expected off-topic refusal for '{adv}'"

    # Now verify that after off-topic prompts, the bot can still answer catalog queries
    cat_res = await client4.send_message("what machines are there?")
    print(f"\nCatalog Input: 'what machines are there?'")
    print(f"  -> OnTopic: {cat_res.get('isOnTopic')} | Options: {cat_res.get('suggestedOptions')}")
    assert len(cat_res.get('suggestedOptions', [])) == 5, "Expected 5 catalog options"
    await client4.close()

    # -------------------------------------------------------------------------
    # TEST SUITE 5: Shorthand & Industrial Slang
    # -------------------------------------------------------------------------
    print("\n[SUITE 5] Shorthand & Industrial Slang Queries")
    client5 = ChatbotTester(f"stress-suite-5-{int(time.time())}")
    await client5.connect()

    slang_tests = [
        ("bfp 48h", "Boiler Feed Pump Motor", "48h"),
        ("comp motor 7 days", "Air Compressor Motor", "7d"),
        ("crusher past fortnight", "Primary Crusher Motor", "14d")
    ]

    for idx, (slang_inp, exp_asset, exp_time) in enumerate(slang_tests):
        cli = ChatbotTester(f"stress-suite-5-{idx}-{int(time.time())}")
        await cli.connect()
        slang_res = await cli.send_message(slang_inp)
        ext_asset = slang_res.get("extractedParameters", {}).get("assetName")
        ext_time = slang_res.get("extractedParameters", {}).get("timeRange")
        print(f"Slang Input: '{slang_inp}' -> Extracted Asset: '{ext_asset}' | Time: '{ext_time}'")
        await cli.close()
        assert ext_asset == exp_asset, f"Expected {exp_asset}, got {ext_asset}"
        assert ext_time == exp_time, f"Expected {exp_time}, got {ext_time}"

    print("\n" + "=" * 80)
    print(f"🎉 ALL CHATBOT CONVERSATIONAL STRESS TESTS PASSED!")
    print(f"Enqueued Report Job IDs: {job_ids}")
    print("=" * 80)

    return job_ids

def inspect_generated_pdfs():
    print("\n" + "=" * 80)
    print("🔍 INSPECTING AND VALIDATING GENERATED PDF REPORTS")
    print("=" * 80)

    # 1. Copy reports from Docker backend container to host
    print("Copying PDF reports from backend container storage...")
    os.system("docker cp sensorbot_backend:/app/storage/reports/. \"C:/Users/Mukesh Patil/Documents/chat_bot_1/tmp/test_reports/\"")

    pdf_files = [f for f in os.listdir(REPORTS_DIR) if f.lower().endswith(".pdf")]
    print(f"Found {len(pdf_files)} PDF reports in storage.")

    if not pdf_files:
        print("Waiting 5 seconds for background worker to complete PDF jobs...")
        time.sleep(5)
        os.system("docker cp sensorbot_backend:/app/storage/reports/. \"C:/Users/Mukesh Patil/Documents/chat_bot_1/tmp/test_reports/\"")
        pdf_files = [f for f in os.listdir(REPORTS_DIR) if f.lower().endswith(".pdf")]

    inspection_results = []

    for pdf_name in pdf_files:
        pdf_path = os.path.join(REPORTS_DIR, pdf_name)
        file_size_kb = os.path.getsize(pdf_path) / 1024

        try:
            doc = fitz.open(pdf_path)
            num_pages = len(doc)
            is_encrypted = doc.is_encrypted
            metadata = doc.metadata

            # Extract first page text for verification
            first_page_text = doc[0].get_text()
            has_telemetry_title = "TELEMETRY" in first_page_text or "REPORT" in first_page_text or "ASSET" in first_page_text
            has_metrics = "Min" in first_page_text or "Max" in first_page_text or "Mean" in first_page_text or "Std Dev" in first_page_text

            # Render Page 1 to PNG for visual inspection
            page1 = doc[0]
            pix = page1.get_pixmap(dpi=150)
            png_name = f"{os.path.splitext(pdf_name)[0]}_p1.png"
            png_path = os.path.join(REPORTS_DIR, png_name)
            pix.save(png_path)

            # Render Page 2 if exists
            if num_pages > 1:
                page2 = doc[1]
                pix2 = page2.get_pixmap(dpi=150)
                png_name2 = f"{os.path.splitext(pdf_name)[0]}_p2.png"
                png_path2 = os.path.join(REPORTS_DIR, png_name2)
                pix2.save(png_path2)

            res = {
                "filename": pdf_name,
                "size_kb": round(file_size_kb, 1),
                "pages": num_pages,
                "encrypted": is_encrypted,
                "has_title": has_telemetry_title,
                "has_metrics": has_metrics,
                "rendered_preview": png_path,
                "status": "VALID"
            }
            inspection_results.append(res)
            print(f"\n[PDF REPORT]: {pdf_name}")
            print(f"  - Size: {res['size_kb']} KB")
            print(f"  - Page Count: {res['pages']} pages")
            print(f"  - Telemetry Header Found: {res['has_title']}")
            print(f"  - Summary Statistical Envelope Found: {res['has_metrics']}")
            print(f"  - Rendered Preview: {res['rendered_preview']}")
            print(f"  - Integrity Status: {res['status']}")

        except Exception as e:
            print(f"\n[PDF REPORT ERROR]: {pdf_name} - {e}")
            inspection_results.append({
                "filename": pdf_name,
                "error": str(e),
                "status": "CORRUPT"
            })

    print("\n" + "=" * 80)
    print("PDF VALIDATION SUMMARY:")
    for r in inspection_results:
        print(f"  * {r['filename']}: {r.get('pages', 0)} pages, {r.get('size_kb', 0)} KB [{r['status']}]")
    print("=" * 80)

if __name__ == "__main__":
    asyncio.run(run_stress_tests())
    print("\nSleeping 8 seconds to allow RabbitMQ background worker to finish writing all queued PDFs...")
    time.sleep(8)
    inspect_generated_pdfs()
