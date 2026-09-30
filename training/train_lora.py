#!/usr/bin/env python3
"""
Fine-tuning script for Edge Industrial Telemetry Chatbot.
Fine-tunes SmolLM2-135M-Instruct (or Qwen2.5-0.5B-Instruct) using LoRA (PEFT) and TRL SFTTrainer.

Usage:
    pip install torch transformers datasets peft trl accelerate bitsandbytes
    python train_lora.py --model HuggingFaceTB/SmolLM2-135M-Instruct --output_dir ./models/smollm2-135m-industrial
"""

import argparse
import os
import torch
from datasets import load_dataset
from transformers import (
    AutoModelForCausalLM,
    AutoTokenizer,
    TrainingArguments
)
from peft import LoraConfig, get_peft_model, TaskType
from trl import SFTTrainer

def parse_args():
    parser = argparse.ArgumentParser(description="Fine-tune lightweight LLM for intent extraction")
    parser.add_argument("--model", type=str, default="HuggingFaceTB/SmolLM2-135M-Instruct",
                        help="HuggingFace model ID (e.g. HuggingFaceTB/SmolLM2-135M-Instruct or Qwen/Qwen2.5-0.5B-Instruct)")
    parser.add_argument("--train_file", type=str, default="data/train_chatml.jsonl")
    parser.add_argument("--val_file", type=str, default="data/val_chatml.jsonl")
    parser.add_argument("--output_dir", type=str, default="./models/industrial-extractor-lora")
    parser.add_argument("--epochs", type=int, default=3)
    parser.add_argument("--batch_size", type=int, default=8)
    parser.add_argument("--lr", type=float, default=2e-4)
    parser.add_argument("--max_seq_length", type=int, default=512)
    return parser.parse_args()

def main():
    args = parse_args()
    print(f"Loading model: {args.model}")

    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"Using device: {device}")

    tokenizer = AutoTokenizer.from_pretrained(args.model, trust_remote_code=True)
    if tokenizer.pad_token is None:
        tokenizer.pad_token = tokenizer.eos_token

    # Load base model
    model = AutoModelForCausalLM.from_pretrained(
        args.model,
        torch_dtype=torch.bfloat16 if torch.cuda.is_available() and torch.cuda.is_bf16_supported() else torch.float32,
        trust_remote_code=True
    )

    # Configure LoRA
    lora_config = LoraConfig(
        r=16,
        lora_alpha=32,
        target_modules=["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"],
        lora_dropout=0.05,
        bias="none",
        task_type=TaskType.CAUSAL_LM
    )
    model = get_peft_model(model, lora_config)
    model.print_trainable_parameters()

    # Load dataset
    data_files = {"train": args.train_file, "validation": args.val_file}
    raw_datasets = load_dataset("json", data_files=data_files)

    from trl import SFTConfig, SFTTrainer

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
        report_to="none"
    )

    trainer = SFTTrainer(
        model=model,
        train_dataset=raw_datasets["train"],
        eval_dataset=raw_datasets["validation"],
        args=training_args
    )

    print("\nStarting training...")
    trainer.train()

    print(f"\nSaving fine-tuned adapter to {args.output_dir}...")
    trainer.save_model(args.output_dir)
    tokenizer.save_pretrained(args.output_dir)
    print("Training finished successfully!")

if __name__ == "__main__":
    main()
