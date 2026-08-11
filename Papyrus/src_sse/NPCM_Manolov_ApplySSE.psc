Scriptname NPCM_Manolov_ApplySSE extends Actor
{
  NPC Manager (Manolov) — applies the RaceMenu/NiOverride options that CANNOT live in an ESP record
  and CANNOT be baked into a mesh or texture, to an NPC, on its first spawn.

  Attached to the NPC_ base record via VMAD (see NpcVmadBuilder.vb). A script on an ActorBase is
  inherited by every reference of it, and because the Papyrus type `Actor` extends `ObjectReference`
  it receives per-instance events — that is why OnLoad() fires per spawned actor.

  WHAT THIS SCRIPT DOES *NOT* DO — on purpose, to avoid applying anything twice:
    * LA CARA, casi entera — morphs, sculpt, tints y los face overlays del pool NORMAL: todo eso se
                           HORNEA en el NIF/las texturas del FaceGen. DOS salvedades, las dos reales:
                           (a) los Face [Ovl] sí se emiten cuando el usuario apaga el bake de overlays
                               de cara (Setting_BakeSseRaceMenuOverlays), porque entonces nadie más los
                               aplicaría;
                           (b) los Face [SOvl] (pool MAGIC) se emiten SIEMPRE: el fold no los pliega en
                               ningún caso — son la capa que un magic effect prende en runtime — así que
                               este script es su único dueño.

  BODY MORPHS (BodySlide) ARE DELIVERED HERE NOW, ALONGSIDE the BodyGen .ini for the same plugin.
  ⛔ DECIA "and the BodyGen .ini is then NOT emitted for the same plugin": ES FALSO, y el saver dice lo
  contrario en mayusculas (NpcOverrideSaver.vb:1000-1012). El .ini se emite igual y NO se borra nunca,
  porque es por PLUGIN y lista TODOS sus NPC: borrarlo le corta la entrega a todos los que el usuario no
  re-grabo (su VMAD viejo les llega INERTE). Ya paso una vez.
  Lo que SI es cierto es el peligro que sigue, y lo resuelve el BARRIDO (ClearMorphs, poda total del
  actor), no dejar de emitir el .ini: las dos entregas SIN barrido no podrian coexistir: skee SUMS the per-key values of a morph by default
  (Impl_GetBodyMorphs, BodyMorphInterface.cpp:220-240, g_bodyMorphMode = 0 in main.cpp:145), so a
  BodyGen row plus ours would apply the slider TWICE. See ApplyBodyMorphs() below.
}

;-- ⛔⛔ LAS DOS REGLAS DE PAPYRUS QUE ME COSTARON UNA TARDE ENTERA -----------------------------
;
; (1) NUNCA pasar un array como ARGUMENTO a una función propia.
;     Papyrus tira "Cannot cast from None to X[]" AL BINDEAR el argumento, así que un `if a == None`
;     dentro del helper NUNCA llega a correr. Lo aprendí con Count/At/AtBool, lo documenté, y lo
;     repetí igual con RotAt. En este archivo no hay helpers que reciban arrays. Que siga así.
;
; (2) NUNCA comparar un array contra None: `if arr == None` / `if arr != None`.
;     Es la trampa peor, porque PARECE el guard correcto y compila sin chistar. Papyrus emite un CAST
;     de None al tipo del array, y ese cast REVIENTA — tenga o no valor el array. MEDIDO: los guards
;     eran el 100% de los errores del Papyrus.0.log. Vanilla nunca lo hace: chequea `.Length`
;     (p.ej. `if (WaterStages.Length == 0)`).
;
; ⇒ Acá los arrays se chequean SIEMPRE por `.Length`, jamás contra None. Eso sólo es seguro porque el
;   emisor GARANTIZA que toda array-property existe y trae al menos 1 elemento: cuando no hay datos
;   manda un CENTINELA ("" o 0) que los loops de abajo saltean solos. Esa garantía vive en
;   NpcApplyScriptEmitter.AddArray y este script DEPENDE de ella.
;   (Un array vacío es ilegal en Papyrus, y una propiedad ausente queda en None: el centinela es la
;   única salida que no cae en ninguna de las dos.)

;-- ⭐⭐⭐ EL SUFIJO `_G<n>` Y EL NOMBRE POR PLUGIN — LO PONE LA APP, NO SE TOCA A MANO ------------
;
; LEY MEDIDA (Skyrim SE, 2026-07-28, sobre instancias demostrablemente congeladas):
;
;   Al cargar la partida, el motor reconcilia cada instancia de script guardada contra el .pex y el
;   VMAD actuales:
;     * la variable que el savegame TIENE     -> se RESTAURA del savegame  => valor RANCIO para siempre
;     * la variable que el savegame NO tiene  -> se INICIALIZA del VMAD    => valor FRESCO del plugin
;   Vale para escalares y para arrays: llegan con su longitud, sin None y sin reventar.
;
; Una property `Auto` se compila a una variable de script (`::X_var`), y las variables se serializan.
; Por eso el payload de una version quedaba pegado para siempre en toda referencia que ya existia en
; la partida del jugador: re-publicar el mod no la alcanzaba.
;
; ⇒ Cada version publicada emite el payload con NOMBRES NUEVOS. El sufijo es el numero de generacion.
;
; ⚠️ ESTE ARCHIVO ES UNA PLANTILLA. Se compila con `_G0000010000` y con el nombre `NPCM_Manolov_ApplySSE`,
;   y NINGUNO de los dos es lo que llega al juego: al guardar el ESP, la app reescribe DENTRO del .pex
;   (PexPatcher.vb, a nivel bytes) tanto el nombre del script como la generacion:
;
;       plantilla:  NPCM_Manolov_ApplySSE                 _G0000010000
;       instalado:  NPCM_Manolov_<Plugin_esp>_ApplySSE    _G000007
;
;   ⇒ NO subir el sufijo a mano y NO renombrar el Scriptname. Si se cambia alguno hay que actualizar
;     `BaselineScriptSse` / `BaselineGeneration` en NpcApplyScriptEmitter.vb, o el parcheo falla.
;
; DE DONDE SALE EL NUMERO: del sidecar `<plugin>.bssliders`, campo `payloadGeneration`. Sube +1 en cada
; Save ESP y vuelve a 0 despues de 999999 (ancho fijo de 6 digitos para que el reemplazo en el .pex sea
; byte a byte). El wrap es seguro: el motor DESCARTA la variable que el script ya no declara — medido,
; "Variable ::OvlNode_var ... not found within the actual object. This variable will be skipped".
; Se puede forzar a mano con "Override version" en el dialogo de Save ESP.
;
; POR QUE EL NOMBRE LLEVA EL PLUGIN: los .pex sueltos de Data\Scripts no se fusionan, gana uno. Con un
; nombre unico, dos mods hechos con la app conviven. El stem va ANTES de "ApplySSE" para que el nombre
; legado no sea prefijo de ninguno nuevo, y asi el borrado por prefijo del VMAD no toque lo de otro autor.
;
; ⚠️ `appliedVersion` NO lleva sufijo A PROPOSITO: es el lado que TIENE que persistir. Asi
;   `appliedVersion` (viejo, del save) != `SchemaVersion_G<n>` (fresco, del plugin) ⇒ el actor aplica UNA
;   vez y despues saltea. Si el payload de ese NPC no cambio entre releases su hash es el mismo, asi que
;   ni siquiera re-aplica.
;
; Los `KEY_*`/`IDX_*` tampoco lo llevan: son `AutoReadOnly`, o sea SIN variable de respaldo (verificado
; en el binario del .pex), asi que su valor sale del .pex y ya es fresco por definicion.

;-- NiOverride override keys (skee64 OverrideVariant.h:31-59) -----------------------------------
int Property KEY_TINT    = 7 AutoReadOnly   ; kParam_ShaderTintColor — packed 0xAARRGGBB
int Property KEY_ALPHA   = 8 AutoReadOnly   ; kParam_ShaderAlpha
int Property KEY_TEXTURE = 9 AutoReadOnly   ; kParam_ShaderTexture — index 0 = diffuse, 1 = normal
int Property IDX_DIFFUSE = 0 AutoReadOnly
int Property IDX_NORMAL  = 1 AutoReadOnly

;-- Techo del barrido de overlays. Ver PurgeOverlayGroup ---------------------------------------
; Hasta dónde se saca del store un overlay que skee NUNCA instancia (índice >= iNumOverlays).
;
; ⭐ ES EL TOPE DEL MOTOR, NO UN NÚMERO ELEGIDO. skee clampea TODOS los contadores de overlay a 0x7F
; (main.cpp:810-828) y los crea con `for (i = 0; i < count; i++)` (OverlayInterface.cpp:659,664,675,
; 681,689), así que el nodo más alto que puede existir en cualquier instalación es "Body [Ovl126]".
; Este número es el tope EXCLUSIVO del while ⇒ 127 barre 0..126, ni un índice de más.
; Barriéndolo entero, NADA de lo que la app pueda autorar queda irrecuperable, en ninguna PC, sin
; depender del iNumOverlays del jugador (que es suyo y no lo podemos saber).
; (Precisión: un .jslot IMPORTADO puede traer un "[Ovl127]" o más alto, y eso sí queda fuera del
; barrido. No importa: por encima del clamp de skee ese nodo no se instancia con NINGÚN iNumOverlays,
; así que es basura inerte en el co-save, nunca un overlay que aparezca.)
;
; ⛔ UN TECHO MÁS BAJO NO SE AHORRA UN PROBLEMA, SE LO CREA A LA UI. Con 16, un índice entre 16 y el
; iNumOverlays del autor quedaba clavado en la partida del jugador, y la app tenía que sostener DOS
; clases de aviso y una banda de índices "depende de la PC del otro" para explicarlo. Barrer hasta el
; tope del motor borra esa categoría entera.
;
; Tiene gemelo en VB (SseCatalogs.OverlaySweepCeiling). Si se cambia acá hay que cambiarlo allá y
; recompilar: son artefactos distintos y nadie los sincroniza en build. Lo chequea
; Papyrus\tools\check_sweep_ceiling.py.
int Property OVL_SWEEP_MAX = 127 AutoReadOnly

;-- Techo del APAGADO VISUAL del pool MAGIC ([SOvl{n}]). Ver ClearOverlayGroup / RemovePrevious ----
;
; ⭐ POR QUE ES UN NUMERO Y NO UN GETTER: el pool magic tiene su propio contador en skee
; (g_numSpell*Overlays, key iSpellOverlays por zona, main.cpp:775-781, default 1), con su propio clamp
; (:812-828) y su propio cero por bEnableFaceOverlays (:833-836) -- pero **Papyrus no expone getter**
; para el. NiOverride.psc tiene GetNumBodyOverlays / GetNumFaceOverlays / etc. y ninguno del spell.
;
; ⭐ POR QUE NO ES 127 COMO EL OTRO: apagar un slot cuesta AddNodeOverride*, que es un GetObjectByName
; sobre el 3D del actor -- un recorrido del arbol POR LLAMADA (OverrideInterface.cpp:750-763). A 127 x
; 4 pools eso es caro y no compra nada, porque la app no puede autorar tan alto (ver abajo). El
; barrido del STORE si va hasta OVL_SWEEP_MAX: es la llamada barata y un .jslot importado puede traer
; un [SOvl40].
;
; ⛔ Y HAY QUE APAGARLOS, no alcanza con sacarlos del store: RevertOverlay llega a Papyrus con
; resetDiffuse=false, asi que el nodo se queda mostrando la ultima textura (ver el bloque de
; ClearOverlayGroup mas abajo).
;
; ⛔⛔ ACA VIVIA `OVL_SPELL_CLEAR_MAX = 8` Y NO TENIA QUE EXISTIR. Su justificacion era que "Papyrus no
; expone getter del contador del pool magic", y es FALSO: NiOverride.GetNumSpell{Body,Hand,Feet,Face}Overlays
; estan registradas (PapyrusNiOverride.cpp:1844-1853) y son NoWait (:2422-2425). Ver RemovePrevious.
; Con el contador real, la promesa "lo que se escribe se puede deshacer" se cumple SIN ningun techo: el
; apagado visual cubre exactamente los nodos que existen, y el barrido del store llega a OVL_SWEEP_MAX.
; Se fueron con ella: el descarte de overlays magic en el emisor (que perdia dato del usuario), el gemelo
; en VB y su entrada en check_sweep_ceiling.py.

bool Property IsFemale_G0000010000 = false Auto
{Género para el que se autoraron los overrides. NiOverride guarda los sets male/female por separado.}

bool Property Verbose_G0000010000 = false Auto
{DIAGNOSTICO. false (default, y lo que se publica) = el script NO traza NADA. La app lo pone en true
 cuando ella misma esta diagnosticando (Logger.Enabled, que es Debug-only).

 ⭐ NO ES SOLO PARA AHORRARSE LINEAS DE LOG. Lo que gatea es COSTO QUE HOY SE PAGA SIEMPRE:

 1) La CONCATENACION de cada traza. `"...=" + applied + " de " + n + " key=" + ovrKey` es bytecode de
    Papyrus y se ejecuta tenga el jugador el log prendido o no — el motor solo decide si ESCRIBE.
    Dentro del `if` no se ejecuta nada.
 2) ⭐⭐ LAS NATIVAS DE SONDA. GetMorphNames / GetMorphKeys / GetBodyMorph se llaman UNICAMENTE para
    trazar. Son llamadas nativas de verdad, y en FO4 medimos que una nativa sin NoWait cuesta ~17 ms
    (512 SetMorph = ~9 s). Ese es el gasto caro, no el Debug.Trace.

 Por eso el flag envuelve los BLOQUES DE SONDA COMPLETOS, no solo las lineas de Debug.Trace.
 ⛔ JAMAS envolver una llamada FUNCIONAL (ClearMorphs, AddNodeOverride*, SetBodyMorph...): si el
 flag se apagara, el script dejaria de aplicar. Solo se envuelve lo que existe para mirar.

 Por que una property y no dos .pex (uno con trazas y otro sin): Papyrus no tiene preprocesador, asi
 que dos .pex exigirian un paso de build que borre lineas por texto y compile dos veces — dos
 artefactos que mantener sincronizados y la chance de shipear el equivocado, que es justo el modo de
 falla contra el que el .pex se embebe en la DLL. Con la property hay UN artefacto, y ademas se puede
 prender sin recompilar Papyrus: se pone true, se re-guarda el ESP y listo.}

int Property SchemaVersion_G0000010000 = 1 Auto
{Hash del payload de ESTE NPC. Cambia sólo si cambian sus valores ⇒ sólo ESE actor re-aplica.}

;-- overlays: Body/Hands/Feet, y Face sólo cuando el bake NO se la queda -------------------------
; ⭐ El pool MAGIC (Face/Body/Hands/Feet [SOvl{n}]) viaja SIEMPRE por acá: el fold no lo pliega nunca
;   (es la capa que un magic effect prende en runtime), así que este script es su único dueño.
;   Los nodos van en OvlNode tal cual, con su nombre — el nombre ES el pool, no hay array aparte.
string[] Property OvlNode_G0000010000 Auto
string[] Property OvlDiffuse_G0000010000 Auto
string[] Property OvlNormal_G0000010000 Auto
bool[]   Property OvlHasTint_G0000010000 Auto
int[]    Property OvlTint_G0000010000 Auto
bool[]   Property OvlHasAlpha_G0000010000 Auto
float[]  Property OvlAlpha_G0000010000 Auto

;-- skin overrides (por slot biped) -------------------------------------------------------------
int[]    Property SkinSlot_G0000010000 Auto
string[] Property SkinDiffuse_G0000010000 Auto
string[] Property SkinNormal_G0000010000 Auto
bool[]   Property SkinHasTint_G0000010000 Auto
int[]    Property SkinTint_G0000010000 Auto

;-- node transforms -----------------------------------------------------------------------------
string[] Property NodeName_G0000010000 Auto
bool[]   Property NodeHasScale_G0000010000 Auto
float[]  Property NodeScale_G0000010000 Auto
bool[]   Property NodeHasPos_G0000010000 Auto
float[]  Property NodePosX_G0000010000 Auto
float[]  Property NodePosY_G0000010000 Auto
float[]  Property NodePosZ_G0000010000 Auto
bool[]   Property NodeHasRot_G0000010000 Auto
float[]  Property NodeRotM0_G0000010000 Auto
float[]  Property NodeRotM1_G0000010000 Auto
float[]  Property NodeRotM2_G0000010000 Auto
float[]  Property NodeRotM3_G0000010000 Auto
float[]  Property NodeRotM4_G0000010000 Auto
float[]  Property NodeRotM5_G0000010000 Auto
float[]  Property NodeRotM6_G0000010000 Auto
float[]  Property NodeRotM7_G0000010000 Auto
float[]  Property NodeRotM8_G0000010000 Auto
{La matriz 3x3 row-major, repartida en NUEVE arrays — NodeRotM<k>[i] = elemento k del nodo i.

 ⛔ LA RAZON QUE DECIA ACA ERA FALSA. Decia: "no un array plano de 9xN porque los arrays de Papyrus
 topan en 128 elementos y un plano se pasaria a los 15 nodos". MEDIDO 2026-07-28 y REFUTADO: un array
 servido por el VMAD llega con 512 elementos sin drama en los DOS juegos. El 128 es del COMPILADOR
 sobre `new T[n]`, y aca el compilador no interviene.
 Se mantiene el split porque el indice i significa "nodo i" en todas las arrays del grupo — la misma
 invariante de overlays, skin y morphs — no por un limite que no existe.

 NO es euler. AddNodeTransformRotation acepta 3 (euler en grados) O 9 (matriz cruda), y con 9 los
 copia directo a NiMatrix33::arr[i] (PapyrusNiOverride.cpp:1190-1193) — el mismo arr[i] que skee
 empaqueta después bajo la key 32 índice i, que es exactamente lo que guarda un .jslot. O sea que le
 devolvemos SU PROPIA secuencia de floats y no hay ninguna convención euler de por medio.}
int[]    Property NodeScaleMode_G0000010000 Auto
{-1 = no tocar. 0 mult / 1 avg / 2 add / 3 max (NiTransformInterface.cpp:682-707).}

string[] Property NodeNeutralNode_G0000010000 Auto
string[] Property NodeNeutralName_G0000010000 Auto
{⭐⭐ PARES PLANOS (nodo, nombre-de-capa) que hay que NEUTRALIZAR con identidad completa.

 ⛔ SON PARALELAS ENTRE SI, **NO** con NodeName: el indice i es "el par i", no "el nodo i". Papyrus no tiene
 arrays irregulares y un hueso puede traer varios nombres, asi que la unica forma es una lista de pares.

 QUE PROBLEMA RESUELVEN. Nuestro aporte lleva el valor EFECTIVO del hueso: la app compuso los aportes que el
 preset traia (cada slider de RaceMenu/XPMSE escribe su propia key) en un solo numero. Si esos mismos aportes
 estan ADEMAS en el co-save del jugador, el motor compone los suyos con nuestro total y el hueso sale al doble.
 Y si estan es por una razon concreta: algun mod le aplico ESE preset a ESE NPC con
 `CharGen.LoadCharacterPresetEx` — la unica via de Papyrus para aplicar un .jslot a un actor cualquiera.

 COMO SE RESUELVE. Se les escribe IDENTIDAD COMPLETA bajo su propio nombre: escala 1, posicion (0,0,0) y
 rotacion identidad. Escribir la misma (nodo, nombre, key, index) REEMPLAZA — `Impl_AddNodeTransform` hace
 erase+insert y el set compara por (key,index) (OverrideVariant.h:19) — asi que el aporte queda inerte.

 ⛔ POR QUE IDENTIDAD **COMPLETA** y no "solo lo que el preset tenia": el preset dice que tenia EL, pero no
 sabemos que hay bajo ese nombre en el co-save DEL JUGADOR. Otra cosa pudo haber escrito una posicion ahi.
 La identidad completa neutraliza el nombre entero, sin suponer.

 ⛔ POR QUE NEUTRALIZAR Y NO BORRAR. Se toca EXACTAMENTE lo que nuestro valor ya representa. Hubo una version
 (`ClaimNode`) que enumeraba las keys del nodo y borraba TODA la ajena: se llevaba `internal` —el lift de los
 tacos altos, donde componer ES correcto— y el aporte de un mod que nunca vimos, sin vuelta atras. Los nombres
 que llegan aca los filtro la app (RaceMenuJslot.IsNeutralizableLayerName): nunca `internal`, nunca
 `NodeDestination`, y nunca uno terminado en .esp/.esm/.esl (skee los poda en cada carga del co-save).

 ⛔ NO SE ESCRIBE key 33 (el motor no lee el scaleMode por nodo: busca (33,-1) y todo se guarda en (33,0)) ni
 key 40 (NodeDestination no es un valor que se componga, es un re-parenteo, y su "neutro" seria otra orden).}

;-- body morphs (BodySlide) ---------------------------------------------------------------------
bool Property MorphsOwned_G0000010000 = false Auto
{⛔ QUIEN ES EL DUEÑO DE LOS BODY MORPHS DE ESTE NPC. false = los entrega el par BodyGen .ini y este
 script NO TOCA NADA de morphs (ni siquiera barre, ni repinta); true = los entrega este script.

 ⛔ DECIA ACA "en SSE nuestro barrido es por key propia, asi que borrar de mas no puede pasar". ES
 FALSO y lo contradecia el doc de MorphValue 20 lineas mas abajo: el barrido de morphs es ClearMorphs,
 PODA TOTAL del actor bajo cualquier key. Por eso el flag es lo unico que impide el colateral cuando el
 dueño es el .ini, y ademas: (a) el repintado (UpdateModelWeight) tampoco tiene por que dispararse si no
 somos el dueño, y (b) una sola ley en los dos juegos, que en FO4 SI es obligatoria (alla el slot es el
 mismo que usa BodyGen).

 ⚠️ Volver del modo script al modo .ini deja los morphs que este script ya aplico pegados al actor (con
 morphs en el mapa, skee no re-evalua BodyGen). No es una perdida: los dos modos sacan los valores del
 MISMO sidecar.}
string[] Property MorphName_G0000010000 Auto
{Nombre del morph de BodySlide (la key del .tri PIRT). Array paralelo con MorphValue.}
float[]  Property MorphValue_G0000010000 Auto
{Valor YA SUMADO de todas las contribuciones keyed de ese morph. Va bajo UNA sola key nuestra
 (XformKey()), no bajo las keys de BodySlide, por dos razones MEDIDAS:

 1) skee SUMA las keys de un mismo morph (Impl_GetBodyMorphs, BodyMorphInterface.cpp:220-240, con
    el default iBodyMorphMode=0), asi que la suma bajo una key rinde EXACTAMENTE lo mismo que las
    keys por separado. Es ademas lo que ya hace el emisor del .ini y lo que renderiza el preview
    de la app, asi que preview == juego.
 2) Con UNA sola key el payload es la unica fuente del cuerpo, que es lo que el barrido asume: antes de
    aplicar se hace ClearMorphs (poda total del actor) y se re-escribe todo. Es WYSIWYG por construccion.
    (Hubo una version que barria key por key para no tocar lo de otros mods; se abandono porque dejaba
    los NOMBRES de morph huerfanos acumulandose en el co-save para siempre. Ver ApplyBodyMorphs.)

 ⚠️ Con iBodyMorphMode 1 (promedio) o 2 (max) del skee64.ini del JUGADOR, keyed daria otro numero
 que nuestra suma. El preview de la app tambien suma, asi que la consistencia preview<->juego se
 mantiene igual; lo que cambia es el valor absoluto. Acotado y a proposito.}

;-- estado por instancia (persiste en el savegame, como el TeleportActorScript vanilla) ----------
int appliedVersion = -1

Event OnLoad()
    ; TRAZA: el script no logueaba NADA, y sin eso no se puede distinguir tres cosas que dan el MISMO
    ; sintoma ("el NPC no cambia"): (a) OnLoad no se dispara en esa referencia, (b) se dispara y se saltea
    ; por el sello, (c) se dispara pero con las propiedades CONGELADAS del savegame (o sea el payload viejo).
    ; MEDIDO: una copia por placeatme toma los cambios y la referencia que ya existia no cambia nada -- las
    ; tres explicaciones encajan con eso, asi que hay que verlas. Si aparece esta linea, (a) queda descartada;
    ; el valor de SchemaVersion_G0000010000 dice si el record fresco llego o no.
    if Verbose_G0000010000
        Debug.Trace("[NPCM] OnLoad ref=" + self.GetFormID() + " appliedVersion=" + appliedVersion + " SchemaVersion_G0000010000=" + SchemaVersion_G0000010000)
    endif

    ; TRAZA DEL PAYLOAD. Es la prueba end-to-end de la ley del sufijo: si estas lineas salen sobre una
    ; referencia que YA existia en la partida, el dato nuevo llego. Tocar los arrays aca es seguro
    ; porque el emisor GARANTIZA que toda array-property existe y trae >= 1 elemento (centinela), y esa
    ; garantia vale TAMBIEN para el spec de limpieza, que se arma con el builder normal y allowEmpty.
    ; Una lectura por traza: indexar el mismo array dos veces en una expresion imprime N veces el
    ; ULTIMO elemento (quirk del codegen de Papyrus).
    if Verbose_G0000010000
        ; **CUENTA REAL, SIN EL CENTINELA.** Un array vacio del VMAD viaja con UN elemento de string vacio
        ; (ver AddArray en el emisor: sin el, `Length` revienta en Papyrus). Trazar `.Length` crudo hacia que
        ; un preset SIN overlays dijera "payload ovl=1", y un preset SIN morphs dijera "morphs=1" seguido de
        ; "BM aplicados=0 de 1" — que se lee como una falla silenciosa y NO lo es.
        ; ⛔ Me lo cobre yo mismo: reporte esa linea como una anomalia a investigar cuando era mi propio trace
        ; contando el centinela. Un trace que miente cuesta lo mismo que un comentario que miente.
        Debug.Trace("[NPCM] payload ovl=" + RealLen(OvlNode_G0000010000) + " skin=" + RealLenInt(SkinSlot_G0000010000) + " nodes=" + RealLen(NodeName_G0000010000))
        Debug.Trace("[NPCM] BM payload morphs=" + RealLen(MorphName_G0000010000))
    endif

    ; ⛔ INSTANCIA HUERFANA: no soy la version activa de este actor, no toco NADA.
    ;
    ; El nombre del script lleva el nombre del ESP. Si el autor RENOMBRA su plugin publicado, el savegame
    ; del jugador se queda con la instancia del nombre anterior pegada al actor. Esa instancia ya no
    ; aparece en el VMAD, asi que no recibe ninguna property y sus arrays llegan con LONGITUD 0 (medido
    ; 2026-07-28: "payload ovl=0 skin=0 nodes=0"). Sin este guard correria igual, su sello no coincidiria,
    ; haria DONE y su RemovePrevious() barreria TODO -- borrandole al script activo lo que acaba de
    ; aplicar. Se vieron los dos OnLoad sobre la MISMA referencia en el mismo instante.
    ;
    ; Length == 0 es una senal SIN AMBIGUEDAD: el emisor garantiza que toda array-property llega con al
    ; menos 1 elemento (el centinela), tambien en el spec de LIMPIEZA. Un 0 solo puede venir de que el
    ; VMAD no nombra a este script.
    ;
    ; ⛔ Y NO SE BORRA EL .pex HUERFANO PARA "ARREGLARLO". Medido 2026-07-28: al borrarlo, el tipo deja de
    ; resolver y —como el script extends Actor— ese actor queda SIN TABLA DE METODOS PARA TODOS LOS DEMAS
    ; SCRIPTS. RaceMenuHHScaleEffect fallo 17 veces sobre FF000911 con "Method GetLeveledActorBase not
    ; found on NPCM_Manolov_ApplySSE" y "Cannot call GetSex() on a None object". El .pex viejo se queda
    ; donde esta; este guard lo vuelve inofensivo.
    if OvlNode_G0000010000.Length == 0
        if Verbose_G0000010000
            Debug.Trace("[NPCM] INERTE: sin payload del VMAD (instancia de un nombre de script viejo)")
        endif
        return
    endif
    if Verbose_G0000010000
        if OvlNode_G0000010000.Length > 0
            Debug.Trace("[NPCM] payload OvlNode_G0000010000[0]=" + OvlNode_G0000010000[0])
        endif
        if OvlDiffuse_G0000010000.Length > 0
            Debug.Trace("[NPCM] payload OvlDiffuse_G0000010000[0]=" + OvlDiffuse_G0000010000[0])
        endif
    endif

    if appliedVersion == SchemaVersion_G0000010000
        if Verbose_G0000010000
            Debug.Trace("[NPCM] SKIP: sello igual, no se limpia ni se aplica nada")
        endif
        return                    ; ya aplicado a ESTE actor, y nada cambió desde entonces
    endif
    appliedVersion = SchemaVersion_G0000010000

    ; ⭐⭐ EL REGISTRO EN SKEE VA PRIMERO, ANTES DE BORRAR. Con el default [Overlays] bPlayerOnly=1
    ; (verificado en skee64.ini) skee sólo construye nodos de overlay para un actor que HasOverlays(), y
    ; AddOverlays() es lo que mete al actor en ese set. Estaba DENTRO de ApplyOverlays(), o sea DESPUÉS de
    ; RemovePrevious() ⇒ los Remove* salían contra un actor todavía sin registrar y se perdían sin error:
    ; los overlays nuevos aparecían y los viejos no se iban nunca. Peor: si no quedaba nada que aplicar,
    ; ApplyOverlays() retorna en su primera línea (n == 0) y el registro no ocurría NUNCA, así que "borré
    ; todos los overlays" no borraba nada.
    ; Es idempotente y barato, así que va incondicional: el barrido tiene que poder correr aunque el payload
    ; nuevo esté vacío, que es justamente el caso de una limpieza.
    NiOverride.AddOverlays(self)

    RemovePrevious()
    ApplyOverlays()
    ApplySkin()
    ApplyNodeTransforms()
    ApplyBodyMorphs()

    ; ⭐ EL REPINTADO DE LOS MORPHS VA ACA, UNA SOLA VEZ Y SIN CONDICION.
    ; UpdateModelWeight recompone la malla DESDE CERO: MorphFileCache::ApplyMorph restaura el bloque de
    ; vertices desde el backup pristine "SHAPEDATA" y recien despues re-aplica lo que quedo en el store
    ; (BodyMorphInterface.cpp:522-535). O sea que aca SI hay deshacer de verdad, al reves que los
    ; overlays de nodo. Por eso tiene que correr TAMBIEN cuando el payload viene vacio: ese es
    ; justamente el caso de limpieza, donde lo unico que hay que hacer es volver al cuerpo base.
    ; Gateado por MorphsOwned: si los morphs los entrega el .ini, este script no repinta nada.
    if MorphsOwned_G0000010000
        NiOverride.UpdateModelWeight(self)
    endif
    if Verbose_G0000010000
        Debug.Trace("[NPCM] DONE ref=" + self.GetFormID())
    endif
EndEvent

; La "name" del transform es la KEY del override: namespacea NUESTRA capa, y RemoveNodeTransform* saca
; sólo la que se le nombra.
; Y eso vale para TODOS los huesos: RaceMenu, XPMSE y nosotros podemos tener un valor sobre el mismo hueso
; sin pisarnos. Llegué a romperlo con una función que borraba las capas ajenas de los huesos autorados; se
; revirtió, y el porqué está en ApplyNodeTransforms.
; ⚠️ El residuo que eso deja, dicho: nuestro valor es el TOTAL del hueso, así que si el actor tiene otro
; aporte ahí, el motor los compone y el juego muestra más de lo que muestra la app. Con `internal` (tacos
; altos) eso es CORRECTO. Con otro mod, es un conflicto de mods.
; (No puede ser una variable llamada `key`: `Key` es un tipo real de Skyrim — `Key
; extends MiscObject` — y Papyrus rechaza una variable con nombre de tipo conocido.)
string Function XformKey()
    return "NPCM_Manolov"
EndFunction

; NOTA: la key con la que el BodyGen de skee escribe SUS morphs es "RSMBodyGen"
; (SetMorph(actor, morphName, "RSMBodyGen", value) -- BodyMorphInterface.cpp:1825), y se confirmo in-game.
; Ya no hace falta nombrarla: ClearMorphs poda el actor entero, RSMBodyGen incluido.

; ============================================================================================
; DESHACER LO QUE APLICAMOS LA VEZ ANTERIOR.
;
; Todo entra con persist=true ⇒ va al store de skee, de ahí al co-save, y el motor lo re-aplica en
; cada carga. Si el usuario BORRA un overlay / skin / transform en la app y re-guarda, el set nuevo se
; aplica, pero el override VIEJO sigue ahí y nadie lo saca: queda pegado al actor para siempre.
;
; Se borra POR KEY EXACTA, nunca con RemoveAll* (que se llevarían puestas las capas de XPMSE o de
; cualquier otro mod sobre CUALQUIER hueso, incluidos los que este NPC no autora).
; Overlays y skin se barren por ENUMERACIÓN — los nodos de overlay son un
; conjunto conocido y contable ("Body [Ovl{n}]"…, skee OverlayInterface.h:33,38,43) y los slots de
; skin son 32 — así que no hace falta recordar nada.
;
; Los node transforms se barren POR ENUMERACION con GetNodeTransformNames — ver el bloque de abajo.
; (Antes se barrian solo los nodos del payload actual, y un nodo sacado del preset quedaba pegado.)
; ============================================================================================
Function RemovePrevious()
    ; --- overlays: los nodos son enumerables.
    ; Dos rangos por zona: 0..iNumOverlays-1 = los nodos que skee CREA (apagar + sacar del store), y de
    ; ahí hasta OVL_SWEEP_MAX = los que la app pudo autorar de más y skee nunca crea (sólo sacar del
    ; store, que es lo único que existe de ellos). Ver la cabecera de PurgeOverlayGroup.
    int nBody = NiOverride.GetNumBodyOverlays()
    ClearOverlayGroup("Body [Ovl", nBody)
    PurgeOverlayGroup("Body [Ovl", nBody, OVL_SWEEP_MAX)
    int nBodySp = NiOverride.GetNumSpellBodyOverlays()
    ClearOverlayGroup("Body [SOvl", nBodySp)
    PurgeOverlayGroup("Body [SOvl", nBodySp, OVL_SWEEP_MAX)
    int nHands = NiOverride.GetNumHandOverlays()
    ClearOverlayGroup("Hands [Ovl", nHands)
    PurgeOverlayGroup("Hands [Ovl", nHands, OVL_SWEEP_MAX)
    int nHandsSp = NiOverride.GetNumSpellHandOverlays()
    ClearOverlayGroup("Hands [SOvl", nHandsSp)
    PurgeOverlayGroup("Hands [SOvl", nHandsSp, OVL_SWEEP_MAX)
    int nFeet = NiOverride.GetNumFeetOverlays()
    ClearOverlayGroup("Feet [Ovl", nFeet)
    PurgeOverlayGroup("Feet [Ovl", nFeet, OVL_SWEEP_MAX)
    int nFeetSp = NiOverride.GetNumSpellFeetOverlays()
    ClearOverlayGroup("Feet [SOvl", nFeetSp)
    PurgeOverlayGroup("Feet [SOvl", nFeetSp, OVL_SWEEP_MAX)
    ; FACE: decia "NUNCA Face: la cara es del bake" y era cierto mientras el emisor jamas mandaba un
    ; nodo Face. Ya no: el bake de overlays de cara esta gateado por Setting_BakeSseRaceMenuOverlays y,
    ; con ese toggle APAGADO, el emisor SI manda los nodos Face (si no, no los aplicaba nadie y el
    ; overlay desaparecia). Sin este barrido, cambiar el toggle de OFF a ON dejaba el override viejo
    ; PEGADO al actor -- todo entra con persist=true, o sea al co-save -- y el overlay quedaba aplicado
    ; DOS VECES: el que sigue vivo en el co-save mas el que ahora esta horneado en la textura.
    ; Barrer es incondicional a proposito: es idempotente (RemoveNodeOverride de una key que no existe
    ; es no-op) y NO puede depender del toggle, porque el toggle de HOY no dice que se aplico AYER.
    ; Las dos familias de skee: FACE_NODE "Face [Ovl{}]" y FACE_NODE_SPELL "Face [SOvl{}]"
    ; (OverlayInterface.h:23-24).
    ; ⛔ CORRECCION 2: la version anterior barria "Face [SOvl" con el contador de [Ovl] y lo justificaba con
    ; "nosotros no autoramos [SOvl] nunca". ESO YA NO ES CIERTO: el pool magic es autorable desde los editores
    ; (es el unico dueño de un face-paint magico, que el bake NO pliega por diseño), asi que el barrido tiene
    ; que cubrirlo de verdad y en las CUATRO zonas.
    ; ⛔⛔ CORRECCION 3, Y ERA UN ERROR MIO DE HECHO: este bloque decia "EL CONTADOR DEL POOL MAGIC NO SE
    ; PUEDE PREGUNTAR ... Papyrus no expone getter", y lo repetia en otros dos lugares del archivo. ES FALSO.
    ; NiOverride.GetNumSpellBodyOverlays / GetNumSpellHandOverlays / GetNumSpellFeetOverlays /
    ; GetNumSpellFaceOverlays ESTAN registradas (PapyrusNiOverride.cpp:1844-1853) y ademas flaggeadas NoWait
    ; (:2422-2425), o sea baratas. El pool normal ya las usaba dos lineas mas arriba; el magic usaba una
    ; constante inventada (OVL_SPELL_CLEAR_MAX = 8) apoyada en esa afirmacion falsa.
    ;
    ; ⇒ CONSECUENCIAS DE ARREGLARLO, las tres:
    ;   1) el barrido cubre EXACTAMENTE los nodos que el juego del jugador realmente creo, ni uno mas.
;      OJO CON LA PALABRA "apagado": lo que queda garantizado es el STORE (el override se va y no vuelve al
;      recargar el 3D). El apagado VISUAL inmediato NO esta medido y probablemente no ocurra: la plantilla magic
;      trae un BSEffectShaderPropertyFloatController sobre la Alpha (flags 0x4A = ACTIVE + CYCLE_REVERSE), asi que
;      dentro de una partida en curso la animacion del efecto pisa el KEY_ALPHA=0 con el que se apaga. Decir
;      "apagado visual exacto" era afirmar mas de lo que se midio.
    ;      Antes hacia 8 por zona, de los cuales ~7 caian sobre nodos inexistentes (cada uno un recorrido del
    ;      arbol del NIF por AddNodeOverride*).
    ;   2) el emisor DEJA DE DESCARTAR overlays magic. Descartaba todo indice >= 8 para que no quedara un
    ;      override que este barrido no pudiera apagar. Con el contador real, todo lo que el jugador puede ver
    ;      se puede apagar, asi que no hay nada que descartar: la app deja de perder en silencio un overlay
    ;      que el usuario autoro.
    ;   3) se va la constante gemela en tres artefactos (.psc/.pex/VB) y su gate, que protegia un numero que
    ;      no tenia que existir.
    ; El barrido del STORE sigue llegando al tope del motor (OVL_SWEEP_MAX): un .jslot importado puede traer un
    ; [SOvl40] que igual hay que poder sacar del co-save, exista o no el nodo.
    int nFace = NiOverride.GetNumFaceOverlays()
    ClearOverlayGroup("Face [Ovl", nFace)
    PurgeOverlayGroup("Face [Ovl", nFace, OVL_SWEEP_MAX)
    int nFaceSp = NiOverride.GetNumSpellFaceOverlays()
    ClearOverlayGroup("Face [SOvl", nFaceSp)
    PurgeOverlayGroup("Face [SOvl", nFaceSp, OVL_SWEEP_MAX)
    NiOverride.ApplyNodeOverrides(self)

    ; --- skin: los 32 slots biped. mask arranca en 1 y se duplica; en el bit 31 "desborda" a
    ; negativo, que es EXACTAMENTE el patrón de bits del último slot (Papyrus no chequea overflow:
    ; envuelve, a diferencia de VB).
    int mask = 1
    int b = 0
    while b < 32
        NiOverride.RemoveSkinOverride(self, IsFemale_G0000010000, false, mask, KEY_TEXTURE, IDX_DIFFUSE)
        NiOverride.RemoveSkinOverride(self, IsFemale_G0000010000, false, mask, KEY_TEXTURE, IDX_NORMAL)
        NiOverride.RemoveSkinOverride(self, IsFemale_G0000010000, false, mask, KEY_TINT, -1)
        mask = mask * 2
        b += 1
    endwhile
    NiOverride.ApplySkinOverrides(self)

    ; --- node transforms: POR ENUMERACION, no por el payload actual.
    ;
    ; ⭐ ESTO ARREGLA UN RESIDUAL VIEJO, y de paso RETRACTA lo que decia el comentario anterior. Decia
    ; que GetNodeTransformNames "devuelve None cuando el actor no tiene ninguno" y que por eso habia que
    ; barrer solo los nodos del payload — dejando pegado el transform de un nodo que se sacaba del
    ; preset. LEIDO EN LA FUENTE (PapyrusNiOverride.cpp:1381-1394): la native construye un
    ; VMResultArray LOCAL y lo devuelve; NO hay ningun camino que devuelva null. Devuelve VACIO.
    ; El "Cannot cast from None to String[]" que se le atribuyo venia de una PROPERTY que llegaba None
    ; (regla 2 de la cabecera), no del retorno de esta native.
    ;
    ; ⚠️ ESTE barrido no tiene colateral: se saca SOLO la key NUESTRA de cada nodo, asi que en un nodo
    ; que NO autoramos, XPMSE / RaceMenu / cualquier otro conservan las suyas. Quitar una key que no
    ; existe es no-op, asi que enumerar todos los nodos es seguro.
    ;
    ; ⭐⭐ skee COMPONE EN DOS ORDENES DISTINTOS, y esto no estaba escrito en ningun lado:
    ;   * SetTransforms (el de la CARGA del actor, :680):        combined = combined * local
    ;   * Impl_UpdateNodeTransforms (el que dispara ESTE script): transformResult = local * transformResult  (:466)
    ;   `NiTransform::operator*` NO conmuta, asi que para un nodo con VARIAS capas los dos caminos dan
    ;   resultados distintos — skee es inconsistente consigo misma. Y el de :466 ademas NO MIRA el scaleMode:
    ;   usa el producto crudo de las escalas, mientras el de :680 aplica el iScaleMode del jugador.
    ;   ⇒ Con UNA sola capa los dos coinciden (local * identidad == identidad * local), asi que el desacuerdo
    ;   solo aparece en un nodo con VARIAS capas — el caso que justamente NO forzamos. Pero no se puede decir
    ;   "el motor compone asi" sin decir cual de los dos caminos.
    ;
    ; ⭐ Y ACA SI HAY "DESHACER" DE VERDAD: Impl_UpdateNodeTransforms (NiTransformInterface.cpp:454-497)
    ; RECOMPONE el transform desde cero — arranca de la transform base del nodo e itera las keys que
    ; QUEDAN. Si no queda ninguna, el nodo vuelve a su base. Es lo contrario de los overlays, donde
    ; ApplyNodeOverrides solo empuja lo que quedo y nunca resetea.
    string ovrKey = XformKey()
    string[] xnodes = NiOverride.GetNodeTransformNames(self, false, IsFemale_G0000010000)
    if Verbose_G0000010000
        Debug.Trace("[NPCM] xforms previos=" + xnodes.Length)
    endif
    int i = 0
    while i < xnodes.Length
        string node = xnodes[i]
        if node != ""
            NiOverride.RemoveNodeTransformScale(self, false, IsFemale_G0000010000, node, ovrKey)
            NiOverride.RemoveNodeTransformPosition(self, false, IsFemale_G0000010000, node, ovrKey)
            NiOverride.RemoveNodeTransformRotation(self, false, IsFemale_G0000010000, node, ovrKey)
            NiOverride.RemoveNodeTransformScaleMode(self, false, IsFemale_G0000010000, node, ovrKey)
            NiOverride.UpdateNodeTransform(self, false, IsFemale_G0000010000, node)
        endif
        i += 1
    endwhile

    ; --- body morphs: SONDA DE ORDEN + barrido de NUESTRA key.
    ;
    ; ⭐⭐ LA SONDA ES EL PUNTO. skee corre BodyGen en TESObjectLoadedEvent con el gate
    ; `!HasMorphs(reference)` (ActorUpdateManager.cpp:38-40) y nosotros corremos en OnLoad; el orden
    ; entre los dos NO esta garantizado. Estas tres lineas lo MIDEN en vez de suponerlo:
    ;   * morphs previos = 0            -> corrimos primero (o nadie aplico nada)
    ;   * key[0] = "RSMBodyGen"         -> BodyGen corrio ANTES; si ademas quedo un .ini instalado de
    ;                                      una version anterior, su valor se SUMARIA al nuestro
    ;   * key[0] = "NPCM_Manolov"       -> somos nosotros de una carga anterior (lo normal al re-aplicar)
    ;   * cualquier otra key            -> otro mod le puso morphs a este actor
    ; GetMorphNames/GetMorphKeys NUNCA devuelven None: construyen un VMResultArray LOCAL y lo devuelven
    ; vacio (PapyrusNiOverride.cpp:1416-1463). Es el mismo caso que GetNodeTransformNames, sobre el que
    ; ya se retracto una vez la creencia contraria.
    if !MorphsOwned_G0000010000
        if Verbose_G0000010000
            Debug.Trace("[NPCM] BM SKIP barrido: los morphs los entrega el BodyGen .ini (MorphsOwned=false)")
        endif
        return
    endif

    ; ⭐ BLOQUE DE SONDA COMPLETO bajo Verbose: GetMorphNames y GetMorphKeys se llaman SOLO para trazar.
    ; Son nativas de verdad; gatearlas es el ahorro que importa, no el Debug.Trace.
    if Verbose_G0000010000
        string[] pre = NiOverride.GetMorphNames(self)
        Debug.Trace("[NPCM] BM morphs previos=" + pre.Length)
        if pre.Length > 0
            string pname = pre[0]
            Debug.Trace("[NPCM] BM morph previo[0]=" + pname)
            string[] pkeys = NiOverride.GetMorphKeys(self, pname)
            Debug.Trace("[NPCM] BM morph previo[0] keys=" + pkeys.Length)
            if pkeys.Length > 0
                string pk = pkeys[0]
                Debug.Trace("[NPCM] BM morph previo[0] key[0]=" + pk)
            endif
        endif
    endif

    ; ⭐⭐⭐ PODA TOTAL DEL ACTOR — Impl_ClearMorphs (BodyMorphInterface.cpp:312-320) hace
    ;     actorMorphs.m_data.erase(actor->formID)
    ; o sea BORRA LA ENTRADA ENTERA del actor. En SSE el store NO tiene dimension de genero (la clave es
    ; solo el formID), asi que esto se lleva TODO lo que ese actor tenia. Comparar con FO4, donde el mapa
    ; SI es por genero y el clear solo alcanza al genero que se le pasa: no son equivalentes.
    ;
    ; POR QUE PODA TOTAL Y NO POR KEY. Antes se barria key por key (la nuestra + "RSMBodyGen"). Funcionaba,
    ; pero Impl_ClearBodyMorphKeys borra la KEY y DEJA el NOMBRE del morph con el mapa vacio, y en SSE nada
    ; poda un nombre vacio: se serializan al co-save para siempre y se acumulan cada vez que el usuario
    ; cambia a un preset con otros sliders. MEDIDO: "morphs tras barrido=39" con "keys=0" en todos.
    ;
    ; ⚠️ SE LLEVA LOS MORPHS QUE OTRO MOD LE HAYA PUESTO A ESTE ACTOR, bajo cualquier key. Es la MISMA
    ; decision de producto que Overlays.RemoveAll y que el barrido de RSMBodyGen: el NPC muestra
    ; EXACTAMENTE lo que muestra la app. Si nuestro replacer es el dueño del cuerpo de este NPC, un morph
    ; ajeno sobre el mismo actor ya era un conflicto, no un aporte.
    ;
    ; ⚠️ SOLO toca body morphs: el sculpt y los morphs de CARA viven en FaceMorphInterface, otro store.
    ;
    ; Corre INCONDICIONAL (con payload vacio tambien): ese es justo el caso de limpieza, donde lo correcto
    ; es dejar el cuerpo base. El repintado lo hace UpdateModelWeight al final del OnLoad.
    NiOverride.ClearMorphs(self)
    if Verbose_G0000010000
        Debug.Trace("[NPCM] BM ClearMorphs (poda total del actor) hecho")
    endif

    ; Sonda de control. AHORA "morphs tras barrido" SI ES LA MEDIDA y tiene que dar 0: con la poda total se
    ; borra la entrada del actor, nombres incluidos.
    ; (Con el barrido por key anterior daba 39 con "keys=0" en todos, porque borraba la key y dejaba el
    ;  nombre. Ese numero me hizo dudar una vez; ya no aplica.)
    if Verbose_G0000010000
        string[] post = NiOverride.GetMorphNames(self)
        Debug.Trace("[NPCM] BM morphs tras barrido=" + post.Length)
        if post.Length > 0
            string qname = post[0]
            string[] qkeys = NiOverride.GetMorphKeys(self, qname)
            Debug.Trace("[NPCM] BM tras barrido " + qname + " keys=" + qkeys.Length)
        endif
    endif
EndFunction

; ⛔⛔ BORRAR = APAGAR EL NODO. skee NO tiene "deshacer", y esto costó una tarde de "aplica lo nuevo
; pero no borra lo viejo". Los tres caminos, leídos en la fuente de skee64:
;
;   AddNodeOverride*     -> guarda en el store (si persist) Y **pinta el nodo en el acto**
;                           (Impl_SetNodeProperty, PapyrusNiOverride.cpp:273-306)
;   RemoveNodeOverride   -> **sólo borra la entrada del map. No toca el nodo.**
;                           (OverrideInterface.cpp:333-351)
;   ApplyNodeOverrides   -> sólo empuja lo que QUEDÓ en el store; nunca resetea un nodo cuyo override
;                           se borró (Impl_SetNodeProperties, OverrideInterface.cpp:777)
;
; Y las DOS puertas que parecían la salida están cerradas, verificado:
;   * RevertOverlays / RevertOverlay llegan a Papyrus con **resetDiffuse = FALSE**
;     (PapyrusNiOverride.cpp:103-115) — restauran el normal y DEJAN el tatuaje. RaceMenu los llama
;     internamente con `true`; a los scripts les expusieron la variante capada.
;   * OverlayInterface::GetDefaultTexture() existe en el C++ (OverlayInterface.h:212) pero **NO está
;     registrada como native**, así que no se le puede preguntar cuál es su textura por defecto.
;
; ⇒ Se apaga el nodo con KEY_ALPHA = 0. ES EL ÚNICO MECANISMO QUE NO DEPENDE DE CONFIGURACIÓN QUE NO
;   PODEMOS VER. Repintar el diffuse con el default de skee sería "más limpio" en teoría, pero ese path
;   sale de `skee64.ini [Overlays/Data] sDefaultTexture` (main.cpp:789-793) — el del JUGADOR, no el del
;   autor: si lo cambió, el borrado no limpiaría nada y no habría forma de saberlo. El alpha funciona en
;   toda instalación. Y no queda estado sucio: el store se limpia igual con los Remove* de abajo, así que
;   el próximo attach del 3D reconstruye el nodo desde cero.
;
; persist = FALSE: pinta el nodo pero NO deja entrada en el store (PapyrusNiOverride.cpp:503-514). Los
; Remove* siguen siendo necesarios: sacan lo que YA estaba guardado, que es lo que skee re-aplicaría en
; cada carga (todo entró con persist=true).
;
; ⚠️ ESTO OBLIGA a que ApplyOverlays aplique el alpha SIEMPRE — ver el comentario allá. No es opcional.
;
; ⚠️ RESIDUAL: el normal (IDX_NORMAL) no se toca. skee lo restaura copiándolo de la geometría de la piel
; y eso no se puede hacer desde Papyrus. Un overlay viejo con normal propio lo deja hasta el próximo
; rebuild del 3D — pero con alpha 0 el nodo no se ve, así que es inerte.
;
; ⚠️ BARRE TODOS LOS NODOS DE OVERLAY, sean nuestros o de otro mod. DECISIÓN DE PRODUCTO tomada a
; propósito: el NPC muestra exactamente lo que muestra la app. skee no guarda dueño (su store es
; actor+nodo+key+index), así que distinguir no es posible sin un ledger.
;
; Recibe un String y un Int — NO un array (regla 1 de la cabecera).
Function ClearOverlayGroup(string prefix, int n)
    int i = 0
    while i < n
        string node = prefix + i + "]"
        ; 1) apagar el nodo (visual, sin persistir)
        NiOverride.AddNodeOverrideFloat(self, IsFemale_G0000010000, node, KEY_ALPHA, -1, 0.0, false)
        NiOverride.AddNodeOverrideInt(self, IsFemale_G0000010000, node, KEY_TINT, -1, 0, false)
        ; 2) sacar del store lo que hubiera guardado
        NiOverride.RemoveNodeOverride(self, IsFemale_G0000010000, node, KEY_TEXTURE, IDX_DIFFUSE)
        NiOverride.RemoveNodeOverride(self, IsFemale_G0000010000, node, KEY_TEXTURE, IDX_NORMAL)
        NiOverride.RemoveNodeOverride(self, IsFemale_G0000010000, node, KEY_TINT, -1)
        NiOverride.RemoveNodeOverride(self, IsFemale_G0000010000, node, KEY_ALPHA, -1)
        i += 1
    endwhile
EndFunction

;-- ⛔⛔ EL BARRIDO DE ARRIBA NO ALCANZA LO QUE LA APP PUEDE AUTORAR ------------------------------
;
; ClearOverlayGroup enumera 0..iNumOverlays-1 porque ESOS son los nodos que skee CREA. Pero la app
; deja autorar un overlay MÁS ALLÁ de ese número (avisa y lo agrega igual): el override entra al
; store con persist=true — o sea al co-save — aunque el nodo no exista, porque Impl_AddNodeOverride
; guarda SIN mirar si el nodo está (OverrideInterface.cpp:56-63). Sin este segundo barrido ese
; override quedaba PARA SIEMPRE: borrarlo en la app no lo sacaba de la partida del jugador, y el día
; que el jugador subiera iNumOverlays reaparecía — encima de lo que hoy está horneado, en el caso de
; la cara. Es exactamente el agujero que el barrido de Face vino a cerrar, un rango más arriba.
;
; ⭐ POR QUÉ ES OTRA FUNCIÓN Y NO UN `n` MÁS GRANDE: en este rango el nodo NO EXISTE, así que
;   1) apagarlo visualmente no tiene sentido, y
;   2) AddNodeOverride* cuesta un GetObjectByName sobre el 3D del actor (OverrideInterface.cpp:750-763)
;      — un recorrido del árbol entero por llamada, para no encontrar nada.
;   Sólo se sacan las entradas del store, que son 4 lookups de mapa guardados (Impl_RemoveNodeOverride
;   chequea actor, nodo y variante, y no inserta nada si falta cualquiera: :333-353). Barato y sin
;   efectos sobre lo que se ve.
;
; ⚠️⚠️ EL COSTO ESTÁ EN EL RE-APPLY, NO EN EL PRIMER SPAWN — y es lo que se aceptó a sabiendas al elegir
;   el techo del motor. CIFRA ACTUALIZADA (el pool magic la DUPLICÓ; la vieja decía ~1970 y quedó a la mitad
;   de la realidad). Con el ini shipeado (6/3/3/3) y por actor, UNA VEZ (RemovePrevious corre detrás del
;   gate appliedVersion == SchemaVersion):
;     * purge pool NORMAL:  (127-6) + 3x(127-3) = 493 iteraciones x 4 removes = 1972
;     * purge pool MAGIC:   4 zonas x (127-8)   = 476 iteraciones x 4 removes = 1904
;     ⇒ ~3876 nativas de store (las BARATAS: RemoveNodeOverride muere en el primer find si no existe).
;   Y ClearOverlayGroup, que por índice hace 2 AddNodeOverride* (las CARAS: GetObjectByName) + 4 removes:
;     * caras:   normal 2x(6+3+3+3) = 30  +  magic 2x(8+8+8+8) = 64   ⇒ 94 recorridos de árbol
;     * removes: (15 + 32) x 4 = 188 nativas de store más
;   ⇒ TOTAL ~4158 por actor. (Este desglose omitía los 188 mientras el README daba el total completo: dos
;    cuentas del mismo número que no coincidían.)
;   ⭐ CIFRA CORREGIDA: el magic ya NO hace 8 por zona. Usa GetNumSpell*Overlays(), así que con el default
;   del motor (iSpellOverlays=1) son 2x(1+1+1+1) = 8 recorridos de árbol en vez de 64. Los 56 que caían sobre
;   nodos inexistentes eran el precio de una afirmación falsa ("Papyrus no expone getter"), no del diseño.
;   Las tres nativas que usa están
;   registradas NoWait desde el C++ (PapyrusNiOverride.cpp:2417,2454,2531 —- OJO: acá NoWait NO es un
;   keyword del .psc como en F4SE, lo setea el plugin con SetFunctionFlags; no "corregir" esto borrándolo).
;
;   ⛔ PERO NoWait NO ABARATA EL TRABAJO EN C++, sólo evita que la VM lata. Y ahí las dos pasadas cuestan
;   MUY distinto:
;     * PRIMER SPAWN: el actor no tiene entrada en el store ⇒ cada llamada muere en el primer find
;       (OverrideInterface.cpp:337) sin internar nada. Gratis.
;     * RE-APPLY (el actor ya trae overrides de una versión anterior): la llamada llega a
;       g_stringTable.GetString(nodeName) con un nombre que NO está en la tabla ⇒ lo internea, el temporal
;       muere, y DeleteStringEntry dispara RemoveString, que es un SCAN LINEAL de m_tableVector con un
;       weak_ptr::lock() atómico por elemento, bajo el lock global (StringTable.cpp:29-62). N = todos los
;       strings vivos del co-save DEL JUGADOR (nombres de nodo, rutas de textura, morphs de BodySlide):
;       2000-5000 en un save modeado normal ⇒ del orden de 40-100 ms por NPC actualizado, en la primera
;       carga después de publicar.
;   Es el único costo de toda la feature que escala con el save del jugador y no con nuestro payload. Si
;   algún día molesta, la salida NO es bajar el techo (reabre el agujero): es que el emisor mande el índice
;   más alto que este mod haya autorado y barrer max(contador del jugador, ese+1).
;
; ⚠️ SÍ se extiende al pool MAGIC ("<zona> [SOvl") en las cuatro zonas. Decía que NO, con el argumento
;   "la app nunca los autora": eso caducó — el pool magic es autorable y este script es su único dueño
;   (el fold no lo pliega). Y el argumento del colateral no aplica al caso: acá se está autorando ESTE
;   NPC, así que sus capas magic son nuestras; dejarlas pegadas sería el bug, no el respeto.
;   El apagado visual del pool magic va hasta GetNumSpell*Overlays() — el contador REAL del juego del
;   jugador, igual que el pool normal — y el barrido del store hasta el mismo tope del motor.
;
; Recibe un String y dos Int — NO un array (regla 1 de la cabecera).
Function PurgeOverlayGroup(string prefix, int from, int to)
    int i = from
    while i < to
        string node = prefix + i + "]"
        NiOverride.RemoveNodeOverride(self, IsFemale_G0000010000, node, KEY_TEXTURE, IDX_DIFFUSE)
        NiOverride.RemoveNodeOverride(self, IsFemale_G0000010000, node, KEY_TEXTURE, IDX_NORMAL)
        NiOverride.RemoveNodeOverride(self, IsFemale_G0000010000, node, KEY_TINT, -1)
        NiOverride.RemoveNodeOverride(self, IsFemale_G0000010000, node, KEY_ALPHA, -1)
        i += 1
    endwhile
EndFunction


Function ApplyOverlays()
    int n = OvlNode_G0000010000.Length
    if n == 0
        return
    endif

    ; El registro en skee (NiOverride.AddOverlays) ya NO va acá: se MOVIÓ al principio de OnLoad().
    ; Motivo: es igual de obligatorio para BORRAR que para aplicar, y acá corría DESPUÉS de
    ; RemovePrevious() — con el default [Overlays] bPlayerOnly=true, OverlayInterface::OnAttach sólo
    ; construye los nodos de overlay para un actor que HasOverlays() (OverlayInterface.cpp:854 mete el
    ; formID; el gate de :1011 pasa), así que los Remove* caían sobre nodos que nunca se crearon: sin
    ; error, invisible. Y con n == 0 esta función retorna arriba, así que el registro no ocurría NUNCA
    ; justo en el caso de limpieza. Un solo dueño, y es OnLoad.

    int i = 0
    while i < n
        string node = OvlNode_G0000010000[i]
        if node != ""
            ; persist = TRUE siempre. Con persist=false skee lo aplica visualmente pero NO lo mete en
            ; el store ⇒ no se serializa al co-save y desaparece en la próxima carga
            ; (PapyrusNiOverride.cpp:503-514).
            if i < OvlDiffuse_G0000010000.Length
                if OvlDiffuse_G0000010000[i] != ""
                    NiOverride.AddNodeOverrideString(self, IsFemale_G0000010000, node, KEY_TEXTURE, IDX_DIFFUSE, OvlDiffuse_G0000010000[i], true)
                endif
            endif
            if i < OvlNormal_G0000010000.Length
                if OvlNormal_G0000010000[i] != ""
                    NiOverride.AddNodeOverrideString(self, IsFemale_G0000010000, node, KEY_TEXTURE, IDX_NORMAL, OvlNormal_G0000010000[i], true)
                endif
            endif
            if i < OvlHasTint_G0000010000.Length
                if OvlHasTint_G0000010000[i]
                    if i < OvlTint_G0000010000.Length
                        NiOverride.AddNodeOverrideInt(self, IsFemale_G0000010000, node, KEY_TINT, -1, OvlTint_G0000010000[i], true)
                    endif
                endif
            endif
            ; ⚠️ EL ALPHA SE APLICA SIEMPRE, no sólo cuando el overlay trae uno propio. NO es opcional: es el
            ; complemento obligatorio del barrido, que deja TODOS los nodos en alpha 0. Si acá se gateara por
            ; OvlHasAlpha_G0000010000, un nodo que recibe textura nueva sin alpha explícito se quedaría con ese 0 e
            ; INVISIBLE. El emisor ya manda 1.0 cuando el overlay no define alpha, así que el valor es válido
            ; siempre. (OvlHasAlpha_G0000010000 sigue declarada y emitida —la garantía 1:1 con el .psc— sin decidir nada.)
            if i < OvlAlpha_G0000010000.Length
                NiOverride.AddNodeOverrideFloat(self, IsFemale_G0000010000, node, KEY_ALPHA, -1, OvlAlpha_G0000010000[i], true)
            endif
        endif
        i += 1
    endwhile

    NiOverride.ApplyNodeOverrides(self)
EndFunction

Function ApplySkin()
    int n = SkinSlot_G0000010000.Length
    if n == 0
        return
    endif

    int i = 0
    while i < n
        int slot = SkinSlot_G0000010000[i]
        if slot != 0
            ; firstPerson = false: los NPC no tienen esqueleto de primera persona.
            if i < SkinDiffuse_G0000010000.Length
                if SkinDiffuse_G0000010000[i] != ""
                    NiOverride.AddSkinOverrideString(self, IsFemale_G0000010000, false, slot, KEY_TEXTURE, IDX_DIFFUSE, SkinDiffuse_G0000010000[i], true)
                endif
            endif
            if i < SkinNormal_G0000010000.Length
                if SkinNormal_G0000010000[i] != ""
                    NiOverride.AddSkinOverrideString(self, IsFemale_G0000010000, false, slot, KEY_TEXTURE, IDX_NORMAL, SkinNormal_G0000010000[i], true)
                endif
            endif
            if i < SkinHasTint_G0000010000.Length
                if SkinHasTint_G0000010000[i]
                    if i < SkinTint_G0000010000.Length
                        NiOverride.AddSkinOverrideInt(self, IsFemale_G0000010000, false, slot, KEY_TINT, -1, SkinTint_G0000010000[i], true)
                    endif
                endif
            endif
        endif
        i += 1
    endwhile

    NiOverride.ApplySkinOverrides(self)
EndFunction

; ============================================================================================
; NEUTRALIZAR LAS CAPAS QUE LA APP YA COLAPSO — ver el doc de NodeNeutralName_G<n>.
;
; Corre ANTES de escribir lo nuestro, aunque el orden con el store da igual (son keys distintas). Lo que si
; importa es el UpdateNodeTransform: recompone el nodo desde su base con las keys que quedan, asi que tiene que
; correr DESPUES de escribir. Se llama aca por nodo (idempotente: la llamada del loop de abajo no molesta) para
; cubrir el caso de un nodo que tiene nombres a neutralizar y ningun TRS nuestro.
; ============================================================================================
; Cuantos elementos REALES tiene una array de strings del VMAD: 0 cuando lo unico que hay es el centinela
; (un solo elemento vacio). Ver AddArray en NpcApplyScriptEmitter: una array vacia NO se puede emitir vacia
; porque `.Length` sobre una property sin valor revienta, asi que viaja con un elemento vacio.
; Igual, para las arrays de ENTEROS: ahi el centinela es 0, no "" (ver AddArray, sobrecarga de Integer).
; Que el 0 sea el centinela y no un dato lo confirma el propio loop de skin overrides: `if slot != 0`.
int Function RealLenInt(int[] a) global
    if a.Length == 1 && a[0] == 0
        return 0
    endif
    return a.Length
EndFunction

int Function RealLen(string[] a) global
    if a.Length == 1 && a[0] == ""
        return 0
    endif
    return a.Length
EndFunction

Function NeutralizeCollapsedLayers()
    int m = NodeNeutralNode_G0000010000.Length
    if m == 0
        return
    endif
    ; Guard de arrays desparejas: si el payload llega recortado, se hace lo que se pueda en vez de reventar.
    if NodeNeutralName_G0000010000.Length < m
        m = NodeNeutralName_G0000010000.Length
    endif

    float[] zero = new float[3]
    float[] ident = new float[9]
    ident[0] = 1.0
    ident[4] = 1.0
    ident[8] = 1.0

    int i = 0
    while i < m
        string node = NodeNeutralNode_G0000010000[i]
        string other = NodeNeutralName_G0000010000[i]
        if node != "" && other != "" && other != XformKey()
            NiOverride.AddNodeTransformScale(self, false, IsFemale_G0000010000, node, other, 1.0)
            NiOverride.AddNodeTransformPosition(self, false, IsFemale_G0000010000, node, other, zero)
            NiOverride.AddNodeTransformRotation(self, false, IsFemale_G0000010000, node, other, ident)
            NiOverride.UpdateNodeTransform(self, false, IsFemale_G0000010000, node)
            if Verbose_G0000010000
                ; **SE LEE DE VUELTA**, no se afirma. Escribir y trazar "lo escribi" no prueba nada: prueba que se
                ; llamo a la funcion. `GetNodeTransformScale` esta expuesto a Papyrus y es NoWait
                ; (PapyrusNiOverride.cpp:1110 / :2312), asi que se puede preguntar QUE QUEDO bajo ese nombre.
                ; Nacio de una limitacion real del test: el usuario no tenia forma de saber si el hueso habia
                ; quedado en 1.32 o en 1.74 mirando el NPC. Un numero en el log lo contesta; un pecho no.
                float back = NiOverride.GetNodeTransformScale(self, false, IsFemale_G0000010000, node, other)
                Debug.Trace("[NPCM] xform NEUTRAL " + node + ": '" + other + "' -> leido de vuelta scale=" + back +                             " (tiene que ser 1.0; su aporte ya esta en el nuestro)")
            endif
        endif
        i += 1
    endwhile
EndFunction

Function ApplyNodeTransforms()
    ; ⭐ PRIMERO se neutralizan los nombres que la app colapso. Va aca y no en RemovePrevious porque no es un
    ; barrido de lo anterior: es parte de ESCRIBIR este payload — sin esto, nuestro total se sumaria al aporte
    ; que el mismo preset pudo haber dejado en el co-save del jugador.
    NeutralizeCollapsedLayers()

    int n = NodeName_G0000010000.Length
    if n == 0
        return
    endif

    string ovrKey = XformKey()
    int i = 0
    while i < n
        string node = NodeName_G0000010000[i]
        if node != ""

            ; ⛔⛔ ACA SE LLAMABA A ClaimNode(), QUE BORRABA LAS CAPAS AJENAS DE ESTE HUESO. Se fue, y la razon
            ; de fondo es que confundi dos cosas distintas:
            ;   * las capas que trae un PRESET son el desglose por slider de UN autor (RaceMenu/XPMSE escriben
            ;     una key por slider), y viven en un ARCHIVO;
            ;   * las capas que tiene un ACTOR en runtime son de mods distintos.
            ; Yo use el argumento de las segundas para justificar borrar las primeras. Y sobre un NPC real las
            ; unicas capas ajenas que hay son `internal` del motor —donde COMPONER ES CORRECTO: el NPC con tacos
            ; tiene que levantarse— y los nodos de arma de XPMSE, que vuelven en el proximo cambio de arma.
            ; ⛔ Y no se podian distinguir: el motor no dice "soy derivado del equipo" ni "soy otro mod pisandote".
            ; Cualquier politica automatica se equivoca en uno de los dos, y equivocarse BORRANDO no tiene vuelta
            ; atras mientras equivocarse mostrando otro numero si.
            ; ⇒ Se escribe lo nuestro y no se toca nada ajeno. Lo que SI queda pendiente es neutralizar los
            ; nombres que el preset traia (identidad declarada en el payload), que es preciso en vez de a ciegas.

            ; --- escala
            if i < NodeHasScale_G0000010000.Length
                if NodeHasScale_G0000010000[i]
                    if i < NodeScale_G0000010000.Length
                        NiOverride.AddNodeTransformScale(self, false, IsFemale_G0000010000, node, ovrKey, NodeScale_G0000010000[i])
                    endif
                endif
            endif

            ; --- posición
            if i < NodeHasPos_G0000010000.Length
                if NodeHasPos_G0000010000[i]
                    if i < NodePosX_G0000010000.Length
                        float[] pos = new float[3]
                        pos[0] = NodePosX_G0000010000[i]
                        pos[1] = NodePosY_G0000010000[i]
                        pos[2] = NodePosZ_G0000010000[i]
                        NiOverride.AddNodeTransformPosition(self, false, IsFemale_G0000010000, node, ovrKey, pos)
                    endif
                endif
            endif

            ; --- rotación: 9 floats de matriz CRUDA, no euler (ver el doc de NodeRotM0_G0000010000..8)
            if i < NodeHasRot_G0000010000.Length
                if NodeHasRot_G0000010000[i]
                    if i < NodeRotM0_G0000010000.Length
                        float[] rot = new float[9]
                        rot[0] = NodeRotM0_G0000010000[i]
                        rot[1] = NodeRotM1_G0000010000[i]
                        rot[2] = NodeRotM2_G0000010000[i]
                        rot[3] = NodeRotM3_G0000010000[i]
                        rot[4] = NodeRotM4_G0000010000[i]
                        rot[5] = NodeRotM5_G0000010000[i]
                        rot[6] = NodeRotM6_G0000010000[i]
                        rot[7] = NodeRotM7_G0000010000[i]
                        rot[8] = NodeRotM8_G0000010000[i]
                        NiOverride.AddNodeTransformRotation(self, false, IsFemale_G0000010000, node, ovrKey, rot)
                    endif
                endif
            endif

            ; --- scale mode
            if i < NodeScaleMode_G0000010000.Length
                if NodeScaleMode_G0000010000[i] >= 0
                    NiOverride.AddNodeTransformScaleMode(self, false, IsFemale_G0000010000, node, ovrKey, NodeScaleMode_G0000010000[i])
                endif
            endif

            ; ⛔⛔ ESTA LLAMADA NO HACE NADA, EN SILENCIO, SI IsFemale NO COINCIDE CON EL SEXO DEL ACTOR BASE:
            ; UpdateNodeTransform compara `isFemale` contra `actorBase->GetSex()` y hace `return` sin tocar nada
            ; ni loguear (PapyrusNiOverride.cpp:1286-1293). Y es la unica cosa que recompone el nodo, asi que un
            ; IsFemale_G<n> equivocado deja los Add* en el store y NADA visible en el 3D — el sintoma seria "el
            ; NPC no cambia" sin ningun error. El mismo gate corre en el UpdateNodeTransform de RemovePrevious,
            ; o sea que tampoco se veria el deshacer. Es diagnostico futuro, no una guarda que agregar aca: el
            ; sexo sale del record en el emisor.
            NiOverride.UpdateNodeTransform(self, false, IsFemale_G0000010000, node)

            ; **EL VALOR NUESTRO, LEIDO DE VUELTA.** Es el numero que decide si el hueso quedo como la app dice o
            ; al doble, y mirando el NPC no se puede saber. Ojo con lo que este numero ES y lo que NO es:
            ;   ES  -> lo que quedo guardado bajo NUESTRA key. Si no coincide con lo que muestra el editor, el
            ;          problema esta en el emisor o en el payload.
            ;   NO ES -> el total COMPUESTO que el motor va a usar. skee no expone un getter del compuesto: los
            ;          getters son por (nodo, nombre). Para saber si hay doble conteo hay que mirar tambien lo que
            ;          traza la neutralizacion: si un nombre ajeno vuelve con algo != 1.0, ahi esta el doble.
            if Verbose_G0000010000
                float mine = NiOverride.GetNodeTransformScale(self, false, IsFemale_G0000010000, node, XformKey())
                Debug.Trace("[NPCM] xform " + node + ": nuestra key quedo scale=" + mine)
            endif
        endif
        i += 1
    endwhile
EndFunction


; ============================================================================================
; BODY MORPHS DE BODYSLIDE (los que antes entregaba el par BodyGen morphs.ini/templates.ini).
;
; POR QUE SE MUDARON ACA: BodyGen se evalua UNA sola vez, en el primer load del actor, y con el gate
; `!HasMorphs` (skee64/ActorUpdateManager.cpp:38-40). Una referencia que YA existe en la partida del
; jugador no lo recibe NUNCA. El apply-script con el sufijo _G<n> si le llega, porque una property con
; nombre nuevo se inicializa del VMAD en vez de restaurarse rancia del savegame. Esa es toda la ganancia.
;
; ⛔⛔ DECIA ACA "POR ESO EL .ini NO SE EMITE MAS PARA ESTE PLUGIN, Y SI HABIA UNO SE BORRA". ES FALSO EN
;   LAS DOS MITADES y el saver dice lo contrario en mayusculas (NpcOverrideSaver.vb:1000-1012): el .ini se
;   emite cuando el usuario lo pide, INDEPENDIENTE de quien entregue los morphs, y no se borra nunca.
;   ⛔ Y NO SE DEBE: el .ini es por PLUGIN y lista TODOS sus NPC, asi que borrarlo le corta la entrega a
;   todos los que el usuario no re-grabo (su VMAD viejo les llega INERTE). Ya paso una vez.
;   La coexistencia es SEGURA por construccion, y la razon esta escrita justo abajo: si corremos primero,
;   BodyGen se saltea por su gate `!HasMorphs`; si corre primero BodyGen, ClearMorphs se lleva su key. No es preferencia:
; skee SUMA las contribuciones keyed de un mismo morph (Impl_GetBodyMorphs, BodyMorphInterface.cpp:220-240,
; default iBodyMorphMode=0), asi que un row de BodyGen mas el nuestro aplicaria el slider DOS VECES.
; (En FO4 el mismo choque da MAX en vez de suma — ver el .psc de alla. Son motores distintos.)
;
; NO hace falta registrar el actor en ningun lado antes de escribir (a diferencia de los overlays, que
; exigen AddOverlays): Impl_SetMorph escribe directo al store (BodyMorphInterface.cpp:150-154) y
; UpdateModelWeight no gatea por ningun set de actores.
;
; El barrido de la vez anterior vive en RemovePrevious() (ClearMorphs, poda total), y el repintado
; (UpdateModelWeight) en OnLoad: los dos tienen que correr TAMBIEN con payload vacio.
; ============================================================================================
Function ApplyBodyMorphs()
    if !MorphsOwned_G0000010000
        return                    ; el .ini es el dueño — ya se trazo en RemovePrevious()
    endif

    int n = MorphName_G0000010000.Length
    if n == 0
        return
    endif

    string ovrKey = XformKey()
    int applied = 0
    int i = 0
    while i < n
        string mname = MorphName_G0000010000[i]
        if mname != ""
            ; Guarda INLINE por .Length, jamas contra None, y jamas pasando el array a un helper
            ; (reglas 1 y 2 de la cabecera). El emisor garantiza que las dos arrays son igual de largas,
            ; pero el guard queda igual: es gratis y el .psc no puede depender de eso para no reventar.
            if i < MorphValue_G0000010000.Length
                float mval = MorphValue_G0000010000[i]
                NiOverride.SetBodyMorph(self, mname, ovrKey, mval)
                applied += 1
            endif
        endif
        i += 1
    endwhile

    ; TRAZA DE VERIFICACION. Una lectura por linea: indexar el mismo array dos veces en UNA expresion
    ; imprime N veces el ULTIMO elemento (quirk del codegen de Papyrus, ya mordio antes).
    if Verbose_G0000010000
        ; ⛔ EL `n` DE ACA ES EL TOPE DEL LOOP (`.Length` crudo) Y TIENE QUE SEGUIR SIENDOLO: el loop recorre
        ; todo y saltea el centinela con `!= ""`. Lo que estaba mal era TRAZARLO: arregle el trace del payload
        ; para que dijera 0 y me deje este, asi que el MISMO log decia "morphs=0" y dos lineas despues
        ; "aplicados=0 de 1". Dos traces que se contradicen es peor que uno solo mintiendo.
        Debug.Trace("[NPCM] BM aplicados=" + applied + " de " + RealLen(MorphName_G0000010000) + " key=" + ovrKey)
        if n > 0
            string m0 = MorphName_G0000010000[0]
            if m0 != ""
                ; Read-back: si esto NO devuelve lo que acabamos de escribir, la nativa no tomo el valor
                ; (o skee no esta cargado) y hay que mirar ahi, no en el emisor.
                float back = NiOverride.GetBodyMorph(self, m0, ovrKey)
                Debug.Trace("[NPCM] BM readback " + m0 + " = " + back)
            endif
        endif
    endif
EndFunction
