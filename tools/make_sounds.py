#!/usr/bin/env python3
"""Synthesises AVAArchon's sound effects and tunes into ../src/AVAArchon/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, sine and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAArchon", "Sounds")
RNG = random.Random(1983)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The board.
    write("Cursor.wav", tone(0.025, 1500, kind="tri", release=1.0), 0.18)
    write("Menu.wav", tone(0.03, 1200, kind="tri", release=0.8), 0.2)
    write("Select.wav", cat(tone(0.04, hz("E5"), kind="pulse", release=0.5), tone(0.08, hz("B5"), kind="pulse", release=0.7)), 0.3)
    write("Cancel.wav", cat(tone(0.04, hz("B5"), kind="pulse", release=0.5), tone(0.08, hz("E5"), kind="pulse", release=0.7)), 0.28)
    write("Buzz.wav", tone(0.16, 110, kind="square", release=0.4), 0.25)
    write("Step.wav", cat(*[cat(tone(0.04, 160, 90, "noise", release=1.2), silence(0.08)) for _ in range(3)]), 0.3)
    write("Fly.wav", cat(*[tone(0.12, 500, 900, "noise", release=0.6, gain=0.6 + 0.2 * i) for i in range(3)]), 0.25)
    write("Clash.wav", mix(cat(tone(0.12, hz("A3"), kind="saw", release=0.3), tone(0.12, hz("A3"), kind="saw", release=0.3), tone(0.5, hz("D4"), kind="saw", release=0.5)),
                           cat(silence(0.24), tone(0.5, 180, 60, "noise", release=0.7, gain=0.6))), 0.5)
    write("Spell.wav", cat(*[tone(0.05, hz(n), kind="sine", release=0.4) for n in ["C6", "E6", "G6", "B6", "D7", "G7"]], tone(0.3, hz("C7"), kind="sine", vibrato=0.02, release=0.8)), 0.4)
    write("PowerPoint.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.4) for n in ["G4", "D5", "G5"]], tone(0.3, hz("B5"), kind="pulse", release=0.8)), 0.38)
    write("Lumen.wav", mix(tone(0.5, hz("E5"), kind="sine", release=1.0, gain=0.6), tone(0.5, hz("B5"), kind="sine", release=1.2, gain=0.4)), 0.22)

    # The arena.
    write("Swing.wav", tone(0.12, 1800, 300, "noise", release=1.2), 0.35)
    write("Arrow.wav", mix(tone(0.08, 2200, 1200, "noise", release=1.0), tone(0.06, 900, 600, "tri", release=1.0, gain=0.4)), 0.3)
    write("Bolt.wav", tone(0.18, 1400, 300, "square", release=0.8, vibrato=0.04, vib_rate=40), 0.3)
    write("Boulder.wav", mix(tone(0.3, 140, 60, "noise", release=0.6), tone(0.3, 90, 50, "tri", release=0.6)), 0.5)
    write("Lightning.wav", mix(tone(0.3, 4000, 400, "noise", release=0.9), tone(0.25, 1600, 200, "saw", release=0.9, gain=0.4)), 0.42)
    write("Acid.wav", cat(tone(0.06, 600, 300, "noise", release=0.5), tone(0.12, 300, 900, "tri", release=0.8, vibrato=0.1, vib_rate=30)), 0.32)
    write("Fire.wav", tone(0.25, 900, 200, "noise", release=0.7), 0.35)
    write("Flame.wav", mix(tone(0.7, 300, 1200, "noise", release=0.5), tone(0.7, 120, 240, "saw", release=0.6, gain=0.4)), 0.45)
    write("Wail.wav", tone(0.8, 700, 1300, "sine", release=0.4, vibrato=0.06, vib_rate=7), 0.4)
    write("Hit.wav", mix(tone(0.08, 220, 80, "square", release=0.8), tone(0.06, 1200, 200, "noise", release=1.0, gain=0.5)), 0.4)
    write("Death.wav", cat(*[tone(0.1, f, f * 0.8, "square", release=0.5) for f in [500, 420, 350, 290, 240, 200, 160, 120]]), 0.4)

    # The title: a stately march in D minor, light against dark (new). Loops.
    lead = [("D5", 1), ("A4", 0.5), ("D5", 0.5), ("F5", 1), ("E5", 0.5), ("D5", 0.5),
            ("C5", 1), ("G4", 0.5), ("C5", 0.5), ("E5", 1.5), ("-", 0.5),
            ("D5", 1), ("F5", 0.5), ("A5", 0.5), ("Bb5", 1), ("A5", 0.5), ("G5", 0.5),
            ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C#5", 0.5), ("D5", 2),
            ("A5", 1), ("G5", 0.5), ("F5", 0.5), ("G5", 1), ("F5", 0.5), ("E5", 0.5),
            ("F5", 1), ("E5", 0.5), ("D5", 0.5), ("E5", 1.5), ("-", 0.5),
            ("D5", 1), ("F5", 0.5), ("A5", 0.5), ("D6", 1), ("C6", 0.5), ("Bb5", 0.5),
            ("A5", 0.5), ("G5", 0.5), ("E5", 0.5), ("C#5", 0.5), ("D5", 2)]
    bass = [(n, 1) for n in ["D3", "A2", "D3", "A2", "C3", "G2", "C3", "G2", "Bb2", "F2", "G2", "D3", "A2", "E2", "D3", "D3",
                             "F2", "C3", "C3", "G2", "D3", "A2", "C3", "C3", "Bb2", "F2", "G2", "G2", "A2", "A2", "D3", "D3"]]
    write("TitleTune.wav", tune(112, lead, bass), 0.5)
    write("Victory.wav", tune(126,
        [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 1.5), ("C6", 0.5), ("A5", 0.5), ("B5", 0.5), ("G5", 0.5), ("A5", 0.5), ("F#5", 0.5), ("D6", 3)],
        [("D3", 1.5), ("A2", 1.5), ("G2", 1), ("A2", 1), ("D3", 4)]), 0.55)


if __name__ == "__main__":
    main()
