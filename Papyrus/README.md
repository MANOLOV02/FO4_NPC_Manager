# Papyrus — apply-scripts para lo que no entra en el record ni se puede hornear

Un script **por juego**. No son portes uno del otro: RaceMenu (`NiOverride`, ~180 funciones) y
LooksMenu (`Overlays` + `BodyGen`, 20) exponen APIs distintas y **capacidades distintas**.

| Subsistema | SSE (RaceMenu) | FO4 (LooksMenu) |
|---|---|---|
| Overlays / tatuajes | ✅ textura + tint + alpha | ✅ template + tint + **UV** + **priority** |
| Skin override | ✅ per-slot (diffuse/normal/tint) | ⚠️ **solo por id de template** (el per-slot es Scaleform-only) |
| Node transforms | ✅ escala + posición + rotación | ❌ **no existen en FO4** (`#ifdef _TRANSFORMS`, sin API, sin co-save) |
| **Body morphs (BodySlide)** | ✅ `SetBodyMorph` bajo UNA key nuestra | ✅ `BodyGen.SetMorph` con keyword `None` |

## ⭐ Body morphs: por script O por `.ini`, nunca los dos

Los sliders de BodySlide se entregan por **una** de dos rutas, elegida en Save ESP
("Body morphs delivery"). **Son mutuamente excluyentes por comportamiento del motor, no por gusto**:
si las dos escriben sobre el mismo actor, ninguna saltea a la otra.

| | Combinación entre keys del mismo morph | Resultado si conviven |
|---|---|---|
| SSE (skee) | **SUMA** (`Impl_GetBodyMorphs`, `BodyMorphInterface.cpp:220-240`, default `iBodyMorphMode=0`) | el slider se aplica **DOS VECES** |
| FO4 (f4ee) | **MAX** (`UserValues::GetEffectiveValue`, `BodyMorphInterface.cpp:1001-1009`) | gana el mayor, no el valor autorado |

**Por qué mudarlos al script**: BodyGen se evalúa **una sola vez** y con el gate "este actor no tiene
ningún morph" (`f4ee/ActorUpdateManager.cpp:49-54`, `skee64/ActorUpdateManager.cpp:38-40`), así que una
referencia que YA existe en la partida del jugador **no lo recibe nunca**. Una property con nombre nuevo
(el sufijo `_G<n>`) sí le llega.

- Elegir la ruta "Apply-script" **BORRA** el par `.ini` de ese plugin. Obligatorio: uno instalado de una
  versión anterior seguiría sumando/maxeando.
- El payload lleva un flag **`MorphsOwned`**. Con la ruta `.ini` vale `false` y el script **no toca nada**
  de morphs (ni barre ni repinta) — en FO4 eso es obligatorio, porque nuestro barrido usa el keyword
  `None`, que es **el mismo slot** que escribe BodyGen (`BodyGenInterface.cpp:517`).
- El deshacer **sí existe** para morphs, al revés que para los node overrides: los dos motores
  recomponen la malla desde cero (skee restaura el backup `SHAPEDATA`; f4ee detacha y hace
  `Update3DModel`).

## Lo que los scripts NO hacen (a propósito — se entrega por otra vía)

- **Cara**: morphs, sculpt, tints y los face overlays del pool **normal** → ya están **horneados** en el
  NIF/texturas del FaceGen.

⚠️ **Qué nodos de overlay pasa el emisor** — hay tres casos, y los tres importan:

| Nodo | ¿Lo hornea el bake? | ¿Lo emite el script? |
|---|---|---|
| `Body/Hands/Feet [Ovl*]` y `[SOvl*]` | nunca (no son de cara) | **siempre** |
| `Face [Ovl*]` (pool normal) | **sí**, dentro del diffuse de la cabeza | sólo si el bake NO se lo queda (`Setting_BakeSseRaceMenuOverlays` apagado) |
| `Face [SOvl*]` (pool **MAGIC**) | **NUNCA** | **siempre** |

El predicado que decide es `SseOverlayCompositor.IsFoldableFaceOverlay` = nodo `Face` **menos** el pool
magic, y lo comparten el bake (CPU y GPU), el render plegado y el emisor. Mandar un `Face [Ovl]` por
script cuando el bake lo hornea lo aplicaría **dos veces** (en la textura + decal vivo encima); no
mandar un `Face [SOvl]` lo dejaría **sin dueño** (el fold no lo pliega en ningún caso).

### ⭐ El pool MAGIC (`[SOvl{n}]`) — por qué es otra cosa

skee64 mantiene **dos** pools por zona (`OverlayInterface.h:23-46`), con contadores independientes:
`iNumOverlays` (default 3) y `iSpellOverlays` (default **1**). La plantilla del magic es
`*_magicoverlay.nif`, y **medido** parseando sus bloques: trae un `BSEffectShaderPropertyFloatController`
con `typeOfControlledVariable=5` (=**Alpha**), `flags=0x4A` (ACTIVE + CYCLE_REVERSE), `frequency=8`,
keys `(t=0,v=0)→(t=10,v=1)` ⇒ **el motor le anima la opacidad, pulsando 0↔1**. La geometría NO sale del
NIF en ninguno de los dos pools: `InstallOverlay` descarta la shape del archivo y clona `vertexDesc`,
vértices, transform y skin **de la piel del actor** (`OverlayInterface.cpp:137-186`).

⇒ Por eso el pool magic **no se hornea** (sería permanente una capa que el motor prende y apaga), el
**preview principal no lo dibuja** (un efecto en curso no es el retrato del NPC) y el de los editores sí,
a su pico. Y por eso su opacidad autorada es informativa: in-game la pisa el controller.

## Enganche

El script se cuelga del record `NPC_` vía el subrecord `VMAD` (ver `NpcVmadBuilder.vb`). Un script
en el ActorBase lo heredan todas las instancias, y como el tipo Papyrus `Actor` extiende
`ObjectReference`, recibe eventos por-instancia — por eso `OnLoad()` corre en cada actor spawneado.
Verificado contra vanilla: 805/5118 NPC_ de Skyrim.esm y 382/3015 de Fallout4.esm ya traen scripts
colgados así (`defaultGhostScript`, `TeleportActorScript`, `WorkshopNPCScript`, …).

Los datos por-NPC viajan como **propiedades del script dentro del mismo VMAD** (Papyrus no puede
leer archivos). Arrays paralelos: el índice `i` de cada `Ovl*` describe el overlay `i`.

## Los .pex van EMBEBIDOS en la DLL — y en SSE se PARCHEAN al instalar

`pex_sse/NPCM_Manolov_ApplySSE.pex` y `pex_fo4/NPCM_Manolov_ApplyFO4.pex` se compilan acá y el
`.vbproj` los mete como **EmbeddedResource** dentro de `NPC_Manager_FO4.dll`.

⚠️ **En SSE el `.pex` compilado es una PLANTILLA, no lo que se instala.** Al guardar el ESP,
`PexPatcher` le reescribe adentro (a nivel bytes) el nombre del script y la generación del payload:

```
plantilla:  NPCM_Manolov_ApplySSE                 _G000001
instalado:  NPCM_Manolov_<Plugin_esp>_ApplySSE    _G000007
```

⇒ **No renombrar el `Scriptname` ni tocar el sufijo a mano.** Si se cambia alguno hay que actualizar
`BaselineScriptSse` / `BaselineGeneration` en `NpcApplyScriptEmitter.vb`, o el parcheo falla.

En SSE se instalan **dos** archivos: el activo (parcheado, con nombre por plugin) y
`NPCM_Manolov_ApplySSE.pex` sin parchear, que queda **inerte** y existe sólo para que resuelva el tipo
en saves de la versión publicada anterior — borrarlo le rompe el actor a todos los demás mods.
Ver `GENERACION_DEL_PAYLOAD.md`.

**No** se copian sueltos al lado del `.exe`. Dos motivos, y el segundo es el que importa:

1. No hay carpeta suelta que se pierda al mover o distribuir la app.
2. El `.pex` **no puede desincronizarse** de la build que emitió el VMAD que lo referencia. Un `.pex`
   viejo al lado de una app nueva ignoraría en silencio las propiedades que no conoce: el script
   correría, no aplicaría nada, y no reportaría nada. Ese es el peor modo de falla posible, así que
   lo hacemos irrepresentable.

⇒ **Después de recompilar los `.psc`, hay que rebuildear la app** para que el `.pex` nuevo entre en
la DLL.

## ⛔ NUNCA shippear los .pex de los stubs

`src_sse/NiOverride.psc`, `src_fo4/Overlays.psc`, `src_fo4/BodyGen.psc` (y `ArmorAddon.psc`) son
**stubs de compilación**: declaraciones `global native` transcritas 1:1 desde el C++ de RaceMenu/
LooksMenu. Existen sólo para que el compilador resuelva las llamadas.

**Sus `.pex` compilados NO van al usuario.** Los archivos sueltos le ganan al BSA/BA2, así que copiar
nuestro `NiOverride.pex` pisaría la implementación real de RaceMenu y rompería el mod entero. Por eso
el paso de compilación termina **borrando** los `.pex` de los stubs, y `pex_sse/` y `pex_fo4/`
contienen únicamente `NPCM_Manolov_Apply*.pex` (que son los únicos dos que el `.vbproj` embebe).

Lo único que se instala en el juego es:

- SSE → `Data\Scripts\NPCM_Manolov_ApplySSE.pex`
- FO4 → `Data\Scripts\NPCM_Manolov_ApplyFO4.pex`

## ⛔ SSE: el techo de barrido de overlays (`OVL_SWEEP_MAX`) vive en TRES lados

El editor deja autorar un overlay con índice **por encima de `iNumOverlays`** (avisa una vez por sesión y lo
agrega igual). Ese override entra al store de skee con `persist=true` —o sea al co-save— **aunque el nodo no
exista**: `Impl_AddNodeOverride` guarda sin mirar el 3D (`OverrideInterface.cpp:56-63`). Como
`ClearOverlayGroup` sólo enumera `0..iNumOverlays-1`, sin un segundo barrido ese override **no se podía sacar
nunca más** de la partida del jugador, y reaparecía el día que subiera `iNumOverlays` — encima de lo horneado,
en el caso de la cara.

`PurgeOverlayGroup(prefix, iNumOverlays, OVL_SWEEP_MAX)` cierra eso sacando **sólo del store** (en ese rango el
nodo no existe: apagarlo visualmente no tiene sentido y costaría un `GetObjectByName` por llamada).

**El pool MAGIC usa el mismo par de rangos, con su propio contador.**
`ClearOverlayGroup("<zona> [SOvl", GetNumSpell*Overlays())` apaga visualmente los nodos que el juego del jugador
realmente creó, y `PurgeOverlayGroup("<zona> [SOvl", ese contador, OVL_SWEEP_MAX)` saca del store hasta el tope
del motor (un `.jslot` importado puede traer un `[SOvl40]`).

⛔ **ACÁ HABÍA UN TECHO INVENTADO Y SE FUE.** El párrafo anterior decía que el contador del pool magic "no se
puede preguntar: Papyrus no expone getter de `g_numSpell*Overlays`", y de ahí colgaba una constante
`OVL_SPELL_CLEAR_MAX = 8` gemela en tres artefactos, más un descarte en el emisor de todo magic con índice ≥ 8.
**La afirmación es falsa**: `NiOverride.GetNumSpellBodyOverlays` / `Hand` / `Feet` / `Face` están registradas
(`PapyrusNiOverride.cpp:1844-1853`) y flaggeadas `NoWait` (`:2422-2425`). El pool normal ya usaba sus getters
dos líneas más arriba.

Consecuencias de arreglarlo: el apagado visual es exacto (con el default `iSpellOverlays=1` son 8 recorridos de
árbol en vez de 64, de los cuales 56 caían sobre nodos inexistentes), **el emisor dejó de perder en silencio los
overlays magic que el usuario autoró**, y desapareció la constante gemela con su gate.

El número que sigue viviendo en tres artefactos —y que **nadie sincroniza en build**— es uno solo:

| Dónde | `OVL_SWEEP_MAX` (hasta dónde se saca del store) |
|---|---|
| `src_sse\NPCM_Manolov_ApplySSE.psc` | `int Property OVL_SWEEP_MAX` |
| `pex_sse\NPCM_Manolov_ApplySSE.pex` (embebido en la DLL) | ⭐ **lo que corre en el juego** |
| `FO4_NPC_Manager\NPC\SseCatalogs.vb` | `OverlaySweepCeiling` |

⇒ `python tools\check_sweep_ceiling.py` compara **los tres**, más la key de override (una string) y —desde su
sección "lo que el `.pex` HACE"— que el binario compilado realmente llame lo que tiene que llamar (exit 0 / 4).
**Correrlo después de tocar cualquiera de ellos**: si el `.pex` barre menos de lo que la app cree, la app le
promete al usuario que un
overlay se puede sacar y en la partida queda pegado.

⇒ Y `Tools\OverlaySlotGate` (exit 0 / 4) cubre lo que ese script no ve: el lector del `skee64.ini` (las dos
keys), el codec de nombre de nodo de los dos pools, el orden de composición, el índice libre por pool y los
textos de los avisos. **Correrlo después de tocar `SseCatalogs` o `SseOverlayCompositor`** — hay que
buildear primero `NPC_Manager_FO4` con `-p:Platform=x64`, que es el bin del que el gate toma la referencia.

⭐ **128 no es un número elegido: es el tope del motor.** skee clampea todo contador de overlay a `0x7F`
(`main.cpp:810-828`) y los crea con `i < count`, así que **`[Ovl126]`** es el último nodo que puede existir en
cualquier instalación (127 es el tope EXCLUSIVO del barrido, que lo cubre justo).
Barriendo hasta ahí, **nada de lo que la app pueda autorar queda irrecuperable** — y eso es lo que permite que
haya UN solo aviso en la UI. Con un techo más bajo aparecía una banda de índices (entre el techo y el
`iNumOverlays` del autor) que quedaba clavada en la partida del jugador y que la app tenía que explicar con un
segundo aviso y un predicado aparte. El costo del techo alto, con el ini shipeado (6/3/3/3) y **una sola vez
por actor**: ~1972 nativas del pool normal + ~1904 del pool magic = **~3876** de `PurgeOverlayGroup` (sólo store),
más lo de `ClearOverlayGroup`: **94** `AddNodeOverride*` (esas sí recorren el 3D) **+ 188** `RemoveNodeOverride`
⇒ **~4158 en total**. La cifra vieja de "~1970" era de antes del pool magic — el desglose está en el comentario
de `PurgeOverlayGroup` en el `.psc`.

## Recompilar

```powershell
# SSE
& "F:\SteamLibrary\steamapps\common\Skyrim Special Edition\Papyrus Compiler\PapyrusCompiler.exe" `
  "src_sse" -f="TESV_Papyrus_Flags.flg" `
  -i="F:\SteamLibrary\steamapps\common\Skyrim Special Edition\Data\Source\Scripts;src_sse" `
  -o="pex_sse" -all

# FO4
& "F:\SteamLibrary\steamapps\common\Fallout 4\Papyrus Compiler\PapyrusCompiler.exe" `
  "src_fo4" -f="Institute_Papyrus_Flags.flg" `
  -i="F:\SteamLibrary\steamapps\common\Fallout 4\Data\Scripts\Source\Base;src_fo4" `
  -o="pex_fo4" -all
```
Después **borrar los `.pex` de los stubs** de las carpetas de salida (ver arriba), **rebuildear la app** (el
`.pex` es un `EmbeddedResource`) y correr `python tools\check_sweep_ceiling.py`.

## Dependencia: blanda, no dura

El ESP **no lleva master** de RaceMenu/LooksMenu (no referenciamos ninguno de sus forms). Si el
plugin SKSE/F4SE no está instalado, la clase nativa no se registra y la llamada falla: el NPC queda
sin overlays, pero el record y el FaceGen horneado siguen intactos. No agrega ninguna dependencia
nueva — BodyGen, que ya emitimos, también necesita el plugin.

*(Pendiente de verificar: el modo de fallo exacto sin el plugin — si la VM loguea y sigue, o aborta
el stack del script. Se comprueba corriendo sin SKSE y mirando `Papyrus.0.log`.)*

## Node transforms: la rotación (SSE)

⛔ **Esta sección decía lo contrario y era FALSO.** Decía que `AddNodeTransformRotation` "toma 3 ángulos
euler en GRADOS y **no** acepta la matriz de 9 floats", y de ahí colgaba un pendiente sobre el orden
euler. La nativa acepta **3 O 9** y con 9 los copia directo a `NiMatrix33::arr[i]`
(`PapyrusNiOverride.cpp:1190-1193`) — el mismo `arr[i]` que skee empaqueta después bajo la key 32.

Por eso el emisor manda **los 9 floats crudos** y no hay ninguna convención euler de por medio: le
devolvemos al motor su propia secuencia.

⛔ Y ESTO **NO ERA CIERTO** hasta 2026-08-10, aunque estaba escrito acá: decía "armada por la misma función
que escribe el `.jslot`, así que el script y el archivo no pueden divergir". Eran funciones distintas y **sí
divergían** — `RotationRowMajor` (el ESP) rearmaba siempre desde axis-angle mientras el `.jslot` prefería la
matriz cruda, así que una rotación de 180° o una reflexión sobrevivía en el archivo y **se perdía en el
plugin**. Ahora la elección vive una sola vez, en `RotationRowMajor`, y los tres caminos la llaman; recién
ahora la frase se sostiene.

## Node transforms: un aporte, el nuestro

Un node transform está **keyeado por nombre**: un hueso puede tener varios aportes, cada contribuyente
escribe el suyo, y el motor los **compone**. El nombre existe para una sola cosa — que cada uno pueda sacar
el suyo sin tocar los ajenos.

La app modela **un TRS por hueso**, así que al importar compone los aportes del preset y guarda el
**efectivo**. Y el ESP escribe ese efectivo como **un** aporte, bajo `XformKey()`. Eso es forzado por dos
hechos, no una preferencia:

1. Los aportes de un preset **no llegan solos a un NPC**. Nada los lleva: el menú de RaceMenu aplica presets
   al jugador, y la única vía de Papyrus (`CharGen.LoadCharacterPresetEx`) la tiene que llamar un mod a
   propósito. Si nuestro aporte no lleva el efectivo, el NPC no recibe la forma del preset.
2. **Sólo podemos escribir bajo nuestro nombre**, porque es el único que `RemovePrevious` puede borrar
   después. Escribir bajo `RMX_Head` dejaría un valor clavado en la partida del jugador para siempre.

### ⛔ Lo que se probó y se revirtió

Hubo una función `ClaimNode()` que, antes de escribir, borraba las capas ajenas del hueso. Se fue, y la
razón es que confundía dos cosas distintas: las capas de un **preset** (el desglose por slider de un autor,
que vive en un archivo) con las de un **actor** en runtime (de mods distintos). Sobre un NPC real las únicas
ajenas son `internal` del motor —donde componer **es correcto**: el NPC con tacos tiene que levantarse— y los
nodos de arma de XPMSE, que vuelven en el próximo cambio de arma.

Y no se podían distinguir: el motor no dice "soy derivado del equipo" ni "soy otro mod pisándote". Cualquier
política automática se equivoca en uno de los dos, y equivocarse **borrando** no tiene vuelta atrás mientras
equivocarse mostrando otro número sí.


### La neutralización: lo único que se toca, y es del propio preset

Nuestro aporte lleva el **total** del hueso. Si los aportes que la app compuso están **además** en el co-save del
jugador, el motor compone los suyos con nuestro total y el hueso sale **al doble**. Y están por una razón
concreta: algún mod le aplicó **ese mismo preset** a **ese mismo NPC** con `CharGen.LoadCharacterPresetEx` — la
única vía de Papyrus para aplicar un `.jslot` a un actor cualquiera.

Solución: el payload lleva los **nombres** de las capas que la app colapsó, como pares planos `(nodo, nombre)`,
y `NeutralizeCollapsedLayers()` les escribe **identidad completa** (escala 1, posición 0, rotación identidad)
bajo su propio nombre. Escribir la misma `(nodo, nombre, key, index)` **reemplaza** —`Impl_AddNodeTransform` hace
erase+insert y el set compara por `(key,index)`— así que el aporte queda inerte **sin borrar nada**.

- **Identidad completa, no "sólo lo que el preset tenía"**: el preset dice qué tenía **él**, pero no sabemos qué
  hay bajo ese nombre en el co-save **del jugador**.
- **Los nombres se persisten en el sidecar** (campo `cl`). Sin eso se perdían al reabrir la app y el ESP dejaba
  de neutralizar — el mismo defecto que ya había mordido con la matriz cruda de rotación.

### ⭐⭐ Y LA REGLA QUE CIERRA TODO: absorber es un compromiso de DOS mitades

> *"me quedo con tu número **Y** me hago cargo de apagar el tuyo"*

Si no podemos cumplir la segunda mitad, **no tomamos la primera**. Un solo predicado
—`RaceMenuJslot.IsNeutralizableLayerName`— gobierna **las tres** decisiones:

| decisión | dónde vive |
|---|---|
| qué se **compone** al importar | el decode del `.jslot` (saltea la capa) |
| qué se **saca** del `.jslot` al guardar | `StripForeignTrsLayers` (la deja pasar entera) |
| a qué se le escribe **identidad** | `CollapsedLayerNames` → `NeutralizeCollapsedLayers()` |

⛔ **Antes eran DOS predicados con respuestas distintas, y eso duplicaba el hueso in-game.** El decode componía
TODAS las capas y el strip sacaba el TRS de todas las ajenas, pero la lista de neutralización excluye `internal`,
`NodeDestination` y los sufijos de plugin. O sea: nuestro valor ya incluía su aporte, el archivo perdía la capa
original, y el ESP no podía apagarla.

Lo que **nunca** se absorbe ni se toca: `internal` (es del motor — el lift de los tacos: neutralizarla hunde al
NPC), `NodeDestination` (**key 40**: no es un número, es una *mudanza* — de qué otro hueso cuelga el nodo; su
"neutro" sería la cadena vacía, que para el motor es **otra orden**), la **key 33** (`scaleMode`: el motor nunca
lee la de un nodo, así que es inerte ⇒ misma condición que la 40), los nombres terminados en `.esp`/`.esm`/`.esl`
(skee los poda en cada carga del co-save), y la nuestra.

⇒ Esas capas quedan **intactas en el archivo** y aportan por su cuenta. Es lo correcto para los tacos, y un
conflicto de mods en el otro caso.

**Y la neutralización es recuperable**: si el valor era el de un mod vivo, el jugador mueve el slider de ese mod y
su valor pisa nuestra identidad, por el mismo `erase`+`insert`.

### El residuo, dicho

Si el actor tiene un aporte bajo un nombre que el preset **no** trajo, el motor lo compone con el nuestro y el
juego muestra más que la app. Con `internal` eso es lo correcto; con otro mod es un conflicto de mods. El
reporte de compatibilidad del preset lo dice, y dice también que nada de esto llega al juego si el NPC se
guarda sin el apply-script.

⚠️ Y la key **40** (`NodeDestination`) no se toca en ningún lado: no es un valor de transform sino un
**re-parenteo** (de qué otro hueso cuelga el nodo — el mecanismo con el que XPMSE te pone la espada en la
espalda). No se compone, no la modelamos, y no tiene "identidad" posible: su neutro sería la cadena vacía,
que para el motor **es otra orden**. Por eso el recorte del archivo es por **componente** (30/31/32/33) y
nunca por capa.
