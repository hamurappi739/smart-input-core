#!/usr/bin/env python3
"""Audit DAWG terminal-bit hypotheses against known local lexicons.

This tool reports aggregate hit rates only. It never writes extracted words or
user text; the recovered model and lexicons are supplied explicitly.
"""
from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path


def read_varint(data: bytes, offset: int) -> tuple[int, int]:
    value = 0
    shift = 0
    while True:
        if offset >= len(data) or shift >= 64:
            raise ValueError("invalid protobuf varint")
        current = data[offset]
        offset += 1
        value |= (current & 0x7F) << shift
        if current < 0x80:
            return value, offset
        shift += 7


def field(data: bytes, number: int) -> bytes:
    offset = 0
    while offset < len(data):
        key, offset = read_varint(data, offset)
        current_number, wire = key >> 3, key & 7
        if wire == 2:
            size, offset = read_varint(data, offset)
            end = offset + size
            if end > len(data):
                raise ValueError("truncated protobuf field")
            value = data[offset:end]
            offset = end
            if current_number == number:
                return value
        elif wire == 0:
            _, offset = read_varint(data, offset)
        elif wire == 1:
            offset += 8
        elif wire == 5:
            offset += 4
        else:
            raise ValueError("unsupported protobuf wire type")
    raise KeyError(number)


def unpack(block: bytes) -> list[int]:
    count = int.from_bytes(block[:4], "little")
    if len(block) != (count + 1) * 4:
        raise ValueError("invalid DAWG block")
    return [int.from_bytes(block[i:i + 4], "little") for i in range(4, len(block), 4)]


def follow(records: list[int], word: str) -> tuple[bool, int]:
    state = 0
    for label in word.encode("utf-8"):
        record = records[state]
        base = (record >> 10) << (8 if record & 0x200 else 0)
        candidate = state ^ label ^ base
        if candidate < 0 or candidate >= len(records):
            return False, -1
        if records[candidate] & 0x800000FF != label:
            return False, -1
        state = candidate
    return True, state


def load_words(path: Path, limit: int) -> list[str]:
    if path.suffix.lower() == ".csv":
        with path.open(encoding="utf-8", newline="") as stream:
            return [row[0] for row in csv.reader(stream) if row and row[0] != "lemma"][:limit]
    return [line.split()[0] for line in path.read_text(encoding="utf-8").splitlines() if line.strip()][:limit]


def score(records: list[int], words: list[str]) -> dict[str, object]:
    reached: list[tuple[int, int]] = []
    for word in words:
        ok, state = follow(records, word)
        if ok:
            reached.append((state, len(word)))
    bits = [0x100, 0x200, 0x400, 0x800, 0x1000, 0x2000, 0x4000, 0x8000, 0x80000000]
    return {
        "wordsChecked": len(words),
        "pathsReached": len(reached),
        "terminalBitHits": {hex(bit): sum(1 for state, _ in reached if records[state] & bit) for bit in bits},
        "lowByteNonZero": sum(1 for state, _ in reached if records[state] & 0xFF),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("model", type=Path)
    parser.add_argument("ru", type=Path)
    parser.add_argument("en", type=Path)
    parser.add_argument("--limit", type=int, default=50000)
    args = parser.parse_args()
    model = args.model.read_bytes()
    field2 = field(model, 2)
    field5 = field(model, 5)
    blocks = [unpack(field(field2, 2)), unpack(field(field5, 1))]
    words = [load_words(args.ru, args.limit), load_words(args.en, args.limit)]
    print(json.dumps({"blockA": score(blocks[0], words[0]), "blockB": score(blocks[1], words[1])}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
