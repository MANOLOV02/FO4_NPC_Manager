Imports System.Windows.Forms
Imports FO4_Base_Library

''' <summary>Texto + tooltip GAME-AWARE de los checkboxes de render/morphs. Los 10 canales de
''' <see cref="RenderToggles"/> son los mismos en los dos juegos, pero lo que cuelga de cada canal NO:
'''
'''   canal            FO4                              Skyrim (SSE)
'''   ───────────────  ───────────────────────────────  ─────────────────────────────────────────
'''   ApplyBoneMorphs  NPC_.FMRI/FMRS (bone morphs)     RaceMenu NiOverride node transforms
'''   ApplyVertexMorphs NPC_.MSDK/MSDV sobre FRTRI003   NAM9/NAMA + race base + custom morphs
'''   ApplyBodyWeight  NPC_.MWGT → bone-scale BSMS/MRSV NPC_.NAM7 → LERP _0/_1 + SkinnyMorph
'''   ApplySculpt      ARMA SCLP (bone scale delta)     RaceMenu sculpt per-vértice (.jslot)
'''   BodyTri          BodySlide PIRT (F4SE)            BodyMorph NiOverride (BODYTRI por shape)
'''   RenderUnderarmor outfit [U]/BODY + guantes        el outfit (no hay capa [U]/[A] en Skyrim)
'''   RenderArmor      [A] over-armor + Pipboy          accesorios: ring/shield/tail/mod-slots
'''   RenderGore       meatcaps (HDPT 7 + SubIndex)     NO EXISTE → checkbox deshabilitado
'''
''' Skyrim no tiene meatcaps: sus HDPT son Misc/Face/Eyes/Hair/Facial Hair/Scar/Eyebrows
''' (wbDefinitionsTES5.pas:5616-5624) y BSSubIndexTriShape es un bloque FO4. El gore de Skyrim son
''' mallas de decapitación aparte (slots 50/51 + BPTD) que nunca entran al preview.
'''
''' Un solo ToolTip Shared sirve a los 3 forms (MainForm / EditBody / EditFace).</summary>
Public Module RenderToggleLabels

    Private ReadOnly _tips As New ToolTip() With {.AutoPopDelay = 20000, .InitialDelay = 400, .ReshowDelay = 150}

    ''' <summary>True cuando la sesión está pineada a Skyrim (Preflight_Form fijó Config_App.Current.Game).</summary>
    Public Function IsSse() As Boolean
        Return Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim
    End Function

    ''' <summary>En SSE el canal RenderGore no tiene geometría que ocultar → el checkbox se deshabilita.
    ''' El valor persistido (NPC_Config.RenderGore) se conserva: los editores lo siguen heredando.</summary>
    Public Function GoreEnabledForGame() As Boolean
        Return Not IsSse()
    End Function

    ''' <summary>Rotula los checkboxes que el form tenga (cualquiera puede venir Nothing: EditBody sólo
    ''' tiene 4, EditFace sólo gore).</summary>
    Public Sub Apply(boneMorphs As CheckBox, vertexMorphs As CheckBox, bodyWeight As CheckBox, sculpt As CheckBox, bodyTri As CheckBox,
                     renderBody As CheckBox, renderUnderarmor As CheckBox, renderArmor As CheckBox, renderHeadwear As CheckBox, renderGore As CheckBox)
        Dim sse = IsSse()

        SetLabel(boneMorphs,
                 If(sse, "Node transforms", "Bone morphs (FMRS)"),
                 If(sse, "RaceMenu NiOverride: scale / position / rotation per skeleton node (.jslot). Skyrim has no facial bone-morphs.",
                         "NPC_.FMRI/FMRS — facial bone morphs, applied as a MorphDeltaTransform layer."))

        SetLabel(vertexMorphs,
                 If(sse, "Face morphs (NAM9/NAMA)", "Vertex morphs (TRI)"),
                 If(sse, "NPC_.NAM9 (18 sliders) + NAMA (Nose/Brow/Eyes/Lip type) + race base + RaceMenu custom morphs, over the chargen .tri.",
                         "NPC_.MSDK/MSDV — facial chargen vertex morphs over FRTRI003.tri."))

        SetLabel(bodyWeight,
                 If(sse, "Body weight (NAM7)", "Body weight (MWGT)"),
                 If(sse, "NPC_.NAM7 — actor weight: body LERP _0/_1 + 'SkinnyMorph' morph of the head/hair.",
                         "NPC_.MWGT (Thin/Muscular/Fat) — bone-scaling via RACE.BSMS + MRSV, and the neck-fat NNAM."))

        SetLabel(sculpt,
                 If(sse, "Sculpt", "Sculpt (ARMA SCLP)"),
                 If(sse, "RaceMenu per-vertex sculpt (.jslot): free deltas over head/brows/eyes/mouth. Skyrim has no ARMA SCLP.",
                         "ARMA SCLP (Bone Scale Delta) — per-bone scale deltas, over per-ARMA cloned skeletons."))

        SetLabel(bodyTri,
                 If(sse, "Body sliders (BodyMorph)", "Body Sliders (Tri)"),
                 If(sse, "NiOverride/RaceMenu BodyMorph — body vertex morphs via each shape's BODYTRI.",
                         "BodySlide — body vertex morphs via the PIRT .tri (F4SE field)."))

        SetLabel(renderBody, "Render body",
                 If(sse, "NPC skin: body (32), hands/forearms (33/34), feet/calves (37/38) and head parts.",
                         "NPC skin: body (33, covers torso+legs+feet), bare hands (34/35) and head parts."))

        SetLabel(renderUnderarmor,
                 If(sse, "Render outfit", "Render underarmor"),
                 If(sse, "The clothing/armor the NPC wears (body and hand slots). Turning it off uncovers the skin it hid — like an `unequipall`. Skyrim has no [U]/[A] layer.",
                         "Underarmor clothing (Outfit with BODY/[U]) + outfit gloves. Turning it off uncovers the skin it hid."))

        SetLabel(renderArmor,
                 If(sse, "Render accessories", "Render armor"),
                 If(sse, "Accessories and modular pieces: ring (36), shield (39), tail (40) and the mod-slots (44-49/52-61: capes, backpacks, SOS…). They don't occlude skin.",
                         "[A] over-armor pieces (slots 41-45) + Pipboy (60)."))

        SetLabel(renderHeadwear, "Render headwear",
                 If(sse, "Head/face wear: head (30), hair (31), long hair (41), circlet (42), ears (43) and the amulet/necklace (35). Turning it off uncovers the occluded head parts (hair under helmet, etc.).",
                         "Head/face/neck wear (slots 30-32, 46-50). Turning it off uncovers the occluded head parts (hair under helmet, beard under mask)."))

        SetLabel(renderGore, "Render gore",
                 If(sse, "Not applicable in Skyrim: there are no meatcaps (Skyrim's HDPT have no Meatcap type and the gore is separate decapitation meshes, outside the preview).",
                         "Meatcaps: SECTIONCAP/TORSOCAP sub-segments of BSSubIndexTriShape and HDPT type 7."))

        If renderGore IsNot Nothing Then renderGore.Enabled = GoreEnabledForGame()
    End Sub

    Private Sub SetLabel(cb As CheckBox, text As String, tip As String)
        If cb Is Nothing Then Return
        cb.Text = text
        _tips.SetToolTip(cb, tip)
    End Sub

End Module
