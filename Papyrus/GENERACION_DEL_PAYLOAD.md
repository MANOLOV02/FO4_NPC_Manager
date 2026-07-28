# Cómo una actualización del mod llega a una partida en curso

**Estado: SHIPPED y verificado in-game en SSE (2026-07-28). FO4 pendiente.**

## El problema

Editar un NPC en la app y re-publicar el ESP **no tenía ningún efecto sobre un actor que ya existía en
la partida del jugador**: ni aplicaba lo nuevo ni borraba lo viejo. Una copia fresca por `placeatme`
tomaba los cambios perfecto.

## La ley — MEDIDA, no inferida

Skyrim SE, 2026-07-28. Observado sobre la misma instancia, con la prueba de que estaba congelada **en
la misma línea de traza** (`SchemaVersion=803257752` cuando el plugin ya tenía otro número):

> Al cargar la partida, el motor reconcilia cada instancia de script guardada contra el `.pex` y el
> VMAD actuales:
> - la variable que el savegame **tiene** → se **restaura del savegame** ⇒ **rancia para siempre**;
> - la variable que el savegame **no tiene** → se **inicializa del VMAD** ⇒ **fresca**.
>
> Vale para escalares **y para arrays**: llegan con su longitud, sin `None` y sin reventar.

Verificado en el binario con `tools\pex_dump.py`: una property `Auto` se compila a una variable de
script (`::X_var`) y las variables se serializan. Una `AutoReadOnly` **no tiene** variable de respaldo
(`flags=0x01`, getter = un único `return <literal>`), así que su valor sale del `.pex`.

## La arquitectura

**Dos piezas ortogonales, las dos automáticas.**

```
plantilla compilada          ->  NPCM_Manolov_ApplySSE          _G000001
lo que la app instala        ->  NPCM_Manolov_<Plugin_esp>_ApplySSE   _G000007
```

| Pieza | Qué resuelve | Cadencia |
|---|---|---|
| **Nombre por ESP** | que dos mods hechos con la app **convivan** (los `.pex` sueltos no se fusionan, gana uno) y que el payload pueda evolucionar | fijo de por vida del mod |
| **Sufijo `_G<n>`** | que una actualización **de ese mod** llegue a una partida en curso | +1 por Save ESP |

Las dos las reescribe `PexPatcher.vb` **dentro del `.pex`**, a nivel bytes (el `.pex` no es UTF-8;
decodificar corrompería los docstrings). Es seguro porque todo lo que no es la tabla de strings
referencia por **índice**: cambiar el contenido de una entrada cambia coherentemente la property **y**
su variable de respaldo. Verificado end-to-end: el `.pex` parcheado vuelve a parsear con la misma
estructura (32 auto + 5 `AutoReadOnly`).

**El stem del plugin va ANTES de `ApplySSE`** para que el nombre legado no sea prefijo de ninguno
nuevo — eso es lo que permite acotar el borrado por prefijo del VMAD a lo nuestro (dos `UpsertScript`)
sin tocar `FO4_Base_Library`.

`appliedVersion` **no lleva sufijo, a propósito**: es el lado que tiene que persistir. Así
`appliedVersion` (viejo) ≠ `SchemaVersion_G<n>` (fresco) ⇒ el actor aplica **una vez** y después
saltea. Y si el payload de ese NPC no cambió, su hash es el mismo y ni siquiera re-aplica.

## De dónde sale el número

Del sidecar **`<plugin>.bssliders`**, campo `payloadGeneration`. Sube +1 en cada Save ESP y vuelve a 0
después de `999999` (ancho fijo de 6 dígitos, así el reemplazo en el `.pex` es byte a byte).

El wrap es seguro: el motor **descarta** la variable que el script ya no declara — medido,
`Variable ::OvlNode_var ... not found within the actual object. This variable will be skipped`.

⚠️ **El sidecar es la única fuente.** Si se pierde, el contador vuelve a 1 y un jugador que ya tenga esa
generación recibiría datos rancios. Por eso `BssliderSidecar.Write` **ya no borra el archivo** cuando no
hay NPCs con datos: el contador tiene que sobrevivir a un guardado sin overlays.

**Override manual**: en el diálogo de Save ESP, checkbox *"Override version"* + numeric. Muestra el
número que se iba a grabar y permite forzar otro; ese valor va al VMAD, al `.pex` y al sidecar. Se
refresca al cambiar de plugin de destino, **incluso con el override tildado** — a propósito: un número
tipeado para el plugin A grabado en el B podría hacerlo *retroceder* de generación.

## ⭐⭐⭐ Un `.pex` = UNA generación ⇒ hay que refrescar el VMAD de TODO el plugin

El `.pex` instalado declara **una sola** generación de properties. Un NPC del plugin cuyo VMAD haya quedado
en una generación anterior nombra properties que ese `.pex` **ya no declara**: no recibe ninguna, sus arrays
llegan en longitud 0, el guard de instancia huérfana corta, y el actor queda **INERTE**.

Hasta 2026-07-28 el guardado sólo re-escribía el VMAD de los NPC incluidos en ESE guardado; el resto se
preservaba con su VMAD viejo. O sea que **cada Save ESP dejaba atrás a todo NPC que no tocaras**.
Medido sobre `NPC_Manager2.esp`: `Aeri` en `_G000011` y `EncBandit04MissileKhajiitM` con properties **sin
sufijo** (de una versión anterior al esquema), inerte desde entonces.

**Arreglado** en `NpcOverrideSaver.RefreshPreservedApplyScripts`: antes de escribir, todo NPC_ preservado que
lleve script nuestro se re-emite con la generación de este guardado, reconstruyendo su payload desde el
sidecar (`BssliderSidecar.HydratePresets`, el único espejo entry→preset). De yapa migra el nombre legado al
nombre por plugin.

Es barato y de bajo riesgo porque los NPC_ preservados **nunca fueron una copia byte a byte**:
`SerializeExistingRecord` ya hacía `ParseNPC` + `NpcSubrecordWriter` completo
(`SaveNpcEspWriter.vb:1254-1272`). El refresh hace lo mismo un paso antes y manda el record por `entries`,
que termina en el MISMO `SerializeNpcRecord`. Sin cambios en `FO4_Base_Library`.

⛔ **Sin entrada en el sidecar NO se toca el record.** No hay con qué reconstruir el payload, y `ApplyToNpc`
con preset `Nothing` emitiría el spec de LIMPIEZA, que le BORRARÍA los overlays al actor. Se lo deja inerte
(que es lo que ya era) y se loguea. Perder la entrega es malo; borrarle datos al usuario es peor.

## Nada que tocar a mano

No hay que subir sufijos ni renombrar el `Scriptname`. Lo único manual que queda es
**`ScriptLogicRevision`** en `NpcApplyScriptEmitter.vb`: se sube cuando cambia el **comportamiento** del
`.psc` (no los datos de un NPC), para que ese arreglo alcance también a los actores cuyo payload no
cambió.

⚠️ Si se renombra el `.psc` o se cambia el ancho del sufijo, hay que actualizar `BaselineScriptSse` /
`BaselineGeneration` o el parcheo del `.pex` falla (ruidoso, no en silencio).

## El `.pex` del nombre legado se sigue shippeando, INERTE

`Data\Scripts\NPCM_Manolov_ApplySSE.pex` va **además** del activo, sin parchear.

⛔ **Nunca borrarlo.** Medido 2026-07-28: al borrarlo, los saves de la versión publicada anterior quedan
con instancias de un tipo que no resuelve, y —como el script `extends Actor`— **ese actor pierde la tabla
de métodos para TODOS los scripts**. RaceMenu falló 17 veces sobre un NPC nuestro con
`Method GetLeveledActorBase not found` y `Cannot call GetSex() on a None object`. No es que no se
apliquen nuestros overlays: le rompemos el NPC a cualquier mod.

Y no re-aplica nada, porque el `.psc` corta arriba con el **guard de instancia huérfana**:

```papyrus
if OvlNode_G<n>.Length == 0        ; el VMAD no nombra a este script
    return
endif
```

`Length == 0` es inequívoco: el emisor garantiza que toda array-property llega con al menos 1 elemento
(el centinela), **también** en el spec de limpieza.

Es un artefacto de migración: se puede dejar de shippear cuando ningún savegame publicado arrastre el
nombre viejo. Como eso no se puede saber, se queda.

## El borrado de overlays: `KEY_ALPHA = 0`

skee **no tiene "deshacer"** a nivel de nodo, verificado en su fuente:

| | |
|---|---|
| `AddNodeOverride*` | guarda en el store (si `persist`) **y pinta el nodo en el acto** |
| `RemoveNodeOverride` | **sólo borra la entrada del map. No toca el nodo** (`OverrideInterface.cpp:333-351`) |
| `ApplyNodeOverrides` | sólo empuja lo que **quedó** en el store (`:777`) |
| `RevertOverlays` / `RevertOverlay` | llegan a Papyrus con **`resetDiffuse = FALSE`** ⇒ restauran el normal y **dejan el tatuaje** |
| `GetDefaultTexture()` | existe en el C++, **no está registrada como native** |

⇒ Se apaga el nodo con **`KEY_ALPHA = 0`** y `persist=false`. Es **el único mecanismo que no depende de
configuración que no vemos**: el path del default sale de `skee64.ini [Overlays/Data] sDefaultTexture`,
que es el del **jugador**, no el del autor.

Esto **obliga** a que `ApplyOverlays` aplique el alpha siempre (el barrido deja todo en 0, y un nodo con
textura nueva sin alpha propio quedaría invisible). El emisor ya manda `1.0` por defecto.

⚠️ **Residual**: el normal (`IDX_NORMAL`) no se repinta — skee lo restaura copiándolo de la geometría de
la piel y eso no existe desde Papyrus. Con alpha 0 el nodo no se ve, así que es inerte.

⚠️ **Barre TODOS los nodos de overlay**, sean nuestros o de otro mod. **Decisión de producto**: el NPC
muestra exactamente lo que muestra la app. skee no guarda dueño (su store es actor+nodo+key+index).

## Verificación in-game (2026-07-28, `Papyrus.0.log` 12:55)

```
OnLoad ref=-16774895 appliedVersion=-1         SchemaVersion_G000005=1355956200   <- ACTIVO, payload fresco
payload ovl=2 skin=1 nodes=1 ... 01 Hands.dds
OnLoad ref=-16774895 appliedVersion=803257752  SchemaVersion_G000001=1            <- legado
INERTE: sin payload del VMAD                                                      <- corta limpio
DONE ref=-16774895
...  segunda pasada -> SKIP                                                       <- no re-aplica de más
```

Cero errores propios. Visual confirmado por el usuario: el tatuaje viejo desaparece y el nuevo se aplica.

## Body morphs de BodySlide por el script (revisión de lógica 5)

Los sliders de BodySlide dejaron de ser exclusivos del par BodyGen `.ini`. Ahora hay **dos rutas
mutuamente excluyentes** (combo "Body morphs delivery" en Save ESP), y la exclusión **es del motor**:

| | Combinación entre keys del mismo morph | Si conviven |
|---|---|---|
| SSE (skee) | **SUMA** (`Impl_GetBodyMorphs`, `BodyMorphInterface.cpp:220-240`, default `iBodyMorphMode=0`) | el slider entra **dos veces** |
| FO4 (f4ee) | **MAX** (`UserValues::GetEffectiveValue`, `BodyMorphInterface.cpp:1001-1009`) | gana el mayor, no lo autorado |

**Por qué mudarlos**: BodyGen se evalúa **una sola vez** por actor y con el gate "este actor no tiene
ningún morph" (`f4ee/ActorUpdateManager.cpp:49-54`, `skee64/ActorUpdateManager.cpp:38-40`) ⇒ una
referencia que ya existe en la partida del jugador **nunca** lo recibe. Es exactamente el problema que
resuelve el sufijo `_G<n>`, así que por el script sí llega.

Payload (idéntico en los dos juegos): `MorphName[]` + `MorphValue[]` + el flag escalar **`MorphsOwned`**.

- **`MorphsOwned = false`** (ruta `.ini`): el script **no toca morphs**, ni barre ni repinta. ⛔ En FO4
  no es opcional: nuestro barrido usa el keyword `None`, que es **el mismo slot** que escribe BodyGen
  (`BodyGenInterface.cpp:517`), y el orden entre el evento de f4ee y el `OnLoad` de Papyrus **no está
  garantizado** — sin el flag, el barrido borraría lo que BodyGen acaba de aplicar, o no, según quién
  corriera primero.
- **`MorphsOwned = true`**: barre lo suyo, aplica, y repinta.
  - SSE: `ClearBodyMorphKeys(ref, "NPCM_Manolov")` — **deshacer quirúrgico sin colateral**: recorre todos
    los nombres y saca sólo NUESTRA key, así que `RSMBodyGen`/XPMSE conservan las suyas. Valor = la suma
    de las contribuciones keyed (skee las suma igual, y así el preview de la app rinde lo mismo).
  - FO4: `RemoveMorphsByKeyword(a, isFemale, None)` — saca el slot 0 de cada nombre y respeta lo que otro
    mod tenga bajo una keyword propia.
- El repintado (`UpdateModelWeight` / `UpdateMorphs`) va **incondicional**, también con payload vacío:
  los dos motores **recomponen la malla desde cero** (skee restaura el backup `SHAPEDATA`,
  `BodyMorphInterface.cpp:522-535`; f4ee detacha y hace `Update3DModel`, `:517-556`), así que ese es el
  camino de la limpieza. ⭐ A diferencia de los overlays de nodo, acá **el deshacer sí existe**.
- ⭐ **El `.ini` NO se toca.** El script gana por construcción corra antes o después que BodyGen (si va
  primero, BodyGen se saltea por su gate; si va segundo, barremos su key). Y el `.ini` se sigue emitiendo
  **a propósito**: es la red para los NPC del plugin que el usuario no re-grabó — ésos conservan su VMAD
  viejo, el script les llega inerte, y el `.ini` es su única vía. Borrarlo les cortaba la entrega.
- ⛔ Barrer la key de BodyGen **no es reemplazable** por borrar el `.ini`: lo que BodyGen aplicó en una
  sesión anterior está en el **co-save del jugador** y se restaura en cada carga. MEDIDO 2026-07-28: con
  el `.ini` ya borrado de disco y ausente de los BSA, un NPC seguía trayendo 39 morphs bajo `RSMBodyGen`.
- Techo: **128 elementos** por array (límite de Papyrus). Si sobran, el emisor recorta por `|valor|` y lo
  **loguea** en `fo4lib.log` — nunca en silencio.

⚠️ **Prácticamente de ida**: una vez que el script escribió un morph, el mapa del actor queda no vacío
(ninguno de los dos `Remove*` borra la entrada del actor), así que BodyGen no vuelve a evaluarlo. Volver
a la ruta `.ini` sólo afecta a actores que todavía no spawnearon.

Trazas para medirlo, todas con el prefijo `[NPCM] BM`: `payload morphs=`, `morphs previos=` +
`morph previo[0] key[0]=` (SSE) / `slot None =` (FO4) — **la sonda de orden**, que dice si alguien
escribió antes que nosotros y quién —, `aplicados=`, y `readback <morph> = <valor>`.

## Pendiente

- **`ScriptLogicRevision`** sigue siendo la única constante manual.
