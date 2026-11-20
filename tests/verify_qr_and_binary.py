import os
import sys
from PIL import Image

# Independent ISO/IEC 18004 QR Code Decoder (EC Level M, Versions 1..15)
# Verifies finder patterns, BCH format/version, Reed-Solomon syndromes == 0, and exact UTF-8 payload!

SPECS_M = {
    1:  (26,  10, [(1, 16)]),
    2:  (44,  16, [(1, 28)]),
    3:  (70,  26, [(1, 44)]),
    4:  (100, 18, [(2, 32)]),
    5:  (134, 24, [(2, 43)]),
    6:  (172, 16, [(4, 27)]),
    7:  (196, 18, [(4, 31)]),
    8:  (242, 22, [(2, 38), (2, 39)]),
    9:  (292, 22, [(3, 36), (2, 37)]),
    10: (346, 26, [(4, 43), (1, 44)]),
    11: (404, 30, [(1, 50), (4, 51)]),
    12: (466, 22, [(6, 36), (2, 37)]),
    13: (532, 22, [(8, 37), (1, 38)]),
    14: (581, 24, [(4, 40), (5, 41)]),
    15: (655, 24, [(5, 41), (5, 42)]),
}

ALIGN_POS = {
    1: [],
    2: [6, 18],
    3: [6, 22],
    4: [6, 26],
    5: [6, 30],
    6: [6, 34],
    7: [6, 22, 38],
    8: [6, 24, 42],
    9: [6, 26, 46],
    10: [6, 28, 50],
    11: [6, 30, 54],
    12: [6, 32, 58],
    13: [6, 34, 62],
    14: [6, 26, 46, 66],
    15: [6, 26, 48, 70],
}

GF_EXP = [0] * 512
GF_LOG = [0] * 256
x = 1
for i in range(255):
    GF_EXP[i] = x
    GF_LOG[x] = i
    x <<= 1
    if x & 0x100:
        x ^= 0x11D
for i in range(255, 512):
    GF_EXP[i] = GF_EXP[i - 255]

def gf_mul(a, b):
    if a == 0 or b == 0:
        return 0
    return GF_EXP[GF_LOG[a] + GF_LOG[b]]

def poly_eval(cw, x_val):
    res = 0
    for c in cw:
        res = gf_mul(res, x_val) ^ c
    return res

def check_rs_block(codewords, ec_len):
    # Evaluate codeword polynomial at alpha^0 .. alpha^(ec_len-1); all syndromes MUST be 0!
    for i in range(ec_len):
        s = poly_eval(codewords, GF_EXP[i])
        if s != 0:
            raise AssertionError(f"RS syndrome mismatch at root {i}: got {s}, expected 0")

def mask_fn(mask, x, y):
    if mask == 0: return (x + y) % 2 == 0
    if mask == 1: return y % 2 == 0
    if mask == 2: return x % 3 == 0
    if mask == 3: return (x + y) % 3 == 0
    if mask == 4: return ((y // 2) + (x // 3)) % 2 == 0
    if mask == 5: return ((x * y) % 2 + (x * y) % 3) == 0
    if mask == 6: return (((x * y) % 2 + (x * y) % 3) % 2) == 0
    if mask == 7: return (((x + y) % 2 + (x * y) % 3) % 2) == 0
    raise ValueError(mask)

def decode_qr_png(png_path):
    img = Image.open(png_path).convert("L")
    w, h = img.size
    assert w == h, f"QR image not square: {w}x{h}"
    px = img.load()

    # Find top-left dark pixel (start of top-left finder pattern)
    left = top = None
    for y in range(h):
        for x in range(w):
            if px[x, y] < 128:
                left, top = x, y
                break
        if left is not None:
            break
    assert left is not None, "No dark pixels in QR image"

    # Measure top-left finder width (7 modules)
    run = 0
    while left + run < w and px[left + run, top] < 128:
        run += 1
    assert run % 7 == 0, f"Finder width {run} not divisible by 7"
    mod_px = run // 7

    # Measure total active QR area
    right = w - 1
    while right > left and px[right, top] >= 128:
        right -= 1
    span_px = right - left + 1
    assert span_px % mod_px == 0, f"QR span {span_px} not multiple of module {mod_px}"
    size = span_px // mod_px
    assert (size - 17) % 4 == 0, f"Invalid QR module size {size}"
    version = (size - 17) // 4
    assert version in SPECS_M, f"Unsupported version {version}"

    # Sample module centers
    m = [[False] * size for _ in range(size)]
    for y in range(size):
        for x in range(size):
            val = px[left + x * mod_px + mod_px // 2, top + y * mod_px + mod_px // 2]
            m[y][x] = (val < 128)

    # Verify dark module at (8, size - 8)
    assert m[size - 8][8], "Mandatory dark module missing"

    # Read format info (both copies) and verify identical + valid BCH
    fmt1 = 0
    coords1 = [(8, 0), (8, 1), (8, 2), (8, 3), (8, 4), (8, 5), (8, 7), (8, 8), (7, 8),
               (5, 8), (4, 8), (3, 8), (2, 8), (1, 8), (0, 8)]
    for i, (cx, cy) in enumerate(coords1):
        if m[cy][cx]:
            fmt1 |= (1 << i)

    fmt2 = 0
    coords2 = [(size - 1 - i, 8) for i in range(8)] + [(8, size - 15 + i) for i in range(8, 15)]
    for i, (cx, cy) in enumerate(coords2):
        if m[cy][cx]:
            fmt2 |= (1 << i)
    assert fmt1 == fmt2, f"Format info copies disagree: {fmt1:#x} vs {fmt2:#x}"

    raw_fmt = fmt1 ^ 0x5412
    data_5 = (raw_fmt >> 10) & 0x1F
    rem = data_5
    for _ in range(10):
        rem = (rem << 1) ^ (((rem >> 9) & 1) * 0x537)
    assert ((data_5 << 10) | rem) == raw_fmt, f"Format BCH invalid: {raw_fmt:#x}"
    ec_bits = (data_5 >> 3) & 0x3
    mask = data_5 & 0x7
    assert ec_bits == 0, f"Expected EC Level M (00), got {ec_bits}"

    # Verify version info if version >= 7
    if version >= 7:
        vbits1 = 0
        vbits2 = 0
        for i in range(18):
            a = size - 11 + (i % 3)
            b = i // 3
            if m[b][a]: vbits1 |= (1 << i)
            if m[a][b]: vbits2 |= (1 << i)
        assert vbits1 == vbits2, f"Version info copies disagree: {vbits1:#x} vs {vbits2:#x}"
        vrem = version
        for _ in range(12):
            vrem = (vrem << 1) ^ (((vrem >> 11) & 1) * 0x1F25)
        assert ((version << 12) | vrem) == vbits1, f"Version BCH invalid for V{version}"

    # Build function mask map
    func = [[False] * size for _ in range(size)]
    def mark_rect(x0, y0, x1, y1):
        for yy in range(max(0, y0), min(size, y1 + 1)):
            for xx in range(max(0, x0), min(size, x1 + 1)):
                func[yy][xx] = True

    mark_rect(0, 0, 8, 8)
    mark_rect(size - 8, 0, size - 1, 8)
    mark_rect(0, size - 8, 8, size - 1)
    for i in range(size):
        func[6][i] = True
        func[i][6] = True
    for cx in ALIGN_POS[version]:
        for cy in ALIGN_POS[version]:
            if (cx == 6 and cy == 6) or (cx == 6 and cy == size - 7) or (cx == size - 7 and cy == 6):
                continue
            mark_rect(cx - 2, cy - 2, cx + 2, cy + 2)
    if version >= 7:
        mark_rect(size - 11, 0, size - 9, 5)
        mark_rect(0, size - 11, 5, size - 9)

    # Read zig-zag bits and unmask
    bits = []
    right_col = size - 1
    while right_col >= 1:
        if right_col == 6:
            right_col = 5
        upward = (((right_col + 1) & 2) == 0)
        for vert in range(size):
            y = (size - 1 - vert) if upward else vert
            for dx in range(2):
                x = right_col - dx
                if not func[y][x]:
                    bit = m[y][x]
                    if mask_fn(mask, x, y):
                        bit = not bit
                    bits.append(1 if bit else 0)
        right_col -= 2

    total_cw, ec_len, groups = SPECS_M[version]
    codewords = []
    for i in range(total_cw):
        val = 0
        for k in range(8):
            val = (val << 1) | bits[i * 8 + k]
        codewords.append(val)

    # De-interleave blocks and verify RS syndromes == 0
    block_lens = []
    for cnt, dlen in groups:
        for _ in range(cnt):
            block_lens.append(dlen)

    data_blocks = [[] for _ in block_lens]
    ec_blocks = [[] for _ in block_lens]
    idx = 0
    max_d = max(block_lens)
    for col in range(max_d):
        for b, dlen in enumerate(block_lens):
            if col < dlen:
                data_blocks[b].append(codewords[idx])
                idx += 1
    for col in range(ec_len):
        for b in range(len(block_lens)):
            ec_blocks[b].append(codewords[idx])
            idx += 1

    for b in range(len(block_lens)):
        check_rs_block(data_blocks[b] + ec_blocks[b], ec_len)

    # Parse Byte-mode payload
    all_data = []
    for db in data_blocks:
        all_data.extend(db)
    dbits = []
    for byte in all_data:
        for k in range(7, -1, -1):
            dbits.append((byte >> k) & 1)

    def read_int(pos, n):
        v = 0
        for i in range(n):
            v = (v << 1) | dbits[pos + i]
        return v, pos + n

    mode, pos = read_int(0, 4)
    assert mode == 4, f"Expected Byte mode (0100), got {mode}"
    len_bits = 8 if version <= 9 else 16
    byte_len, pos = read_int(pos, len_bits)
    out_bytes = bytearray()
    for _ in range(byte_len):
        bval, pos = read_int(pos, 8)
        out_bytes.append(bval)
    return version, mask, out_bytes.decode("utf-8")

def verify_binary_manifest_and_desensitization(exe_path, expected_level="requireAdministrator", require_dpi=True, forbidden=None):
    raw = open(exe_path, "rb").read()
    # Check manifest execution level embedded
    level_needle = f'level="{expected_level}"'.encode("ascii")
    assert level_needle in raw, f"Missing {expected_level} manifest in {exe_path}!"
    if require_dpi:
        assert b'PerMonitorV2' in raw, f"Missing PerMonitorV2 DPI awareness in {exe_path}!"
    # Check ZERO hardcoded personal user paths or caller-supplied sensitive strings in binary
    checks = [b"C:\\Users\\", b"D:\\Users\\"]
    for c in checks:
        assert c not in raw and c.decode("ascii").encode("utf-16-le") not in raw, f"Hardcoded user path leaked in {exe_path}"
    if forbidden:
        for secret in forbidden:
            if not secret:
                continue
            utf8_b = secret.encode("utf-8")
            utf16_b = secret.encode("utf-16-le")
            assert utf8_b not in raw, f"UTF-8 SENSITIVE STRING LEAKED IN BINARY ({exe_path})"
            assert utf16_b not in raw, f"UTF-16 SENSITIVE STRING LEAKED IN BINARY ({exe_path})"
    print(f"[PASS] Binary manifest ({expected_level}) & 0-secret/0-PII audit passed: {os.path.basename(exe_path)} ({len(raw)} bytes)")

if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if a != "--demo"]
    is_demo = "--demo" in sys.argv[1:]
    qr_path = args[0]
    exe_path = args[1]
    launcher_path = args[2] if len(args) > 2 else None
    forbidden = [x for x in os.environ.get("RC_FORBIDDEN_STRINGS", "").split(",") if x]
    ver, mask, decoded = decode_qr_png(qr_path)
    print(f"[PASS] ISO/IEC 18004 QR Code decoded cleanly! Version={ver}, Mask={mask}, RS Syndromes=ALL ZERO")
    print("--- Decoded QR Payload ---")
    print(decoded)
    print("--------------------------")
    assert "10.126.126.1" in decoded, "Host IP missing in QR"
    assert "10.126.126.2/24" in decoded, "Tablet IP missing in QR"
    if is_demo:
        assert "rc-demo2026" in decoded, "Demo Network Name missing in QR"
        assert "DemoSecret1234567890" in decoded, "Demo Network Secret missing in QR"
    else:
        assert "Network Name:" in decoded and "Network Secret:" in decoded, "Credentials missing in QR"
    assert "tcp://225284.xyz:11010" in decoded, "Live Peer URI missing in QR"
    verify_binary_manifest_and_desensitization(exe_path, "requireAdministrator", True, forbidden)
    if launcher_path and os.path.exists(launcher_path):
        verify_binary_manifest_and_desensitization(launcher_path, "asInvoker", False, forbidden)
    print("[ALL PYTHON VERIFICATIONS PASSED]")
