#!/usr/bin/env python3
"""Comprehensive Stress Testing and PDF Output Verification Suite.

Executes mix-and-match permutations across all catalog assets, diverse temporal
ranges, conversational styles (one-shot, multi-turn, timeframe-first,
context-switch, slang, date ranges, and guardrails), and performs deep PDF
inspection via PyMuPDF.
"""

import asyncio
from datetime import datetime
import json
import os
import sys
import time
from typing import Any, Optional
import urllib.request

import pymupdf as fitz
import websockets

sys.stdout.reconfigure(encoding="utf-8")

BASE_URL = "http://localhost:5000"
HUB_URL = f"{BASE_URL}/hubs/chat"
OUTPUT_DIR = "C:/Users/Mukesh Patil/Documents/chat_bot_1/tmp/stress_test_reports"
os.makedirs(OUTPUT_DIR, exist_ok=True)


class ChatbotClient:
    """SignalR WebSocket client for stress testing conversational flows."""

    def __init__(self, session_id: str) -> None:
        """Initialize test client with a unique session ID.

        Args:
            session_id: Unique identifier for the conversation session.
        """
        self.session_id = session_id
        self.ws: Optional[websockets.WebSocketClientProtocol] = None

    async def connect(self) -> None:
        """Establish HTTP negotiate handshake and open SignalR WebSocket."""
        neg_url = f"{HUB_URL}/negotiate?negotiateVersion=1"
        req = urllib.request.Request(neg_url, method="POST")
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode())

        conn_token = data.get("connectionToken") or data.get("connectionId")
        ws_url = f"ws://localhost:5000/hubs/chat?id={conn_token}"
        self.ws = await websockets.connect(ws_url)

        # SignalR protocol handshake
        await self.ws.send('{"protocol":"json","version":1}\x1e')
        await self.ws.recv()

    async def send_message(self, message: str) -> tuple[dict[str, Any], float]:
        """Send message to ChatHub and wait for response.

        Args:
            message: User input text to transmit.

        Returns:
            tuple[dict[str, Any], float]: Parsed response DTO and roundtrip latency.
        """
        if not self.ws:
            raise RuntimeError("WebSocket client is not connected.")

        payload = {
            "type": 1,
            "target": "SendMessage",
            "arguments": [self.session_id, message],
        }
        t0 = time.time()
        await self.ws.send(json.dumps(payload) + "\x1e")

        while True:
            raw = await self.ws.recv()
            for part in raw.split("\x1e"):
                if not part.strip():
                    continue
                parsed = json.loads(part)
                if (
                    parsed.get("type") == 1
                    and parsed.get("target") == "ReceiveChatResponse"
                ):
                    elapsed = time.time() - t0
                    res_obj = parsed.get("arguments", [{}])[0]
                    return res_obj, elapsed
                elif (
                    parsed.get("type") == 1
                    and parsed.get("target") == "ReceiveError"
                ):
                    elapsed = time.time() - t0
                    return {"error": parsed.get("arguments", [""])[0]}, elapsed

    async def close(self) -> None:
        """Close WebSocket connection gracefully."""
        if self.ws:
            await self.ws.close()


def poll_and_download_pdf(
    job_id: str, asset_name: str, timeframe: str
) -> dict[str, Any]:
    """Poll job status until completed, then download and store the PDF file.

    Args:
        job_id: UUID string of the queued report job.
        asset_name: Name of the asset.
        timeframe: Requested timeframe string.

    Returns:
        dict[str, Any]: Download metadata including local filepath and duration.
    """
    job_url = f"{BASE_URL}/api/reports/jobs/{job_id}"
    download_url = f"{BASE_URL}/api/reports/jobs/{job_id}/download"

    t0 = time.time()
    completed = False
    job_info: dict[str, Any] = {}

    for _ in range(30):
        req = urllib.request.Request(job_url)
        with urllib.request.urlopen(req) as resp:
            job_info = json.loads(resp.read().decode())

        status = job_info.get("status")
        if status == 2 or job_info.get("statusText") == "Completed":
            completed = True
            break
        elif status == 3 or job_info.get("statusText") == "Failed":
            raise RuntimeError(
                f"Job {job_id} failed: {job_info.get('errorMessage')}"
            )
        time.sleep(0.5)

    if not completed:
        raise TimeoutError(f"Job {job_id} did not complete within timeout.")

    poll_duration = time.time() - t0

    safe_asset = asset_name.replace(" ", "_").replace("/", "_")
    safe_time = timeframe.replace(" ", "_").replace("/", "_")
    short_id = job_id[:8]
    local_filename = f"{safe_asset}_{safe_time}_{short_id}.pdf"
    local_path = os.path.join(OUTPUT_DIR, local_filename)

    dl_req = urllib.request.Request(download_url)
    with urllib.request.urlopen(dl_req) as dl_resp:
        pdf_bytes = dl_resp.read()

    with open(local_path, "wb") as f:
        f.write(pdf_bytes)

    return {
        "jobId": job_id,
        "assetName": asset_name,
        "timeframe": timeframe,
        "localPath": local_path,
        "filename": local_filename,
        "sizeBytes": len(pdf_bytes),
        "durationSec": poll_duration,
        "jobInfo": job_info,
    }


def verify_pdf_document(pdf_info: dict[str, Any]) -> dict[str, Any]:
    """Inspect and deeply validate PDF contents using PyMuPDF.

    Args:
        pdf_info: Download metadata dict.

    Returns:
        dict[str, Any]: Verification audit results.
    """
    path = pdf_info["localPath"]
    doc = fitz.open(path)

    page_count = len(doc)
    all_text = ""
    for page in doc:
        all_text += page.get_text() + "\n"

    # 1. Structural Checks
    has_valid_header = doc.is_pdf
    has_pages = page_count >= 1

    # 2. Content Checks
    asset_name = pdf_info["assetName"]
    has_asset_name = asset_name.lower() in all_text.lower()
    has_telemetry_title = (
        "telemetry" in all_text.lower() or "report" in all_text.lower()
    )

    # 3. Signals Check
    signals_detected = []
    for sig in [
        "temp",
        "winding",
        "bearing",
        "ambient",
        "current",
        "voltage",
        "vibration",
        "pressure",
        "speed",
        "flow",
        "power",
    ]:
        if sig in all_text.lower():
            signals_detected.append(sig)

    # 4. Vector Drawings (Sparkline chart curves)
    vector_drawings_count = 0
    for page in doc:
        vector_drawings_count += len(page.get_drawings())

    has_sparklines = vector_drawings_count > 0

    # 5. Render Page 1 to PNG preview
    page1 = doc[0]
    pix = page1.get_pixmap(dpi=130)
    png_path = os.path.splitext(path)[0] + "_preview.png"
    pix.save(png_path)

    doc.close()

    has_envelope = (
        ("min" in all_text.lower() and "max" in all_text.lower())
        or ("no telemetry in requested window" in all_text.lower())
    )

    is_valid = (
        has_valid_header
        and has_pages
        and has_asset_name
        and has_telemetry_title
        and has_envelope
        and len(signals_detected) >= 2
        and has_sparklines
    )

    return {
        "filename": pdf_info["filename"],
        "assetName": asset_name,
        "timeframe": pdf_info["timeframe"],
        "pages": page_count,
        "sizeBytes": pdf_info["sizeBytes"],
        "sizeKb": round(pdf_info["sizeBytes"] / 1024, 1),
        "hasAssetName": has_asset_name,
        "hasTelemetryTitle": has_telemetry_title,
        "hasStatisticalSummary": has_envelope,
        "signalsDetected": signals_detected,
        "vectorDrawingsCount": vector_drawings_count,
        "hasSparklines": has_sparklines,
        "previewImage": png_path,
        "isValid": is_valid,
    }


async def run_stress_test_suite() -> list[dict[str, Any]]:
    """Execute all mix-and-match conversational test permutations.

    Returns:
        list[dict[str, Any]]: Executed test metadata.
    """
    print("=" * 88)
    print("🚀 INDUSTRIAL TELEMETRY CHATBOT STRESS TEST & PDF VERIFICATION")
    print(f"Target: {BASE_URL} | Timestamp: {datetime.now().isoformat()}")
    print("=" * 88)

    test_results: list[dict[str, Any]] = []

    # -------------------------------------------------------------------------
    # PERMUTATION 1: Boiler Feed Pump Motor (48h Shorthand One-Shot)
    # -------------------------------------------------------------------------
    print("\n[PERMUTATION 1] Shorthand: 'bfp 48h' -> Confirm")
    cli1 = ChatbotClient(f"stress-bfp-{int(time.time())}")
    await cli1.connect()

    r1, t1 = await cli1.send_message("bfp 48h")
    print(f"  Step 1: 'bfp 48h' -> Latency: {t1*1000:.1f}ms")
    assert r1.get("suggestedAction") == "confirm_queue"

    r1_conf, t1_conf = await cli1.send_message("yes, queue pdf report")
    print(
        f"  Step 2: Confirm -> Latency: {t1_conf*1000:.1f}ms | "
        f"JobId: {r1_conf.get('jobId')}"
    )
    assert r1_conf.get("jobId")
    await cli1.close()

    test_results.append({
        "testId": 1,
        "name": "Boiler Feed Pump Motor (48h Shorthand)",
        "assetName": "Boiler Feed Pump Motor",
        "timeframe": "48h",
        "jobId": str(r1_conf["jobId"]),
        "latenciesMs": [round(t1 * 1000, 1), round(t1_conf * 1000, 1)],
    })

    # -------------------------------------------------------------------------
    # PERMUTATION 2: Air Compressor Motor (24h Standard Multi-Turn)
    # -------------------------------------------------------------------------
    print("\n[PERMUTATION 2] Multi-Turn: 'hello' -> 'Air Compressor Motor' -> '24h'")
    cli2 = ChatbotClient(f"stress-ac-{int(time.time())}")
    await cli2.connect()

    s1, lat1 = await cli2.send_message("hello")
    print(f"  Step 1: 'hello' -> Latency: {lat1*1000:.1f}ms")
    assert s1.get("suggestedAction") == "select_asset"

    s2, lat2 = await cli2.send_message("Air Compressor Motor")
    print(f"  Step 2: Asset -> Latency: {lat2*1000:.1f}ms")
    assert s2.get("suggestedAction") == "select_timeframe"

    s3, lat3 = await cli2.send_message("Last 24 Hours")
    print(f"  Step 3: Timeframe -> Latency: {lat3*1000:.1f}ms")
    assert s3.get("suggestedAction") == "confirm_queue"

    s4, lat4 = await cli2.send_message("Yes, Queue PDF Report")
    print(f"  Step 4: Confirm -> Latency: {lat4*1000:.1f}ms | JobId: {s4.get('jobId')}")
    assert s4.get("jobId")
    await cli2.close()

    test_results.append({
        "testId": 2,
        "name": "Air Compressor Motor (24h Multi-Turn)",
        "assetName": "Air Compressor Motor",
        "timeframe": "24h",
        "jobId": str(s4["jobId"]),
        "latenciesMs": [round(val * 1000, 1) for val in [lat1, lat2, lat3, lat4]],
    })

    # -------------------------------------------------------------------------
    # PERMUTATION 3: Main Conveyor Drive Motor (7d One-Shot Natural)
    # -------------------------------------------------------------------------
    print("\n[PERMUTATION 3] One-Shot Natural: 'report for Conveyor for 7 days'")
    cli3 = ChatbotClient(f"stress-conv-{int(time.time())}")
    await cli3.connect()

    c1, clat1 = await cli3.send_message(
        "generate a report for Main Conveyor Drive Motor for last 7 days"
    )
    print(f"  Step 1: One-Shot -> Latency: {clat1*1000:.1f}ms")
    assert c1.get("suggestedAction") == "confirm_queue"

    c2, clat2 = await cli3.send_message("ok lets do it")
    print(
        f"  Step 2: Confirm -> Latency: {clat2*1000:.1f}ms | "
        f"JobId: {c2.get('jobId')}"
    )
    assert c2.get("jobId")
    await cli3.close()

    test_results.append({
        "testId": 3,
        "name": "Main Conveyor Drive Motor (7d One-Shot)",
        "assetName": "Main Conveyor Drive Motor",
        "timeframe": "7d",
        "jobId": str(c2["jobId"]),
        "latenciesMs": [round(clat1 * 1000, 1), round(clat2 * 1000, 1)],
    })

    # -------------------------------------------------------------------------
    # PERMUTATION 4: Cooling Tower Fan Motor (14d Timeframe-First Flow)
    # -------------------------------------------------------------------------
    print("\n[PERMUTATION 4] Timeframe-First: 'past 2 weeks' -> 'Cooling Tower'")
    cli4 = ChatbotClient(f"stress-ct-{int(time.time())}")
    await cli4.connect()

    t_s1, t_l1 = await cli4.send_message("past 2 weeks")
    print(f"  Step 1: Timeframe -> Latency: {t_l1*1000:.1f}ms")
    assert t_s1.get("suggestedAction") == "select_asset"

    t_s2, t_l2 = await cli4.send_message("Cooling Tower Fan Motor")
    print(f"  Step 2: Asset -> Latency: {t_l2*1000:.1f}ms")
    assert t_s2.get("suggestedAction") == "confirm_queue"

    t_s3, t_l3 = await cli4.send_message("proceed")
    print(
        f"  Step 3: Confirm -> Latency: {t_l3*1000:.1f}ms | "
        f"JobId: {t_s3.get('jobId')}"
    )
    assert t_s3.get("jobId")
    await cli4.close()

    test_results.append({
        "testId": 4,
        "name": "Cooling Tower Fan Motor (14d Timeframe-First)",
        "assetName": "Cooling Tower Fan Motor",
        "timeframe": "14d",
        "jobId": str(t_s3["jobId"]),
        "latenciesMs": [round(val * 1000, 1) for val in [t_l1, t_l2, t_l3]],
    })

    # -------------------------------------------------------------------------
    # PERMUTATION 5: Primary Crusher Motor (30d Context-Switch Mid-Flow)
    # -------------------------------------------------------------------------
    print("\n[PERMUTATION 5] Context-Switch: Tower -> Crusher -> 30d -> Confirm")
    cli5 = ChatbotClient(f"stress-crush-{int(time.time())}")
    await cli5.connect()

    cs1, cs_l1 = await cli5.send_message("Cooling Tower Fan Motor")
    print(f"  Step 1: Initial Asset -> Latency: {cs_l1*1000:.1f}ms")

    cs2, cs_l2 = await cli5.send_message(
        "Actually wait, change machine to Primary Crusher Motor"
    )
    print(f"  Step 2: Switch Machine -> Latency: {cs_l2*1000:.1f}ms")
    assert "crusher" in cs2.get("extractedParameters", {}).get("assetName", "").lower()

    cs3, cs_l3 = await cli5.send_message("make it 30 days")
    print(f"  Step 3: Timeframe -> Latency: {cs_l3*1000:.1f}ms")
    assert cs3.get("suggestedAction") == "confirm_queue"

    cs4, cs_l4 = await cli5.send_message("Yes, Queue PDF Report")
    print(
        f"  Step 4: Confirm -> Latency: {cs_l4*1000:.1f}ms | "
        f"JobId: {cs4.get('jobId')}"
    )
    assert cs4.get("jobId")
    await cli5.close()

    test_results.append({
        "testId": 5,
        "name": "Primary Crusher Motor (30d Context-Switch)",
        "assetName": "Primary Crusher Motor",
        "timeframe": "30d",
        "jobId": str(cs4["jobId"]),
        "latenciesMs": [round(val * 1000, 1) for val in [cs_l1, cs_l2, cs_l3, cs_l4]],
    })

    # -------------------------------------------------------------------------
    # PERMUTATION 6: Air Compressor Motor (Explicit Date Range)
    # -------------------------------------------------------------------------
    print("\n[PERMUTATION 6] Explicit ISO Date Range: 'from 2026-09-01 to 2026-09-10'")
    cli6 = ChatbotClient(f"stress-dates-{int(time.time())}")
    await cli6.connect()

    d1, d_lat1 = await cli6.send_message("Air Compressor Motor")
    print(f"  Step 1: Asset -> Latency: {d_lat1*1000:.1f}ms")

    d2, d_lat2 = await cli6.send_message("from 2026-09-01 to 2026-09-10")
    print(f"  Step 2: Dates -> Latency: {d_lat2*1000:.1f}ms")
    assert d2.get("suggestedAction") == "confirm_queue"

    d3, d_lat3 = await cli6.send_message("confirm")
    print(
        f"  Step 3: Confirm -> Latency: {d_lat3*1000:.1f}ms | "
        f"JobId: {d3.get('jobId')}"
    )
    assert d3.get("jobId")
    await cli6.close()

    test_results.append({
        "testId": 6,
        "name": "Air Compressor Motor (Explicit Date Range)",
        "assetName": "Air Compressor Motor",
        "timeframe": "2026-09-01 to 2026-09-10",
        "jobId": str(d3["jobId"]),
        "latenciesMs": [round(val * 1000, 1) for val in [d_lat1, d_lat2, d_lat3]],
    })

    # -------------------------------------------------------------------------
    # PERMUTATION 7: Adversarial / Guardrail Robustness
    # -------------------------------------------------------------------------
    print("\n[PERMUTATION 7] Adversarial & Off-Topic Guardrail Verification")
    cli7 = ChatbotClient(f"stress-adv-{int(time.time())}")
    await cli7.connect()

    adv_queries = [
        "what is the weather in Delhi?",
        "who is the CEO of Apple?",
        "write a python quicksort function",
        "tell me a joke",
    ]
    for q in adv_queries:
        adv_res, adv_lat = await cli7.send_message(q)
        is_on_topic = adv_res.get("isOnTopic")
        print(
            f"  Adversarial: '{q}' -> Latency: {adv_lat*1000:.1f}ms | "
            f"OnTopic: {is_on_topic}"
        )
        assert is_on_topic is False, f"Expected off-topic refusal for '{q}'"

    # Verify recovery: follow up with a catalog request
    rec_res, rec_lat = await cli7.send_message("what machines are there?")
    print(f"  Recovery: 'what machines are there?' -> Latency: {rec_lat*1000:.1f}ms")
    assert len(rec_res.get("suggestedOptions", [])) == 5
    await cli7.close()

    return test_results


def main() -> None:
    """Run full conversational stress tests and deeply verify all resulting PDFs."""
    test_results = asyncio.run(run_stress_test_suite())

    print("\n" + "=" * 88)
    print("⏳ POLLING & DOWNLOADING GENERATED PDF AUDIT REPORTS")
    print("=" * 88)

    downloaded_pdfs: list[dict[str, Any]] = []
    for test in test_results:
        job_id = test["jobId"]
        asset_name = test["assetName"]
        timeframe = test["timeframe"]
        print(f"Polling Job {job_id} ({asset_name} - {timeframe})...")
        dl_info = poll_and_download_pdf(job_id, asset_name, timeframe)
        size_kb = round(dl_info["sizeBytes"] / 1024, 1)
        dur = dl_info["durationSec"]
        print(f"  -> Saved: {dl_info['filename']} ({size_kb} KB in {dur:.2f}s)")
        downloaded_pdfs.append(dl_info)

    print("\n" + "=" * 88)
    print("🔬 DEEP PYMUPDF INSPECTION & VECTOR VALIDATION OF OUTPUT PDFS")
    print("=" * 88)

    verified_reports: list[dict[str, Any]] = []
    for dl in downloaded_pdfs:
        audit = verify_pdf_document(dl)
        verified_reports.append(audit)
        status_label = "✅ PASSED" if audit["isValid"] else "❌ FAILED"
        print(f"\n[REPORT]: {audit['filename']}")
        print(f"  - Asset: {audit['assetName']} | Timeframe: {audit['timeframe']}")
        print(f"  - Pages: {audit['pages']} | Size: {audit['sizeKb']} KB")
        print(f"  - Signals Verified: {', '.join(audit['signalsDetected'])}")
        print(f"  - Sparkline Curves: {audit['vectorDrawingsCount']} detected")
        print(f"  - Preview Screenshot: {audit['previewImage']}")
        print(f"  - Integrity Status: {status_label}")

    summary_path = os.path.join(OUTPUT_DIR, "stress_test_summary.json")
    with open(summary_path, "w", encoding="utf-8") as f:
        json.dump(
            {
                "timestamp": datetime.now().isoformat(),
                "totalTests": len(test_results),
                "totalPdfsVerified": len(verified_reports),
                "allValid": all(r["isValid"] for r in verified_reports),
                "tests": test_results,
                "pdfAudits": verified_reports,
            },
            f,
            indent=2,
        )

    print("\n" + "=" * 88)
    print("🎉 STRESS TESTING & PDF VALIDATION COMPLETE!")
    print(f"Summary JSON saved to: {summary_path}")
    print(
        f"All {len(verified_reports)} PDFs passed validation: "
        f"{all(r['isValid'] for r in verified_reports)}"
    )
    print("=" * 88)


if __name__ == "__main__":
    main()
