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

# Raiz del repo: por defecto se deriva de la ubicacion de este script
# (<repo>\FO4_NPC_Manager\Papyrus\tools\), y se puede pisar con la variable de entorno FO4_REPO.
REPO = os.environ.get("FO4_REPO") or os.path.abspath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
# Los targets se DESCUBREN: se barre el Data de cada juego buscando plugins que nombren un script
# NPCM_ (con descompresion de records, porque casi todos los NPC_ vienen comprimidos).
#
# Antes eran DOS RUTAS FIJAS a "NPC_Manager.esp". Hoy la de FO4 no existe y la de SSE existe pero no
# tiene ni un script nuestro, asi que el gate miraba CERO records y salia PASS. Su propio encabezado
# documenta que esto ya habia pasado una vez. Ver 81-gates-que-pasaban-en-vacio.
DATA_DIRS = [
    ("SSE", r"F:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data",
             os.path.join(REPO, r"FO4_NPC_Manager\Papyrus\src_sse\NPCM_Manolov_ApplySSE.psc")),
    ("FO4", r"F:\SteamLibrary\steamapps\common\Fallout 4\Data",
             os.path.join(REPO, r"FO4_NPC_Manager\Papyrus\src_fo4\NPCM_Manolov_ApplyFO4.psc")),
]

_PREFIJO = re.compile(rb"NPCM_Manolov_[A-Za-z0-9_]{0,64}")

def _hay_en_comprimidos(b):
    """Los NPC_ suelen venir COMPRIMIDOS (flag 0x00040000): un grep crudo no los ve."""
    def walk(off, end):
        while off + 24 <= end:
            sig = b[off:off+4]
            if sig == b"GRUP":
                size = struct.unpack_from("<I", b, off+4)[0]
                if size < 24:
                    return False
                if walk(off+24, min(off+size, end)):
                    return True
                off += size
                continue
            size, flags = struct.unpack_from("<II", b, off+4)
            data = b[off+24:off+24+size]
            if flags & 0x00040000:
                try:
                    data = zlib.decompress(data[4:])
                except Exception:
                    pass
                if _PREFIJO.search(data):
                    return True
            off += 24 + size
        return False
    try:
        return walk(0, len(b))
    except Exception:
        return False

def descubrir_targets():
    """(label, esp, psc) por cada plugin del Data que NOMBRE un script NPCM_."""
    out = []
    for label, data, psc in DATA_DIRS:
        if not os.path.isdir(data):
            continue
        for fn in sorted(os.listdir(data)):
            if not fn.lower().endswith((".esp", ".esm", ".esl")):
                continue
            ruta = os.path.join(data, fn)
            try:
                b = open(ruta, "rb").read()
            except Exception:
                continue
            if _PREFIJO.search(b) or _hay_en_comprimidos(b):
                out.append((label, ruta, psc))
    return out

TARGETS = descubrir_targets()

# tipo VMAD -> tipo Papyrus
VMAD_T = {1:"object",2:"string",3:"int",4:"float",5:"bool",
          11:"object[]",12:"string[]",13:"int[]",14:"float[]",15:"bool[]"}

# Los nombres del payload llevan sufijo de generacion (_G1, _G2, ...) porque una property que el
# savegame YA tiene se restaura RANCIA y solo una con nombre nuevo se inicializa del VMAD. Los grupos
# de abajo se escriben SIN sufijo y se comparan con base_name(), asi el validador no hay que tocarlo
# en cada release. Ver la cabecera de NPCM_Manolov_ApplySSE.psc.
def base_name(n):
    # El sufijo es _G<6 digitos><4 hex de sal>, p.ej. _G000016A3F2. La sal existe para que el nombre sea
    # nuevo aunque el contador se repita (ver PexPatcher.NewSalt). Se acepta tambien el formato viejo sin
    # sal, para poder validar un ESP emitido por una version anterior de la app.
    return re.sub(r"_G\d{6}[0-9A-Fa-f]{4}$|_G\d+$", "", n)

# grupos de arrays paralelos que DEBEN tener la misma longitud
GROUPS = {
    "SSE": [("overlays", ["OvlNode","OvlDiffuse","OvlNormal","OvlHasTint","OvlTint","OvlHasAlpha","OvlAlpha"]),
            ("skin",     ["SkinSlot","SkinDiffuse","SkinNormal","SkinHasTint","SkinTint"]),
            # ⛔ FALTABAN LOS NUEVE NodeRotM: el grupo declaraba NodeHasRot pero no las arrays que ese flag
            # gatea, asi que un NodeRotM3 corto NO se reportaba como desparejo y el script leeria basura (o
            # nada) para la rotacion de los ultimos nodos. Son el unico grupo del payload donde el indice i
            # significa "nodo i" en 17 arrays a la vez.
            ("nodes",    ["NodeName","NodeHasScale","NodeScale","NodeHasPos","NodePosX","NodePosY","NodePosZ",
                          "NodeHasRot","NodeScaleMode"] + [f"NodeRotM{k}" for k in range(9)]),
            # ⛔ GRUPO PROPIO, **NO** parte de "nodes": estas dos son pares (nodo, nombre-de-capa) y su indice
            # significa "el par i", no "el nodo i". Un hueso puede traer varios nombres, asi que su largo NO tiene
            # por que coincidir con NodeName — meterlas en el grupo de arriba haria fallar el gate por diseño.
            # Lo que si tiene que valer es que sean paralelas ENTRE SI: si una llega mas corta, el script leeria
            # un nombre para el nodo equivocado y neutralizaria una capa que nadie le pidio.
            ("neutralise", ["NodeNeutralNode","NodeNeutralName"]),
            ("morphs",   ["MorphName","MorphValue"])],
    "FO4": [("overlays", ["OvlTemplate","OvlPriority","OvlRed","OvlGreen","OvlBlue","OvlAlpha",
                          "OvlOffsetU","OvlOffsetV","OvlScaleU","OvlScaleV"]),
            ("morphs",   ["MorphName","MorphValue"])],
}

def psc_properties(path):
    """name -> declared papyrus type (lowercase)"""
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r'\s*([A-Za-z_][\w\[\]:]*)\s+Property\s+(\w+)', line)
        if m:
            out[m.group(2)] = m.group(1).lower()
    return out


def psc_autoreadonly(path):
    """Los nombres declarados `AutoReadOnly`, o sea SIN variable de respaldo: su valor sale del .pex y el
    VMAD no los trae ni tiene por que. Se DERIVA del .psc en vez de mantener una lista de nombres a mano:
    la lista hardcodeada que habia aca (KEY_*/IDX_*) ya se habia quedado corta -- OVL_SWEEP_MAX no estaba --
    y el sintoma era ruido informativo sobre una constante que nunca va a estar en el VMAD."""
    out = set()
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r'\s*[A-Za-z_][\w\[\]:]*\s+Property\s+(\w+)\s*=.*\bAutoReadOnly\b', line, re.I)
        if m:
            out.add(m.group(1))
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
vistos = []
for label, esp, psc in TARGETS:
    print(f"########## {label}")
    if not os.path.exists(esp):
        print(f"  [SKIP] no existe {esp}\n"); continue
    if not os.path.exists(psc):
        print(f"  [SKIP] no existe {psc}\n"); continue
    decl = psc_properties(psc)
    consts = psc_autoreadonly(psc)
    print(f"  .psc declara {len(decl)} propiedades ({len(consts)} AutoReadOnly = constantes, no payload)")

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
                vistos.append((label, esp))
                print(f"\n  NPC_ {fid:08X}  script '{name}'  ({pc} props, VMAD ver={ver})")
                errs = []
                # ⛔⛔ ESTO COMPARABA CON EL SUFIJO DE GENERACION PUESTO, y el comentario de abajo lo defendia
                # como "asi una desincronizacion sale como error duro". Era imposible que coincidiera: el .psc es
                # una PLANTILLA con `_G0000010000` y el emisor acuña una generacion+sal NUEVA en cada Save ESP,
                # asi que todo plugin real trae otro sufijo. Consecuencia: este gate venia pasando EN VACIO
                # ("ningun script NPCM_ en el plugin") y la primera vez que vio datos de verdad tiro 72 errores.
                # La invariante que SI hay que verificar es otra: que todas las properties de UNA instancia
                # compartan la MISMA generacion. Eso es lo que se rompe si el emisor se desincroniza.
                decl_base = {base_name(k): v for k, v in decl.items()}
                gens = set()
                for pn,(t,n) in props.items():
                    vt = VMAD_T.get(t, f"?{t}")
                    b = base_name(pn)
                    if pn != b:
                        gens.add(pn[len(b):])
                    if b not in decl_base:
                        errs.append(f"PROPIEDAD INEXISTENTE en el .psc: {pn} (base '{b}')")
                    elif decl_base[b] != vt:
                        errs.append(f"TIPO NO COINCIDE {pn}: VMAD={vt} vs .psc={decl_base[b]}")
                    if n == 0:
                        errs.append(f"ARRAY VACIO (ilegal en Skyrim): {pn}")
                if len(gens) > 1:
                    errs.append(f"GENERACIONES MEZCLADAS en una misma instancia: {sorted(gens)}")
                # Los grupos se declaran sin sufijo de generacion; el VMAD lo trae. Indexar por base.
                props_by_base = {base_name(k): v for k, v in props.items()}
                for gname, members in GROUPS[label]:
                    lens = {m: props_by_base[m][1] for m in members if m in props_by_base}
                    if lens and len(set(lens.values())) > 1:
                        errs.append(f"ARRAYS PARALELOS DESPAREJOS en '{gname}': {lens}")
                # ⛔ Una ARRAY-property declarada y AUSENTE del VMAD queda en None, y en Skyrim
                # `if X == None` sobre eso TIRA. Es un ERROR, no un aviso.
                # Las constantes salen del propio .psc (AutoReadOnly = sin variable de respaldo), no de una
                # lista de nombres a mano. Ver psc_autoreadonly.
                # Idem por BASE: `d not in props` con sufijo daba TODAS por ausentes.
                props_bases = set(props_by_base.keys())
                consts_bases = {base_name(c) for c in consts}
                missing = [d for d in decl_base if d not in props_bases and d not in consts_bases]
                for d in missing:
                    if decl_base[d].endswith("[]"):
                        errs.append(f"ARRAY-PROPERTY AUSENTE del VMAD (quedaria en None): {d} ({decl_base[d]})")
                if errs:
                    fails += len(errs)
                    for e in errs: print(f"      [X] {e}")
                else:
                    print("      [OK] tipos OK - sin arrays vacíos - arrays paralelos parejos")
                nonarr = [d for d in missing if not decl_base[d].endswith("[]")]
                if nonarr:
                    print(f"      - escalares ausentes (OK): {', '.join(nonarr)}")
    if not found_any:
        print("  (ningun script NPCM_ en el plugin)")
    print()

# NO CONCLUYENTE != PASS. Un gate que no miro ningun record no puede afirmar nada, y salir 0 ahi es
# exactamente como este archivo se quedo verde mientras el defecto vivia. Codigo 5, la misma ley que
# ya sigue SamAditivoGate.
if not TARGETS:
    print("=== RESULT: NO CONCLUYENTE (ningun plugin del Data nombra un script NPCM_) ===")
    sys.exit(5)
if not vistos:
    print(f"=== RESULT: NO CONCLUYENTE ({len(TARGETS)} plugin(s) mirados, 0 records con script) ===")
    sys.exit(5)
print(f"records con script NPCM_ mirados: {len(vistos)}  en {len(TARGETS)} plugin(s)")
print("=== RESULT:", "PASS" if fails == 0 else f"FAIL ({fails} problema(s))", "===")
sys.exit(1 if fails else 0)
