"""Dump de un .pex: tabla de strings, variables del objeto y PROPIEDADES (nombre + tipo + flags +
variable de respaldo). Ground truth de lo que el script realmente declara.

Sirve para los DOS juegos: Skyrim SE escribe el .pex en BIG-endian (magic FA57C0DE) y Fallout 4 en
LITTLE-endian (los mismos 4 bytes al revés). El endianness se detecta del magic.

⚠️ CON UNA EXCEPCIÓN MEDIDA: la sección FUNCIONES (destinos de llamada y literales) hoy sólo parsea
Skyrim. `OPARGS` llega hasta el opcode 35 y Fallout 4 (formato v3.9) agrega los de struct — el `.pex` de
FO4 de este repo muere en `KeyError: 37` (`struct_create`, que sale del `new Overlays:Entry` de su
`.psc`). El LAYOUT de la sección sí es el mismo en los dos (para llegar al opcode ya leyó bien estados,
nombres y la cabecera de la función); lo único que falta son las cantidades de argumentos de los opcodes
36-46. ⛔ NO se completan a ojo: un OPARGS inventado no falla, DESALINEA el stream y devuelve llamadas
que no existen — un verde falso, que es peor que el fallback ruidoso que hay ahora. Hasta tener esa tabla
de una fuente, un gate de comportamiento equivalente para FO4 no se puede construir.

⭐ POR QUÉ IMPORTA LA COLUMNA "respaldo": una property `Auto` se compila a una VARIABLE de script
(`::X_var`), y las variables SE SERIALIZAN AL SAVEGAME — por eso una referencia que ya existe en la
partida conserva para siempre el valor que tenía al crearse, y re-guardar el ESP no la alcanza. Una
property `AutoReadOnly` NO tiene variable de respaldo: su valor es un literal dentro del `.pex` (el
getter es un único `return <literal>`), así que se relee del disco en cada arranque. Este dump es la
forma de comprobar en cuál de las dos categorías cae cada property.
"""
import os, struct, sys

class R:
    """Lector con endianness fijado en el ctor ('>' Skyrim, '<' Fallout 4)."""
    def __init__(s, b, e): s.b = b; s.o = 0; s.e = e
    def _u(s, fmt, n):
        v = struct.unpack_from(s.e + fmt, s.b, s.o)[0]; s.o += n; return v
    def u8(s):  v = s.b[s.o]; s.o += 1; return v
    def u16(s): return s._u("H", 2)
    def u32(s): return s._u("I", 4)
    def i32(s): return s._u("i", 4)
    def f32(s): return s._u("f", 4)
    def i64(s): return s._u("q", 8)
    def st(s):
        n = s.u16(); v = s.b[s.o:s.o+n].decode("utf-8", "replace"); s.o += n; return v

def variant(r, S):
    """Un VarData del .pex: (tipo, valor)."""
    t = r.u8()
    if t == 0: return ("null", None)
    if t == 1: return ("ident", S[r.u16()])
    if t == 2: return ("string", S[r.u16()])
    if t == 3: return ("int", r.i32())
    if t == 4: return ("float", r.f32())
    if t == 5: return ("bool", bool(r.u8()))
    raise ValueError(f"variant type {t} @ 0x{r.o:X}")

# Cantidad de argumentos por opcode. Los tres de llamada (callmethod/callparent/callstatic) llevan
# ADEMÁS un contador de varargs seguido de esa cantidad de VarData.
OPARGS = {0:0, 1:3, 2:3, 3:3, 4:3, 5:3, 6:3, 7:3, 8:3, 9:3, 10:2, 11:2, 12:2, 13:2, 14:2,
          15:3, 16:3, 17:3, 18:3, 19:3, 20:1, 21:2, 22:2, 23:3, 24:2, 25:3, 26:1, 27:3,
          28:3, 29:3, 30:2, 31:2, 32:3, 33:3, 34:4, 35:4}
VARARG_OPS = {23, 24, 25}
OPNAME = {20:"jmp", 21:"jmpt", 22:"jmpf", 23:"callmethod", 24:"callparent", 25:"callstatic",
          26:"return", 13:"assign", 14:"cast"}

def read_function(r, S):
    """Estructura Function (sin nombre: el nombre lo lee quien la contiene, si aplica).
    Devuelve (returnType, [instrucciones]) con las instrucciones como (opcode, [VarData])."""
    rt = S[r.u16()]; r.u16(); r.u32(); r.u8()      # returnType, docString, userFlags, flags
    for _ in range(r.u16()): r.u16(); r.u16()      # params  (name, type)
    for _ in range(r.u16()): r.u16(); r.u16()      # locals  (name, type)
    ninstr = r.u16()                               # ⛔ u16, NO u32
    ops = []
    for _ in range(ninstr):
        op = r.u8()
        args = [variant(r, S) for _ in range(OPARGS[op])]
        if op in VARARG_OPS:
            for _ in range(variant(r, S)[1]): args.append(variant(r, S))
        ops.append((op, args))
    return rt, ops

def calls_of(ops):
    """Los DESTINOS DE LLAMADA de un cuerpo, en orden y sin repetir.

    ⭐ POR QUE HACE FALTA: la presencia de un literal o de un nombre en la tabla de strings NO prueba que el
    script lo USE — una vez que una funcion se declara, su nombre queda interno para siempre aunque nadie la
    llame. El unico artefacto que distingue "declarada" de "llamada" es el stream de instrucciones.
      * callmethod (23): args fijos (nombre, objetivo, destino)  -> el nombre es args[0]
      * callstatic (25): args fijos (objeto, nombre, destino)    -> se emite "Objeto.nombre"
      * callparent (24): args fijos (nombre, destino)
    """
    out = []
    for op, args in ops:
        t = None
        if op == 23 and len(args) >= 1:
            t = args[0][1]
        elif op == 25 and len(args) >= 2:
            t = f"{args[0][1]}.{args[1][1]}"
        elif op == 24 and len(args) >= 1:
            t = f"parent.{args[0][1]}"
        if t and t not in out:
            out.append(t)
    return out


def strings_of(ops):
    """Los literales de STRING que aparecen en un cuerpo, sin repetir. Sirve para gatear una lista de
    literales escrita a mano (p.ej. los cuatro prefijos `<zona> [SOvl` del barrido del pool magic): si alguien
    borra uno, el literal desaparece del cuerpo aunque siga en la tabla de strings por otro uso."""
    out = []
    for _, args in ops:
        for kind, val in args:
            if kind == "string" and val not in out:
                out.append(val)
    return out


def read_states(r, S):
    """La seccion de estados de un objeto: [(nombreEstado, [(nombreFuncion, ops)])].

    Layout: u16 nStates, y por estado u16 nombre + u16 nFunciones + (u16 nombre + Function) por funcion.
    El estado vacio ('') es el default."""
    states = []
    for _ in range(r.u16()):
        sname = S[r.u16()]
        funcs = []
        for _ in range(r.u16()):
            fname = S[r.u16()]
            funcs.append((fname, read_function(r, S)[1]))
        states.append((sname, funcs))
    return states


def const_of(ops):
    """Si el cuerpo es un único `return <literal>`, devuelve ese literal (es el caso de una
    property AutoReadOnly). Si no, None."""
    if len(ops) == 1 and ops[0][0] == 26 and len(ops[0][1]) == 1:
        kind, val = ops[0][1][0]
        if kind in ("int", "float", "bool", "string"):
            return val
    return None

def main(path):
    b = open(path, "rb").read()
    be, le = struct.unpack_from(">I", b, 0)[0], struct.unpack_from("<I", b, 0)[0]
    if be == 0xFA57C0DE:   endian, flavour = ">", "big-endian (Skyrim SE)"
    elif le == 0xFA57C0DE: endian, flavour = "<", "little-endian (Fallout 4)"
    else: raise SystemExit(f"magic {be:08X} desconocido: no parece un .pex")

    fo4 = endian == "<"          # Fallout 4 = LE + formato v3.9; Skyrim SE = BE + v3.2

    r = R(b, endian); r.u32()
    major, minor = r.u8(), r.u8()
    game, ts = r.u16(), r.i64()
    src = r.st(); user = r.st(); machine = r.st()

    ns = r.u16()
    S = [r.st() for _ in range(ns)]

    # --- debug info. Fallout 4 agrega dos tablas más al final (grupos de property y orden de structs).
    if r.u8():
        r.i64()                                        # modification time
        for _ in range(r.u16()):                       # functions
            r.u16(); r.u16(); r.u16(); r.u8()          # object, state, function, functionType
            for _ in range(r.u16()): r.u16()           # line numbers
        if fo4:
            for _ in range(r.u16()):                   # property groups
                r.u16(); r.u16(); r.u16(); r.u32()
                for _ in range(r.u16()): r.u16()
            for _ in range(r.u16()):                   # struct order
                r.u16(); r.u16()
                for _ in range(r.u16()): r.u16()
    for _ in range(r.u16()): r.u16(); r.u8()           # user flags

    nobj = r.u16()
    print(f"=== {os.path.basename(path)}   v{major}.{minor}  {flavour}  strings={ns}  objects={nobj}")
    print(f"    source: {src}\n")

    for _ in range(nobj):
        name = S[r.u16()]
        size = r.u32(); end = r.o + size - 4
        parent = S[r.u16()]; r.u16()                   # parentClass, docString
        if fo4: r.u8()                                 # const flag del objeto (sólo FO4)
        r.u32(); autostate = S[r.u16()]                # userFlags, autoState

        if fo4:
            # Structs DEFINIDOS en este script (usar uno ajeno, como Overlays:Entry, no cuenta).
            # Nuestros .pex traen 0, así que este layout va sin verificar contra un caso real.
            for _ in range(r.u16()):
                r.u16()
                for _ in range(r.u16()):
                    r.u16(); r.u16(); r.u32(); variant(r, S); r.u8(); r.u16()

        nvar = r.u16()
        print(f"OBJETO '{name}' extends '{parent}'")
        print(f"  --- VARIABLES ({nvar}) ---")
        for _ in range(nvar):
            vn = S[r.u16()]; vt = S[r.u16()]; r.u32(); variant(r, S)
            if fo4: r.u8()                             # const flag de la variable (sólo FO4)
            print(f"      {vt:12} {vn}")

        nprop = r.u16()
        print(f"  --- PROPIEDADES ({nprop}) ---")
        arrays, nauto, nconst = [], 0, 0
        for _ in range(nprop):
            pn = S[r.u16()]; pt = S[r.u16()]; r.u16()
            r.u32()                                    # ⛔ userFlags (u32) — el campo que faltaba
            pf = r.u8()
            kind = [k for bit, k in ((1, "read"), (2, "write"), (4, "AUTO")) if pf & bit]
            extra = ""
            if pf & 4:
                nauto += 1
                extra = f"respaldo={S[r.u16()]}  => SE SERIALIZA AL SAVEGAME"
            else:
                # Handlers explícitos: hay que parsear el cuerpo para poder seguir leyendo.
                lit = None
                if pf & 1: lit = const_of(read_function(r, S)[1])
                # ⚠ El getter está ejercitado por 22 .pex reales (todas las AutoReadOnly del corpus);
                # el setter NO: no hay ninguna property full (get+set) en los 137 archivos de prueba.
                if pf & 2: read_function(r, S)
                nconst += 1
                extra = "SIN respaldo  => el valor sale del .pex"
                if lit is not None: extra += f"  (AutoReadOnly = {lit!r})"
            mark = " <<< ARRAY" if pt.endswith("[]") else ""
            print(f"      {pt:12} {pn:20} [{','.join(kind)}]{mark}  {extra}")
            if pt.endswith("[]"): arrays.append(pn)

        print(f"\n  auto (con variable de respaldo): {nauto}   sin respaldo (AutoReadOnly / full): {nconst}")
        print(f"  ARRAYS declarados: {len(arrays)} -> {', '.join(arrays)}")

        # --- ESTADOS / FUNCIONES, con sus DESTINOS DE LLAMADA y sus literales.
        #
        # ⭐ Antes esta sección se salteaba entera (`r.o = end`) con el argumento de que "no interesa acá".
        # Sí interesa: es el único lugar del artefacto donde se ve si una función se LLAMA, y sin eso un gate
        # sobre el .pex no puede distinguir "el .pex trae la funcion" de "alguien la llama".
        #
        # ⚠️ El layout de la sección va DENTRO DE UN try: está ejercitado por el .pex de Skyrim, y el de
        # Fallout 4 (formato 3.9) no tiene caso de prueba acá. Si se desvía, se avisa y se saltea al final del
        # objeto — el dump sigue sirviendo para lo demás. NO se degrada en silencio: el gate que consume esto
        # falla si no encuentra la función que busca, así que un fallback no puede volverse un verde falso.
        save_o = r.o
        try:
            states = read_states(r, S)
            if r.o != end:
                raise ValueError(f"la sección cerró en 0x{r.o:X} y el objeto declara 0x{end:X}")
            nf = sum(len(f) for _, f in states)
            print(f"  --- FUNCIONES ({nf} en {len(states)} estado(s)) ---")
            for sname, funcs in states:
                for fname, ops in funcs:
                    tag = f"[state '{sname}'] " if sname else ""
                    print(f"      {tag}{fname}")
                    cl = calls_of(ops)
                    if cl: print(f"          calls: {', '.join(cl)}")
                    st = strings_of(ops)
                    if st: print(f"          strings: {', '.join(repr(x) for x in st)}")
        except Exception as ex:
            r.o = save_o
            print(f"  --- FUNCIONES: NO PARSEADAS ({type(ex).__name__}: {ex}) ---")
            assert r.o <= end, f"parseo desbordado en '{name}': 0x{r.o:X} > 0x{end:X}"
            r.o = end
        print()

if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("uso: pex_dump.py <archivo.pex> [...]")
    for p in sys.argv[1:]:
        main(p)
