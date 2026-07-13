"""Dump de un .pex de Skyrim (big-endian): tabla de strings, variables del objeto y PROPIEDADES
(nombre + tipo + flags). Ground truth de lo que el script realmente declara."""
import struct, sys

class R:
    def __init__(s, b): s.b = b; s.o = 0
    def u8(s):  v = s.b[s.o]; s.o += 1; return v
    def u16(s): v = struct.unpack_from(">H", s.b, s.o)[0]; s.o += 2; return v
    def u32(s): v = struct.unpack_from(">I", s.b, s.o)[0]; s.o += 4; return v
    def i32(s): v = struct.unpack_from(">i", s.b, s.o)[0]; s.o += 4; return v
    def f32(s): v = struct.unpack_from(">f", s.b, s.o)[0]; s.o += 4; return v
    def i64(s): v = struct.unpack_from(">q", s.b, s.o)[0]; s.o += 8; return v
    def st(s):
        n = s.u16(); v = s.b[s.o:s.o+n].decode("utf-8", "replace"); s.o += n; return v

def variant(r, S):
    t = r.u8()
    if t == 0: return None
    if t == 1: return S[r.u16()]           # identifier
    if t == 2: return S[r.u16()]           # string
    if t == 3: return r.i32()
    if t == 4: return r.f32()
    if t == 5: return bool(r.u8())
    raise ValueError(f"variant type {t}")

path = sys.argv[1]
b = open(path, "rb").read()
r = R(b)
magic = r.u32()
assert magic == 0xFA57C0DE, f"magic {magic:08X} (esperado FA57C0DE = Skyrim BE)"
major, minor = r.u8(), r.u8()
game, ts = r.u16(), r.i64()
src = r.st(); user = r.st(); machine = r.st()

ns = r.u16()
S = [r.st() for _ in range(ns)]
has_debug = r.u8()
if has_debug:
    r.i64()
    n = r.u16()
    for _ in range(n):
        r.u16(); r.u8(); r.u16(); r.u16()
        ln = r.u16()
        for _ in range(ln): r.u16()
nuf = r.u16()
for _ in range(nuf):
    r.u16(); r.u8()

nobj = r.u16()
print(f"=== {path.split(chr(92))[-1]}   v{major}.{minor}  strings={ns}  objects={nobj}\n")
for _ in range(nobj):
    name = S[r.u16()]
    size = r.u32()
    end = r.o + size - 4
    parent = S[r.u16()]; docstring = S[r.u16()]
    uflags = r.u32(); autostate = S[r.u16()]

    nvar = r.u16()
    print(f"OBJETO '{name}' extends '{parent}'")
    print(f"  --- VARIABLES ({nvar}) ---")
    for _ in range(nvar):
        vn = S[r.u16()]; vt = S[r.u16()]; vf = r.u32()
        variant(r, S)
        print(f"      {vt:12} {vn}")

    nprop = r.u16()
    print(f"  --- PROPIEDADES ({nprop}) ---")
    arrays = []
    for _ in range(nprop):
        pn = S[r.u16()]; pt = S[r.u16()]; pdoc = S[r.u16()]; pf = r.u8()
        kind = []
        if pf & 1: kind.append("read")
        if pf & 2: kind.append("write")
        if pf & 4: kind.append("AUTO")
        if pf & 4:
            autovar = S[r.u16()]
        else:
            # read/write handlers: funciones -> las salteamos parseando el cuerpo
            raise SystemExit("  (propiedad no-auto: parser simplificado, abortando)")
        mark = " <<< ARRAY" if pt.endswith("[]") else ""
        print(f"      {pt:12} {pn:16} [{','.join(kind)}]{mark}")
        if pt.endswith("[]"): arrays.append(pn)
    print(f"\n  ARRAYS declarados: {len(arrays)} -> {', '.join(arrays)}")
    break
