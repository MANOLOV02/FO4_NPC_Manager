"""Gate del TECHO DE BARRIDO de overlays: los tres lugares donde vive el mismo número tienen que decir
lo mismo.

    VB    SseCatalogs.OverlaySweepCeiling   -> lo que la app usa para AVISAR
    .psc  int Property OVL_SWEEP_MAX        -> el fuente del script
    .pex  OVL_SWEEP_MAX (AutoReadOnly)      -> ⭐ lo que REALMENTE se instala en el juego

⛔ POR QUE EL .PEX Y NO SOLO EL .PSC. El .pex se compila a mano (ver README) y se embebe en la DLL. Un
.psc editado sin recompilar deja el fuente diciendo una cosa y el juego haciendo otra, y ese desacuerdo
no lo ve nadie: el script no falla, simplemente barre hasta otro indice. El unico artefacto que manda es
el .pex, asi que es el que se lee.

QUE PASA SI DIVERGEN: si el .pex barre MENOS de lo que la app cree, la app le promete al usuario que un
overlay se puede sacar despues y en la partida del jugador queda pegado para siempre. Si barre MAS, se
borran overlays de otro mod en un rango que no hacia falta tocar. Ninguna de las dos avisa sola.

Exit 0 = los tres coinciden, 4 = divergencia (misma convencion que los gates de VB).
"""
import os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.environ.get("FO4_REPO") or os.path.abspath(os.path.join(HERE, "..", "..", ".."))

PEX = os.path.join(REPO, r"FO4_NPC_Manager\Papyrus\pex_sse\NPCM_Manolov_ApplySSE.pex")
PSC = os.path.join(REPO, r"FO4_NPC_Manager\Papyrus\src_sse\NPCM_Manolov_ApplySSE.psc")
VB  = os.path.join(REPO, r"FO4_NPC_Manager\FO4_NPC_Manager\NPC\SseCatalogs.vb")

def fail(msg):
    print(f"  FAIL  {msg}")
    return None

def from_psc():
    m = re.search(r"^\s*int\s+Property\s+OVL_SWEEP_MAX\s*=\s*(\d+)\s+AutoReadOnly",
                  open(PSC, encoding="utf-8", errors="replace").read(), re.M | re.I)
    return int(m.group(1)) if m else fail(f"no encontre `int Property OVL_SWEEP_MAX = N AutoReadOnly` en {PSC}")

def from_vb():
    m = re.search(r"OverlaySweepCeiling\s+As\s+Integer\s*=\s*(\d+)",
                  open(VB, encoding="utf-8", errors="replace").read(), re.I)
    return int(m.group(1)) if m else fail(f"no encontre `OverlaySweepCeiling As Integer = N` en {VB}")

def from_pex():
    """Sale del dump del .pex, que es el unico que sabe leer el binario. Si el formato del dump cambia,
    esto falla RUIDOSO (no encuentra la linea) en vez de dar un numero inventado."""
    out = subprocess.run([sys.executable, os.path.join(HERE, "pex_dump.py"), PEX],
                         capture_output=True, text=True, encoding="utf-8", errors="replace")
    if out.returncode != 0:
        return fail(f"pex_dump.py fallo sobre {PEX}:\n{out.stderr.strip()}")
    m = re.search(r"OVL_SWEEP_MAX\b.*?AutoReadOnly\s*=\s*(\d+)\s*\)", out.stdout)
    if not m:
        return fail("el .pex NO declara OVL_SWEEP_MAX como AutoReadOnly — ¿quedo sin recompilar?")
    if "respaldo=" in re.search(r"^.*OVL_SWEEP_MAX.*$", out.stdout, re.M).group(0):
        return fail("OVL_SWEEP_MAX quedo con variable de respaldo (Auto en vez de AutoReadOnly): "
                    "se serializaria al savegame y quedaria RANCIO para siempre")
    return int(m.group(1))

def main():
    print("Techo de barrido de overlays — VB / .psc / .pex")
    print("==============================================")
    for p in (PEX, PSC, VB):
        if not os.path.isfile(p):
            print(f"  FAIL  no existe: {p}")
            return 4

    vals = {"VB   (SseCatalogs.OverlaySweepCeiling)": from_vb(),
            ".psc (OVL_SWEEP_MAX)": from_psc(),
            ".pex (OVL_SWEEP_MAX, el que se instala)": from_pex()}
    for k, v in vals.items():
        print(f"  {k:44} = {v}")

    if any(v is None for v in vals.values()):
        return 4
    if len(set(vals.values())) != 1:
        print("\n  FAIL  los tres tienen que ser EL MISMO numero. Si cambiaste el .psc: recompila el .pex "
              "(ver Papyrus\\README.md) y rebuildea la app para re-embeberlo.")
        return 4
    print("\n  OK — los tres coinciden")
    return 0

if __name__ == "__main__":
    sys.exit(main())
