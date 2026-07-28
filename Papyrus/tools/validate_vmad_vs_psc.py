"""Valida el VMAD emitido contra el .psc que lo consume, en AMBOS juegos.

Chequea, por cada script NPCM_* encontrado en un plugin:
  1. Toda propiedad emitida EXISTE en el .psc.
  2. El TIPO del VMAD coincide con el tipo declarado en el .psc.
  3. NINGUN array tiene longitud 0  (ilegal en Papyrus de Skyrim -> envenena el script entero).
  4. Los arrays paralelos de un mismo grupo tienen la MISMA longitud.
  5. Reporta propiedades declaradas en el .psc que el VMAD no trae (solo informativo:
     el script las guardea con != None).
"""
import struct, zlib, re, os, sys

REPO = r"c:\Users\jvare\OneDrive\Documentos\Familia Varela\Mis Proyectos\Fallout 4 Related"
TARGETS = [
    ("SSE", r"F:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data\NPC_Manager.esp",
             os.path.join(REPO, r"FO4_NPC_Manager\Papyrus\src_sse\NPCM_Manolov_ApplySSE.psc")),
    ("FO4", r"F:\SteamLibrary\steamapps\common\Fallout 4\Data\NPC_Manager.esp",
             os.path.join(REPO, r"FO4_NPC_Manager\Papyrus\src_fo4\NPCM_Manolov_ApplyFO4.psc")),
]

# tipo VMAD -> tipo Papyrus
VMAD_T = {1:"object",2:"string",3:"int",4:"float",5:"bool",
          11:"object[]",12:"string[]",13:"int[]",14:"float[]",15:"bool[]"}

# Los nombres del payload llevan sufijo de generacion (_G1, _G2, ...) porque una property que el
# savegame YA tiene se restaura RANCIA y solo una con nombre nuevo se inicializa del VMAD. Los grupos
# de abajo se escriben SIN sufijo y se comparan con base_name(), asi el validador no hay que tocarlo
# en cada release. Ver la cabecera de NPCM_Manolov_ApplySSE.psc.
def base_name(n):
    return re.sub(r"_G\d+$", "", n)

# grupos de arrays paralelos que DEBEN tener la misma longitud
GROUPS = {
    "SSE": [("overlays", ["OvlNode","OvlDiffuse","OvlNormal","OvlHasTint","OvlTint","OvlHasAlpha","OvlAlpha"]),
            ("skin",     ["SkinSlot","SkinDiffuse","SkinNormal","SkinHasTint","SkinTint"]),
            ("nodes",    ["NodeName","NodeHasScale","NodeScale","NodeHasPos","NodePosX","NodePosY","NodePosZ",
                          "NodeHasRot","NodeScaleMode"])],
    "FO4": [("overlays", ["OvlTemplate","OvlPriority","OvlRed","OvlGreen","OvlBlue","OvlAlpha",
                          "OvlOffsetU","OvlOffsetV","OvlScaleU","OvlScaleV"])],
}

def psc_properties(path):
    """name -> declared papyrus type (lowercase)"""
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r'\s*([A-Za-z_][\w\[\]:]*)\s+Property\s+(\w+)', line)
        if m:
            out[m.group(2)] = m.group(1).lower()
    return out

class R:
    def __init__(s,b): s.b=b; s.o=0
    def u8(s): x=s.b[s.o]; s.o+=1; return x
    def u16(s): x=struct.unpack_from("<H",s.b,s.o)[0]; s.o+=2; return x
    def i16(s): x=struct.unpack_from("<h",s.b,s.o)[0]; s.o+=2; return x
    def u32(s): x=struct.unpack_from("<I",s.b,s.o)[0]; s.o+=4; return x
    def st(s):
        l=s.u16(); x=s.b[s.o:s.o+l].decode("utf-8","replace"); s.o+=l; return x

def skipval(r,t):
    if t in (0,6): return None
    if t==1: r.o+=8; return None
    if t==2: r.st(); return None
    if t in (3,4): r.o+=4; return None
    if t==5: r.o+=1; return None
    if t==7:
        for _ in range(r.u32()):
            r.st(); tt=r.u8(); r.u8(); skipval(r,tt)
        return None
    if t in (11,12,13,14,15,17):
        c=r.u32()
        for _ in range(c):
            if t==11: r.o+=8
            elif t==12: r.st()
            elif t in (13,14): r.o+=4
            elif t==15: r.o+=1
            elif t==17:
                for _ in range(r.u32()):
                    r.st(); tt=r.u8(); r.u8(); skipval(r,tt)
        return c
    if t==16: r.u32(); return None
    raise ValueError(f"tipo desconocido {t}")

def records(d):
    i=24+struct.unpack_from("<I",d,4)[0]
    while i<len(d)-24:
        sig=d[i:i+4]
        if sig==b"GRUP": i+=24; continue
        dsz=struct.unpack_from("<I",d,i+4)[0]; fl=struct.unpack_from("<I",d,i+8)[0]
        fid=struct.unpack_from("<I",d,i+12)[0]
        b=d[i+24:i+24+dsz]
        if fl&0x40000:
            try: b=zlib.decompress(b[4:])
            except Exception: b=b""
        yield sig.decode(),fid,b; i+=24+dsz

def subs(b):
    i=0
    while i+6<=len(b):
        s=b[i:i+4]; sz=struct.unpack_from("<H",b,i+4)[0]
        yield s.decode(), b[i+6:i+6+sz]; i+=6+sz

fails = 0
for label, esp, psc in TARGETS:
    print(f"########## {label}")
    if not os.path.exists(esp):
        print(f"  [SKIP] no existe {esp}\n"); continue
    if not os.path.exists(psc):
        print(f"  [SKIP] no existe {psc}\n"); continue
    decl = psc_properties(psc)
    print(f"  .psc declara {len(decl)} propiedades")

    found_any = False
    for sig, fid, body in records(open(esp,"rb").read()):
        if sig != "NPC_": continue
        for s, payload in subs(body):
            if s != "VMAD": continue
            r = R(payload)
            ver=r.i16(); objfmt=r.i16(); cnt=r.u16()
            for _ in range(cnt):
                name=r.st(); r.u8(); pc=r.u16()
                mine = name.startswith("NPCM_")
                props = {}
                for _ in range(pc):
                    pn=r.st(); t=r.u8(); r.u8()
                    n = skipval(r,t)
                    if mine: props[pn] = (t, n)
                if not mine: continue
                found_any = True
                print(f"\n  NPC_ {fid:08X}  script '{name}'  ({pc} props, VMAD ver={ver})")
                errs = []
                for pn,(t,n) in props.items():
                    vt = VMAD_T.get(t, f"?{t}")
                    if pn not in decl:
                        errs.append(f"PROPIEDAD INEXISTENTE en el .psc: {pn}")
                    elif decl[pn] != vt:
                        errs.append(f"TIPO NO COINCIDE {pn}: VMAD={vt} vs .psc={decl[pn]}")
                    if n == 0:
                        errs.append(f"ARRAY VACIO (ilegal en Skyrim): {pn}")
                # Los grupos se declaran sin sufijo de generacion; el VMAD lo trae. Indexar por base.
                # (El chequeo `pn not in decl` de arriba SI compara con sufijo, a proposito: asi una
                # desincronizacion entre el .psc y PayloadGeneration del emisor sale como error duro.)
                props_by_base = {base_name(k): v for k, v in props.items()}
                for gname, members in GROUPS[label]:
                    lens = {m: props_by_base[m][1] for m in members if m in props_by_base}
                    if lens and len(set(lens.values())) > 1:
                        errs.append(f"ARRAYS PARALELOS DESPAREJOS en '{gname}': {lens}")
                # ⛔ Una ARRAY-property declarada y AUSENTE del VMAD queda en None, y en Skyrim
                # `if X == None` sobre eso TIRA. Es un ERROR, no un aviso.
                CONSTS = ("KEY_TINT","KEY_ALPHA","KEY_TEXTURE","IDX_DIFFUSE","IDX_NORMAL")
                missing = [d for d in decl if d not in props and d not in CONSTS]
                for d in missing:
                    if decl[d].endswith("[]"):
                        errs.append(f"ARRAY-PROPERTY AUSENTE del VMAD (quedaria en None): {d} ({decl[d]})")
                if errs:
                    fails += len(errs)
                    for e in errs: print(f"      [X] {e}")
                else:
                    print("      [OK] tipos OK - sin arrays vacíos - arrays paralelos parejos")
                nonarr = [d for d in missing if not decl[d].endswith("[]")]
                if nonarr:
                    print(f"      - escalares ausentes (OK): {', '.join(nonarr)}")
    if not found_any:
        print("  (ningun script NPCM_ en el plugin)")
    print()

print("=== RESULT:", "PASS" if fails == 0 else f"FAIL ({fails} problema(s))", "===")
sys.exit(1 if fails else 0)
