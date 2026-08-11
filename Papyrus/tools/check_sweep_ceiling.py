"""Gate de los TECHOS DE BARRIDO de overlays: cada numero vive en tres artefactos y los tres tienen que
decir lo mismo.

    POOL NORMAL ([Ovl{n}]) — hasta donde se saca del STORE un overlay cuyo nodo skee no instancia:
      VB    SseCatalogs.OverlaySweepCeiling      -> lo que la app usa para AVISAR
      .psc  int Property OVL_SWEEP_MAX           -> el fuente del script
      .pex  OVL_SWEEP_MAX (AutoReadOnly)         -> ⭐ lo que REALMENTE se instala en el juego

    (El pool MAGIC tenia su propia constante gemela aca y SE FUE: estaba apoyada en que Papyrus no exponia
    el contador del pool magic, y si lo expone — GetNumSpell*Overlays, PapyrusNiOverride.cpp:1844-1853. El
    script ahora las llama, asi que no hay ningun numero que triangular.)

⛔ POR QUE EL .PEX Y NO SOLO EL .PSC. El .pex se compila a mano (ver README) y se embebe en la DLL. Un
.psc editado sin recompilar deja el fuente diciendo una cosa y el juego haciendo otra, y ese desacuerdo
no lo ve nadie: el script no falla, simplemente barre hasta otro indice. El unico artefacto que manda es
el .pex, asi que es el que se lee.

QUE PASA SI DIVERGEN: si el .pex barre MENOS de lo que la app cree, la app le promete al usuario que un
overlay se puede sacar despues y en la partida del jugador queda pegado para siempre. Si barre MAS, se
borran overlays de otro mod en un rango que no hacia falta tocar. Ninguna de las dos avisa sola.


Ademas del .pex de SSE se mira el de FO4, pero con un chequeo mas DEBIL (presencia de los nombres de las
NATIVAS de barrido) porque su formato v3.9 todavia no se puede decodificar. Ver NATIVES_FO4.

Exit 0 = todo coincide, 4 = divergencia (misma convencion que los gates de VB).
"""
import os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.environ.get("FO4_REPO") or os.path.abspath(os.path.join(HERE, "..", "..", ".."))

PEX = os.path.join(REPO, r"FO4_NPC_Manager\Papyrus\pex_sse\NPCM_Manolov_ApplySSE.pex")
PEX_FO4 = os.path.join(REPO, r"FO4_NPC_Manager\Papyrus\pex_fo4\NPCM_Manolov_ApplyFO4.pex")
PSC = os.path.join(REPO, r"FO4_NPC_Manager\Papyrus\src_sse\NPCM_Manolov_ApplySSE.psc")
VB  = os.path.join(REPO, r"FO4_NPC_Manager\FO4_NPC_Manager\NPC\SseCatalogs.vb")
# La key de override no vive con los techos: vive en el modelo del .jslot.
VB_JSLOT = os.path.join(REPO, r"FO4_Base_Library\RaceMenuJslot.vb")

# (etiqueta, nombre en el .psc/.pex, nombre de la constante VB)
CEILINGS = [
    ("pool NORMAL [Ovl]", "OVL_SWEEP_MAX",       "OverlaySweepCeiling"),
]

# ⭐ GEMELOS DE **STRING**. Mismo problema que los techos y con peor final si divergen, pero no se pueden
# comparar con los extractores de arriba (que son regex de \d+). El unico que hay hoy:
#
#   VB    RaceMenuJslot.AppOverrideKey        -> la key con la que el .jslot escribe NUESTRA capa
#   .psc  Function XformKey() return "..."    -> la key con la que el apply-script APLICA y BORRA
#   .pex  el literal en la tabla de strings   -> ⭐ lo que corre en el juego
#
# ⛔ QUE PASA SI DIVERGEN: el script barreria (`RemoveNodeTransform*`) una capa distinta de la que el archivo
# declara, o sea que nuestro override quedaria PEGADO en la partida del jugador mientras la app cree haberlo
# sacado. Y el alcance es mayor que los node transforms: la MISMA key nombra los body morphs
# (`SetBodyMorph(..., XformKey(), ...)`) y el barrido de RemovePrevious.
# El .pex se chequea por PRESENCIA DEL LITERAL en el binario (su tabla de strings): si el literal no esta, el
# script compilado no puede estar usandolo. No hace falta parsear la tabla para eso.
STRINGS = [
    ("key de override (transforms + body morphs)", "XformKey", "AppOverrideKey", VB_JSLOT),
]

# ⭐⭐ GEMELOS DE **COMPORTAMIENTO**, y son los que faltaban. Los dos bloques de arriba comparan VALORES; esto
# compara que el .pex REALMENTE HAGA algo. La asimetria que lo motivo: la mitad de la ley de autoria de node
# transforms que vive en el ARCHIVO esta gateada desde siempre (Tools\JslotTrsProbe: "queda UNA capa y es la
# nuestra"), y la mitad que vive EN EL JUEGO no tenia nada: se podia borrar la llamada central del apply-script
# y todos los gates seguian en verde.
#
# ⛔ POR QUE NO ALCANZA BUSCAR EL NOMBRE EN EL BINARIO: una vez que una funcion se DECLARA, su nombre queda en
# la tabla de strings para siempre, la llame alguien o no. "El literal esta en el .pex" (que es lo que hace
# str_in_pex para la key) no distingue declarada de llamada. Por eso pex_dump ahora decodifica el stream de
# instrucciones y publica los destinos de llamada por funcion.
#
# ⛔ ACA VIVIAN LAS ENTRADAS DE `ClaimNode`, QUE YA NO EXISTE. Esa funcion borraba las capas ajenas de los
# huesos autorados, y se fue: confundia las capas de un PRESET (el desglose por slider de un autor, que vive en
# un archivo) con las capas de un ACTOR en runtime (de mods distintos). Sobre un NPC real las unicas ajenas son
# `internal` del motor —donde componer ES correcto— y los nodos de arma de XPMSE, que vuelven al proximo equip.
#
# (funcion que llama, destino esperado) + por que importa
CALLSITES = [
    # --- el camino de ESCRITURA de los node transforms
    ("ApplyNodeTransforms", "NiOverride.AddNodeTransformScale",
     "es la escritura de la escala. Sin esto el hueso queda en su base y el diseño no llega al NPC, sin error."),
    ("ApplyNodeTransforms", "NiOverride.UpdateNodeTransform",
     "es lo UNICO que recompone el nodo desde su base con las keys que quedan. Sin esta llamada los Add* quedan "
     "en el store y NADA se ve en el 3D — el sintoma seria 'el NPC no cambia', sin ningun error."),
    # --- el camino de DESHACER
    ("RemovePrevious", "NiOverride.GetNodeTransformNames",
     "el barrido de transforms es por ENUMERACION, no por el payload actual: sin esto, un hueso que se saca del "
     "diseño queda PEGADO en la partida del jugador para siempre."),
    ("RemovePrevious", "NiOverride.RemoveNodeTransformScale",
     "saca NUESTRA capa del hueso. Sin esto un re-apply se acumularia sobre lo anterior."),
    ("RemovePrevious", "NiOverride.ClearMorphs",
     "la poda total de body morphs del actor. Sin esto, cambiar a un preset con otros sliders deja los morphs "
     "viejos encima de los nuevos."),
    # --- ⭐ y el getter del pool magic, que es justo lo que se acaba de arreglar
    ("RemovePrevious", "NiOverride.GetNumSpellBodyOverlays",
     "⭐ el contador REAL del pool magic. Hubo una version que usaba una constante inventada (8) porque el .psc "
     "afirmaba —falsamente, en tres lugares— que Papyrus no exponia este getter. Con la constante, el emisor "
     "DESCARTABA en silencio los overlays magic con indice mayor, o sea que la app perdia dato del usuario. Si "
     "esta llamada desaparece, ese camino esta volviendo."),
    ("RemovePrevious", "NiOverride.GetNumSpellFaceOverlays",
     "idem para la cara, que es la zona donde el pool magic es el UNICO dueño (el bake no lo pliega nunca)."),
    # --- ⭐ la neutralizacion de las capas que la app colapso
    ("ApplyNodeTransforms", "NeutralizeCollapsedLayers",
     "⭐ nuestro aporte lleva el valor EFECTIVO del hueso (la app compuso los aportes del preset). Si esos mismos "
     "aportes estan en el co-save del jugador —un mod le aplico ESE preset a ESE NPC con "
     "CharGen.LoadCharacterPresetEx— el motor los compone con nuestro total y el hueso sale AL DOBLE. Sin esta "
     "llamada eso vuelve, y sin fallar."),
    # ⛔ Y LOS TRES COMPONENTES, uno por uno: con solo la fila de arriba, una NeutralizeCollapsedLayers que recorre
    # los pares y NO escribe nada pasaria el gate en verde. Es el mismo verde falso que ya se demostro una vez.
    ("NeutralizeCollapsedLayers", "NiOverride.AddNodeTransformScale",
     "sin esto la ESCALA de la capa colapsada sigue multiplicando nuestro total"),
    ("NeutralizeCollapsedLayers", "NiOverride.AddNodeTransformPosition",
     "sin esto la POSICION de la capa colapsada sigue sumandose a la nuestra"),
    ("NeutralizeCollapsedLayers", "NiOverride.AddNodeTransformRotation",
     "sin esto la ROTACION de la capa colapsada sigue componiendo con la nuestra. ⛔ Y los tres hacen falta juntos: "
     "identidad COMPLETA, porque el preset dice que tenia EL, no que hay bajo ese nombre en el co-save del jugador"),
    ("NeutralizeCollapsedLayers", "NiOverride.UpdateNodeTransform",
     "es lo unico que recompone el nodo. Sin esta llamada las identidades quedan en el store y el 3D sigue "
     "mostrando el doble hasta que algo mas dispare un update"),
]

# ⛔ LO QUE ESTE GATE **NO** PUEDE VER, dicho para que nadie le pida mas de lo que da: mira que las
# instrucciones ESTEN en el cuerpo, no que se ejecuten. Un remove movido a una rama muerta, o el `!=` del
# guard invertido a `==`, siguen pasando — la version invertida borraria justo `internal` y la nuestra. Eso
# necesita evaluar la condicion del branch, y para eso el instrumento correcto es una prueba in-game, no un
# lector de .pex. Lo que este gate SI cierra es el caso barato y probable: el .pex rancio y el borrado de un
# call site.

# (funcion, literales que TIENEN que aparecer en su cuerpo) + por que importa
# ⛔ Esta es una lista de EXCLUSIONES escrita a mano, o sea justo el tipo de cosa que alguien "limpia" sin
# saber lo que sostiene. Se gatea contra el cuerpo compilado, no contra el .psc.
LITERALS = [
    ("RemovePrevious", ["Body [SOvl", "Hands [SOvl", "Feet [SOvl", "Face [SOvl"],
     "el barrido del pool MAGIC en las CUATRO zonas. La primera version barria `[SOvl` con el contador de `[Ovl]` "
     "y lo justificaba con 'nosotros no autoramos magic nunca' — eso caduco: el pool magic es autorable y este "
     "script es su UNICO dueño (el bake no lo pliega). Si uno de estos cuatro prefijos desaparece, los magic de "
     "esa zona quedan pegados al actor en el co-save y nadie los apaga."),
]

# ⭐ NATIVAS QUE EL .PEX DE FO4 TIENE QUE REFERENCIAR. Es un chequeo MAS DEBIL que el de CALLSITES, y hay que
# saber por que igual sirve: para una funcion PROPIA el nombre queda en la tabla de strings por la sola
# DECLARACION, asi que su presencia no prueba nada. Para una NATIVA es al revés — nuestro script no la declara,
# solo la llama — asi que el nombre esta en la tabla SOLO si hay una llamada. No dice CUANTAS ni DONDE, pero si
# dice "este .pex usa esta nativa", que es lo que hace falta para atrapar un .pex rancio.
#
# ⛔ POR QUE NO EL CHEQUEO FUERTE: pex_dump no puede decodificar el stream de FO4 (OPARGS no tiene los opcodes
# 36-46 del formato v3.9; su .pex muere en `struct_create`) — ver su docstring. Y ⛔ EL SUBSISTEMA DE NODE
# TRANSFORMS NO EXISTE EN FO4: el TransformInterface de f4ee esta detras de `#ifdef _TRANSFORMS`, no se registra
# a Papyrus y no se serializa al co-save, asi que las leyes de node transforms NO TIENEN contraparte alla que
# gatear. Lo que si tiene FO4 son SUS leyes de barrido, y son estas dos.
NATIVES_FO4 = [
    ("Overlays.RemoveAll", "RemoveAll",
     "el barrido de overlays de f4ee: sin esto los UID minteados se STACKEAN en cada re-apply (f4ee los "
     "persiste) y el NPC acumula tatuajes duplicados"),
    ("BodyGen.RemoveAllMorphs", "RemoveAllMorphs",
     "la poda total de body morphs del actor. Sin esto, cambiar a un preset con otros sliders deja los "
     "morphs viejos encima de los nuevos"),
]

_pex_dump_cache = {}


def fail(msg):
    print(f"  FAIL  {msg}")
    return None


def from_psc(prop):
    m = re.search(rf"^\s*int\s+Property\s+{prop}\s*=\s*(\d+)\s+AutoReadOnly",
                  open(PSC, encoding="utf-8", errors="replace").read(), re.M | re.I)
    return int(m.group(1)) if m else fail(f"no encontre `int Property {prop} = N AutoReadOnly` en {PSC}")


def from_vb(const):
    m = re.search(rf"{const}\s+As\s+Integer\s*=\s*(\d+)",
                  open(VB, encoding="utf-8", errors="replace").read(), re.I)
    return int(m.group(1)) if m else fail(f"no encontre `{const} As Integer = N` en {VB}")


def pex_dump():
    """Un solo dump del .pex reusado por todas las constantes (era una llamada por constante)."""
    if "out" not in _pex_dump_cache:
        out = subprocess.run([sys.executable, os.path.join(HERE, "pex_dump.py"), PEX],
                             capture_output=True, text=True, encoding="utf-8", errors="replace")
        _pex_dump_cache["out"] = out
    return _pex_dump_cache["out"]


def from_pex(prop):
    """Sale del dump del .pex, que es el unico que sabe leer el binario. Si el formato del dump cambia,
    esto falla RUIDOSO (no encuentra la linea) en vez de dar un numero inventado."""
    out = pex_dump()
    if out.returncode != 0:
        return fail(f"pex_dump.py fallo sobre {PEX}:\n{out.stderr.strip()}")
    # ⛔ La linea se busca por la DECLARACION, no por "la primera que contenga el nombre": el dump puede
    # mencionar la constante en otras lineas (un cuerpo de funcion que la usa, por ejemplo) y entonces el
    # chequeo de `respaldo=` mediria una linea que no es la declaracion.
    decl = re.search(rf"^.*{prop}\b.*?AutoReadOnly\s*=\s*(\d+)\s*\).*$", out.stdout, re.M)
    if not decl:
        return fail(f"el .pex NO declara {prop} como AutoReadOnly — ¿quedo sin recompilar?")
    m, line = decl, decl.group(0)
    if "respaldo=" in line:
        return fail(f"{prop} quedo con variable de respaldo (Auto en vez de AutoReadOnly): "
                    "se serializaria al savegame y quedaria RANCIO para siempre")
    return int(m.group(1))


def str_from_psc(func):
    """El literal que retorna una Function del .psc."""
    m = re.search(rf"Function\s+{func}\s*\(\s*\)[\s\S]*?return\s+\"([^\"]+)\"",
                  open(PSC, encoding="utf-8", errors="replace").read(), re.I)
    return m.group(1) if m else fail(f"no encontre `Function {func}() … return \"…\"` en {PSC}")


def str_from_vb(const, path):
    m = re.search(rf'{const}\s+As\s+String\s*=\s*"([^"]+)"',
                  open(path, encoding="utf-8", errors="replace").read(), re.I)
    return m.group(1) if m else fail(f"no encontre `{const} As String = \"…\"` en {path}")


def str_in_pex(literal):
    """Presencia del literal en el .pex. La tabla de strings guarda UTF-8 sin terminador, asi que una busqueda
    de bytes alcanza para lo que importa: si no esta, el script compilado no lo usa."""
    data = open(PEX, "rb").read()
    return literal.encode("utf-8") in data


def pex_functions():
    """{funcion_minuscula: {"calls": [...], "strings": [...]}} del dump del .pex.

    Los nombres de objeto se internan en MINUSCULA en el .pex (`nioverride.GetNodeTransformKeys`), asi que
    todas las comparaciones de este bloque son case-insensitive."""
    if "funcs" in _pex_dump_cache:
        return _pex_dump_cache["funcs"]
    out = pex_dump()
    funcs, cur = {}, None
    if out.returncode == 0:
        if "FUNCIONES: NO PARSEADAS" in out.stdout:
            funcs = None                       # el dump aviso que se desvio: no se puede afirmar nada
        else:
            for line in out.stdout.splitlines():
                m = re.match(r"^ {6}(?:\[state '[^']*'\] )?(\w+)\s*$", line)
                if m:
                    cur = m.group(1).lower()
                    # ⛔ El dict se indexa por nombre PELADO, asi que dos funciones homonimas en estados
                    # distintos se pisaban y el gate leia el cuerpo que se imprimio ultimo. Hoy no pasa (1
                    # objeto, 1 estado, 12 nombres unicos) pero agregar un estado lo volveria silencioso.
                    # No se mergean los cuerpos: eso podria dar un verde con una llamada que vive en el
                    # estado equivocado. Se marca y el gate falla pidiendo desambiguar.
                    if cur in funcs:
                        funcs[cur]["dup"] = True
                    else:
                        funcs[cur] = {"calls": [], "strings": []}
                    continue
                if cur is None:
                    continue
                m = re.match(r"^ {10}calls: (.+)$", line)
                if m:
                    funcs[cur]["calls"] = [x.strip().lower() for x in m.group(1).split(",")]
                    continue
                m = re.match(r"^ {10}strings: (.+)$", line)
                if m:
                    # Los literales vienen como repr() de python, separados por ", ".
                    try:
                        import ast
                        funcs[cur]["strings"] = list(ast.literal_eval("[" + m.group(1) + "]"))
                    except Exception:
                        funcs[cur]["strings"] = []
    _pex_dump_cache["funcs"] = funcs
    return funcs


def main():
    print("Constantes duplicadas VB / .psc / .pex")
    print("=====================================")
    for p in (PEX, PSC, VB):
        if not os.path.isfile(p):
            print(f"  FAIL  no existe: {p}")
            return 4

    bad = False
    for label, prop, const in CEILINGS:
        print(f"\n{label}")
        vals = {f"VB   ({const})": from_vb(const),
                f".psc ({prop})": from_psc(prop),
                f".pex ({prop}, el que se instala)": from_pex(prop)}
        for k, v in vals.items():
            print(f"  {k:48} = {v}")
        if any(v is None for v in vals.values()):
            bad = True
            continue
        if len(set(vals.values())) != 1:
            print(f"  FAIL  los tres tienen que ser EL MISMO numero ({prop}). Si cambiaste el .psc: "
                  "recompila el .pex (ver Papyrus\\README.md) y rebuildea la app para re-embeberlo.")
            bad = True

    for label, func, const, vbpath in STRINGS:
        print(f"\n{label}")
        v, p = str_from_vb(const, vbpath), str_from_psc(func)
        print(f"  {'VB   (' + const + ')':48} = {v!r}")
        print(f"  {'.psc (' + func + '())':48} = {p!r}")
        if v is None or p is None:
            bad = True
            continue
        if v != p:
            print(f"  FAIL  VB y .psc declaran keys DISTINTAS ({v!r} vs {p!r}): el script barreria una capa que el "
                  "archivo no escribe (y al reves).")
            bad = True
            continue
        inpex = str_in_pex(p)
        print(f"  {'.pex (literal presente, el que se instala)':48} = {inpex}")
        if not inpex:
            print("  FAIL  el literal NO esta en el .pex — ¿quedo sin recompilar?")
            bad = True

    # --- FO4: presencia de las nativas de barrido (chequeo debil; ver la tabla).
    if not os.path.isfile(PEX_FO4):
        print(f"\nFO4: no existe {PEX_FO4} — sin chequear")
    else:
        print("\nlas nativas de barrido que el .pex de FO4 referencia")
        data_fo4 = open(PEX_FO4, "rb").read()
        for label, literal, why in NATIVES_FO4:
            present = literal.encode("utf-8") in data_fo4
            print(f"  {label:34} = {present}")
            if not present:
                print(f"  FAIL  el .pex de FO4 no referencia `{literal}`. {why}")
                bad = True

    # --- comportamiento: lo que el .pex REALMENTE llama y los literales que su cuerpo referencia.
    print("\nlo que el .pex HACE (no lo que declara)")
    funcs = pex_functions()
    if funcs is None:
        print("  FAIL  pex_dump.py no pudo parsear la seccion de funciones del .pex: sin eso no se puede "
              "afirmar que la ley de autoria de node transforms este compilada. NO se deja pasar en verde.")
        bad = True
    else:
        dups = sorted(k for k, v in funcs.items() if v.get("dup"))
        if dups:
            print(f"  FAIL  el .pex declara la misma funcion en mas de un estado ({', '.join(dups)}): este "
                  "gate indexa por nombre pelado y no puede desambiguar cual cuerpo mirar. Hay que extender "
                  "pex_functions() para indexar por (estado, nombre).")
            bad = True
        for caller, target, why in CALLSITES:
            calls = funcs.get(caller.lower(), {}).get("calls", [])
            ok = target.lower() in calls
            print(f"  {caller} -> {target:42} = {ok}")
            if caller.lower() not in funcs:
                print(f"  FAIL  el .pex NO trae la funcion {caller} — ¿quedo sin recompilar?")
                bad = True
            elif not ok:
                print(f"  FAIL  {caller} NO llama a {target}. {why}")
                bad = True

        for func, literals, why in LITERALS:
            # ⚠️ Case-INSENSITIVE, como dice el docstring de pex_functions y como es skee (`_stricmp`,
            # StringTable.h:18-37). Antes solo se bajaban los `calls` y esto comparaba exacto: reescribir el
            # literal como "Internal" es neutro en el juego y ponia el gate en rojo por nada.
            body = [x.lower() for x in funcs.get(func.lower(), {}).get("strings", [])]
            missing = [x for x in literals if x.lower() not in body]
            print(f"  {func} referencia {literals} = {not missing}")
            if func.lower() not in funcs:
                print(f"  FAIL  el .pex NO trae la funcion {func} — ¿quedo sin recompilar?")
                bad = True
            elif missing:
                print(f"  FAIL  al cuerpo de {func} le faltan los literales {missing}. {why}")
                bad = True

    if bad:
        return 4
    print("\n  OK — techos, keys y comportamiento coinciden en los tres artefactos")
    return 0


if __name__ == "__main__":
    sys.exit(main())
