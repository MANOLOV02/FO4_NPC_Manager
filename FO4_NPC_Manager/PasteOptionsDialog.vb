''' <summary>Modal dialog que permite al usuario elegir qué categorías del clipboard preset
''' aplicar al NPC receptor en Paste Look. Cada checkbox controla UNA categoría independiente
''' del LooksMenu preset schema (HeadParts / HairColor / Weights / FaceTints / Morphs / etc.).
''' Las categorías NO tildadas preservan los datos originales del NPC receptor — el caller usa
''' <see cref="BuildOptions"/> para obtener un struct y pasarlo a la helper de paste, que copia
''' del raw NPC los campos no seleccionados (no los borra, no los deja vacíos — los preserva).
''' </summary>
Public Class PasteOptionsDialog

    Public Sub New()
        InitializeComponent()
        AddHandler ButtonSelectAll.Click, Sub(s, e) SetAll(True)
        AddHandler ButtonDeselectAll.Click, Sub(s, e) SetAll(False)
    End Sub

    Private Sub SetAll(state As Boolean)
        CheckBoxBodyWeight.Checked = state
        CheckBoxBodyRegions.Checked = state
        CheckBoxBodySliders.Checked = state
        CheckBoxOverlays.Checked = state
        CheckBoxSkinOverride.Checked = state
        CheckBoxLmSkinTemplate.Checked = state
        CheckBoxOutfit.Checked = state
        CheckBoxFaceParts.Checked = state
        CheckBoxHairColor.Checked = state
        CheckBoxFaceTints.Checked = state
        CheckBoxFaceVertexMorphs.Checked = state
        CheckBoxFaceBoneRegions.Checked = state
        CheckBoxIsCharGenPreset.Checked = state
    End Sub

    ''' <summary>Snapshot the current checkbox states into a <see cref="PasteOptions"/> struct
    ''' the caller can hand to <c>BuildFilteredPaste</c>.</summary>
    Public Function BuildOptions() As PasteOptions
        Return New PasteOptions With {
            .BodyWeight = CheckBoxBodyWeight.Checked,
            .BodyRegions = CheckBoxBodyRegions.Checked,
            .BodySliders = CheckBoxBodySliders.Checked,
            .Overlays = CheckBoxOverlays.Checked,
            .SkinOverride = CheckBoxSkinOverride.Checked,
            .LmSkinTemplate = CheckBoxLmSkinTemplate.Checked,
            .Outfit = CheckBoxOutfit.Checked,
            .FaceParts = CheckBoxFaceParts.Checked,
            .HairColor = CheckBoxHairColor.Checked,
            .FaceTints = CheckBoxFaceTints.Checked,
            .FaceVertexMorphs = CheckBoxFaceVertexMorphs.Checked,
            .FaceBoneRegions = CheckBoxFaceBoneRegions.Checked,
            .IsCharGenPreset = CheckBoxIsCharGenPreset.Checked
        }
    End Function
End Class

''' <summary>Per-category boolean flags. True = take the field from the source clipboard
''' preset; False = leave the target NPC's existing value untouched. The merge is implemented
''' in <c>MainForm.BuildFilteredPaste</c>: it builds a new <see cref="LooksmenuLoader.LooksmenuPreset"/>
''' that, for each False flag, carries the target NPC's RAW value verbatim — so the overlay
''' apply path produces the same result as if that category had never been touched.</summary>
Public Structure PasteOptions
    Public BodyWeight As Boolean         ' WeightThin / WeightMuscular / WeightFat
    Public BodyRegions As Boolean        ' BodyMorphValues (MRSV)
    Public BodySliders As Boolean        ' BodyMorphSliders (BodySlide vertex morphs)
    Public Overlays As Boolean           ' Overlays (F4SE body overlays — tattoos / body paint)
    Public SkinOverride As Boolean       ' SkinFormIDOverride (NPC.WNAM record skin)
    Public LmSkinTemplate As Boolean     ' SkinTemplateId (F4SE LM SkinInterface)
    Public Outfit As Boolean             ' DefaultOutfitFormIDOverride (NPC.DOFT record outfit)
    Public FaceParts As Boolean          ' HeadPartFormIDs
    Public HairColor As Boolean          ' HairColorFormID
    Public FaceTints As Boolean          ' FaceTintLayers
    Public FaceVertexMorphs As Boolean   ' ChargenFaceMorphs (MSDV)
    Public FaceBoneRegions As Boolean    ' FaceBoneRegions (FMRS) + FacialMorphIntensity
    Public IsCharGenPreset As Boolean    ' ACBS bit 0x04
End Structure
