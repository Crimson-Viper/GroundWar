# Makes the placeholder battle sounds in sounds/. Run: python3 tools/make_sounds.py
# Everything is synthesised from noise and simple filters (no recordings), so it can be regenerated or tweaked
# here. Swap in real recordings later by saving WAV files with the same names.
import math, random, struct, wave
R = 22050
rnd = random.Random(3)

def write(name, samples):
    peak = max(1e-6, max(abs(s) for s in samples))
    with wave.open(f"sounds/{name}.wav", "w") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(R)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s / peak * 0.9)) * 32767)) for s in samples))

def noise(n): return [rnd.uniform(-1, 1) for _ in range(n)]

def lowpass(x, cutoff):
    a = 1 - math.exp(-2 * math.pi * cutoff / R); y = 0; out = []
    for s in x: y += a * (s - y); out.append(y)
    return out

def env(n, attack, decay):
    return [min(1, i / max(1, attack * R)) * math.exp(-i / (decay * R)) for i in range(n)]

def mix(*parts):
    n = max(len(p) for p in parts); out = [0.0] * n
    for p in parts:
        for i, s in enumerate(p): out[i] += s
    return out

def shot(length, crack_decay, body_cut, body_decay, boom_freq, boom_decay, tail):
    n = int(R * length)
    crack = [s * e for s, e in zip(noise(n), env(n, 0.0005, crack_decay))]
    body = [s * e for s, e in zip(lowpass(noise(n), body_cut), env(n, 0.001, body_decay))]
    boom = [math.sin(2 * math.pi * boom_freq * i / R * (1 - 0.3 * i / n)) * e for i, e in enumerate(env(n, 0.002, boom_decay))]
    echo = [s * e * 0.25 for s, e in zip(lowpass(noise(n), 900), env(n, 0.05, tail))]
    return mix(crack, [b * 3 for b in body], [b * 0.6 for b in boom], echo)

write("rifle", shot(0.9, 0.006, 2500, 0.05, 110, 0.08, 0.35))
write("mg", shot(0.35, 0.004, 3000, 0.03, 140, 0.04, 0.12))
write("tank_gun", shot(2.2, 0.01, 700, 0.25, 45, 0.5, 0.9))
# A shell or bazooka burst: a deep thump, a crash of noise, debris pattering down.
n = int(R * 2.5)
thump = [math.sin(2 * math.pi * 38 * i / R) * e for i, e in enumerate(env(n, 0.003, 0.35))]
crash = [s * e for s, e in zip(lowpass(noise(n), 1200), env(n, 0.002, 0.3))]
debris = [s * e * (1 if rnd.random() < 0.02 else 0.1) for s, e in zip(noise(n), env(n, 0.3, 0.8))]
write("explosion", mix([t * 1.2 for t in thump], [c * 3 for c in crash], [d * 0.4 for d in debris]))
# Distant battle: a loop of far-off rumble and the odd faint pop.
n = R * 8
rumble = lowpass(lowpass(noise(n), 120), 120)
pops = [0.0] * n
for _ in range(40):
    at = rnd.randrange(n - R)
    for i, s in enumerate(shot(0.4, 0.004, 1500, 0.04, 90, 0.05, 0.2)):
        pops[at + i] += s * rnd.uniform(0.05, 0.2)
lp = lowpass(pops, 1800)
write("distant", mix([r * 6 for r in rumble], lp))
# Radio: a short squelch and two beeps when a message gets through.
n = int(R * 0.45)
sq = [s * e * 0.3 for s, e in zip(noise(n), env(n, 0.001, 0.05))]
beep = [math.sin(2 * math.pi * 1200 * i / R) * (1 if (0.12 * R < i < 0.2 * R or 0.26 * R < i < 0.34 * R) else 0) * 0.5 for i in range(n)]
write("radio", mix(sq, beep))
print("written sounds/*.wav")
