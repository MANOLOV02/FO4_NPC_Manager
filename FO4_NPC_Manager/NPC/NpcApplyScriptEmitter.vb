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
''' <para>NOT emitted here, on purpose — these already reach the game another way, and sending them
''' again would apply them TWICE:</para>
''' <list type="bullet">
''' <item>Body morphs → the BodyGen templates.ini/morphs.ini pair (BodyGenIniWriter / SseBodyGenIniWriter).</item>
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

    ''' <summary>Generacion que trae la plantilla compilada (el sufijo _G000001 del .psc).</summary>
    Public Const BaselineGeneration As Integer = 1
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
    Private Function GenProp(baseName As String, generation As Integer) As String
        Return baseName & PexPatcher.GenerationSuffix(generation)
    End Function

    ''' <summary>Nombre real de la property de version. SSE lleva sufijo; FO4 todavia no.</summary>
    Private Function VersionPropertyNameFor(game As Config_App.Game_Enum, generation As Integer) As String
        Return GenProp(VersionPropertyName, generation)
    End Function

    ''' <summary>Nombre del script ANTES del esquema por plugin, por juego. Se limpia del VMAD, y es lo
    ''' que PexPatcher busca dentro de la plantilla para renombrarla.</summary>
    Private Function LegacyScriptFor(game As Config_App.Game_Enum) As String
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

    ''' <summary>Pack a 0..1 RGBA tint into skee's 0xAARRGGBB int (kParam_ShaderTintColor, key 7).</summary>
    Private Function PackTint(r As Single, g As Single, b As Single, a As Single) As Integer
        Dim ToByte = Function(v As Single) CInt(Math.Round(Math.Max(0.0F, Math.Min(1.0F, v)) * 255.0F))
        Return (ToByte(a) << 24) Or (ToByte(r) << 16) Or (ToByte(g) << 8) Or ToByte(b)
    End Function

    ''' <summary>Upsert (or remove) our script on <paramref name="npcSpec"/>'s VMAD.
    ''' <paramref name="enabled"/> = False removes ours and keeps every other script — so unchecking the
    ''' option in Save ESP actually strips a previously-emitted script instead of leaving it stale.
    ''' Returns True when a script was written (the caller uses that to decide whether to install the .pex).</summary>
    Public Function ApplyToNpc(npcSpec As NPC_Data,
                               preset As LooksmenuLoader.LooksmenuPreset,
                               game As Config_App.Game_Enum,
                               enabled As Boolean,
                               pluginFileName As String,
                               generation As Integer) As Boolean
        If npcSpec Is Nothing Then Return False

        Dim spec As NpcVmadBuilder.VmadScriptSpec = Nothing
        If enabled Then
            ' ACBS bit 0 = Female (identical in both games — verified against TES5Edit ACBS 'Flags').
            Dim isFemale = (npcSpec.AcbsFlags And 1UI) <> 0UI
            spec = BuildSpec(preset, game, isFemale, generation)
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
            spec = BuildCleanupSpec(game, isFemaleCleanup, generation)
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
                              generation As Integer) As NpcVmadBuilder.VmadScriptSpec
        If preset Is Nothing Then Return Nothing
        Dim spec = If(game = Config_App.Game_Enum.Skyrim,
                      BuildSpecSse(preset, isFemale, generation),
                      BuildSpecFo4(preset, isFemale, generation))
        Return StampVersion(spec, game, generation)
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
    ''' <para>⚠️ ALCANCE REAL: esto sólo sirve para las instancias que de verdad releen sus propiedades del
    ''' record. Una referencia que YA existe en el savegame del usuario conserva las propiedades que tenía
    ''' al crearse (medido: copia vieja no cambia, copia nueva por <c>placeatme</c> sí), así que a esa NO la
    ''' alcanza ningún sello. Ese techo es del diseño de llevar el payload en propiedades del VMAD y no se
    ''' arregla acá.</para>
    '''
    ''' <para>Historial:
    ''' 1 = comportamiento original.
    ''' 2 = RemovePrevious barre tambien los nodos Face + AddOverlays movido al inicio de OnLoad (el registro
    '''     en skee tiene que preceder al barrido).
    ''' 3 = payload con sufijo de generacion _G&lt;n&gt;.
    ''' 4 = nombre de script POR PLUGIN + guard de instancia huerfana en los dos juegos; SSE borra apagando
    '''     el nodo con KEY_ALPHA=0 (skee no tiene deshacer); FO4 pasa a Overlays.RemoveAll + Update y pierde
    '''     el ledger de uids (f4ee destruye y reconstruye el subarbol, asi que no hay nada que apilar).
    ''' </para></summary>
    Private Const ScriptLogicRevision As String = "4"

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
    Private Function BuildCleanupSpec(game As Config_App.Game_Enum, isFemale As Boolean, generation As Integer) As NpcVmadBuilder.VmadScriptSpec
        Dim emptyPreset As New LooksmenuLoader.LooksmenuPreset()
        Return StampVersion(If(game = Config_App.Game_Enum.Skyrim,
                               BuildSpecSse(emptyPreset, isFemale, generation, allowEmpty:=True),
                               BuildSpecFo4(emptyPreset, isFemale, generation, allowEmpty:=True)), game, generation)
    End Function

    Private Function StampVersion(spec As NpcVmadBuilder.VmadScriptSpec,
                                  game As Config_App.Game_Enum,
                                  generation As Integer) As NpcVmadBuilder.VmadScriptSpec
        If spec Is Nothing Then Return Nothing
        Dim versionProp = VersionPropertyNameFor(game, generation)
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
                                  Optional allowEmpty As Boolean = False) As NpcVmadBuilder.VmadScriptSpec
        ' SSE: los nodos Face se emiten SOLO si el bake NO se los queda (toggle de overlays OFF). Ver SkipFaceOverlays.
        Dim skipFace = SkipFaceOverlays(Config_App.Game_Enum.Skyrim)

        ' --- overlays (Body/Hands/Feet; Face only when it is NOT being baked)
        Dim ovNode As New List(Of String), ovDiff As New List(Of String), ovNorm As New List(Of String)
        Dim ovHasTint As New List(Of Boolean), ovTint As New List(Of Integer)
        Dim ovHasAlpha As New List(Of Boolean), ovAlpha As New List(Of Single)

        If preset.SseBodyOverlays IsNot Nothing Then
            For Each ov In preset.SseBodyOverlays
                If ov Is Nothing OrElse String.IsNullOrEmpty(ov.NodeName) Then Continue For
                ' La cara es del bake sólo cuando el bake la pliega; si no, va por acá. Ver SkipFaceOverlays.
                If skipFace AndAlso IsFaceNode(ov.NodeName) Then Continue For
                ' Nothing to override on this node → don't emit an empty entry.
                If String.IsNullOrEmpty(ov.DiffusePath) AndAlso String.IsNullOrEmpty(ov.NormalPath) AndAlso
                   Not ov.HasTint AndAlso Not ov.HasAlpha Then Continue For

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

        If preset.SseSkinOverrides IsNot Nothing Then
            For Each sk In preset.SseSkinOverrides
                If sk Is Nothing OrElse sk.SlotMask = 0UI Then Continue For
                If String.IsNullOrEmpty(sk.DiffusePath) AndAlso String.IsNullOrEmpty(sk.NormalPath) AndAlso
                   Not sk.HasTint Then Continue For

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
        ' ⛔ Why split: Papyrus arrays in Skyrim are capped at 128 ELEMENTS. A flat 9xN array would overflow
        ' that at 15 node transforms, and the editor lets the user pick far more bones than that. Nine arrays
        ' of length N keep the ceiling at 128 NODES, same as every other array in the payload.
        '
        ' NOT euler — AddNodeTransformRotation accepts 3 (euler degrees) OR 9 (raw matrix), and with 9 it
        ' copies them straight into NiMatrix33::arr[i] (PapyrusNiOverride.cpp:1190-1193), the same arr[i] it
        ' packs out under key 32 index i — exactly what the .jslot stores. So we hand skee back its own float
        ' sequence and no euler convention is involved. RaceMenuJslot.RotationRowMajor is the SAME function
        ' that writes key 32 to the .jslot, so the script and the .jslot cannot diverge.
        Dim ndRotM(8) As List(Of Single)
        For k = 0 To 8 : ndRotM(k) = New List(Of Single)() : Next
        Dim ndScaleMode As New List(Of Integer)

        If preset.SseNodeTransforms IsNot Nothing Then
            For Each nt In preset.SseNodeTransforms
                If nt Is Nothing OrElse String.IsNullOrEmpty(nt.NodeName) Then Continue For
                If Not (nt.HasScale OrElse nt.HasPosition OrElse nt.HasRotation) Then Continue For

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

        If Not allowEmpty AndAlso ovNode.Count = 0 AndAlso skSlot.Count = 0 AndAlso ndName.Count = 0 Then Return Nothing

        Dim spec As New NpcVmadBuilder.VmadScriptSpec With {.Name = LegacyScriptSse}
        Dim P = spec.Properties
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("IsFemale", generation), isFemale))
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromInt(GenProp(VersionPropertyName, generation), 0))   ' placeholder — StampVersion overwrites it with the payload hash

        AddArray(P, GenProp("OvlNode", generation), ovNode)
        AddArray(P, GenProp("OvlDiffuse", generation), ovDiff)
        AddArray(P, GenProp("OvlNormal", generation), ovNorm)
        AddArray(P, GenProp("OvlHasTint", generation), ovHasTint)
        AddArray(P, GenProp("OvlTint", generation), ovTint)
        AddArray(P, GenProp("OvlHasAlpha", generation), ovHasAlpha)
        AddArray(P, GenProp("OvlAlpha", generation), ovAlpha)

        AddArray(P, GenProp("SkinSlot", generation), skSlot)
        AddArray(P, GenProp("SkinDiffuse", generation), skDiff)
        AddArray(P, GenProp("SkinNormal", generation), skNorm)
        AddArray(P, GenProp("SkinHasTint", generation), skHasTint)
        AddArray(P, GenProp("SkinTint", generation), skTint)

        AddArray(P, GenProp("NodeName", generation), ndName)
        AddArray(P, GenProp("NodeHasScale", generation), ndHasScale)
        AddArray(P, GenProp("NodeScale", generation), ndScale)
        AddArray(P, GenProp("NodeHasPos", generation), ndHasPos)
        AddArray(P, GenProp("NodePosX", generation), ndPosX)
        AddArray(P, GenProp("NodePosY", generation), ndPosY)
        AddArray(P, GenProp("NodePosZ", generation), ndPosZ)
        AddArray(P, GenProp("NodeHasRot", generation), ndHasRot)
        For k = 0 To 8
            AddArray(P, GenProp("NodeRotM" & k.ToString(Globalization.CultureInfo.InvariantCulture), generation), ndRotM(k))
        Next
        AddArray(P, GenProp("NodeScaleMode", generation), ndScaleMode)

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
                                  Optional allowEmpty As Boolean = False) As NpcVmadBuilder.VmadScriptSpec
        Dim tpl As New List(Of String), prio As New List(Of Integer)
        Dim r As New List(Of Single), g As New List(Of Single), b As New List(Of Single), a As New List(Of Single)
        Dim ou As New List(Of Single), ov As New List(Of Single)
        Dim su As New List(Of Single), sv As New List(Of Single)

        If preset.Overlays IsNot Nothing Then
            For Each e In preset.Overlays
                If e Is Nothing OrElse String.IsNullOrEmpty(e.TemplateId) Then Continue For
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

        If Not allowEmpty AndAlso tpl.Count = 0 AndAlso skin = "" Then Return Nothing

        Dim spec As New NpcVmadBuilder.VmadScriptSpec With {.Name = LegacyScriptFo4}
        Dim P = spec.Properties
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool(GenProp("IsFemale", generation), isFemale))
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromInt(GenProp(VersionPropertyName, generation), 0))   ' placeholder — StampVersion overwrites it with the payload hash
        AddArray(P, GenProp("OvlTemplate", generation), tpl)
        AddArray(P, GenProp("OvlPriority", generation), prio)
        AddArray(P, GenProp("OvlRed", generation), r)
        AddArray(P, GenProp("OvlGreen", generation), g)
        AddArray(P, GenProp("OvlBlue", generation), b)
        AddArray(P, GenProp("OvlAlpha", generation), a)
        AddArray(P, GenProp("OvlOffsetU", generation), ou)
        AddArray(P, GenProp("OvlOffsetV", generation), ov)
        AddArray(P, GenProp("OvlScaleU", generation), su)
        AddArray(P, GenProp("OvlScaleV", generation), sv)
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromString(GenProp("SkinTemplate", generation), skin))

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
    Public Function PatchedPexBytes(game As Config_App.Game_Enum, pluginFileName As String, generation As Integer) As Byte()
        Dim template = PexBytes(game)
        If template Is Nothing OrElse template.Length = 0 Then Return Nothing
        Return PexPatcher.PatchScript(template, LegacyScriptFor(game), ScriptNameFor(game, pluginFileName),
                                      BaselineGeneration, generation)
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
                               pluginFileName As String, generation As Integer) As String
        If String.IsNullOrEmpty(dataPath) Then Return Nothing
        Dim bytes = PatchedPexBytes(game, pluginFileName, generation)
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
