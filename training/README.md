# Edge LLM Fine-Tuning Pipeline (Industrial Telemetry Chatbot)

This directory contains the dataset generation and fine-tuning toolchain to specialize lightweight edge LLMs (e.g. `SmolLM2-135M` or `Qwen2.5-0.5B`) to run directly on the Intel Atom x6211E edge processor with ultra-low latency (< 1.5 seconds) and minimal memory footprint (~85 MB).

---

## 1. Generated Dataset Statistics

Generated via `python generate_dataset.py`:
- **Total Unique Samples**: 3,156
- **Train Set (90%)**: 2,840 samples
- **Validation Set (10%)**: 316 samples
- **Files**:
  - `data/train_chatml.jsonl` (ChatML format for SFT / Unsloth / Hugging Face)
  - `data/val_chatml.jsonl`
  - `data/train_alpaca.jsonl` (Instruction / Input / Output format for LLaMA-Factory)
  - `data/val_alpaca.jsonl`
  - `data/dataset_summary.json`

### Categories Covered:
1. **One-Shot Queries** (e.g., *"crusher motor for past 2 weeks"*, *"generate pdf for boiler feed pump last 24h"*).
2. **Initial Asset Selection** (e.g., *"Air Compressor Motor"*, *"let's check cooling tower"*).
3. **Conversational Multi-Turn Memory** (e.g., asset already in state, user inputs *"24h"* or *"past 5 days"*, model retains asset and fills timeframe).
4. **Context Switching** (e.g., *"change asset to primary crusher"*, *"switch to 14 days"*).
5. **Direct Confirmation / Queue Triggers** (*"yes"*, *"ok lets do it"*, *"proceed"*, *"generate report"*).
6. **Machine Catalog Queries** (*"what machines are available?"*, *"list assets"*).
7. **Guardrail Refusals / Off-Topic** (weather, sports, jokes, unmonitored equipment like "steam turbine", "diesel generator").

---

## 2. Quick Fine-Tuning on Google Colab or Local GPU

### Prerequisites:
```bash
pip install torch transformers datasets peft trl accelerate bitsandbytes
```

### Run LoRA Training:
```bash
python train_lora.py --model HuggingFaceTB/SmolLM2-135M-Instruct --epochs 3 --batch_size 8 --output_dir ./models/smollm2-135m-industrial
```
*(Takes ~15-20 minutes on a free Colab T4 GPU).*

---

## 3. Convert to GGUF and Run in Ollama on Edge Device

1. Merge LoRA weights with base model:
```python
from peft import PeftModel
from transformers import AutoModelForCausalLM, AutoTokenizer

base = AutoModelForCausalLM.from_pretrained("HuggingFaceTB/SmolLM2-135M-Instruct")
model = PeftModel.from_pretrained(base, "./models/smollm2-135m-industrial")
merged = model.merge_and_unload()
merged.save_pretrained("./models/smollm2-135m-merged")
tokenizer = AutoTokenizer.from_pretrained("HuggingFaceTB/SmolLM2-135M-Instruct")
tokenizer.save_pretrained("./models/smollm2-135m-merged")
```

2. Convert to GGUF using `llama.cpp`:
```bash
git clone https://github.com/ggerganov/llama.cpp
python llama.cpp/convert_hf_to_gguf.py ./models/smollm2-135m-merged --outfile smollm2-135m-industrial-q4_k_m.gguf --outtype q4_k_m
```

3. Register with Ollama on Edge Box (`192.168.0.5`):
```bash
ollama create industrial-extractor -f Modelfile
```
In `docker-compose.yml`, set:
```yaml
Llm__Model=industrial-extractor
```
