Imports System.IO
Imports FO4_Base_Library

''' <summary>Engancha nuestro apply-script de Papyrus a un NPC_ guardado (via VMAD) e instala el .pex
''' compilado, para que el motor aplique -en el PRIMER SPAWN del actor- las opciones de RaceMenu/LooksMenu que
''' no tienen otra via de entrega.
''' <para>GAME-AWARE de punta a punta: los dos juegos no difieren solo en nombres de API, difieren en QUE se
''' puede entregar, asi que hay dos scripts y dos formas de payload. SSE (RaceMenu/NiOverride): overlays
''' (textura + tinte + alpha), skin override POR SLOT y transforms de nodo. FO4 (LooksMenu): overlays
''' (template + tinte + UV + prioridad) y skin override POR TEMPLATE ID; los transforms de nodo NO EXISTEN -la
''' TransformInterface de f4ee vive detras de un #ifdef, no se registra a Papyrus ni se serializa al co-save-,
''' asi que no hay nada que emitir ni nada que perder.</para>
''' <para>Los BODY MORPHS de BodySlide tambien viajan por aca en los dos juegos. El par BodyGen <c>.ini</c> es
''' MUTUAMENTE EXCLUYENTE con el script: si escriben los dos, ningun motor "saltea al que ya tiene morphs" -
''' skee SUMA por key y f4ee toma el MAX. Y se mueven aca porque BodyGen se evalua UNA vez y solo si el actor
''' no tiene morphs, asi que una referencia que YA existe en la partida no lo recibe nunca. Ver
''' 60-papyrus-bodymorph-delivery.</para>
''' <para>⛔ A proposito NO se emiten, porque ya llegan por otra via y mandarlos de nuevo los aplicaria DOS
''' veces: morphs de cara, sculpt y TINTS de cara, todo eso se hornea en el FaceGen en los dos juegos. Los
''' overlays de CARA son el caso con matiz: en SSE solo si el bake no se los queda (ver
''' <see cref="SkipFaceOverlays"/>); en FO4 siempre, porque alla no hay bake de overlays de cara.</para>
''' <para>Idempotente por construccion: <see cref="NpcVmadBuilder.UpsertScript"/> reescribe solo los scripts
''' bajo nuestro prefijo reservado y copia byte a byte los de vanilla y otros mods, asi que guardar repetido
''' converge a una sola copia del nuestro con los valores actuales.</para></summary>
Public Module NpcApplyScriptEmitter

    ''' <summary>Nombre LEGADO de SSE: el que se emitia antes del esquema por plugin, y el que declara la
    ''' plantilla compilada. Se sigue usando para (a) limpiarlo de los VMAD viejos y (b) saber que string
    ''' reescribir dentro del .pex. NUNCA se emite.</summary>
    Public Const LegacyScriptSse As String = NpcVmadBuilder.ReservedScriptPrefix & "ApplySSE"

    ''' <summary>Generacion y SAL que trae la plantilla compilada (el sufijo <c>_G0000010000</c> de los .psc).
    ''' Los dos tienen que coincidir EXACTAMENTE con lo que declaran los .psc o el parcheo del .pex falla ruidoso.</summary>
    Public Const BaselineGeneration As Integer = 1
    Public Const BaselineSalt As String = PexPatcher.BaselineSalt
    Public Const LegacyScriptFo4 As String = NpcVmadBuilder.ReservedScriptPrefix & "ApplyFO4"

    ''' <summary>Nombre de la property que lleva la version del payload. Su valor NO es una constante: es un
    ''' hash del payload DE ESTE NPC, estampado por <see cref="StampVersion"/> cuando el resto de las properties
    ''' ya estan armadas.
    ''' <para>Por que un hash por NPC y no un numero global: el script recuerda por instancia de actor en el
    ''' savegame que version ya aplico y saltea si no cambio. Con hash, editar UN NPC cambia solo SU numero, asi
    ''' que solo ese actor re-aplica; una constante global forzaria a todo el plugin a re-aplicar por cualquier
    ''' edicion. Es tambien lo que hace segura la re-aplicacion en FO4: el script saca los uid de overlay que
    ''' minteo la vez anterior antes de re-agregar, asi que no puede apilar duplicados ni tocar overlays de otro
    ''' mod.</para></summary>
    Public Const VersionPropertyName As String = "SchemaVersion"

    ''' <summary>UNICA via para armar un nombre de property de SSE. Si alguno se arma a mano queda en la
    ''' generacion equivocada y el motor lo sirve rancio (o None) sin decir nada.</summary>
    Private Function GenProp(baseName As String, generation As Integer, salt As String) As String
        Return baseName & PexPatcher.GenerationSuffix(generation, salt)
    End Function

    ''' <summary>Nombre real de la property de version. SSE lleva sufijo; FO4 todavia no.</summary>
    Private Function VersionPropertyNameFor(game As Config_App.Game_Enum, generation As Integer, salt As String) As String
        Return GenProp(VersionPropertyName, generation, salt)
    End Function

    ''' <summary>Nombre del script ANTES del esquema por plugin, por juego. Se limpia del VMAD, y es lo
    ''' que PexPatcher busca dentro de la plantilla para renombrarla.</summary>
    Public Function LegacyScriptFor(game As Config_App.Game_Enum) As String
        Return If(game = Config_App.Game_Enum.Skyrim, LegacyScriptSse, LegacyScriptFo4)
    End Function

    ''' <summary>⛔ En SSE la cara es del bake MIENTRAS EL BAKE SE LA QUEDE: con
    ''' <c>Setting_BakeSseRaceMenuOverlays</c> ON no se emite ningun nodo Face; con el toggle OFF si, porque si
    ''' no los overlays de cara quedan SIN DUENO y se pierden.
    ''' <para>⛔ El gate va SOLO sobre ese toggle. NO volver a gatearlo por "el bake de CharGen corre": ese era
    ''' el agujero original, porque el flag de CharGen es global del guardado mientras el bake ademas se saltea
    ''' POR NPC (raza sin FaceGen), asi que siempre quedaba un caso donde el emisor creia que el bake lo horneaba
    ''' y no lo horneaba.</para>
    ''' <para>⛔ Emitirlos EXIGE que el script barra los nodos <c>Face [Ovl]</c>, y los dos cambios van juntos:
    ''' todo entra con persist=true (co-save), asi que un overlay aplicado con el toggle OFF sobrevive en esa
    ''' partida y sin barrerlo quedaria aplicado DOS veces. Emitir sin barrer es PEOR que no emitir.</para></summary>
    ''' <remarks>SÓLO aplica al pool NORMAL de la cara. El pool MAGIC (<c>Face [SOvl{n}]</c>) queda FUERA de este
    ''' gate: no se hornea en ningún caso, así que el script es su único dueño. Ver <see cref="IsSpellNode"/>.</remarks>
    Friend Function SkipFaceOverlays(game As Config_App.Game_Enum) As Boolean
        If game <> Config_App.Game_Enum.Skyrim Then Return False   ' FO4: el script es su única vía ⇒ se emiten siempre
        ' SSE: se saltean SÓLO si el bake se los va a quedar. Config sin resolver ⇒ se conserva el comportamiento
        ' conservador previo (saltear): no emitir nunca puede duplicar, emitir de más sí.
        Return Config_App.Current Is Nothing OrElse Config_App.Current.Setting_BakeSseRaceMenuOverlays
    End Function

    ''' <summary>Predicado ÚNICO, compartido con el bake (CPU y GPU) y el render. Si el emisor y el bake no
    ''' coinciden EXACTAMENTE en qué es "de cara", un overlay se compone dos veces o ninguna.</summary>
    Private Function IsFaceNode(nodeName As String) As Boolean
        Return SseOverlayCompositor.IsFaceOverlayNodeName(nodeName)
    End Function

    ''' <summary>Predicado ÚNICO del pool MAGIC, delegado a la librería. Para el emisor importa por una sola razón,
    ''' pero es decisiva: un <c>Face [SOvl{n}]</c> NO LO HORNEA NADIE (el fold excluye el pool magic por diseño —
    ''' <see cref="SseOverlayCompositor.IsFoldableFaceOverlay"/>), así que este script es su ÚNICO dueño y hay que
    ''' emitirlo SIEMPRE, tenga el bake de overlays de cara prendido o apagado.</summary>
    Private Function IsSpellNode(nodeName As String) As Boolean
        Return SseOverlayCompositor.IsSpellOverlayNodeName(nodeName)
    End Function

    ''' <summary>⛔⛔ UNA ARRAY-PROPERTY NUNCA VA VACIA **NI AUSENTE**. Cuando no hay datos se emite un array de
    ''' UN elemento CENTINELA que el script saltea solo ("" para nombres, 0 para slots/valores).
    ''' <para>Papyrus de Skyrim deja exactamente esa salida, encerrado entre dos reglas medidas: un array de
    ''' longitud 0 es ILEGAL (la property falla al inicializar, queda en None y envenena la instancia entera, o
    ''' sea que TODAS las demas arrays se leen None), y una property AUSENTE tambien queda en None, pero
    ''' <c>if X == None</c> sobre un array-property TIRA - el guard con el que uno se protege ES lo que explota.
    ''' No hay forma de chequear "esta vacia" sin reventar.</para>
    ''' <para>El centinela satisface las dos y el script ya lo saltea con sus guards normales. FO4 tolera arrays
    ''' vacios pero se emite igual en los dos juegos: una sola ley. Ver 60-papyrus-gotchas.</para>
    ''' <para>Estos helpers son la UNICA via para agregar una array-property: no llamar a
    ''' <c>VmadPropertySpec.From*Array</c> directo desde los builders.</para></summary>
    Private Sub AddArray(props As List(Of NpcVmadBuilder.VmadPropertySpec), name As String, values As List(Of String))
        Dim v = If(values Is Nothing OrElse values.Count = 0, New List(Of String) From {""}, values)
        props.Add(NpcVmadBuilder.VmadPropertySpec.FromStringArray(name, v))
    End Sub

    Private Sub AddArray(props As List(Of NpcVmadBuilder.VmadPropertySpec), name As String, values As List(Of Integer))
        Dim v = If(values Is Nothing OrElse values.Count = 0, New List(Of Integer) From {0}, values)
        props.Add(NpcVmadBuilder.VmadPropertySpec.FromIntArray(name, v))
    End Sub

    Private Sub AddArray(props As List(Of NpcVmadBuilder.VmadPropertySpec), name As String, values As List(Of Single))
        Dim v = If(values Is Nothing OrElse values.Count = 0, New List(Of Single) From {0.0F}, values)
        props.Add(NpcVmadBuilder.VmadPropertySpec.FromFloatArray(name, v))
    End Sub

    Private Sub AddArray(props As List(Of NpcVmadBuilder.VmadPropertySpec), name As String, values As List(Of Boolean))
        Dim v = If(values Is Nothing OrElse values.Count = 0, New List(Of Boolean) From {False}, values)
        props.Add(NpcVmadBuilder.VmadPropertySpec.FromBoolArray(name, v))
    End Sub

    ''' <summary>Techo de elementos por array del payload, para TODAS las familias de arrays paralelos y en los
    ''' dos juegos: una sola ley.
    ''' <para>⛔ 512 NO es un limite del motor, es una decision de COSTO. El famoso tope de 128 es del COMPILADOR
    ''' sobre <c>new T[n]</c>, y un array que arma el LOADER DEL VMAD no pasa por el compilador: medido in-game
    ''' en los dos juegos, 512 elementos entran y se aplican enteros.</para>
    ''' <para>El costo: en FO4 cada <c>SetMorph</c> hace ceder la VM (f4ee no le pone NoWait), asi que 512
    ''' morphs tardan ~9 s; skee si lo marca y tarda &lt;= 2 s. Un preset real (16-80 sliders) cuesta menos de
    ''' 1,5 s. El guard duro de verdad es el techo de 64 KB del subrecord, no este.</para></summary>
    Private Const MaxArrayElements As Integer = 512

    ''' <summary>Techo DURO de un subrecord: el campo de longitud es u16 y la lib no implementa la extensión
    ''' XXXX (<c>PluginWriter.WriteSubrecordHeader</c> tira si se pasa). El chequeo por-NPC vive en
    ''' <c>NpcOverrideSaver.CheckVmadSize</c>, que puede nombrar al NPC; acá sólo se documenta el número.</summary>
    Public Const VmadHardLimitBytes As Integer = 65535

    ''' <summary>Registra un recorte en <paramref name="warnings"/>. Nunca en silencio: un payload recortado
    ''' que no se reporta se ve EXACTAMENTE igual que uno completo, y "aplicó todo" cuando no es el modo de
    ''' falla que este proyecto ya se comió dos veces.</summary>
    Private Sub NoteTrim(warnings As List(Of String), dropped As Integer, kind As String)
        If dropped <= 0 Then Return
        If warnings IsNot Nothing Then
            warnings.Add($"{dropped} {kind} DROPPED — Papyrus caps an array at {MaxArrayElements} elements")
        End If
        If Logger.Enabled Then
            Logger.LogLazy(Function() $"[NPCM-APPLY] {dropped} {kind} dropped (array cap {MaxArrayElements})")
        End If
    End Sub

    ''' <summary>Body morphs de BodySlide a los dos arrays paralelos <c>MorphName</c>/<c>MorphValue</c> que
    ''' consumen los dos <c>.psc</c>. MISMA FORMA EN LOS DOS JUEGOS; lo unico que cambia es que hace el script
    ''' con ellos (SSE los mete bajo una key nuestra, FO4 bajo el keyword None).
    ''' <para>En SSE se emite la SUMA de las contribuciones keyed bajo UNA key nuestra: skee las netea sumando
    ''' al renderizar, asi que el numero es el mismo y encima compra el deshacer quirurgico. En FO4 va el dict
    ''' plano, porque f4ee no tiene keys de string.</para>
    ''' <para>⛔ SE FILTRAN LOS CEROS: en FO4 un valor exactamente 0 BORRA la entrada, asi que un 0 no significa
    ''' "morph en cero" sino "morph ausente"; en SSE sumaria 0 y solo gastaria techo.</para>
    ''' <para>Orden ordinal por nombre y no el del diccionario: el sello es un hash del payload, y un orden
    ''' inestable haria re-aplicar a NPCs que no cambiaron.</para>
    ''' <para>⚠️ Si hay mas de <see cref="MaxArrayElements"/> se recorta por |valor| descendente y se LOGUEA lo
    ''' que quedo afuera: recortar en silencio es el modo de falla que este proyecto ya se comio dos veces.</para></summary>
    Private Sub BuildMorphArrays(preset As LooksmenuLoader.LooksmenuPreset,
                                 game As Config_App.Game_Enum,
                                 names As List(Of String),
                                 values As List(Of Single),
                                 warnings As List(Of String))
        If preset Is Nothing Then Return

        ' --- fuente: keyed (SSE, sumado) o plano.
        Dim flat As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
        If game = Config_App.Game_Enum.Skyrim AndAlso
           preset.BodyMorphsKeyed IsNot Nothing AndAlso preset.BodyMorphsKeyed.Count > 0 Then
            For Each mk In preset.BodyMorphsKeyed
                If String.IsNullOrEmpty(mk.Key) Then Continue For
                Dim sum As Single = 0.0F
                If mk.Value IsNot Nothing Then
                    For Each ikv In mk.Value : sum += ikv.Value : Next
                End If
                Dim existing As Single
                If flat.TryGetValue(mk.Key, existing) Then flat(mk.Key) = existing + sum Else flat(mk.Key) = sum
            Next
        ElseIf preset.BodyMorphSliders IsNot Nothing Then
            For Each kv In preset.BodyMorphSliders
                If String.IsNullOrEmpty(kv.Key) Then Continue For
                Dim existing As Single
                If flat.TryGetValue(kv.Key, existing) Then flat(kv.Key) = existing + kv.Value Else flat(kv.Key) = kv.Value
            Next
        End If

        Dim usable = flat.Where(Function(kv) kv.Value <> 0.0F).ToList()
        If usable.Count = 0 Then Return

        If usable.Count > MaxArrayElements Then
            ' Se conservan los de mayor |valor|: son los que más cambian el cuerpo. Los descartados van al
            ' aviso Y al log con nombre y apellido.
            Dim ordered = usable.OrderByDescending(Function(kv) Math.Abs(kv.Value)).ToList()
            Dim dropped = ordered.Skip(MaxArrayElements).Select(Function(kv) kv.Key).ToList()
            usable = ordered.Take(MaxArrayElements).ToList()
            NoteTrim(warnings, dropped.Count, "body morph(s)")
            If Logger.Enabled Then
                Dim droppedList = String.Join(", ", dropped)
                Logger.LogLazy(Function() $"[NPCM-APPLY] dropped body morphs: {droppedList}")
            End If
        End If

        For Each kv In usable.OrderBy(Function(p) p.Key, StringComparer.Ordinal)
            names.Add(kv.Key)
            values.Add(kv.Value)
        Next
    End Sub

    ''' <summary>Pack a 0..1 RGBA tint into skee's 0xAARRGGBB int (kParam_ShaderTintColor, key 7).</summary>
    Private Function PackTint(r As Single, g As Single, b As Single, a As Single) As Integer
        Dim ToByte = Function(v As Single) CInt(Math.Round(Math.Max(0.0F, Math.Min(1.0F, v)) * 255.0F))
        Return (ToByte(a) << 24) Or (ToByte(r) << 16) Or (ToByte(g) << 8) Or ToByte(b)
    End Function

    ''' <summary>Upsert (or remove) our script on <paramref name="npcSpec"/>'s VMAD.
    ''' <paramref name="enabled"/> = False removes ours and keeps every other script — so unchecking the
    ''' option in Save ESP actually strips a previously-emitted script instead of leaving it stale.
    ''' Returns True when a script was written (the caller uses that to decide whether to install the .pex).</summary>
    ''' <param name="ownBodyMorphs">True ⇒ el script es el DUEÑO de los body morphs de BodySlide: los emite y
    ''' además barre los suyos antes de aplicar. False ⇒ los entrega el par BodyGen .ini y el script no toca
    ''' morphs en absoluto. NO es un simple "no emitir": viaja al `.psc` como <c>MorphsOwned</c> porque en
    ''' FO4 nuestro barrido usa el keyword <c>None</c>, que es EL MISMO SLOT que escribe BodyGen — sin el flag,
    ''' con el modo .ini activo el barrido borraría lo que BodyGen acaba de aplicar, o no, según quién corra
    ''' primero (el orden entre el evento de f4ee y el OnLoad de Papyrus no está garantizado).</param>
    Public Function ApplyToNpc(npcSpec As NPC_Data,
                               preset As LooksmenuLoader.LooksmenuPreset,
                               game As Config_App.Game_Enum,
                               enabled As Boolean,
                               pluginFileName As String,
                               generation As Integer,
                               salt As String,
                               ownBodyMorphs As Boolean,
                               warnings As List(Of String)) As Boolean
        If npcSpec Is Nothing Then Return False

        Dim spec As NpcVmadBuilder.VmadScriptSpec = Nothing
        If enabled Then
            ' ACBS bit 0 = Female (identical in both games — verificado contra los datos
            ' del juego).
            Dim isFemale = (npcSpec.Record.ConfigurationFlags And 1UI) <> 0UI
            spec = BuildSpec(preset, game, isFemale, generation, salt, ownBodyMorphs, warnings)
        End If

        ' TRUE NO-OP para el caso comun: no hay nada que escribir Y no hay nada nuestro que sacar. El VMAD
        ' del record no se toca, asi que la salida de un NPC vanilla queda byte a byte igual.
        Dim hadOurs = NpcVmadBuilder.HasAppScript(npcSpec.Record)
        If spec Is Nothing AndAlso Not hadOurs Then Return False

        ' CLEANUP SCRIPT. The user cleared every option on an NPC we had previously scripted. Simply
        ' dropping the script would be WRONG: the overrides we pushed went into the co-save with
        ' persist=true, so the engine keeps re-applying them on every load, and with no script left nothing
        ' ever removes them — the tattoo would be welded to that actor forever. So we keep the script, with
        ' an EMPTY payload: its ledger still holds what it applied last time, RemovePrevious() undoes it,
        ' and then it applies nothing.
        '
        ' Unchecking "Emit apply-script" (enabled = False) is the deliberate exception: the user asked for
        ' the script GONE, so we strip it, and whatever is already in a running save stays there.
        If spec Is Nothing AndAlso enabled AndAlso hadOurs Then
            Dim isFemaleCleanup = (npcSpec.Record.ConfigurationFlags And 1UI) <> 0UI
            spec = BuildCleanupSpec(game, isFemaleCleanup, generation, salt, ownBodyMorphs, warnings)
        End If

        ' UpsertScript(Nothing) saca el nuestro y deja el resto; si no queda ninguno saca el subrecord VMAD
        ' entero (correcto: el record no tenia scripts propios).
        ' EL BORRADO POR PREFIJO SE ACOTA A LO NUESTRO. UpsertScript borra TODO lo que empieza con el
        ' prefijo que se le pasa. Con el nombre por plugin, usar el prefijo generico NPCM_Manolov_ le
        ' borraria a OTRO AUTOR su script de este mismo record. Por eso van dos pasadas:
        '   1) limpiar el nombre LEGADO (el de antes del esquema por plugin), por prefijo EXACTO;
        '   2) upsert del nuestro, acotado a NUESTRO nombre completo.
        ' Que el stem del plugin vaya ANTES de 'ApplySSE' es lo que hace posible el paso 1 sin tocar la
        ' lib: asi el nombre legado NPCM_Manolov_ApplySSE no es prefijo de ningun nombre nuevo.
        Dim ourName = ScriptNameFor(game, pluginFileName)
        If spec IsNot Nothing Then spec.Name = ourName
        NpcVmadBuilder.UpsertScript(npcSpec.Record, Nothing, game, LegacyScriptFor(game))
        NpcVmadBuilder.UpsertScript(npcSpec.Record, spec, game, ourName)
        Return spec IsNot Nothing
    End Function

    ''' <summary>Build the script spec for this NPC, or Nothing when there is nothing to apply (the
    ''' overwhelmingly common case — an NPC with no overlays / skin / node transforms gets NO script,
    ''' so vanilla records stay untouched).</summary>
    Public Function BuildSpec(preset As LooksmenuLoader.LooksmenuPreset,
                              game As Config_App.Game_Enum,
                              isFemale As Boolean,
                              generation As Integer,
                              salt As String,
                              ownBodyMorphs As Boolean,
                              warnings As List(Of String)) As NpcVmadBuilder.VmadScriptSpec
        If preset Is Nothing Then Return Nothing
        Dim spec = If(game = Config_App.Game_Enum.Skyrim,
                      BuildSpecSse(preset, isFemale, generation, salt, ownBodyMorphs, warnings),
                      BuildSpecFo4(preset, isFemale, generation, salt, ownBodyMorphs, warnings))
        Return StampVersion(spec, game, generation, salt)
    End Function

    ''' <summary>Revision de la LOGICA de los .psc. SUBIRLA cada vez que cambie el COMPORTAMIENTO de un
    ''' apply-script, no cuando cambien los datos de un NPC (eso lo cubre el hash del payload).
    ''' <para>Existe porque el sello se calculaba SOLO sobre el payload y el script arranca con
    ''' <c>if appliedVersion == SchemaVersion : return</c>: un arreglo del .pex no llegaba nunca a los actores
    ''' cuyo payload no habia cambiado, salian en la primera linea sin correr ni RemovePrevious().</para>
    ''' <para>Subirla cambia el sello de TODOS los NPC, asi que cada actor re-aplica UNA vez y despues vuelve el
    ''' comportamiento por-NPC de siempre. Ese re-apply global es el precio y es intencional.</para>
    ''' <para>⚠️ El sufijo de generacion <c>_G&lt;n&gt;</c> resuelve OTRA cosa: una property con nombre nuevo no
    ''' esta en el savegame, asi que el motor la inicializa del VMAD en vez de restaurarla rancia (ver
    ''' 60-papyrus-property-freshness). appliedVersion en cambio SI persiste, a proposito.</para>
    ''' <para>Historial por revision (detalle en 60-papyrus-apply-script): 1 original Â· 2 RemovePrevious barre
    ''' los nodos Face y AddOverlays pasa al inicio de OnLoad Â· 3 payload con sufijo de generacion Â· 4 nombre de
    ''' script por plugin + guard de instancia huerfana Â· 5 body morphs entregados por el script en los dos
    ''' juegos Â· 6 SSE barre tambien la key de BodyGen persistida en el co-save Â· 7 trazas gateadas por la
    ''' property Verbose Â· 8 poda TOTAL del actor antes de aplicar morphs, en vez del barrido por key Â·
    ''' 9 paridad de instrumentacion entre los dos .psc Â·
    ''' 10 SSE: PurgeOverlayGroup barre los overlays de indice &gt;= iNumOverlays (hasta el tope del motor, 127),
    ''' que el barrido viejo no alcanzaba y quedaban clavados en el co-save para siempre.</para>
    ''' <para>⛔⛔ OJO, LA JUSTIFICACION DE ARRIBA YA NO SE SOSTIENE CON ESTE CODIGO. Dice que el sello "se
    ''' calculaba SOLO sobre el payload" y que un NPC sin cambios "ni siquiera re-aplica". Hoy es FALSO:
    ''' <see cref="NpcVmadBuilder.StablePayloadHash"/> mezcla el NOMBRE de cada property (<c>mix(p.Name)</c>), y
    ''' los nombres llevan el sufijo <c>_G&lt;generacion&gt;&lt;salt&gt;</c> con un salt ALEATORIO por Save ESP
    ''' (<see cref="PexPatcher.NewSalt"/>). O sea que el hash cambia en CADA guardado aunque el NPC no se toque,
    ''' y todos los actores re-aplican una vez por publicacion. Medido en Papyrus.0.log: el mismo NPC paso de
    ''' <c>_G0000023620</c> a <c>_G00000349D6</c> con sellos distintos.</para>
    ''' <para>Probablemente era cierto antes de la revision 3 ("payload con sufijo de generacion") y quedo sin
    ''' actualizar. Se conserva el contador igual: es el registro de QUE cambio en cada version del .psc, y es la
    ''' red si algun dia el salt deja de entrar al hash. Pero NO es lo que dispara el re-apply.</para></summary>
    Private Const ScriptLogicRevision As String = "10"

    ''' <summary>Spec de LIMPIEZA: el NPC se quedo sin overlays/skin/transforms pero YA tenia script nuestro,
    ''' asi que hay que dejarle uno que corra <c>RemovePrevious()</c> y no aplique nada.
    ''' <para>⛔ NO se arma a mano. Antes se construia con solo IsFemale + SchemaVersion, y eso ROMPIA la
    ''' garantia de la que el .psc depende explicitamente (toda array-property existe y trae al menos un
    ''' CENTINELA): una array-property ausente le llega al script como None, su <c>.Length</c> revienta y aborta
    ''' el stack, o sea que el spec que existe PARA LIMPIAR moria antes de limpiar. Comparar contra None tampoco
    ''' es salida, el cast revienta igual. Por eso se construye con el builder normal y allowEmpty:=True.</para></summary>
    ''' <param name="ownBodyMorphs">Se propaga TAL CUAL: un spec de limpieza con MorphsOwned=True es lo que hace
    ''' que el script barra los body morphs que aplico la vez anterior. Con False no toca morphs, que es correcto
    ''' porque en ese modo nunca fueron suyos.</param>
    Private Function BuildCleanupSpec(game As Config_App.Game_Enum, isFemale As Boolean, generation As Integer,
                                      salt As String, ownBodyMorphs As Boolean, warnings As List(Of String)) As NpcVmadBuilder.VmadScriptSpec
        Dim emptyPreset As New LooksmenuLoader.LooksmenuPreset()
        Return StampVersion(If(game = Config_App.Game_Enum.Skyrim,
                               BuildSpecSse(emptyPreset, isFemale, generation, salt, ownBodyMorphs, warnings, allowEmpty:=True),
                               BuildSpecFo4(emptyPreset, isFemale, generation, salt, ownBodyMorphs, warnings, allowEmpty:=True)), game, generation, salt)
    End Function

    Private Function StampVersion(spec As NpcVmadBuilder.VmadScriptSpec,
                                  game As Config_App.Game_Enum,
                                  generation As Integer,
                                  salt As String) As NpcVmadBuilder.VmadScriptSpec
        If spec Is Nothing Then Return Nothing
        Dim versionProp = VersionPropertyNameFor(game, generation, salt)
        Dim hash = NpcVmadBuilder.StablePayloadHash(spec, versionProp, ScriptLogicRevision)
        For i = 0 To spec.Properties.Count - 1
            If String.Equals(spec.Properties(i).Name, versionProp, StringComparison.Ordinal) Then
                spec.Properties(i) = NpcVmadBuilder.VmadPropertySpec.FromInt(versionProp, hash)
                Exit For
            End If
        Next
        Return spec
    End Function

    ' ============================================================================================
    ' SSE — RaceMenu / NiOverride
    ' ============================================================================================
    ''' <param name="allowEmpty">True ⇒ emite el spec COMPLETO (todas las array-properties, con centinela)
    ''' aunque no haya nada que aplicar, en vez de devolver Nothing. Lo usa el spec de LIMPIEZA: ver
    ''' <see cref="BuildCleanupSpec"/>.</param>
    Private Function BuildSpecSse(preset As LooksmenuLoader.LooksmenuPreset,
                                  isFemale As Boolean,
                                  generation As Integer,
                                  salt As String,
                                  ownBodyMorphs As Boolean,
                                  warnings As List(Of String),
                                  Optional allowEmpty As Boolean = False) As NpcVmadBuilder.VmadScriptSpec
        ' SSE: los nodos Face se emiten SOLO si el bake NO se los queda (toggle de overlays OFF). Ver SkipFaceOverlays.
        Dim skipFace = SkipFaceOverlays(Config_App.Game_Enum.Skyrim)

        ' --- overlays (Body/Hands/Feet; Face only when it is NOT being baked)
        Dim ovNode As New List(Of String), ovDiff As New List(Of String), ovNorm As New List(Of String)
        Dim ovHasTint As New List(Of Boolean), ovTint As New List(Of Integer)
        Dim ovHasAlpha As New List(Of Boolean), ovAlpha As New List(Of Single)

        Dim ovDropped = 0
        If preset.SseBodyOverlays IsNot Nothing Then
            For Each ov In preset.SseBodyOverlays
                If ov Is Nothing OrElse String.IsNullOrEmpty(ov.NodeName) Then Continue For
                ' La cara es del bake sólo cuando el bake la pliega; si no, va por acá. Ver SkipFaceOverlays.
                ' EXCEPCIÓN QUE NO ES UNA EXCEPCIÓN: el pool MAGIC de la cara (Face [SOvl{n}]) no lo pliega el
                ' bake NUNCA — no es una elección del toggle, es la ley del mecanismo (IsFoldableFaceOverlay).
                ' Gatearlo por `skipFace` lo dejaba SIN DUEÑO con el toggle prendido (que es el default): no se
                ' horneaba y tampoco se emitía ⇒ desaparecía. Ver IsSpellNode.
                If skipFace AndAlso IsFaceNode(ov.NodeName) AndAlso Not IsSpellNode(ov.NodeName) Then Continue For
                ' ACÁ SE DESCARTABAN LOS OVERLAYS MAGIC CON ÍNDICE ≥ 8, y era una PÉRDIDA SILENCIOSA de algo
                ' que el usuario había autorado. Se fue junto con el techo, y la premisa que lo sostenía era falsa:
                ' el `.psc` afirmaba —en tres lugares— que Papyrus no expone el contador del pool magic, y sí lo
                ' expone (`NiOverride.GetNumSpell{Body,Hand,Feet,Face}Overlays`, PapyrusNiOverride.cpp:1844-1853,
                ' además NoWait). El apply-script las llama, así que apaga exactamente los nodos que el juego del
                ' jugador creó ⇒ todo lo que se ve se puede deshacer y no hay nada que descartar.
                ' El límite del pool magic es ahora el mismo que el del normal: el contador del MOTOR.
                ' Nothing to override on this node → don't emit an empty entry.
                If String.IsNullOrEmpty(ov.DiffusePath) AndAlso String.IsNullOrEmpty(ov.NormalPath) AndAlso
                   Not ov.HasTint AndAlso Not ov.HasAlpha Then Continue For
                ' EL TOPE SE APLICA EN LA FUENTE, no recortando los arrays después: así los 7 arrays
                ' paralelos quedan alineados POR CONSTRUCCIÓN y el índice i sigue significando "overlay i".
                If ovNode.Count >= MaxArrayElements Then ovDropped += 1 : Continue For

                ovNode.Add(ov.NodeName)
                ovDiff.Add(If(ov.DiffusePath, ""))
                ovNorm.Add(If(ov.NormalPath, ""))
                ovHasTint.Add(ov.HasTint)
                ovTint.Add(If(ov.HasTint, PackTint(ov.TintR, ov.TintG, ov.TintB, ov.TintA), 0))
                ovHasAlpha.Add(ov.HasAlpha)
                ovAlpha.Add(If(ov.HasAlpha, ov.Alpha, 1.0F))
            Next
        End If

        ' --- skin overrides (per biped slot)
        Dim skSlot As New List(Of Integer), skDiff As New List(Of String), skNorm As New List(Of String)
        Dim skHasTint As New List(Of Boolean), skTint As New List(Of Integer)

        Dim skDropped = 0
        If preset.SseSkinOverrides IsNot Nothing Then
            For Each sk In preset.SseSkinOverrides
                If sk Is Nothing OrElse sk.SlotMask = 0UI Then Continue For
                If String.IsNullOrEmpty(sk.DiffusePath) AndAlso String.IsNullOrEmpty(sk.NormalPath) AndAlso
                   Not sk.HasTint Then Continue For
                If skSlot.Count >= MaxArrayElements Then skDropped += 1 : Continue For

                ' REINTERPRET the bits, do NOT convert. SlotMask is a UInteger and comes from the .jslot
                ' untruncated (RaceMenuJslot.vb ~:531). Skyrim biped slot 61 = bit 31 = &H80000000 = 2147483648,
                ' which is > Int32.MaxValue — and VB's integer overflow checks are ON, so CInt() would THROW
                ' and take the whole save down. The bit
                ' pattern is what skee wants; the sign of the Int32 is irrelevant to it.
                skSlot.Add(BitConverter.ToInt32(BitConverter.GetBytes(sk.SlotMask), 0))
                skDiff.Add(If(sk.DiffusePath, ""))
                skNorm.Add(If(sk.NormalPath, ""))
                skHasTint.Add(sk.HasTint)
                skTint.Add(If(sk.HasTint, PackTint(sk.TintR, sk.TintG, sk.TintB, sk.TintA), 0))
            Next
        End If

        ' --- node transforms (scale + position; see the ROTATION note below)
        Dim ndName As New List(Of String)
        Dim ndHasScale As New List(Of Boolean), ndScale As New List(Of Single)
        Dim ndHasPos As New List(Of Boolean)
        Dim ndPosX As New List(Of Single), ndPosY As New List(Of Single), ndPosZ As New List(Of Single)
        Dim ndHasRot As New List(Of Boolean)
        ' La rotación es la matriz 3x3 row-major partida en NUEVE arrays paralelos (el elemento k del nodo i
        ' vive en ndRotM(k)(i)), no un array plano de 9xN. Se mantiene así porque el .psc los consume como
        ' nueve arrays y porque con un array por elemento el índice i significa "nodo i" en TODAS las arrays
        ' del grupo — la misma invariante que sostiene overlays, skin y morphs.
        ' NO son ángulos de Euler: skee acepta 3 (euler) o 9 (matriz cruda), y con 9 los copia tal cual a
        ' la misma NiMatrix33 que después empaqueta al .jslot. Le devolvemos su propia secuencia de floats, y
        ' La arma `RaceMenuJslot.RotationRowMajor`, que es el ÚNICO dueño de la elección "matriz cruda vs
        ' rearmar desde axis-angle" — y hasta 2026-08-10 no lo era: esta línea afirmaba la no-divergencia
        ' mientras el ESP rearmaba siempre y el .jslot prefería el crudo, o sea que 180° y reflexiones se
        ' perdían SÓLO por acá. Ver el doc de esa función.
        Dim ndRotM(8) As List(Of Single)
        For k = 0 To 8 : ndRotM(k) = New List(Of Single)() : Next
        Dim ndScaleMode As New List(Of Integer)
        ' Pares planos (nodo, nombre) de las capas ajenas a neutralizar con identidad. Ver el bloque que las llena.
        Dim ndNeutralNode As New List(Of String), ndNeutralName As New List(Of String)
        Dim ndNeutralDropped = 0

        Dim ndDropped = 0
        If preset.SseNodeTransforms IsNot Nothing Then
            For Each nt In preset.SseNodeTransforms
                If nt Is Nothing OrElse String.IsNullOrEmpty(nt.NodeName) Then Continue For
                If Not (nt.HasScale OrElse nt.HasPosition OrElse nt.HasRotation) Then Continue For
                ' ESTE ES EL ÚNICO ARRAY GENUINAMENTE ILIMITADO del payload: los overlays los acota el motor
                ' (GetNumBodyOverlays/Hand/Feet ≈ 6/3/3) y el skin los 32 slots biped, pero acá el usuario puede
                ' escalar CUALQUIER hueso y un esqueleto tiene 100+. Sin este tope se emitirían 17 arrays
                ' paralelos de más de 128 elementos.
                If ndName.Count >= MaxArrayElements Then ndDropped += 1 : Continue For

                ndName.Add(nt.NodeName)
                ndHasScale.Add(nt.HasScale)
                ndScale.Add(If(nt.HasScale, nt.Scale, 1.0F))
                ndHasPos.Add(nt.HasPosition)
                ndPosX.Add(If(nt.HasPosition, nt.PosX, 0.0F))
                ndPosY.Add(If(nt.HasPosition, nt.PosY, 0.0F))
                ndPosZ.Add(If(nt.HasPosition, nt.PosZ, 0.0F))

                Dim rot = RaceMenuJslot.RotationRowMajor(nt)   ' Nothing when the node has no rotation
                ndHasRot.Add(rot IsNot Nothing)
                ' Every node contributes one element to EACH of the nine arrays (zeros when it has no
                ' rotation), so all nine stay exactly as long as NodeName and index i always means node i.
                ' The script gates on NodeHasRot(i) anyway.
                For k = 0 To 8
                    ndRotM(k).Add(If(rot IsNot Nothing, rot(k), 0.0F))
                Next

                ' SIEMPRE -1 = "no tocar": el scaleMode por nodo es INERTE en skee. La composición lo busca con
                ' un OverrideVariant default, o sea (33,-1) (NiTransformInterface.cpp:667-670), y TODOS los caminos lo
                ' almacenan en (33,0) (:1047 y :1000/:1083/:1135) ⇒ el find nunca matchea y el motor usa
                ' `g_scaleMode`, el `[General] iScaleMode` del jugador (main.cpp:144/797).
                ' Mandarlo era una nativa por nodo que no cambia nada. La versión anterior de este comentario decía
                ' que mandar 0 "fijaba la única lectura correcta": era falso, y encima se contradecía sola al mandar
                ' -1 para los nodos sin escala.
                ' El residuo (un jugador con iScaleMode≠0 compone distinto) NO tiene arreglo desde acá: la key que
                ' serviría es justo la que el motor no lee. Queda dicho, no disimulado.
                ndScaleMode.Add(-1)

                ' LOS NOMBRES A NEUTRALIZAR, como pares PLANOS (nodo, nombre).
                '
                ' POR QUE HACE FALTA: nuestro aporte lleva el valor EFECTIVO del hueso (el decode compuso los
                ' aportes del preset). Si esos mismos aportes están además en el co-save del jugador —pasa cuando un
                ' mod le aplica ESTE preset a ESTE NPC con `CharGen.LoadCharacterPresetEx`— el motor compone los
                ' suyos con nuestro total y el hueso sale al doble. Escribirles IDENTIDAD COMPLETA los vuelve
                ' inertes sin borrar nada de nadie.
                '
                ' POR QUE PLANOS Y NO UNA LISTA POR NODO: Papyrus no tiene arrays irregulares. Dos arrays
                ' paralelos ENTRE SÍ (no con NodeName) es la única forma; el script las recorre de a pares.
                ' Y POR QUE POR NOMBRE Y NO BARRIENDO: así se toca exactamente lo que nuestro valor ya
                ' representa. Un barrido a ciegas se llevaba `internal` —el lift de los tacos altos, donde componer
                ' ES correcto— y el aporte de un mod que nunca vimos, y eso no tiene vuelta atrás. El filtro de qué
                ' nombre es neutralizable vive en RaceMenuJslot.IsNeutralizableLayerName, no acá.
                If nt.CollapsedLayerNames IsNot Nothing Then
                    For Each layerName In nt.CollapsedLayerNames
                        If String.IsNullOrWhiteSpace(layerName) Then Continue For
                        If ndNeutralNode.Count >= MaxArrayElements Then ndNeutralDropped += 1 : Continue For
                        ndNeutralNode.Add(nt.NodeName)
                        ndNeutralName.Add(layerName)
                    Next
                End If
            Next
        End If

        ' --- body morphs (BodySlide). Ver BuildMorphArrays: en SSE se suman las contribuciones keyed y van
        ' bajo UNA key nuestra, que es lo que hace posible ClearBodyMorphKeys como deshacer quirúrgico.
        NoteTrim(warnings, ovDropped, "overlay(s)")
        NoteTrim(warnings, skDropped, "skin override(s)")
        NoteTrim(warnings, ndDropped, "node transform(s)")
        NoteTrim(warnings, ndNeutralDropped, "collapsed-layer name(s) to neutralise")

        Dim mNames As New List(Of String), mValues As New List(Of Single)
        If ownBodyMorphs Then BuildMorphArrays(preset, Config_App.Game_Enum.Skyrim, mNames, mValues, warnings)

        ' mNames CUENTA para "¿hay algo que aplicar?". Sin esto, un NPC cuyo ÚNICO dato son los body
        ' morphs no recibiría script y sus sliders no llegarían por ninguna vía (el .ini ya no se emite).
        If Not allowEmpty AndAlso ovNode.Count = 0 AndAlso skSlot.Count = 0 AndAlso ndName.Count = 0 AndAlso mNames.Count = 0 Then Return Nothing

        Dim spec As New NpcVmadBuilder.VmadScriptSpec With {.Name = LegacyScriptSse}
        Dim P = spec.Properties
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("IsFemale", generation, salt), isFemale))
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromInt(GenProp(VersionPropertyName, generation, salt), 0))   ' placeholder — StampVersion overwrites it with the payload hash
        ' Verbose: el script traza SOLO cuando la app esta diagnosticando. Logger.Enabled ya es la senal
        ' establecida de "estoy debuggeando" (es la que decide si se escribe fo4lib.log) y es Debug-only, asi
        ' que no hace falta un control nuevo. Lo que se ahorra con false NO son lineas de log: es la
        ' CONCATENACION de cada traza (bytecode de Papyrus, corre siempre) y sobre todo LAS NATIVAS DE SONDA
        ' (GetMorphNames/GetMorphKeys/GetBodyMorph), que existen unicamente para trazar. Ver el docstring de
        ' Verbose_G<n> en los .psc.
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("Verbose", generation, salt), Logger.Enabled))

        AddArray(P, GenProp("OvlNode", generation, salt), ovNode)
        AddArray(P, GenProp("OvlDiffuse", generation, salt), ovDiff)
        AddArray(P, GenProp("OvlNormal", generation, salt), ovNorm)
        AddArray(P, GenProp("OvlHasTint", generation, salt), ovHasTint)
        AddArray(P, GenProp("OvlTint", generation, salt), ovTint)
        AddArray(P, GenProp("OvlHasAlpha", generation, salt), ovHasAlpha)
        AddArray(P, GenProp("OvlAlpha", generation, salt), ovAlpha)

        AddArray(P, GenProp("SkinSlot", generation, salt), skSlot)
        AddArray(P, GenProp("SkinDiffuse", generation, salt), skDiff)
        AddArray(P, GenProp("SkinNormal", generation, salt), skNorm)
        AddArray(P, GenProp("SkinHasTint", generation, salt), skHasTint)
        AddArray(P, GenProp("SkinTint", generation, salt), skTint)

        AddArray(P, GenProp("NodeName", generation, salt), ndName)
        AddArray(P, GenProp("NodeHasScale", generation, salt), ndHasScale)
        AddArray(P, GenProp("NodeScale", generation, salt), ndScale)
        AddArray(P, GenProp("NodeHasPos", generation, salt), ndHasPos)
        AddArray(P, GenProp("NodePosX", generation, salt), ndPosX)
        AddArray(P, GenProp("NodePosY", generation, salt), ndPosY)
        AddArray(P, GenProp("NodePosZ", generation, salt), ndPosZ)
        AddArray(P, GenProp("NodeHasRot", generation, salt), ndHasRot)
        For k = 0 To 8
            AddArray(P, GenProp("NodeRotM" & k.ToString(Globalization.CultureInfo.InvariantCulture), generation, salt), ndRotM(k))
        Next
        AddArray(P, GenProp("NodeScaleMode", generation, salt), ndScaleMode)
        ' Paralelas ENTRE SÍ, no con NodeName: son pares (nodo, nombre). Ver NeutralizeCollapsedLayers en el .psc.
        AddArray(P, GenProp("NodeNeutralNode", generation, salt), ndNeutralNode)
        AddArray(P, GenProp("NodeNeutralName", generation, salt), ndNeutralName)

        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("MorphsOwned", generation, salt), ownBodyMorphs))
        AddArray(P, GenProp("MorphName", generation, salt), mNames)
        AddArray(P, GenProp("MorphValue", generation, salt), mValues)

        Return spec
    End Function

    ' ============================================================================================
    ' FO4 — LooksMenu / Overlays + BodyGen
    ' ============================================================================================
    ''' <param name="allowEmpty">Igual que en <see cref="BuildSpecSse"/>: emite el spec COMPLETO aunque no
    ''' haya nada que aplicar, para el caso de LIMPIEZA.</param>
    Private Function BuildSpecFo4(preset As LooksmenuLoader.LooksmenuPreset,
                                  isFemale As Boolean,
                                  generation As Integer,
                                  salt As String,
                                  ownBodyMorphs As Boolean,
                                  warnings As List(Of String),
                                  Optional allowEmpty As Boolean = False) As NpcVmadBuilder.VmadScriptSpec
        Dim tpl As New List(Of String), prio As New List(Of Integer)
        Dim r As New List(Of Single), g As New List(Of Single), b As New List(Of Single), a As New List(Of Single)
        Dim ou As New List(Of Single), ov As New List(Of Single)
        Dim su As New List(Of Single), sv As New List(Of Single)

        Dim tplDropped = 0
        If preset.Overlays IsNot Nothing Then
            For Each e In preset.Overlays
                If e Is Nothing OrElse String.IsNullOrEmpty(e.TemplateId) Then Continue For
                ' Tope en la fuente: mantiene alineados los 10 arrays paralelos por construcción.
                If tpl.Count >= MaxArrayElements Then tplDropped += 1 : Continue For
                tpl.Add(e.TemplateId)
                prio.Add(e.Priority)
                ' Tint / offsetUV / scaleUV are Nothing when the preset didn't carry them — mirror f4ee's
                ' OWN defaults, which are NOT symmetric:
                ' tint → (0,0,0,0) NOT white. f4ee treats the tint as absent only when it is exactly
                '                       zero: OverlayData ctor (OverlayInterface.h:76-79), preset loader with
                '                       no "tint" member (CharGenInterface.cpp:587-597), and UpdateFlags()
                '                       which sets kHasTintColor iff tint != 0 (OverlayInterface.h:97-100).
                '                       Emitting white would TURN THE FLAG ON and make the engine tint the
                '                       overlay with white instead of the NPC's skinColor.
                '   offset→ (0,0)   scale → (1,1)   (CharGenInterface.cpp:598-611)
                r.Add(If(e.Tint IsNot Nothing AndAlso e.Tint.Length > 0, e.Tint(0), 0.0F))
                g.Add(If(e.Tint IsNot Nothing AndAlso e.Tint.Length > 1, e.Tint(1), 0.0F))
                b.Add(If(e.Tint IsNot Nothing AndAlso e.Tint.Length > 2, e.Tint(2), 0.0F))
                a.Add(If(e.Tint IsNot Nothing AndAlso e.Tint.Length > 3, e.Tint(3), 0.0F))
                ou.Add(If(e.OffsetUV IsNot Nothing AndAlso e.OffsetUV.Length > 0, e.OffsetUV(0), 0.0F))
                ov.Add(If(e.OffsetUV IsNot Nothing AndAlso e.OffsetUV.Length > 1, e.OffsetUV(1), 0.0F))
                su.Add(If(e.ScaleUV IsNot Nothing AndAlso e.ScaleUV.Length > 0, e.ScaleUV(0), 1.0F))
                sv.Add(If(e.ScaleUV IsNot Nothing AndAlso e.ScaleUV.Length > 1, e.ScaleUV(1), 1.0F))
            Next
        End If

        Dim skin = If(preset.SkinTemplateId, "")

        ' --- body morphs (BodySlide). En FO4 es el dict PLANO: BodyMorphsKeyed es SSE-only (f4ee no tiene
        ' keys de string, tiene Keywords, y el script escribe bajo el keyword None). Ver BuildMorphArrays.
        NoteTrim(warnings, tplDropped, "overlay(s)")

        Dim mNames As New List(Of String), mValues As New List(Of Single)
        If ownBodyMorphs Then BuildMorphArrays(preset, Config_App.Game_Enum.Fallout4, mNames, mValues, warnings)

        ' mNames CUENTA para "¿hay algo que aplicar?" — mismo motivo que en SSE.
        If Not allowEmpty AndAlso tpl.Count = 0 AndAlso skin = "" AndAlso mNames.Count = 0 Then Return Nothing

        Dim spec As New NpcVmadBuilder.VmadScriptSpec With {.Name = LegacyScriptFo4}
        Dim P = spec.Properties
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("IsFemale", generation, salt), isFemale))
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromInt(GenProp(VersionPropertyName, generation, salt), 0))   ' placeholder — StampVersion overwrites it with the payload hash
        ' Verbose: el script traza SOLO cuando la app esta diagnosticando. Logger.Enabled ya es la senal
        ' establecida de "estoy debuggeando" (es la que decide si se escribe fo4lib.log) y es Debug-only, asi
        ' que no hace falta un control nuevo. Lo que se ahorra con false NO son lineas de log: es la
        ' CONCATENACION de cada traza (bytecode de Papyrus, corre siempre) y sobre todo LAS NATIVAS DE SONDA
        ' (GetMorphNames/GetMorphKeys/GetBodyMorph), que existen unicamente para trazar. Ver el docstring de
        ' Verbose_G<n> en los .psc.
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("Verbose", generation, salt), Logger.Enabled))

        AddArray(P, GenProp("OvlTemplate", generation, salt), tpl)
        AddArray(P, GenProp("OvlPriority", generation, salt), prio)
        AddArray(P, GenProp("OvlRed", generation, salt), r)
        AddArray(P, GenProp("OvlGreen", generation, salt), g)
        AddArray(P, GenProp("OvlBlue", generation, salt), b)
        AddArray(P, GenProp("OvlAlpha", generation, salt), a)
        AddArray(P, GenProp("OvlOffsetU", generation, salt), ou)
        AddArray(P, GenProp("OvlOffsetV", generation, salt), ov)
        AddArray(P, GenProp("OvlScaleU", generation, salt), su)
        AddArray(P, GenProp("OvlScaleV", generation, salt), sv)
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromString(GenProp("SkinTemplate", generation, salt), skin))

        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("MorphsOwned", generation, salt), ownBodyMorphs))
        AddArray(P, GenProp("MorphName", generation, salt), mNames)
        AddArray(P, GenProp("MorphValue", generation, salt), mValues)

        Return spec
    End Function

    ' ============================================================================================
    ' .pex install
    ' ============================================================================================

    ''' <summary>Nombre del script (y del .pex) PARA ESTE PLUGIN. En SSE es unico por ESP publicado, y eso es
    ''' lo que permite que dos mods hechos con la app CONVIVAN: los sueltos de Data\Scripts no se fusionan, uno
    ''' gana, y hasta ahora los dos shipeaban el mismo NPCM_Manolov_ApplySSE.pex — al perdedor le quedaba un
    ''' .pex que no declara sus properties (se ignoran en silencio, o llegan None y el .Length revienta).
    ''' <para>El stem del plugin va ANTES de 'ApplySSE' a proposito: asi el nombre legado no es prefijo de
    ''' ningun nombre nuevo, y el borrado por prefijo de UpsertScript no toca el script de otro autor.</para>
    ''' <para>Dos ESP con el mismo nombre vuelven a colisionar: es responsabilidad del autor darle un nombre
    ''' unico a su plugin. Decision tomada a proposito, sin hash ni aviso.</para>
    ''' <para>FO4 sigue con el nombre unico de siempre hasta medir la misma ley en ese motor.</para></summary>
    Public Function ScriptNameFor(game As Config_App.Game_Enum, pluginFileName As String) As String
        Dim tail = If(game = Config_App.Game_Enum.Skyrim, "_ApplySSE", "_ApplyFO4")
        Return NpcVmadBuilder.ReservedScriptPrefix & SanitizeStem(pluginFileName) & tail
    End Function

    ''' <summary>Nombre de archivo del plugin -> identificador valido de Papyrus. Conserva la extension para
    ''' que .esp/.esl/.esm del mismo nombre no colisionen: NPC_Manager2.esp -> NPC_Manager2_esp.</summary>
    Private Function SanitizeStem(pluginFileName As String) As String
        Dim name = Path.GetFileName(If(pluginFileName, ""))
        Dim sb As New Text.StringBuilder(name.Length)
        For Each c In name
            If (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "A"c AndAlso c <= "Z"c) OrElse
               (c >= "0"c AndAlso c <= "9"c) OrElse c = "_"c Then
                sb.Append(c)
            Else
                sb.Append("_"c)
            End If
        Next
        Dim r = sb.ToString()
        Return If(r.Length = 0, "Plugin", r)
    End Function

    ''' <summary>Bytes del .pex LISTOS PARA INSTALAR: la plantilla embebida con el nombre del script y la
    ''' generacion ya reescritos. UNICA fuente para el disco Y para el FOMOD — si los dos no salen de aca, el
    ''' paquete puede llevar un .pex que no coincide con el VMAD del ESP.</summary>
    Public Function PatchedPexBytes(game As Config_App.Game_Enum, pluginFileName As String,
                                    generation As Integer, salt As String) As Byte()
        Dim template = PexBytes(game)
        If template Is Nothing OrElse template.Length = 0 Then Return Nothing
        Return PexPatcher.PatchScript(template, LegacyScriptFor(game), ScriptNameFor(game, pluginFileName),
                                      BaselineGeneration, BaselineSalt, generation, salt)
    End Function

    ''' <summary>El .pex compilado va EMBEBIDO en este assembly, no leido de una carpeta al lado del exe. Dos
    ''' razones, y la segunda es la real: no hay nada que perder al mover la app, y el .pex no puede quedar
    ''' desincronizado del build que emitio el VMAD que lo referencia. Un .pex suelto rancio ignoraria en
    ''' silencio cualquier property que no conozca: el script correria, no aplicaria nada y no reportaria nada,
    ''' que es el peor modo de falla posible.
    ''' <para>Nothing si falta el recurso, o sea si la app se compilo sin correr el paso de Papyrus.</para>
    ''' <para>Sale SANEADO por <see cref="PexPatcher.SanitizeHeader"/>: el compilador de Papyrus estampa en
    ''' el header la ruta absoluta del <c>.psc</c>, el usuario y el nombre de la maquina que compilo, y este es
    ''' el unico punto por el que pasan TODOS los <c>.pex</c> que la app publica. Ver
    ''' 00-reglas-sin-datos-personales.</para></summary>
    Public Function PexBytes(game As Config_App.Game_Enum) As Byte()
        ' El recurso embebido es la PLANTILLA, asi que su nombre es el de la compilacion (legado), NO el
        ' nombre por plugin: ese lo pone PatchedPexBytes reescribiendo el .pex.
        Dim templateName = LegacyScriptFor(game)
        Dim resourceName = "NpcManager.Papyrus." & templateName & ".pex"   ' LogicalName pinned in the .vbproj
        Using s = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            If s Is Nothing Then Return Nothing
            Using ms As New MemoryStream()
                s.CopyTo(ms)
                Return PexPatcher.SanitizeHeader(ms.ToArray())
            End Using
        End Using
    End Function

    ''' <summary>Write the compiled script into <c>Data\Scripts\</c>. Only OUR script — never the native
    ''' stubs (NiOverride/Overlays/BodyGen), which would SHADOW RaceMenu's/LooksMenu's real .pex, since loose
    ''' files win over the BSA/BA2. See Papyrus\README.md.
    ''' Returns the destination path, or Nothing when the embedded .pex is missing.</summary>
    Public Function InstallPex(dataPath As String, game As Config_App.Game_Enum,
                               pluginFileName As String, generation As Integer, salt As String) As String
        If String.IsNullOrEmpty(dataPath) Then Return Nothing
        Dim bytes = PatchedPexBytes(game, pluginFileName, generation, salt)
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return Nothing

        Dim destDir = Path.Combine(dataPath, "Scripts")
        Directory.CreateDirectory(destDir)
        Dim dest = Path.Combine(destDir, ScriptNameFor(game, pluginFileName) & ".pex")

        ' Write only when the bytes actually differ — avoids touching the file on every save (and avoids
        ' churning a mod manager's overwrite folder for a file that did not change).
        If File.Exists(dest) Then
            Try
                If File.ReadAllBytes(dest).SequenceEqual(bytes) Then Return dest
            Catch
                ' Unreadable (locked / permissions) → fall through and try to overwrite it.
            End Try
        End If

        File.WriteAllBytes(dest, bytes)
        InstallLegacyPex(destDir, game)
        Return dest
    End Function


    ''' <summary>Instala TAMBIEN el .pex del nombre LEGADO (el publicado antes del esquema por plugin), sin
    ''' parchear. Suena a basura; es obligatorio.
    ''' <para>MEDIDO: si ese .pex no esta, el savegame del jugador sigue teniendo instancias de ese tipo pegadas
    ''' al actor, el tipo no resuelve y -como el script extends Actor- ese actor queda SIN TABLA DE METODOS PARA
    ''' TODOS LOS DEMAS SCRIPTS. No es que no se apliquen nuestros overlays: le rompemos el NPC a cualquier mod
    ''' (observado con RaceMenuHHScaleEffect).</para>
    ''' <para>Y no re-aplica nada: el .psc corta con el guard de instancia huerfana (arrays de longitud 0 = el
    ''' VMAD no nombra a este script). Es un artefacto de MIGRACION: se puede dejar de shippear cuando ningun
    ''' savegame publicado arrastre instancias del nombre viejo, y como eso no se puede saber, se queda.</para></summary>
    Private Sub InstallLegacyPex(scriptsDir As String, game As Config_App.Game_Enum)
        Dim template = PexBytes(game)
        If template Is Nothing OrElse template.Length = 0 Then Return
        Dim dest = Path.Combine(scriptsDir, LegacyScriptFor(game) & ".pex")
        Try
            If File.Exists(dest) AndAlso File.ReadAllBytes(dest).SequenceEqual(template) Then Return
            File.WriteAllBytes(dest, template)
        Catch
            ' Bloqueado (juego abierto) o sin permisos. Se reintenta en el proximo Save ESP.
        End Try
    End Sub

End Module
