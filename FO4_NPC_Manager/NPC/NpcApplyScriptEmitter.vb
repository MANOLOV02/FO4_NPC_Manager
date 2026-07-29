Imports System.IO
Imports FO4_Base_Library

''' <summary>Attaches our Papyrus apply-script to a saved NPC_ record (via VMAD) and installs the
''' compiled .pex, so the engine applies — on the actor's FIRST SPAWN — the RaceMenu/LooksMenu options
''' that have no other delivery route.
'''
''' <para>GAME-AWARE END TO END. The two games do not merely differ in API names; they differ in what
''' can be delivered at all, so there are two scripts and two payload shapes:</para>
''' <list type="table">
''' <item><term>SSE (RaceMenu / NiOverride)</term><description>overlays (texture + tint + alpha),
''' skin overrides PER SLOT, node transforms (scale + position).</description></item>
''' <item><term>FO4 (LooksMenu / Overlays + BodyGen)</term><description>overlays (template + tint + UV
''' + priority) and a skin override BY TEMPLATE ID. Node transforms DO NOT EXIST in FO4 — f4ee's
''' TransformInterface sits behind #ifdef _TRANSFORMS, is never registered to Papyrus and is not even
''' serialized to the co-save. Nothing to emit, and nothing lost: node transforms are an SSE-only
''' feature in this app.</description></item>
''' </list>
'''
''' <para>⭐ BODY MORPHS (BodySlide) ALSO SHIP HERE, in both games — see <see cref="BuildMorphArrays"/>.
''' They used to be the BodyGen <c>templates.ini</c>/<c>morphs.ini</c> pair's job exclusively; that pair is now
''' MUTUALLY EXCLUSIVE with the script, because neither engine "skips the one that already has morphs" once
''' both wrote: skee SUMS the per-key values of a morph (BodyMorphInterface.cpp:220-240, default
''' <c>iBodyMorphMode = 0</c>) and f4ee takes the MAX (BodyMorphInterface.cpp:1001-1009). The reason to move
''' them is the whole point: BodyGen is evaluated ONCE and gated on "this actor has no morphs at all"
''' (f4ee ActorUpdateManager.cpp:49-54, skee64 ActorUpdateManager.cpp:38-40), so a reference that ALREADY
''' exists in the player's save never gets it — while a <c>_G&lt;n&gt;</c>-suffixed property does.</para>
'''
''' <para>NOT emitted here, on purpose — these already reach the game another way, and sending them
''' again would apply them TWICE:</para>
''' <list type="bullet">
''' <item>Face morphs / sculpt / face TINTS → baked into the FaceGen NIF + textures, in BOTH games. The
''' script never emits a face tint of any kind.</item>
''' <item>FACE overlays, in SSE → the bake owns the head **only while `Setting_BakeSseRaceMenuOverlays` is
''' ON**; with that toggle OFF the bake does NOT fold them and the script IS their only route, so they ARE
''' emitted. <see cref="SkipFaceOverlays"/> is the single place that decides it, and
''' <see cref="SseOverlayCompositor.IsFaceOverlayNodeName"/> is the single place that defines what "face"
''' means (the bake, the render and this emitter all call it — five sites used to decide on their own and
''' they did not agree). Fallout 4 has NO face-overlay bake, so there the script emits every overlay, face
''' ones included: it is their only route.</item>
''' </list>
'''
''' <para>Idempotent by construction: <see cref="NpcVmadBuilder.UpsertScript"/> rewrites only scripts
''' under our reserved prefix and copies vanilla / other-mod scripts through byte-for-byte, so repeated
''' saves converge to exactly one copy of ours with the current values.</para></summary>
Public Module NpcApplyScriptEmitter

    ''' <summary>Script names — must match the compiled .pex filenames in <c>Papyrus\pex_*\</c>.</summary>
    ''' <summary>Nombre LEGADO de SSE: el que se emitia antes del esquema por plugin, y el que declara la
    ''' plantilla compilada. Se sigue usando para (a) limpiarlo de los VMAD viejos y (b) saber que string
    ''' reescribir dentro del .pex. NUNCA se emite.</summary>
    Public Const LegacyScriptSse As String = NpcVmadBuilder.ReservedScriptPrefix & "ApplySSE"

    ''' <summary>Generacion y SAL que trae la plantilla compilada (el sufijo <c>_G0000010000</c> de los .psc).
    ''' Los dos tienen que coincidir EXACTAMENTE con lo que declaran los .psc o el parcheo del .pex falla ruidoso.</summary>
    Public Const BaselineGeneration As Integer = 1
    Public Const BaselineSalt As String = PexPatcher.BaselineSalt
    Public Const LegacyScriptFo4 As String = NpcVmadBuilder.ReservedScriptPrefix & "ApplyFO4"

    ''' <summary>Name of the property that carries the payload version. Its value is NOT a constant: it is a
    ''' hash of THIS NPC's payload (<see cref="NpcVmadBuilder.StablePayloadHash"/>), stamped by
    ''' <see cref="StampVersion"/> once the rest of the properties are built.
    '''
    ''' <para>Why a per-NPC hash and not a global number: the script remembers, per actor instance in the
    ''' savegame, which version it already applied, and skips if unchanged. With a hash, editing ONE NPC and
    ''' re-saving changes ONLY that NPC's number — so only that actor re-applies on its next load, and every
    ''' other NPC in the plugin stays quiet. A global constant would force the whole plugin to re-apply on any
    ''' edit. This is also what makes re-application safe in FO4: the script removes the overlay uids it
    ''' minted last time (remembered per instance) before re-adding, so a re-apply cannot stack duplicates and
    ''' cannot touch overlays another mod added.</para></summary>
    Public Const VersionPropertyName As String = "SchemaVersion"

    ''' <summary>Generacion del payload (SSE). El sufijo _G<n> hace que una property tenga un NOMBRE que el
    ''' savegame del jugador NO tiene, y por eso el motor la inicializa desde el VMAD del plugin en vez de
    ''' restaurarla rancia del save. LEY MEDIDA en Skyrim SE 2026-07-28. Ver Papyrus\GENERACION_DEL_PAYLOAD.md.
    ''' <para>Tiene que coincidir con el sufijo del .psc. PENDIENTE: que la app la resuelva sola por plugin
    ''' (PexPatcher ya sabe leerla y reescribirla); hoy sigue siendo una constante.</para></summary>

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

    ''' <summary>⛔ EN SSE LA CARA ES DEL BAKE **MIENTRAS EL BAKE SE LA QUEDE**. Con
    ''' <c>Setting_BakeSseRaceMenuOverlays</c> ON el script no emite ningún nodo Face (lo hornea el bake); con ese
    ''' toggle OFF los emite, porque si no NADIE los aplica.
    '''
    ''' <para>⭐ CORREGIDO — este summary decía "SIEMPRE / NUNCA, sin condiciones, sin excepciones", y era falso
    ''' para UNA combinación, en la que los overlays quedaban SIN DUEÑO.
    ''' El argumento que estaba escrito acá era: "sin el bake de CharGen NO llega NADA de la cara al juego — los
    ''' morphs, el sculpt y los tints se hornean todos; un overlay de cara vivo sobre una cara vanilla sin hornear
    ''' sería el único elemento fuera de lugar, así que que no llegue es CONSISTENTE, no una pérdida". Eso vale con
    ''' el bake de CharGen APAGADO. NO cubre <b>CharGen bake ON + <c>Setting_BakeSseRaceMenuOverlays</c> OFF</b>:
    ''' ahí la cara SÍ se hornea (morphs, sculpt, tints y facetint llegan) y los overlays de cara no los hornea
    ''' nadie — y tampoco los emitía nadie. Se perdían. Para esa combinación no es consistencia, es una pérdida.</para>
    '''
    ''' <para>El gate vuelve, pero SOLO sobre el toggle de overlays, que es el que de verdad decide si el bake se
    ''' los queda. NO se vuelve a gatear por "el bake de CharGen corre": ESE era el agujero original (el flag de
    ''' CharGen es GLOBAL del guardado mientras el bake se saltea además POR NPC — raza sin FaceGen, sin HDPT de
    ''' cara —, así que siempre quedaba un caso donde el emisor decía "ya lo hornea el bake" y no lo horneaba).
    ''' Residual conocido y ACOTADO: con el toggle ON y un NPC que el bake se saltea por raza, su overlay de cara
    ''' no se emite; pero un NPC sin FaceGen no tiene cara que pintar, así que es inerte en la práctica.</para>
    '''
    ''' <para>⛔ ESTO EXIGE EL BARRIDO DE <c>Face [Ovl]</c> EN EL SCRIPT, y los dos cambios van JUNTOS. Todo entra
    ''' con <c>persist=true</c> (store de skee → co-save), así que un overlay aplicado con el toggle OFF sigue vivo
    ''' en esa partida aunque después se grabe con el toggle ON. Sin barrerlo quedaría aplicado DOS VECES: el que
    ''' sobrevive en el co-save + el que ahora está horneado en la textura. Ver <c>RemovePrevious()</c> en
    ''' NPCM_Manolov_ApplySSE.psc. Emitir sin barrer es PEOR que no emitir.</para>
    '''
    ''' <para>Fallout 4 es distinto y ahí sí emitimos TODOS los overlays, cara incluida: f4ee no tiene bake de
    ''' overlays de cara (<c>WriteSseFaceDiffuseWithOverlays</c> es SSE-only y nada más compone overlays dentro
    ''' de la textura de cara de FO4), así que el script es su ÚNICA vía. Lo que sí se hornea en FO4 es el TINT
    ''' de cara — y el script no emite tints en ningún juego.</para></summary>
    Private Function SkipFaceOverlays(game As Config_App.Game_Enum) As Boolean
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

    ''' <summary>⛔⛔ UNA ARRAY-PROPERTY NUNCA VA VACÍA **NI AUSENTE**. Cuando no hay datos se emite un array de
    ''' UN elemento CENTINELA que el script saltea solo ("" para nombres, 0 para slots/valores).
    '''
    ''' <para>Papyrus de Skyrim deja exactamente esa única salida, encerrado entre dos reglas — las dos MEDIDAS
    ''' en Papyrus.0.log, no supuestas:</para>
    ''' <list type="number">
    ''' <item>Un array de longitud 0 es ILEGAL: la propiedad falla al inicializar ("cannot be initialized
    ''' because the value is the incorrect type") y queda en None. Peor: envenena la instancia entera y TODAS
    ''' las demás arrays se leen como None.</item>
    ''' <item>Una propiedad AUSENTE también queda en None — y en Skyrim <c>if X == None</c> sobre un
    ''' array-property en None <b>TIRA</b> ("Cannot cast from None to Int[]"). O sea: el guard con el que uno se
    ''' protege ES lo que explota. No hay forma de chequear "está vacía" sin reventar.</item>
    ''' </list>
    '''
    ''' <para>Omitir era mi fix del bug (1) y provocó el bug (2). El centinela satisface las dos: la propiedad
    ''' SIEMPRE existe y NUNCA está vacía, así que nunca es None y nunca falla al inicializar. El script ya
    ''' saltea esos elementos (<c>if node != ""</c>, <c>if slot != 0</c>) sin ningún código nuevo.</para>
    '''
    ''' <para>Fallout 4 tolera arrays vacíos (los suyos son redimensionables) — pero el centinela se emite en
    ''' AMBOS juegos: una sola ley, y una regla menos que recordar.</para>
    '''
    ''' <para>Estos helpers son la ÚNICA vía por la que se agrega una array-property. NO llamar a
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

    ''' <summary>Techo de elementos por array del payload. Se aplica a TODAS las familias de arrays paralelos y
    ''' en los dos juegos: una sola ley.
    '''
    ''' <para>⭐⭐ <b>512 ESTÁ MEDIDO IN-GAME</b> (2026-07-28, SSE y FO4): un NPC con 512 body morphs en el VMAD
    ''' reportó <c>BM payload morphs=512</c> y <c>BM aplicados=512 de 512</c> con read-back correcto y CERO
    ''' errores en los dos juegos. Antes se probó 256 en SSE, también entero.</para>
    '''
    ''' <para>⛔ <b>RETRACTACIÓN</b>: este tope decía 128 "porque los arrays de Papyrus topan en 128". Eso
    ''' MEZCLABA DOS COSAS. 128 es un límite del COMPILADOR sobre <c>new T[n]</c> — medido compilando
    ''' <c>new float[129]</c>: SSE dice <i>"arrays must be between 1 and 128 elements in size"</i> y FO4
    ''' <i>"Array size of 129 is invalid. Must be between 0 and 128 (inclusive)"</i> (de paso confirma que FO4
    ''' acepta <c>[0]</c> y SSE no). Pero un array que arma el LOADER DEL VMAD no pasa por el compilador y
    ''' <b>no tiene ese techo</b>.</para>
    '''
    ''' <para>Entonces 512 no es un límite del motor, es una <b>decisión de costo</b>: en FO4 cada
    ''' <c>BodyGen.SetMorph</c> hace ceder la VM porque f4ee NO le pone <c>kFunctionFlag_NoWait</c> (sólo se lo
    ''' pone a la clase <c>Overlays</c>), así que 512 morphs tardan ~9 s de reloj; skee sí marca
    ''' <c>SetBodyMorph</c> como <c>NoWait</c> (PapyrusNiOverride.cpp:2556) y los mismos 512 tardan ≤2 s.
    ''' Un preset real (16-80 sliders) cuesta menos de 1,5 s en FO4. Ningún preset se acerca a 512.</para>
    '''
    ''' <para>El guard duro de verdad ya no es éste sino el techo de 64 KB del subrecord — ver
    ''' <see cref="VmadHardLimitBytes"/> y <c>NpcOverrideSaver.CheckVmadSize</c>. Con 512 morphs el VMAD pesó
    ''' ~11,9 KB (18 % del techo).</para></summary>
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

    ''' <summary>Body morphs de BodySlide → los dos arrays paralelos <c>MorphName</c>/<c>MorphValue</c> que
    ''' consumen los dos <c>.psc</c>. MISMA FORMA EN LOS DOS JUEGOS; lo único que cambia es qué hace el script
    ''' con ellos (SSE los mete bajo una key nuestra, FO4 bajo el keyword <c>None</c>).
    '''
    ''' <para><b>SSE — se SUMAN las contribuciones keyed.</b> RaceMenu guarda una entrada por cada fuente de
    ''' BodySlide (<c>BodyMorphsKeyed</c>: morph → key → valor) y skee las NETEA sumándolas al renderizar
    ''' (<c>Impl_GetBodyMorphs</c>, BodyMorphInterface.cpp:220-240, con el default <c>iBodyMorphMode = 0</c>).
    ''' Emitir la suma bajo UNA key nuestra rinde el mismo número y compra el deshacer quirúrgico
    ''' (<c>ClearBodyMorphKeys</c> saca sólo la nuestra). Es además exactamente lo que ya hacía
    ''' <c>EmitSseBodyGenFromSidecar</c> y lo que renderiza el preview, así que preview == juego.</para>
    '''
    ''' <para><b>FO4 — el dict plano</b> (<c>BodyMorphsKeyed</c> es SSE-only; f4ee no tiene keys de string,
    ''' tiene Keywords).</para>
    '''
    ''' <para>⛔ SE FILTRAN LOS CEROS. En FO4 <c>UserValues::SetValue</c> BORRA la entrada cuando el valor es
    ''' exactamente 0 (BodyMorphInterface.cpp:983-987), así que un 0 no significa "morph en cero" sino "morph
    ''' ausente"; en SSE sí se guardaría, pero sumaría 0 y sólo ocuparía lugar del techo de 128. Una sola ley.</para>
    '''
    ''' <para>Orden ordinal por nombre: el sello (<see cref="StampVersion"/>) es un hash del payload, así que
    ''' un orden dependiente del dict haría re-aplicar a NPCs que no cambiaron.</para>
    '''
    ''' <para>⚠️ Si hay más de <see cref="MaxArrayElements"/> morphs se recorta por |valor| descendente y se
    ''' LOGUEA lo que quedó afuera. Recortar en silencio sería exactamente el modo de falla que este proyecto
    ''' ya se comió dos veces ("aplicó todo" cuando no).</para></summary>
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
    ''' morphs en absoluto. ⛔ NO es un simple "no emitir": viaja al `.psc` como <c>MorphsOwned</c> porque en
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
            ' ACBS bit 0 = Female (identical in both games — verified against TES5Edit ACBS 'Flags').
            Dim isFemale = (npcSpec.AcbsFlags And 1UI) <> 0UI
            spec = BuildSpec(preset, game, isFemale, generation, salt, ownBodyMorphs, warnings)
        End If

        ' TRUE NO-OP for the common case: nothing to write AND nothing of ours to strip. Leave the VMAD
        ' object untouched — npcSpec may still BE the shared raw parse (ApplyPresetOverlayToNpcData
        ' returns it unchanged when the NPC has no preset), so replacing .Vmad here would mutate the
        ' cached record for a no-change save. Also keeps vanilla output byte-identical.
        Dim hadOurs = NpcVmadBuilder.HasAppScript(npcSpec.Vmad)
        If spec Is Nothing AndAlso Not hadOurs Then Return False

        ' ⛔ CLEANUP SCRIPT. The user cleared every option on an NPC we had previously scripted. Simply
        ' dropping the script would be WRONG: the overrides we pushed went into the co-save with
        ' persist=true, so the engine keeps re-applying them on every load, and with no script left nothing
        ' ever removes them — the tattoo would be welded to that actor forever. So we keep the script, with
        ' an EMPTY payload: its ledger still holds what it applied last time, RemovePrevious() undoes it,
        ' and then it applies nothing.
        '
        ' Unchecking "Emit apply-script" (enabled = False) is the deliberate exception: the user asked for
        ' the script GONE, so we strip it, and whatever is already in a running save stays there.
        If spec Is Nothing AndAlso enabled AndAlso hadOurs Then
            Dim isFemaleCleanup = (npcSpec.AcbsFlags And 1UI) <> 0UI
            spec = BuildCleanupSpec(game, isFemaleCleanup, generation, salt, ownBodyMorphs, warnings)
        End If

        ' UpsertScript(Nothing) removes ours and keeps the rest; it returns Nothing when nothing is left,
        ' which makes EmitVmad drop the VMAD subrecord (correct — the record had no scripts of its own).
        ' ⛔ EL BORRADO POR PREFIJO SE ACOTA A LO NUESTRO. UpsertScript borra TODO lo que empieza con el
        ' prefijo que se le pasa. Con el nombre por plugin, usar el prefijo generico NPCM_Manolov_ le
        ' borraria a OTRO AUTOR su script de este mismo record. Por eso van dos pasadas:
        '   1) limpiar el nombre LEGADO (el de antes del esquema por plugin), por prefijo EXACTO;
        '   2) upsert del nuestro, acotado a NUESTRO nombre completo.
        ' Que el stem del plugin vaya ANTES de 'ApplySSE' es lo que hace posible el paso 1 sin tocar la
        ' lib: asi el nombre legado NPCM_Manolov_ApplySSE no es prefijo de ningun nombre nuevo.
        Dim ourName = ScriptNameFor(game, pluginFileName)
        If spec IsNot Nothing Then spec.Name = ourName
        npcSpec.Vmad = NpcVmadBuilder.UpsertScript(npcSpec.Vmad, Nothing, game, LegacyScriptFor(game))
        npcSpec.Vmad = NpcVmadBuilder.UpsertScript(npcSpec.Vmad, spec, game, ourName)
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

    ''' <summary>Replace the version property's placeholder with the hash of everything else in the spec, so
    ''' the number changes exactly when THIS NPC's payload changes. See <see cref="VersionPropertyName"/>.</summary>
    ''' <summary>⭐ Revisión de la LÓGICA de los .psc. SUBIRLA cada vez que cambie el COMPORTAMIENTO de un
    ''' apply-script (no cuando cambien los datos de un NPC: eso ya lo cubre el hash del payload).
    '''
    ''' <para>POR QUÉ EXISTE (medido 2026-07-26): el sello se calculaba SÓLO sobre el payload, y el script
    ''' arranca con <c>if appliedVersion == SchemaVersion : return</c>. Entonces un arreglo del .pex no
    ''' llegaba nunca a los actores cuyo payload no había cambiado — se salteaban en la primera línea, sin
    ''' correr siquiera <c>RemovePrevious()</c>. Caso concreto: se le agregó al script el barrido de los
    ''' nodos <c>Face [Ovl*]</c>/<c>Face [SOvl*]</c> y no se ejecutó en ningún actor ya aplicado.</para>
    '''
    ''' <para>Al subirla cambia el sello de TODOS los NPC ⇒ cada actor re-aplica UNA vez y después vuelve el
    ''' comportamiento por-NPC de siempre. Ese re-apply global es el precio, y es intencional: es
    ''' exactamente lo que hay que pagar para que un cambio de lógica llegue. Por eso NO se toca al editar
    ''' un NPC — sólo al cambiar los .psc.</para>
    '''
    ''' <para>⚠️ ALCANCE — CORREGIDO. Esto decía que a una referencia que YA existe en el savegame "no la
    ''' alcanza ningún sello", y era cierto ANTES del esquema <c>_G&lt;n&gt;</c>. Ya no: una property con NOMBRE
    ''' NUEVO no está en el savegame, así que el motor la inicializa desde el VMAD en vez de restaurarla rancia
    ''' (LEY MEDIDA, Skyrim SE 2026-07-28, y verificada también en FO4). Por eso el sufijo de generación sube en
    ''' cada Save ESP. Lo que sí sigue congelado es <c>appliedVersion</c> — a propósito, es el lado que tiene que
    ''' persistir — y de ahí que un cambio de LÓGICA necesite esta revisión.</para>
    '''
    ''' <para>Historial:
    ''' 1 = comportamiento original.
    ''' 2 = RemovePrevious barre tambien los nodos Face + AddOverlays movido al inicio de OnLoad (el registro
    '''     en skee tiene que preceder al barrido).
    ''' 3 = payload con sufijo de generacion _G&lt;n&gt;.
    ''' 4 = nombre de script POR PLUGIN + guard de instancia huerfana en los dos juegos; SSE borra apagando
    '''     el nodo con KEY_ALPHA=0 (skee no tiene deshacer); FO4 pasa a Overlays.RemoveAll + Update y pierde
    '''     el ledger de uids (f4ee destruye y reconstruye el subarbol, asi que no hay nada que apilar).
    ''' 5 = BODY MORPHS de BodySlide entregados POR EL SCRIPT en los dos juegos (MorphName/MorphValue), con
    '''     barrido propio (SSE ClearBodyMorphKeys de NUESTRA key; FO4 RemoveMorphsByKeyword(None)) y repintado
    '''     incondicional (UpdateModelWeight / UpdateMorphs) para que la LIMPIEZA vuelva al cuerpo base. El par
    '''     BodyGen .ini pasa a ser MUTUAMENTE EXCLUYENTE con el script. Trazas [NPCM] BM ... para medirlo.
    ''' 6 = SSE barre TAMBIEN la key "RSMBodyGen". MEDIDO 2026-07-28: con el .ini ya borrado de disco Y ausente
    '''     de los BSA, un NPC aplicado con el BodySlide viejo seguia trayendo 39 morphs bajo esa key — estan
    '''     PERSISTIDOS EN EL CO-SAVE y skee los restaura y los SUMA a los nuestros. Borrar el .ini no alcanza
    '''     y no hay arreglo del lado de la app: el co-save es del jugador. FO4 no lo necesita (alla BodyGen
    '''     escribe en el slot None, que el .psc ya barria).
    ''' 7 = las trazas de los dos .psc pasan a estar gateadas por la property <c>Verbose_G&lt;n&gt;</c>, que el
    '''     emisor pone desde <c>Logger.Enabled</c> (Debug-only). NO gatea sólo el <c>Debug.Trace</c>: envuelve
    '''     los BLOQUES DE SONDA completos, porque GetMorphNames/GetMorphKeys/GetBodyMorph (SSE) y
    '''     GetMorphs/GetMorph/GetKeywords (FO4) son nativas que se llaman ÚNICAMENTE para trazar — y en FO4
    '''     cada nativa hace ceder la VM (f4ee no le pone NoWait a la clase BodyGen).
    ''' 8 = PODA TOTAL del actor antes de aplicar body morphs, en vez del barrido por key/keyword:
    '''     SSE <c>NiOverride.ClearMorphs</c>, FO4 <c>BodyGen.RemoveAllMorphs</c>. El barrido por key dejaba los
    '''     NOMBRES de morph huérfanos (con 0 keys) acumulándose en el co-save del jugador para siempre, porque
    '''     ningún motor poda un nombre vacío. Se lleva los morphs de otros mods sobre ESE actor — misma decisión
    '''     de producto que <c>Overlays.RemoveAll</c>: el NPC muestra exactamente lo que muestra la app.
    '''     ⚠️ Los dos motores NO son equivalentes: en SSE el store no tiene dimensión de género y se borra la
    '''     entrada entera; en FO4 el mapa es POR GÉNERO y el clear sólo alcanza al que se le pasa.
    ''' 9 = paridad de instrumentación entre los dos .psc: FO4 gana la sonda post-poda
    '''     (<c>BM morphs tras barrido=</c>, que ya tenía SSE y era la única forma de MEDIR la poda de frente
    '''     en vez de deducirla) y la identidad del primer overlay (<c>payload OvlTemplate[0]=</c>, sin la cual
    '''     el log decía cuántos overlays llegaban pero no cuáles). Sube la revisión aunque sean trazas porque
    '''     si no, los NPC cuyo payload no cambió saltean por el sello y la sonda nueva no correría nunca.
    ''' </para></summary>
    Private Const ScriptLogicRevision As String = "9"

    ''' <summary>Spec de LIMPIEZA: el NPC se quedó sin overlays/skin/transforms pero YA tenía script nuestro,
    ''' así que hay que dejarle uno que corra <c>RemovePrevious()</c> y no aplique nada.
    '''
    ''' <para>⛔ NO se arma a mano. Antes se construía con SÓLO <c>IsFemale</c> + <c>SchemaVersion</c>, y eso
    ''' ROMPÍA la garantía de la que el .psc depende explícitamente (cabecera del .psc, regla 2: «toda
    ''' array-property existe y trae al menos 1 elemento — un CENTINELA»). Una array-property ausente le llega
    ''' al script como <c>None</c>, y su <c>OvlNode.Length</c> revienta y aborta el stack — o sea que el spec
    ''' que existe PARA LIMPIAR moría antes de limpiar, sin que nadie lo viera salvo en el Papyrus.log.
    ''' Comparar contra None tampoco es salida (misma regla 2: el cast revienta igual). Por eso se construye
    ''' con el builder normal y <c>allowEmpty:=True</c>: las arrays salen por <c>AddArray</c> y el centinela
    ''' queda garantizado POR CONSTRUCCIÓN, sin duplicar acá la lista de nombres de propiedades.</para></summary>
    ''' <param name="ownBodyMorphs">Se propaga TAL CUAL: un spec de limpieza con <c>MorphsOwned = True</c> es
    ''' justamente lo que hace que el script barra los body morphs que aplicó la vez anterior. Con False no
    ''' toca morphs — correcto, porque en ese modo nunca fueron suyos.</param>
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
                If skipFace AndAlso IsFaceNode(ov.NodeName) Then Continue For
                ' Nothing to override on this node → don't emit an empty entry.
                If String.IsNullOrEmpty(ov.DiffusePath) AndAlso String.IsNullOrEmpty(ov.NormalPath) AndAlso
                   Not ov.HasTint AndAlso Not ov.HasAlpha Then Continue For
                ' ⛔ EL TOPE SE APLICA EN LA FUENTE, no recortando los arrays después: así los 7 arrays
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

                ' ⛔ REINTERPRET the bits, do NOT convert. SlotMask is a UInteger and comes from the .jslot
                ' untruncated (RaceMenuJslot.vb ~:531). Skyrim biped slot 61 = bit 31 = &H80000000 = 2147483648,
                ' which is > Int32.MaxValue — and VB's integer overflow checks are ON (see the notes in
                ' NpcVmadBuilder/NpcVmadScanner), so CInt() would THROW and take the whole save down. The bit
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
        ' Rotation = the 3x3 matrix, row-major, split across NINE parallel arrays (element k of node i lives
        ' at ndRotM(k)(i)) — NOT one flat 9xN array.
        '
        ' ⛔ POR QUÉ VAN PARTIDOS — Y LA RAZÓN ORIGINAL ERA FALSA. Acá decía: "los arrays de Papyrus topan en
        ' 128 ELEMENTOS, así que uno plano de 9xN desbordaría a los 15 nodos". MEDIDO 2026-07-28 y REFUTADO:
        ' un array servido por el VMAD llega con 512 elementos sin problema en los DOS juegos (ver
        ' MaxArrayElements). El 128 es del COMPILADOR sobre `new T[n]`, que no interviene acá.
        ' Se mantiene el split igual, por dos motivos que sí valen: el .psc los consume como nueve arrays
        ' paralelos (cambiarlo obligaría a tocar la lógica y subir ScriptLogicRevision para nada), y con un
        ' array por elemento el índice i significa "nodo i" en TODAS las arrays del grupo, que es la misma
        ' invariante que sostiene overlays, skin y morphs.
        '
        ' NOT euler — AddNodeTransformRotation accepts 3 (euler degrees) OR 9 (raw matrix), and with 9 it
        ' copies them straight into NiMatrix33::arr[i] (PapyrusNiOverride.cpp:1190-1193), the same arr[i] it
        ' packs out under key 32 index i — exactly what the .jslot stores. So we hand skee back its own float
        ' sequence and no euler convention is involved. RaceMenuJslot.RotationRowMajor is the SAME function
        ' that writes key 32 to the .jslot, so the script and the .jslot cannot diverge.
        Dim ndRotM(8) As List(Of Single)
        For k = 0 To 8 : ndRotM(k) = New List(Of Single)() : Next
        Dim ndScaleMode As New List(Of Integer)

        Dim ndDropped = 0
        If preset.SseNodeTransforms IsNot Nothing Then
            For Each nt In preset.SseNodeTransforms
                If nt Is Nothing OrElse String.IsNullOrEmpty(nt.NodeName) Then Continue For
                If Not (nt.HasScale OrElse nt.HasPosition OrElse nt.HasRotation) Then Continue For
                ' ⛔ ESTE ES EL ÚNICO ARRAY GENUINAMENTE ILIMITADO del payload: los overlays los acota el motor
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

                ndScaleMode.Add(If(nt.HasScaleMode, nt.ScaleMode, -1))
            Next
        End If

        ' --- body morphs (BodySlide). Ver BuildMorphArrays: en SSE se suman las contribuciones keyed y van
        ' bajo UNA key nuestra, que es lo que hace posible ClearBodyMorphKeys como deshacer quirúrgico.
        NoteTrim(warnings, ovDropped, "overlay(s)")
        NoteTrim(warnings, skDropped, "skin override(s)")
        NoteTrim(warnings, ndDropped, "node transform(s)")

        Dim mNames As New List(Of String), mValues As New List(Of Single)
        If ownBodyMorphs Then BuildMorphArrays(preset, Config_App.Game_Enum.Skyrim, mNames, mValues, warnings)

        ' ⛔ mNames CUENTA para "¿hay algo que aplicar?". Sin esto, un NPC cuyo ÚNICO dato son los body
        ' morphs no recibiría script y sus sliders no llegarían por ninguna vía (el .ini ya no se emite).
        If Not allowEmpty AndAlso ovNode.Count = 0 AndAlso skSlot.Count = 0 AndAlso ndName.Count = 0 AndAlso mNames.Count = 0 Then Return Nothing

        Dim spec As New NpcVmadBuilder.VmadScriptSpec With {.Name = LegacyScriptSse}
        Dim P = spec.Properties
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("IsFemale", generation, salt), isFemale))
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromInt(GenProp(VersionPropertyName, generation, salt), 0))   ' placeholder — StampVersion overwrites it with the payload hash
        ' ⭐ Verbose: el script traza SOLO cuando la app esta diagnosticando. Logger.Enabled ya es la senal
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
                '   tint  → (0,0,0,0)   ⛔ NOT white. f4ee treats the tint as absent only when it is exactly
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

        ' ⛔ mNames CUENTA para "¿hay algo que aplicar?" — mismo motivo que en SSE.
        If Not allowEmpty AndAlso tpl.Count = 0 AndAlso skin = "" AndAlso mNames.Count = 0 Then Return Nothing

        Dim spec As New NpcVmadBuilder.VmadScriptSpec With {.Name = LegacyScriptFo4}
        Dim P = spec.Properties
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("IsFemale", generation, salt), isFemale))
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromInt(GenProp(VersionPropertyName, generation, salt), 0))   ' placeholder — StampVersion overwrites it with the payload hash
        ' ⭐ Verbose: el script traza SOLO cuando la app esta diagnosticando. Logger.Enabled ya es la senal
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

    ''' <summary>Script (and .pex file) name for this game.</summary>
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

    ''' <summary>The compiled .pex is EMBEDDED in this assembly (see the EmbeddedResource items in the
    ''' .vbproj), not read from a folder next to the exe. Two reasons, and the second is the real one:
    ''' <list type="number">
    ''' <item>Nothing to lose when the app is moved or handed to someone else.</item>
    ''' <item>The .pex can never drift out of sync with the app build that emitted the VMAD referencing it.
    ''' A stale loose .pex would silently ignore any property it does not know about — the script would run,
    ''' apply nothing, and report nothing. That is the worst possible failure mode, so we make it
    ''' unrepresentable.</item>
    ''' </list>
    ''' Returns Nothing if the resource is missing (i.e. the app was built without running the Papyrus
    ''' compile step — see Papyrus\README.md).</summary>
    Public Function PexBytes(game As Config_App.Game_Enum) As Byte()
        ' El recurso embebido es la PLANTILLA, asi que su nombre es el de la compilacion (legado), NO el
        ' nombre por plugin: ese lo pone PatchedPexBytes reescribiendo el .pex.
        Dim templateName = LegacyScriptFor(game)
        Dim resourceName = "NpcManager.Papyrus." & templateName & ".pex"   ' LogicalName pinned in the .vbproj
        Using s = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            If s Is Nothing Then Return Nothing
            Using ms As New MemoryStream()
                s.CopyTo(ms)
                Return ms.ToArray()
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
    ''' <para>MEDIDO 2026-07-28. Si ese .pex NO esta, el savegame del jugador sigue teniendo instancias de ese
    ''' tipo pegadas al actor, el tipo no resuelve, y —como el script extends Actor— ese actor queda SIN TABLA
    ''' DE METODOS PARA TODOS LOS DEMAS SCRIPTS. No es que no se apliquen nuestros overlays: le rompemos el NPC
    ''' a cualquier mod. Observado: RaceMenuHHScaleEffect fallando 17 veces sobre FF000911 con 'Method
    ''' GetLeveledActorBase not found on NPCM_Manolov_ApplySSE' y 'Cannot call GetSex() on a None object'.</para>
    ''' <para>Y no re-aplica nada: el .psc corta con el guard de instancia huerfana (arrays de longitud 0 = el
    ''' VMAD no nombra a este script). Resuelve el tipo y no hace nada mas.</para>
    ''' <para>Artefacto de MIGRACION: se puede dejar de shippear cuando ningun savegame publicado arrastre
    ''' instancias del nombre viejo. Como eso no se puede saber, se queda.</para></summary>
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
