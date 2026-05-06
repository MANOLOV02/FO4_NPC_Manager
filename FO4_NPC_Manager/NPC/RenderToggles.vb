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
Public Class RenderToggles
    ''' <summary>NPC bone-region morphs (FMRI/FMRS). Master toggle for the FMRS contribution to
    ''' the merged pose. Off = bind pose for those bones.</summary>
    Public Property ApplyBoneMorphs As Boolean = True

    ''' <summary>Vertex morphs (NPC.MSDK/MSDV chargen morphs over FRTRI003). Toggles the face
    ''' morph resolver inside the composite morph plan.</summary>
    Public Property ApplyVertexMorphs As Boolean = True

    ''' <summary>NPC.MWGT body weight applied via bone scaling on the body skeleton. Off =
    ''' weight neutral.</summary>
    Public Property ApplyBodyWeight As Boolean = True

    ''' <summary>ARMA SCLP sculpt deltas applied to per-ARMA cloned skeletons. Off = no sculpt.</summary>
    Public Property ApplySculpt As Boolean = True

    ''' <summary>BodySlide PIRT vertex morphs (F4SE-only). Independent of <see cref="ApplyVertexMorphs"/>
    ''' (which gates the face FRTRI003 path); they share the composite morph plan but are
    ''' resolved by separate resolvers.</summary>
    Public Property BodyTri As Boolean = True

    ''' <summary>Render outer armor (ShapeRenderCategory.Armor). Off hides outer armor only.</summary>
    Public Property RenderArmor As Boolean = True

    ''' <summary>Render underarmor (slot [U] / [A] extended underarmor classification).</summary>
    Public Property RenderUnderarmor As Boolean = True

    ''' <summary>Render body shapes (ShapeRenderCategory.Body — base body, hands, feet).</summary>
    Public Property RenderBody As Boolean = True

    ''' <summary>Render headwear (helmets, masks, etc — slot 30 group).</summary>
    Public Property RenderHeadwear As Boolean = True

    ''' <summary>Render gore (decapitation / dismemberment caps and meatcap shapes). Read from
    ''' the main form's checkbox even inside editor previews so the user's preference holds
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
