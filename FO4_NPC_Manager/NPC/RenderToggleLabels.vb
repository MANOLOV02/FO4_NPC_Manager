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
                 If(sse, "RaceMenu NiOverride: escala / posición / rotación por nodo del esqueleto (.jslot). Skyrim no tiene bone-morphs faciales.",
                         "NPC_.FMRI/FMRS — morphs de hueso faciales, aplicados como capa MorphDeltaTransform."))

        SetLabel(vertexMorphs,
                 If(sse, "Face morphs (NAM9/NAMA)", "Vertex morphs (TRI)"),
                 If(sse, "NPC_.NAM9 (18 sliders) + NAMA (Nose/Brow/Eyes/Lip type) + race base + custom morphs de RaceMenu, sobre el chargen .tri.",
                         "NPC_.MSDK/MSDV — morphs de vértice del chargen facial sobre FRTRI003.tri."))

        SetLabel(bodyWeight,
                 If(sse, "Body weight (NAM7)", "Body weight (MWGT)"),
                 If(sse, "NPC_.NAM7 — peso del actor: LERP _0/_1 del cuerpo + morph 'SkinnyMorph' de la cabeza/pelo.",
                         "NPC_.MWGT (Thin/Muscular/Fat) — bone-scaling vía RACE.BSMS + MRSV, y el neck-fat NNAM."))

        SetLabel(sculpt,
                 If(sse, "Sculpt", "Sculpt (ARMA SCLP)"),
                 If(sse, "Sculpt per-vértice de RaceMenu (.jslot): deltas libres sobre cabeza/cejas/ojos/boca. Skyrim no tiene ARMA SCLP.",
                         "ARMA SCLP (Bone Scale Delta) — deltas de escala por hueso, sobre esqueletos clonados por-ARMA."))

        SetLabel(bodyTri,
                 If(sse, "Body sliders (BodyMorph)", "Body Sliders (Tri)"),
                 If(sse, "BodyMorph de NiOverride/RaceMenu — morphs de vértice del cuerpo vía el BODYTRI de cada shape.",
                         "BodySlide — morphs de vértice del cuerpo vía el .tri PIRT (campo F4SE)."))

        SetLabel(renderBody, "Render body",
                 If(sse, "Piel del NPC: cuerpo (32), manos/antebrazos (33/34), pies/pantorrillas (37/38) y head parts.",
                         "Piel del NPC: cuerpo (33, cubre torso+piernas+pies), manos desnudas (34/35) y head parts."))

        SetLabel(renderUnderarmor,
                 If(sse, "Render outfit", "Render underarmor"),
                 If(sse, "La ropa/armadura que viste el NPC (slots de cuerpo y manos). Apagarlo destapa la piel que cubría — como un `unequipall`. Skyrim no tiene capa [U]/[A].",
                         "Ropa underarmor (Outfit con BODY/[U]) + guantes de outfit. Apagarlo destapa la piel que cubría."))

        SetLabel(renderArmor,
                 If(sse, "Render accessories", "Render armor"),
                 If(sse, "Accesorios y piezas modulares: anillo (36), escudo (39), cola (40) y los mod-slots (44-49/52-61: capas, mochilas, SOS…). No ocluyen piel.",
                         "Piezas [A] over-armor (slots 41-45) + Pipboy (60)."))

        SetLabel(renderHeadwear, "Render headwear",
                 If(sse, "Prendas de cabeza/cara: head (30), hair (31), long hair (41), circlet (42), ears (43) y el amuleto/collar (35). Apagarlo destapa los head parts ocluidos (pelo bajo casco, etc.).",
                         "Prendas de cabeza/cara/cuello (slots 30-32, 46-50). Apagarlo destapa los head parts ocluidos (pelo bajo casco, barba bajo máscara)."))

        SetLabel(renderGore, "Render gore",
                 If(sse, "No aplica en Skyrim: no hay meatcaps (los HDPT de Skyrim no tienen tipo Meatcap y el gore son mallas de decapitación aparte, fuera del preview).",
                         "Meatcaps: sub-segmentos SECTIONCAP/TORSOCAP de BSSubIndexTriShape y HDPT tipo 7."))

        If renderGore IsNot Nothing Then renderGore.Enabled = GoreEnabledForGame()
    End Sub

    Private Sub SetLabel(cb As CheckBox, text As String, tip As String)
        If cb Is Nothing Then Return
        cb.Text = text
        _tips.SetToolTip(cb, tip)
    End Sub

End Module
