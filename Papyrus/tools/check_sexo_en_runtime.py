# -*- coding: utf-8 -*-
"""Gate del SEXO EN RUNTIME: que el `.pex` que se instala saque el sexo del ACTOR y que el barrido y el
apply usen EL MISMO valor.

QUE AFIRMA, sobre el STREAM DE INSTRUCCIONES de cada `.pex` compilado (no sobre el `.psc`, y no sobre la
tabla de strings: que un nombre este en la tabla no prueba que el cuerpo lo use):

  1. `SexoDelActor` existe y LLAMA a `GetActorBase` y a `GetSex`.
     ⭐ `GetActorBase()` es `return GetBaseObject() as ActorBase` (Actor.psc:138-139 en SSE, :159-160 en
     FO4), o sea el MISMO `refr->baseForm` que leen skee y f4ee en TODOS sus caminos. Si alguien lo
     cambiara por `GetLeveledActorBase()` (otra nativa, la "fake base" del leveled) el valor podria dejar
     de ser el que lee el consumidor, y el sintoma seria mudo: nada se aplica y nada se loguea.
  2. `OnLoad` LLAMA a `SexoDelActor` y toca `femaleActivo`. Sin eso la variable se queda con lo que haya
     quedado en el SAVEGAME (es una variable de script: se serializa) y el apply correria con un sexo
     rancio.
  3. NINGUNA funcion de apply/barrido lee `::IsFemale_G0000010000_var`. La property sigue existiendo —es
     el FALLBACK y lo que viaja en el VMAD— pero solo la pueden nombrar `SexoDelActor` (que la devuelve
     cuando el actor no tiene base o su `GetSex()` da -1) y `OnLoad` (que la compara y la usa para el
     barrido de migracion).
     ⛔ ESTE ES EL CHEQUEO QUE IMPORTA: si UNA sola funcion se quedara con la property mientras el resto
     usa `femaleActivo`, el actor podria BARRER con un sexo y ESCRIBIR con el otro — y lo escrito con el
     que no es queda invisible y sin barrer en el co-save, para siempre.

⛔ POR QUE EL .pex Y NO EL .psc: mismo motivo que `check_sweep_ceiling.py`. El `.pex` se compila a mano y
se embebe en la DLL; un `.psc` editado sin recompilar deja el fuente diciendo una cosa y el juego
haciendo otra, sin que nada falle.

CONTROL NEGATIVO (corrido 2026-09-18, y por eso esto no pasa en vacio): contra los `.pex` ANTERIORES al
cambio —los que pasaban la constante del VMAD— el gate da ROJO en los 6 chequeos (3 por juego), con las
7 funciones de SSE y las 2 de FO4 listadas como "todavia leen la property".

Exit 0 = OK, 4 = ROJO (misma convencion que los demas gates).
"""
import os, sys, struct

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pex_dump as PD

# ⛔ `pex_dump.OPARGS` solo cubre el set de opcodes de Skyrim (0..35), y por eso su propio dump dice
# "FUNCIONES: NO PARSEADAS" sobre el .pex de Fallout 4: el formato v3.9 agrega los opcodes de STRUCT (que
# nuestro script usa para `Overlays:Entry`) y los de array nuevos, y el primero que aparece revienta con
# `KeyError: 37`. Aca se completan SOLO para este gate, sin tocar la herramienta compartida: cambiar
# `pex_dump` haria que `check_sweep_ceiling` pasara de su camino de fallback al de instrucciones, que es
# otro chequeo, y eso no se decide de costado.
#
# ⭐ NO ES ADIVINANZA: si una aridad estuviera mal, el parseo se desincroniza y la seccion no cierra en el
# offset que el objeto declara — el `assert` de abajo lo caza. Con estas cerro EXACTO en los dos juegos.
PD.OPARGS.update({36: 3, 37: 1, 38: 3, 39: 3, 40: 5, 41: 5, 42: 3, 43: 3, 44: 1, 45: 3, 46: 1})

PAP = os.path.dirname(HERE)
CASOS = [
    ("SSE", os.path.join(PAP, "pex_sse", "NPCM_Manolov_ApplySSE.pex")),
    ("FO4", os.path.join(PAP, "pex_fo4", "NPCM_Manolov_ApplyFO4.pex")),
]

PERMITIDAS = {"SexoDelActor", "OnLoad", "IsFemale_G0000010000"}
PROP_VAR = "::IsFemale_G0000010000_var"


def funciones(path):
    """{nombre de funcion: [instrucciones]} del unico objeto del .pex."""
    b = open(path, "rb").read()
    endian = ">" if struct.unpack_from(">I", b, 0)[0] == 0xFA57C0DE else "<"
    fo4 = endian == "<"
    r = PD.R(b, endian)
    r.u32(); r.u8(); r.u8(); r.u16(); r.i64()
    r.st(); r.st(); r.st()                                  # source, user, machine
    S = [r.st() for _ in range(r.u16())]
    if r.u8():                                              # debug info
        r.i64()
        for _ in range(r.u16()):
            r.u16(); r.u16(); r.u16(); r.u8()
            for _ in range(r.u16()): r.u16()
        if fo4:
            for _ in range(r.u16()):                        # property groups
                r.u16(); r.u16(); r.u16(); r.u32()
                for _ in range(r.u16()): r.u16()
            for _ in range(r.u16()):                        # struct order
                r.u16(); r.u16()
                for _ in range(r.u16()): r.u16()
    for _ in range(r.u16()): r.u16(); r.u8()                # user flags

    out = {}
    for _ in range(r.u16()):                                # objetos
        S[r.u16()]
        size = r.u32(); end = r.o + size - 4
        r.u16(); r.u16()                                    # parentClass, docString
        if fo4: r.u8()                                      # const flag del objeto
        r.u32(); r.u16()                                    # userFlags, autoState
        if fo4:
            for _ in range(r.u16()):                        # structs definidos aca (los nuestros: 0)
                r.u16()
                for _ in range(r.u16()):
                    r.u16(); r.u16(); r.u32(); PD.variant(r, S); r.u8(); r.u16()
        for _ in range(r.u16()):                            # variables
            r.u16(); r.u16(); r.u32(); PD.variant(r, S)
            if fo4: r.u8()
        for _ in range(r.u16()):                            # propiedades
            r.u16(); r.u16(); r.u16(); r.u32()
            pf = r.u8()
            if pf & 4:
                r.u16()                                     # nombre de la variable de respaldo
            else:
                if pf & 1: PD.read_function(r, S)
                if pf & 2: PD.read_function(r, S)
        for _, funcs in PD.read_states(r, S):
            for fname, ops in funcs:
                out[fname] = ops
        assert r.o == end, "la seccion de funciones cerro en 0x%X y el objeto declara 0x%X" % (r.o, end)
    return out


def idents(ops):
    return {val for _, args in ops for kind, val in args if kind == "ident"}


def main():
    fallos = []
    for juego, path in CASOS:
        print("=====", juego, os.path.basename(path))
        if not os.path.exists(path):
            print("  FALTA el .pex compilado — ver Papyrus/README.md")
            fallos.append(juego + " sin .pex")
            continue
        fs = funciones(path)

        llamadas = set(PD.calls_of(fs["SexoDelActor"])) if "SexoDelActor" in fs else set()
        ok1 = {"GetActorBase", "GetSex"} <= llamadas
        print("  1. SexoDelActor -> GetActorBase + GetSex        :", "OK" if ok1 else "FALLO", sorted(llamadas))
        if not ok1: fallos.append(juego + " 1")

        onload = fs.get("OnLoad", [])
        ok2 = "SexoDelActor" in set(PD.calls_of(onload)) and "femaleActivo" in idents(onload)
        print("  2. OnLoad resuelve el sexo y escribe femaleActivo:", "OK" if ok2 else "FALLO")
        if not ok2: fallos.append(juego + " 2")

        sucias = sorted(f for f, ops in fs.items() if PROP_VAR in idents(ops) and f not in PERMITIDAS)
        usan = sorted(f for f, ops in fs.items() if "femaleActivo" in idents(ops))
        print("  3. funciones que todavia leen la property       :", sucias if sucias else "ninguna (OK)")
        print("     funciones que usan femaleActivo              :", ", ".join(usan))
        if sucias: fallos.append(juego + " 3")

    print()
    if fallos:
        print("=== ROJO:", ", ".join(fallos))
        return 4
    print("=== OK: en los dos .pex el sexo sale del ACTOR y el barrido y el apply usan la misma variable")
    return 0


if __name__ == "__main__":
    sys.exit(main())
