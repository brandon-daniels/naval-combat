"""Bake a seamless, linear RG slope texture for the two advected wavelet scales."""

from pathlib import Path
from random import Random
from math import cos, sin, hypot, pi

import numpy as np
from PIL import Image


def build(size=512, count=88):
    rng = Random(74213)
    # Integer wave numbers make the surface and its derivatives exactly periodic.
    # Allocate equal groups in log wavelength so both gravity and capillary ripples survive.
    frequencies = set()
    x, y = np.meshgrid((np.arange(size) + 0.5) * (2 * pi / size),
                       (np.arange(size) + 0.5) * (2 * pi / size))
    slopes = np.zeros((size, size, 2))
    while len(frequencies) < count:
        frequency = 3.0 * 29.0 ** ((len(frequencies) + rng.random()) / count)
        angle = rng.gauss(0.12, 0.39) if rng.random() < 0.68 else rng.uniform(-pi, pi)
        kx, ky = round(cos(angle) * frequency), round(sin(angle) * frequency)
        # Low frequencies allow few distinct integer headings. Skip the band once
        # occupied by drawing a slightly higher frequency with the same heading.
        while (kx, ky) in frequencies or (-kx, -ky) in frequencies:
            frequency += 0.72
            kx, ky = round(cos(angle) * frequency), round(sin(angle) * frequency)
        frequencies.add((kx, ky))
        magnitude = hypot(kx, ky)
        phase = rng.uniform(-pi, pi)
        power = (0.74 + 0.52 * rng.random()) / (1.0 + (magnitude / 48.0) ** 1.4)
        derivative = power * np.cos(kx * x + ky * y + phase)
        slopes[:, :, 0] += derivative * kx / magnitude
        slopes[:, :, 1] += derivative * ky / magnitude
    # Preserve a linear signed quantity during texture filtering and mip generation.
    slopes *= 0.95 / np.percentile(np.abs(slopes), 99.98)
    rgb = np.full((size, size, 3), 128, dtype=np.uint8)
    rgb[:, :, :2] = np.rint(127.5 + 127.5 * np.clip(slopes, -1, 1)).astype(np.uint8)
    target = Path(__file__).resolve().parents[1] / "Assets/materials/ocean/wavelet_slopes.png"
    Image.fromarray(rgb).save(target, optimize=True)


if __name__ == "__main__":
    build()
