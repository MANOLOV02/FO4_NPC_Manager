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
''' <item>FACE overlays, in SSE → the bake owns the head, ALWAYS. No Face node is ever emitted, with no
''' condition attached. <see cref="SkipFaceOverlays"/> is the single place that says so, and
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
    Public Const ScriptNameSse As String = NpcVmadBuilder.ReservedScriptPrefix & "ApplySSE"
    Public Const ScriptNameFo4 As String = NpcVmadBuilder.ReservedScriptPrefix & "ApplyFO4"

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

    ''' <summary>⛔⛔ EN SSE LA CARA ES DEL BAKE. SIEMPRE. El script no aplica NUNCA un nodo Face — sin
    ''' condiciones, sin excepciones, sin "salvo que…".
    '''
    ''' <para>Esto NO está gateado por si el bake corre o no, y es a propósito. Antes lo estaba (CharGen bake ON
    ''' + <c>Setting_BakeSseRaceMenuOverlays</c> ON) y era una fuente inagotable de agujeros: el flag de CharGen
    ''' es GLOBAL del guardado, pero el bake se saltea además POR NPC (raza sin FaceGen, sin HDPT de tipo Face),
    ''' así que siempre quedaba un caso donde el emisor decía "ya lo hornea el bake" y el bake no lo horneaba.</para>
    '''
    ''' <para>Y la condición nunca tuvo sentido: sin el bake de CharGen NO llega NADA de la cara al juego — los
    ''' morphs, el sculpt y los tints se hornean todos. Un overlay de cara aplicado como decal vivo sobre una
    ''' cara vanilla sin hornear sería el único elemento fuera de lugar. Que no llegue es CONSISTENTE, no una
    ''' pérdida.</para>
    '''
    ''' <para>Fallout 4 es distinto y ahí sí emitimos TODOS los overlays, cara incluida: f4ee no tiene bake de
    ''' overlays de cara (<c>WriteSseFaceDiffuseWithOverlays</c> es SSE-only y nada más compone overlays dentro
    ''' de la textura de cara de FO4), así que el script es su ÚNICA vía. Lo que sí se hornea en FO4 es el TINT
    ''' de cara — y el script no emite tints en ningún juego.</para></summary>
    Private Function SkipFaceOverlays(game As Config_App.Game_Enum) As Boolean
        Return game = Config_App.Game_Enum.Skyrim
    End Function

    ''' <summary>Predicado ÚNICO, compartido con el bake (CPU y GPU) y el render. Si el emisor y el bake no
    ''' coinciden EXACTAMENTE en qué es "de cara", un overlay se compone dos veces o ninguna.</summary>
    Private Function IsFaceNode(nodeName As String) As Boolean
        Return SseOverlayCompositor.IsFaceOverlayNodeName(nodeName)
    End Function

    ''' <summary>⛔ NEVER EMIT A ZERO-LENGTH ARRAY PROPERTY. Skyrim's Papyrus has no zero-length arrays, so a
    ''' VMAD array property with count 0 fails to initialize — the VM logs "cannot be initialized because the
    ''' value is the incorrect type" and leaves the property None. Worse, that poisons the whole script
    ''' instance: every other array property reads back as None too, and the apply silently does nothing.
    ''' (MEASURED: an NPC with overlays + node transforms but no skin overrides emitted 5 empty Skin* arrays;
    ''' the log then showed 20 "Cannot cast from None to X[]" errors and the node rotations never applied.)
    '''
    ''' <para>Fallout 4's arrays are resizable and tolerate empty, which is exactly why the FO4 script worked
    ''' while the SSE one did not — but we omit empties in BOTH games, because a property that is simply
    ''' absent is what both scripts already handle (they guard every array with <c>!= None</c>).</para>
    '''
    ''' <para>These helpers are the ONLY way array properties get added. Do not call
    ''' <c>VmadPropertySpec.From*Array</c> directly from the builders.</para></summary>
    Private Sub AddArray(props As List(Of NpcVmadBuilder.VmadPropertySpec), name As String, values As List(Of String))
        If values Is Nothing OrElse values.Count = 0 Then Return
        props.Add(NpcVmadBuilder.VmadPropertySpec.FromStringArray(name, values))
    End Sub

    Private Sub AddArray(props As List(Of NpcVmadBuilder.VmadPropertySpec), name As String, values As List(Of Integer))
        If values Is Nothing OrElse values.Count = 0 Then Return
        props.Add(NpcVmadBuilder.VmadPropertySpec.FromIntArray(name, values))
    End Sub

    Private Sub AddArray(props As List(Of NpcVmadBuilder.VmadPropertySpec), name As String, values As List(Of Single))
        If values Is Nothing OrElse values.Count = 0 Then Return
        props.Add(NpcVmadBuilder.VmadPropertySpec.FromFloatArray(name, values))
    End Sub

    Private Sub AddArray(props As List(Of NpcVmadBuilder.VmadPropertySpec), name As String, values As List(Of Boolean))
        If values Is Nothing OrElse values.Count = 0 Then Return
        props.Add(NpcVmadBuilder.VmadPropertySpec.FromBoolArray(name, values))
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
                               enabled As Boolean) As Boolean
        If npcSpec Is Nothing Then Return False

        Dim spec As NpcVmadBuilder.VmadScriptSpec = Nothing
        If enabled Then
            ' ACBS bit 0 = Female (identical in both games — verified against TES5Edit ACBS 'Flags').
            Dim isFemale = (npcSpec.AcbsFlags And 1UI) <> 0UI
            spec = BuildSpec(preset, game, isFemale)
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
            spec = New NpcVmadBuilder.VmadScriptSpec With {.Name = ScriptNameFor(game)}
            spec.Properties.Add(NpcVmadBuilder.VmadPropertySpec.FromBool("IsFemale", isFemaleCleanup))
            spec.Properties.Add(NpcVmadBuilder.VmadPropertySpec.FromInt(VersionPropertyName, 0))
            spec = StampVersion(spec)
        End If

        ' UpsertScript(Nothing) removes ours and keeps the rest; it returns Nothing when nothing is left,
        ' which makes EmitVmad drop the VMAD subrecord (correct — the record had no scripts of its own).
        npcSpec.Vmad = NpcVmadBuilder.UpsertScript(npcSpec.Vmad, spec, game)
        Return spec IsNot Nothing
    End Function

    ''' <summary>Build the script spec for this NPC, or Nothing when there is nothing to apply (the
    ''' overwhelmingly common case — an NPC with no overlays / skin / node transforms gets NO script,
    ''' so vanilla records stay untouched).</summary>
    Public Function BuildSpec(preset As LooksmenuLoader.LooksmenuPreset,
                              game As Config_App.Game_Enum,
                              isFemale As Boolean) As NpcVmadBuilder.VmadScriptSpec
        If preset Is Nothing Then Return Nothing
        Dim spec = If(game = Config_App.Game_Enum.Skyrim,
                      BuildSpecSse(preset, isFemale),
                      BuildSpecFo4(preset, isFemale))
        Return StampVersion(spec)
    End Function

    ''' <summary>Replace the version property's placeholder with the hash of everything else in the spec, so
    ''' the number changes exactly when THIS NPC's payload changes. See <see cref="VersionPropertyName"/>.</summary>
    Private Function StampVersion(spec As NpcVmadBuilder.VmadScriptSpec) As NpcVmadBuilder.VmadScriptSpec
        If spec Is Nothing Then Return Nothing
        Dim hash = NpcVmadBuilder.StablePayloadHash(spec, VersionPropertyName)
        For i = 0 To spec.Properties.Count - 1
            If String.Equals(spec.Properties(i).Name, VersionPropertyName, StringComparison.Ordinal) Then
                spec.Properties(i) = NpcVmadBuilder.VmadPropertySpec.FromInt(VersionPropertyName, hash)
                Exit For
            End If
        Next
        Return spec
    End Function

    ' ============================================================================================
    ' SSE — RaceMenu / NiOverride
    ' ============================================================================================
    Private Function BuildSpecSse(preset As LooksmenuLoader.LooksmenuPreset,
                                  isFemale As Boolean) As NpcVmadBuilder.VmadScriptSpec
        ' SSE: los nodos Face NUNCA se emiten. La cara es del bake, siempre. Ver SkipFaceOverlays.
        Dim skipFace = SkipFaceOverlays(Config_App.Game_Enum.Skyrim)

        ' --- overlays (Body/Hands/Feet; Face only when it is NOT being baked)
        Dim ovNode As New List(Of String), ovDiff As New List(Of String), ovNorm As New List(Of String)
        Dim ovHasTint As New List(Of Boolean), ovTint As New List(Of Integer)
        Dim ovHasAlpha As New List(Of Boolean), ovAlpha As New List(Of Single)

        If preset.SseBodyOverlays IsNot Nothing Then
            For Each ov In preset.SseBodyOverlays
                If ov Is Nothing OrElse String.IsNullOrEmpty(ov.NodeName) Then Continue For
                ' The face belongs to the bake — ALL Face nodes, no exceptions. See IsFaceNode.
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

        If ovNode.Count = 0 AndAlso skSlot.Count = 0 AndAlso ndName.Count = 0 Then Return Nothing

        Dim spec As New NpcVmadBuilder.VmadScriptSpec With {.Name = ScriptNameSse}
        Dim P = spec.Properties
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool("IsFemale", isFemale))
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromInt(VersionPropertyName, 0))   ' placeholder — StampVersion overwrites it with the payload hash

        AddArray(P, "OvlNode", ovNode)
        AddArray(P, "OvlDiffuse", ovDiff)
        AddArray(P, "OvlNormal", ovNorm)
        AddArray(P, "OvlHasTint", ovHasTint)
        AddArray(P, "OvlTint", ovTint)
        AddArray(P, "OvlHasAlpha", ovHasAlpha)
        AddArray(P, "OvlAlpha", ovAlpha)

        AddArray(P, "SkinSlot", skSlot)
        AddArray(P, "SkinDiffuse", skDiff)
        AddArray(P, "SkinNormal", skNorm)
        AddArray(P, "SkinHasTint", skHasTint)
        AddArray(P, "SkinTint", skTint)

        AddArray(P, "NodeName", ndName)
        AddArray(P, "NodeHasScale", ndHasScale)
        AddArray(P, "NodeScale", ndScale)
        AddArray(P, "NodeHasPos", ndHasPos)
        AddArray(P, "NodePosX", ndPosX)
        AddArray(P, "NodePosY", ndPosY)
        AddArray(P, "NodePosZ", ndPosZ)
        AddArray(P, "NodeHasRot", ndHasRot)
        For k = 0 To 8
            AddArray(P, "NodeRotM" & k.ToString(Globalization.CultureInfo.InvariantCulture), ndRotM(k))
        Next
        AddArray(P, "NodeScaleMode", ndScaleMode)

        Return spec
    End Function

    ' ============================================================================================
    ' FO4 — LooksMenu / Overlays + BodyGen
    ' ============================================================================================
    Private Function BuildSpecFo4(preset As LooksmenuLoader.LooksmenuPreset,
                                  isFemale As Boolean) As NpcVmadBuilder.VmadScriptSpec
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

        If tpl.Count = 0 AndAlso skin = "" Then Return Nothing

        Dim spec As New NpcVmadBuilder.VmadScriptSpec With {.Name = ScriptNameFo4}
        Dim P = spec.Properties
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromBool("IsFemale", isFemale))
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromInt(VersionPropertyName, 0))   ' placeholder — StampVersion overwrites it with the payload hash
        AddArray(P, "OvlTemplate", tpl)
        AddArray(P, "OvlPriority", prio)
        AddArray(P, "OvlRed", r)
        AddArray(P, "OvlGreen", g)
        AddArray(P, "OvlBlue", b)
        AddArray(P, "OvlAlpha", a)
        AddArray(P, "OvlOffsetU", ou)
        AddArray(P, "OvlOffsetV", ov)
        AddArray(P, "OvlScaleU", su)
        AddArray(P, "OvlScaleV", sv)
        P.Add(NpcVmadBuilder.VmadPropertySpec.FromString("SkinTemplate", skin))

        Return spec
    End Function

    ' ============================================================================================
    ' .pex install
    ' ============================================================================================

    ''' <summary>Script (and .pex file) name for this game.</summary>
    Public Function ScriptNameFor(game As Config_App.Game_Enum) As String
        Return If(game = Config_App.Game_Enum.Skyrim, ScriptNameSse, ScriptNameFo4)
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
        Dim resourceName = "NpcManager.Papyrus." & ScriptNameFor(game) & ".pex"   ' LogicalName pinned in the .vbproj
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
    Public Function InstallPex(dataPath As String, game As Config_App.Game_Enum) As String
        If String.IsNullOrEmpty(dataPath) Then Return Nothing
        Dim bytes = PexBytes(game)
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return Nothing

        Dim destDir = Path.Combine(dataPath, "Scripts")
        Directory.CreateDirectory(destDir)
        Dim dest = Path.Combine(destDir, ScriptNameFor(game) & ".pex")

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
        Return dest
    End Function

End Module
