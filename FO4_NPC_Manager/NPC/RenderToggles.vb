''' <summary>Boolean knob bundle for the NPC render pipeline. Replaces direct
''' <c>CheckBox*.Checked</c> reads inside the pipeline so a render can be requested with a
''' specific configuration regardless of which UI surface drives it.
'''
''' MainForm builds one from its own checkboxes via <see cref="FromMainCheckBoxes"/>.
''' EditFace_Form builds one with <see cref="OnlyFace"/> so its embedded preview hides body /
''' armor meshes regardless of the main form's checkbox state. EditBody_Form builds one with
''' <see cref="FullBody"/>. Both editor presets force every render/morph toggle ON except
''' RenderGore which is read from the main form (the user's gore preference is global).
''' </summary>
''' <para><b>Los 10 son CANALES, no campos de un juego.</b> El canal es el mismo en FO4 y en Skyrim;
''' lo que cuelga de él NO. El rótulo que ve el usuario lo pone <see cref="RenderToggleLabels"/> según
''' Config_App.Current.Game, y cada propiedad documenta abajo su semántica por juego.</para>
Public Class RenderToggles
    ''' <summary>Deformación por HUESO/NODO.
    ''' FO4: bone-region morphs del NPC_ (FMRI/FMRS) → capa MorphDeltaTransform. Off = bind pose de esos huesos.
    ''' SSE: node transforms de RaceMenu (NiOverride: escala/pos/rot por nodo) — Skyrim no tiene FMRS.
    ''' Rótulo SSE: "Node transforms".</summary>
    Public Property ApplyBoneMorphs As Boolean = True

    ''' <summary>Morphs de VÉRTICE de la CARA (gatea el face resolver del composite).
    ''' FO4: NPC_.MSDK/MSDV sobre FRTRI003. SSE: NAM9 + NAMA + race base + custom morphs de RaceMenu, sobre el
    ''' chargen .tri. Rótulo SSE: "Face morphs (NAM9/NAMA)".</summary>
    Public Property ApplyVertexMorphs As Boolean = True

    ''' <summary>Peso del actor. Off = peso neutro.
    ''' FO4: NPC_.MWGT vía bone-scaling (RACE.BSMS + MRSV) + neck-fat NNAM.
    ''' SSE: NPC_.NAM7 vía morph de vértice — LERP _0/_1 del cuerpo (BuildSseBodyWeightResolver) + canal
    ''' "SkinnyMorph" de cabeza/pelo (dentro del plan de cara). Rótulo SSE: "Body weight (NAM7)".</summary>
    Public Property ApplyBodyWeight As Boolean = True

    ''' <summary>Sculpt. Off = sin sculpt.
    ''' FO4: deltas ARMA SCLP aplicados a esqueletos clonados por-ARMA (bone scale).
    ''' SSE: sculpt per-vértice de RaceMenu (.jslot), canal del plan de cara — Skyrim no tiene ARMA SCLP.
    ''' Rótulo SSE: "Sculpt".</summary>
    Public Property ApplySculpt As Boolean = True

    ''' <summary>Morphs de vértice del CUERPO. Independiente de <see cref="ApplyVertexMorphs"/> (que gatea la
    ''' cara): comparten el plan pero los resuelven resolvers distintos.
    ''' FO4: BodySlide PIRT (campo F4SE). SSE: BodyMorph de NiOverride/RaceMenu (BODYTRI por shape).</summary>
    Public Property BodyTri As Boolean = True

    ''' <summary>ShapeRenderCategory.ArmorOver.
    ''' FO4: over-armor [A] (slots 41-45) + Pipboy (60) → "Render armor".
    ''' SSE: accesorios y mod-slots — anillo (36), escudo (39), cola (40), slots 44-49/52-61. No ocluyen piel.
    ''' Rótulo SSE: "Render accessories".</summary>
    Public Property RenderArmor As Boolean = True

    ''' <summary>ShapeRenderCategory.Underarmor + GloveOutfit. Off destapa la piel que cubrían (`unequipall`).
    ''' FO4: outfit con BODY/[U] + guantes → "Render underarmor".
    ''' SSE: el outfit a secas (slots de cuerpo/manos) — Skyrim no tiene capa [U]/[A]. Rótulo SSE: "Render outfit".</summary>
    Public Property RenderUnderarmor As Boolean = True

    ''' <summary>Piel del NPC: body skin + naked hands + head parts. FO4 body = slot 33 (torso+piernas+pies);
    ''' SSE = 32 body + 37/38 pies/pantorrillas. Mismo significado en los dos juegos.</summary>
    Public Property RenderBody As Boolean = True

    ''' <summary>Prendas de cabeza/cara (+ cuello) y la oclusión de head-parts que provocan.
    ''' FO4: slots 30-32 / 46-50. SSE: 30/31/41/42/43 (head, hair, long hair, circlet, ears) + 35 (amulet).</summary>
    Public Property RenderHeadwear As Boolean = True

    ''' <summary>Gore (meatcaps: sub-segmentos SECTIONCAP/TORSOCAP de BSSubIndexTriShape + HDPT tipo 7).
    ''' FO4-ONLY: Skyrim no tiene meatcaps (sus HDPT son Misc/Face/Eyes/Hair/Facial Hair/Scar/Eyebrows y el gore
    ''' son mallas de decapitación aparte), así que el checkbox se deshabilita bajo SSE — el valor persistido se
    ''' conserva. Read from the main form's checkbox even inside editor previews so the user's preference holds
    ''' globally — see <see cref="OnlyFace"/> / <see cref="FullBody"/>.</summary>
    Public Property RenderGore As Boolean = False

    ''' <summary>Snapshot the toggles from the MainForm checkboxes. Single point of read so a
    ''' code path can ask "what does the user have on right now?" without coupling to the form's
    ''' control hierarchy.</summary>
    Public Shared Function FromMainCheckBoxes(form As MainForm) As RenderToggles
        Return New RenderToggles With {
            .ApplyBoneMorphs = form.CheckBoxApplyBoneMorphs.Checked,
            .ApplyVertexMorphs = form.CheckBoxApplyVertexMorphs.Checked,
            .ApplyBodyWeight = form.CheckBoxApplyBodyWeight.Checked,
            .ApplySculpt = form.CheckBoxApplySculpt.Checked,
            .BodyTri = form.CheckBoxBodyTri.Checked,
            .RenderArmor = form.CheckBoxRenderArmor.Checked,
            .RenderUnderarmor = form.CheckBoxRenderUnderarmor.Checked,
            .RenderBody = form.CheckBoxRenderBody.Checked,
            .RenderHeadwear = form.CheckBoxRenderHeadwear.Checked,
            .RenderGore = form.CheckBoxRenderGore.Checked
        }
    End Function

    ''' <summary>Editor preset for EditFace_Form. The editor host ALSO sets
    ''' <c>NpcRenderHost.OnlyFaceCollect = True</c> so non-head meshes (Skin / Outfit /
    ''' Headwear-via-Outfit) never enter the pipeline at all — same path as MainForm's
    ''' "Only Face" PreviewMode. With those meshes excluded at collect-time, the visibility
    ''' toggles below are effectively no-ops (HeadParts have no Body/Armor/Headwear category),
    ''' so we leave every render toggle ON to keep the visibility rule simple. Gore comes from
    ''' the main form to honour the user's global choice.</summary>
    Public Shared Function OnlyFace(mainGore As Boolean) As RenderToggles
        Return New RenderToggles With {
            .ApplyBoneMorphs = True,
            .ApplyVertexMorphs = True,
            .ApplyBodyWeight = True,
            .ApplySculpt = True,
            .BodyTri = True,
            .RenderArmor = True,
            .RenderUnderarmor = True,
            .RenderBody = True,
            .RenderHeadwear = True,
            .RenderGore = mainGore
        }
    End Function

    ''' <summary>Editor preset for EditBody_Form: everything on except gore (which is read from
    ''' the main form's checkbox). Body editing wants to see the whole NPC so the user can judge
    ''' how MWGT / MRSV / BodySlide changes interact with their outfit.</summary>
    Public Shared Function FullBody(mainGore As Boolean) As RenderToggles
        Return New RenderToggles With {
            .ApplyBoneMorphs = True,
            .ApplyVertexMorphs = True,
            .ApplyBodyWeight = True,
            .ApplySculpt = True,
            .BodyTri = True,
            .RenderArmor = True,
            .RenderUnderarmor = True,
            .RenderBody = True,
            .RenderHeadwear = True,
            .RenderGore = mainGore
        }
    End Function
End Class
