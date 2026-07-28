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

- **Cara entera** (morphs, sculpt, tints, **face overlays**) → ya está **horneada** en el NIF/texturas
  del FaceGen.

⚠️ **El emisor debe pasar SOLO nodos de overlay Body/Hands/Feet.** Los nodos `Face [Ovl*]` ya están
horneados en el diffuse de la cabeza (`SseOverlayCompositor.HasBakeableFaceOverlays` filtra por
`NodeName.StartsWith("Face")`). Mandarlos también por script los aplicaría **dos veces**: horneados
en la textura + otra vez como decal vivo encima. Salvedad: ese bake está gateado por
`Setting_BakeSseRaceMenuOverlays`; si el usuario lo apaga, ahí sí hay que emitirlos.

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
Después **borrar los `.pex` de los stubs** de las carpetas de salida (ver arriba).

## Dependencia: blanda, no dura

El ESP **no lleva master** de RaceMenu/LooksMenu (no referenciamos ninguno de sus forms). Si el
plugin SKSE/F4SE no está instalado, la clase nativa no se registra y la llamada falla: el NPC queda
sin overlays, pero el record y el FaceGen horneado siguen intactos. No agrega ninguna dependencia
nueva — BodyGen, que ya emitimos, también necesita el plugin.

*(Pendiente de verificar: el modo de fallo exacto sin el plugin — si la VM loguea y sigue, o aborta
el stack del script. Se comprueba corriendo sin SKSE y mirando `Papyrus.0.log`.)*

## ⚠️ Pendiente antes de que el emisor use ROTACIÓN (SSE)

`NiOverride.AddNodeTransformRotation` toma **3 ángulos euler en GRADOS** (heading/attitude/bank) y
arma la matriz él mismo (`NiTransformInterface.cpp:1019-1034`) — **no** acepta la matriz de 9 floats
que guarda el `.jslot`. Nuestro modelo guarda axis-angle y la UI muestra euler XYZ.

Falta verificar que el orden euler de `NiMatrix33::SetEulerAngles(heading, attitude, bank)` coincida
con el de nuestro `Matrix33ToEulerXYZ`. Si no coincide, la rotación sale mal. **Escala y posición no
tienen esta ambigüedad** y se pueden emitir ya.
