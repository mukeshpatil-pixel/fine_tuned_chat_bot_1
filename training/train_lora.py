#!/usr/bin/env python3
"""Fine-tuning script for Edge Industrial Telemetry Chatbot.

Fine-tunes SmolLM2-135M-Instruct (or Qwen2.5-0.5B-Instruct) using LoRA (PEFT)
and TRL SFTTrainer for structured telemetry intent extraction.

Usage:
    python train_lora.py --model HuggingFaceTB/SmolLM2-135M-Instruct \\
        --output_dir ./models/smollm2-135m-industrial
"""

import argparse
from datasets import load_dataset
from peft import LoraConfig, TaskType, get_peft_model
import torch
from transformers import AutoModelForCausalLM, AutoTokenizer
from trl import SFTConfig, SFTTrainer


def parse_args() -> argparse.Namespace:
    """Parse command line arguments for LoRA fine-tuning.

    Returns:
        argparse.Namespace: Parsed CLI options containing hyperparameters,
            model path, and training configuration.
    """
    parser = argparse.ArgumentParser(
        description="Fine-tune lightweight LLM for industrial intent extraction"
    )
    parser.add_argument(
        "--model",
        type=str,
        default="HuggingFaceTB/SmolLM2-135M-Instruct",
        help="HuggingFace model ID (e.g., SmolLM2-135M-Instruct or Qwen2.5-0.5B)",
    )
    parser.add_argument(
        "--train_file",
        type=str,
        default="data/train_chatml.jsonl",
        help="Path to ChatML training JSONL file",
    )
    parser.add_argument(
        "--val_file",
        type=str,
        default="data/val_chatml.jsonl",
        help="Path to ChatML validation JSONL file",
    )
    parser.add_argument(
        "--output_dir",
        type=str,
        default="./models/industrial-extractor-lora",
        help="Directory to save fine-tuned LoRA adapters",
    )
    parser.add_argument(
        "--epochs",
        type=int,
        default=3,
        help="Number of training epochs",
    )
    parser.add_argument(
        "--batch_size",
        type=int,
        default=8,
        help="Per-device batch size",
    )
    parser.add_argument(
        "--lr",
        type=float,
        default=2e-4,
        help="Peak learning rate for AdamW",
    )
    parser.add_argument(
        "--max_seq_length",
        type=int,
        default=512,
        help="Maximum sequence length in tokens",
    )
    return parser.parse_args()


def main() -> None:
    """Execute model fine-tuning pipeline using Hugging Face TRL and PEFT."""
    args = parse_args()
    print(f"Loading model: {args.model}")

    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"Using compute device: {device}")

    tokenizer = AutoTokenizer.from_pretrained(args.model, trust_remote_code=True)
    if tokenizer.pad_token is None:
        tokenizer.pad_token = tokenizer.eos_token

    # Determine optimal precision based on device capabilities
    is_bf16_ok = (
        torch.cuda.is_available() and torch.cuda.is_bf16_supported()
    )
    compute_dtype = torch.bfloat16 if is_bf16_ok else torch.float32

    # Load base pretrained causal language model
    model = AutoModelForCausalLM.from_pretrained(
        args.model,
        torch_dtype=compute_dtype,
        trust_remote_code=True,
    )

    # Configure Low-Rank Adaptation (LoRA)
    lora_config = LoraConfig(
        r=16,
        lora_alpha=32,
        target_modules=[
            "q_proj",
            "k_proj",
            "v_proj",
            "o_proj",
            "gate_proj",
            "up_proj",
            "down_proj",
        ],
        lora_dropout=0.05,
        bias="none",
        task_type=TaskType.CAUSAL_LM,
    )
    model = get_peft_model(model, lora_config)
    model.print_trainable_parameters()

    # Load instruction datasets
    data_files = {"train": args.train_file, "validation": args.val_file}
    raw_datasets = load_dataset("json", data_files=data_files)

    training_args = SFTConfig(
        output_dir=args.output_dir,
        num_train_epochs=args.epochs,
        per_device_train_batch_size=args.batch_size,
        per_device_eval_batch_size=args.batch_size,
        learning_rate=args.lr,
        warmup_steps=100,
        weight_decay=0.01,
        logging_steps=20,
        eval_strategy="epoch",
        save_strategy="epoch",
        save_total_limit=2,
        fp16=torch.cuda.is_available(),
        max_length=args.max_seq_length,
        loss_type="nll",
        report_to="none",
    )

    trainer = SFTTrainer(
        model=model,
        train_dataset=raw_datasets["train"],
        eval_dataset=raw_datasets["validation"],
        args=training_args,
    )

    print("\nStarting training loop...")
    trainer.train()

    print(f"\nSaving fine-tuned adapter to {args.output_dir}...")
    trainer.save_model(args.output_dir)
    tokenizer.save_pretrained(args.output_dir)
    print("Training finished successfully!")


if __name__ == "__main__":
    main()
