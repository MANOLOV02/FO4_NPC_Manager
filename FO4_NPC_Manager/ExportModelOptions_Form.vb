Imports FO4_Base_Library

''' <summary>
''' Opciones del botón "NPC Model to NIF". Diálogo modal, chico y fijo: geometría skinned/unskinned
''' y qué hacer con las texturas de la cara.
''' <para>El sub-grupo de overlays existe SÓLO en SSE. En FO4 el bake hornea el tint DENTRO del
''' diffuse (los tres DDS de <c>FaceCustomization</c>), así que no hay pliegue que elegir.</para>
''' </summary>
Public Class ExportModelOptions_Form

    ''' <summary>Lo que el diálogo devuelve. Lo consume <see cref="SceneNifExporter.Export"/>.</summary>
    Public ReadOnly Property Options As New SceneExportOptions()

    ''' <summary>True si el NPC tiene overlays de cara o máscaras skee que el bake PLEGARÍA. Es el
    ''' default de la sub-opción de overlays; ver <see cref="Prepare"/>.</summary>
    Private _foldIsTheBakeDefault As Boolean

    ''' <summary>Fracción del alto que usa el default del locator. Medida sobre los 63 loadscreens de FO4
    ''' que traen el nodo: el cluster de bípedos erguidos cae en 0,78..0,95 y 0,85 los reproduce dentro
    ''' de ±10 %. Ver <c>SceneNifExporter.LoadScreenZoomHeightFraction</c>, que es el mismo número para el
    ''' camino headless.</summary>
    Private Const DefaultHeightFraction As Integer = 85

    ''' <summary>Bbox del modelo tal como quedaría horneado, medido por el MISMO código que usa el export.
    ''' Es lo que convierte el % del slider en unidades del NIF.</summary>
    Private _bounds As SceneNifExporter.BakedBounds

    ''' <summary>Cómo volver a medir el bbox. El diálogo recibe una medición hecha al abrirlo, y esa
    ''' medición puede salir VACÍA: <c>MeasureBakedBounds</c> descarta todo shape que todavía no pasó por
    ''' el cómputo de oclusión del render. Sin alto no hay traducción % ↔ unidades, así que el slider —lo
    ''' único que la necesita— quedaba muerto para el resto de la vida del diálogo. Con esto se remide en
    ''' el momento en que el valor hace falta.</summary>
    Private _measureBounds As Func(Of SceneNifExporter.BakedBounds)

    ''' <summary>Guarda contra la realimentación slider ↔ Z: cada uno escribe en el otro.</summary>
    Private _syncingPlacement As Boolean

    ''' <summary>
    ''' Configura el diálogo para el NPC en pantalla.
    ''' <para><paramref name="npcFoldsOverlays"/> tiene que venir del RENDER
    ''' (<c>NpcFaceTintResolver.LastSseFoldWasMandatory</c>): dice si este NPC trae overlays de cara
    ''' o máscaras skee foldables.</para>
    ''' <para>⛔ El default combina ese dato CON el toggle "Bake RaceMenu overlays", porque la
    ''' condición del BAKE es la conjunción de los dos (FaceGenBuilder.WriteSseFaceDiffuseWithOverlays:
    ''' sale temprano si el toggle está apagado) y es el bake el que decide qué archivo va a existir.
    ''' El RENDER, en cambio, pliega mirando sólo los overlays — así que con el toggle apagado el
    ''' preview pliega y el bake no. Seguir al render acá apuntaría a un DDS que nadie escribe.</para>
    ''' </summary>
    ''' <param name="bakedBounds">Bbox que produciría el export unskinned de la escena en pantalla, de
    ''' <c>SceneNifExporter.MeasureBakedBounds</c>. Alimenta el default del locator del menú de carga y la
    ''' conversión %→unidades del slider. Si no es usable, esa sección se deshabilita y lo dice.</param>
    Public Sub Prepare(isSse As Boolean, npcFoldsOverlays As Boolean,
                       bakedBounds As SceneNifExporter.BakedBounds,
                       Optional remeasureBounds As Func(Of SceneNifExporter.BakedBounds) = Nothing)
        Dim bakeOverlaysOn As Boolean = (Config_App.Current IsNot Nothing AndAlso
                                         Config_App.Current.Setting_BakeSseRaceMenuOverlays)
        _foldIsTheBakeDefault = isSse AndAlso npcFoldsOverlays AndAlso bakeOverlaysOn

        PanelOverlays.Visible = isSse
        RadioWithOverlays.Checked = _foldIsTheBakeDefault
        RadioWithoutOverlays.Checked = Not _foldIsTheBakeDefault

        ' El skin tone se escribe en el shader de los shapes de piel. La IMPLICACIÓN es distinta por juego y
        ' hay que decirla: en FO4 el motor reemplaza el material entero desde el .bgsm, así que para que el
        ' color se lea hay que embeber el material y cortar ese link — el shape deja de seguir al archivo.
        ' En SSE no hay carga de material por nombre, así que alcanza con el color y no se pierde nada.
        LabelSkinTone.Text =
            If(isSse,
               "Skin shapes (body, hands, feet) get the NPC's tone in their shader, so the exported model matches the preview instead of showing the source NIF's default. The face is not affected — its tone travels in its own textures.",
               "Skin shapes (body, hands, feet) get the NPC's tone in their shader. In Fallout 4 this also embeds the resolved material into the shape and clears its .bgsm link, so the shape stops following that material file. The face is not affected. In game, an actor's tone is recomputed from the record and overrides this.")

        ' El locator del menú de carga es de FO4: 63 de los 173 loadscreens vanilla lo traen y NINGUNO
        ' de los 139 de SSE, donde el encuadre viaja en el LSCR (SNAM/RNAM/XNAM) y no en el NIF.
        GroupLoadScreen.Visible = Not isSse
        _bounds = bakedBounds
        _measureBounds = remeasureBounds
        ResetPlacementToDefault()

        UpdateFaceSubOptions()
        UpdateLoadScreenGate()
    End Sub

    ''' <summary>Deja los seis campos + la escala en el default derivado del bbox: centrado en XY, a
    ''' <see cref="DefaultHeightFraction"/> % del alto, sin rotación y escala 1 — que es exactamente lo que
    ''' traen los 63 loadscreens vanilla salvo por la altura, que ellos ponen a mano.</summary>
    Private Sub ResetPlacementToDefault()
        _syncingPlacement = True
        Try
            SliderHeight.Value = DefaultHeightFraction
            NumHeightPct.Value = DefaultHeightFraction
            NumRotX.Value = 0D : NumRotY.Value = 0D : NumRotZ.Value = 0D
            NumScale.Value = 1D
            If _bounds.IsUsable Then
                Dim p = _bounds.ZoomTargetAt(DefaultHeightFraction / 100.0F)
                NumPosX.Value = ClampToRange(NumPosX, p.X)
                NumPosY.Value = ClampToRange(NumPosY, p.Y)
                NumPosZ.Value = ClampToRange(NumPosZ, p.Z)
            Else
                NumPosX.Value = 0D : NumPosY.Value = 0D : NumPosZ.Value = 0D
            End If
        Finally
            _syncingPlacement = False
        End Try
        UpdateBoundsLabel()
    End Sub

    ''' <summary>Un NumericUpDown tira si le asignás fuera de rango. Un modelo absurdo (o un bbox
    ''' degenerado) no debe voltear el diálogo.</summary>
    Private Shared Function ClampToRange(box As NumericUpDown, v As Single) As Decimal
        If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then Return 0D
        Dim d = CDec(v)
        If d < box.Minimum Then Return box.Minimum
        If d > box.Maximum Then Return box.Maximum
        Return d
    End Function

    Private Sub UpdateBoundsLabel()
        If Not _bounds.IsUsable Then
            LabelBounds.Text = "The scene could not be measured, so there is no default to offer — type the position by hand or leave it at the origin."
            Return
        End If
        LabelBounds.Text = $"Model bounds: Z {_bounds.Min.Z:F1} to {_bounds.Max.Z:F1} (height {_bounds.Height:F1}), measured on {_bounds.VertexCount:N0} baked vertices."
    End Sub

    Private Sub ExportModelOptions_Form_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        UpdateFaceSubOptions()
        UpdateLoadScreenGate()
    End Sub

    ''' <summary>Las sub-opciones sólo tienen sentido si se van a reescribir los paths.</summary>
    Private Sub UpdateFaceSubOptions()
        PanelOverlays.Enabled = CheckUseFaceGenBake.Checked
    End Sub

    ''' <summary>
    ''' El nodo del menú de carga se ofrece en FO4 con CUALQUIERA de las dos geometrías: es un locator
    ''' inerte y no depende del skin. Lo atado a unskinned es el caso vanilla —los 173 loadscreens de FO4
    ''' se referencian desde un STAT, que no tiene esqueleto que deforme la malla, y los 173 son
    ''' estáticos—, no el nodo.
    ''' <para>Con skin el default de posición se mide sobre el modelo POSADO mientras la geometría se
    ''' escribe en bind, así que ahí el valor es aproximado y se dice en el texto.</para>
    ''' </summary>
    Private Sub UpdateLoadScreenGate()
        Dim writing As Boolean = CheckAddLoadScreenNode.Checked
        CheckAddLoadScreenNode.Enabled = True
        ' Los valores sólo se editan si el nodo se va a escribir. El slider además necesita un alto real
        ' para traducir % a unidades; sin bbox se deja escribir a mano pero el slider no sirve, así que
        ' antes de apagarlo se le da otra oportunidad a la medición.
        If writing Then EnsureBounds()
        LoadScreenValues.Enabled = writing
        SliderHeight.Enabled = writing AndAlso _bounds.IsUsable
        NumHeightPct.Enabled = SliderHeight.Enabled
        LabelLoadScreen.Text =
            "Adds an empty 'LoadingMenuZoomTarget' node — the pivot the loading-screen camera orbits. Default: model centre, 85% up. Right for an upright biped; on a dog or deathclaw the top is not the head, so move it." &
            If(RadioUnskinned.Checked,
               "",
               " Skinned export: the node is still written, but this position is measured on the posed model, not on the bind pose that gets written.")
    End Sub

    ''' <summary>Segunda (y última) oportunidad de medir el bbox. Sólo corre si la medición de apertura
    ''' salió vacía; con bbox bueno no toca nada, así que no puede pisarle valores al usuario.</summary>
    Private Sub EnsureBounds()
        If _bounds.IsUsable OrElse _measureBounds Is Nothing Then Return
        Dim fresh = _measureBounds()
        If Not fresh.IsUsable Then Return
        _bounds = fresh
        ' Los campos estaban en el default de "sin bbox" (0,0,0): ahora hay un default de verdad.
        ResetPlacementToDefault()
    End Sub

    Private Sub CheckUseFaceGenBake_CheckedChanged(sender As Object, e As EventArgs) Handles CheckUseFaceGenBake.CheckedChanged
        UpdateFaceSubOptions()
    End Sub

    Private Sub GeometryChoice_CheckedChanged(sender As Object, e As EventArgs) Handles RadioUnskinned.CheckedChanged, RadioSkinned.CheckedChanged
        UpdateLoadScreenGate()
    End Sub

    Private Sub CheckAddLoadScreenNode_CheckedChanged(sender As Object, e As EventArgs) Handles CheckAddLoadScreenNode.CheckedChanged
        UpdateLoadScreenGate()
    End Sub

    ''' <summary>Slider (o su caja de %) → Z en unidades del NIF. El % NO es un campo del NIF: el nodo sólo
    ''' guarda traslación, rotación y escala. Es un atajo para posicionar la Z relativa al alto del modelo
    ''' (0 = base del bounding box, 100 = tope), que es la forma en que uno piensa "ponelo en la cabeza".
    ''' La guarda evita el ping-pong con <see cref="NumPosZ_ValueChanged"/>, que hace el camino inverso.</summary>
    Private Sub HeightFraction_Changed(sender As Object, e As EventArgs) Handles SliderHeight.ValueChanged, NumHeightPct.ValueChanged
        If _syncingPlacement OrElse Not _bounds.IsUsable Then Return
        _syncingPlacement = True
        Try
            Dim pct As Integer = If(sender Is SliderHeight, SliderHeight.Value, CInt(NumHeightPct.Value))
            SliderHeight.Value = pct
            NumHeightPct.Value = pct
            NumPosZ.Value = ClampToRange(NumPosZ, _bounds.Min.Z + _bounds.Height * (pct / 100.0F))
        Finally
            _syncingPlacement = False
        End Try
    End Sub

    ''' <summary>Z escrita a mano → posición del slider, para que los dos digan lo mismo. Se redondea al
    ''' entero más cercano porque el slider es entero; la Z fina NO se toca (la fuente de verdad es la
    ''' caja, no el slider).</summary>
    Private Sub NumPosZ_ValueChanged(sender As Object, e As EventArgs) Handles NumPosZ.ValueChanged
        If _syncingPlacement OrElse Not _bounds.IsUsable OrElse _bounds.Height <= 0.0F Then Return
        _syncingPlacement = True
        Try
            Dim frac = (CSng(NumPosZ.Value) - _bounds.Min.Z) / _bounds.Height * 100.0F
            Dim pct = CInt(Math.Round(Math.Max(SliderHeight.Minimum, Math.Min(SliderHeight.Maximum, frac))))
            SliderHeight.Value = pct
            NumHeightPct.Value = pct
        Finally
            _syncingPlacement = False
        End Try
    End Sub

    Private Sub ButtonResetPlacement_Click(sender As Object, e As EventArgs) Handles ButtonResetPlacement.Click
        ResetPlacementToDefault()
    End Sub

    Private Sub ButtonExport_Click(sender As Object, e As EventArgs) Handles ButtonExport.Click
        Options.Skinned = RadioSkinned.Checked
        Options.RepointFaceTextures = CheckUseFaceGenBake.Checked
        ' Fuera de SSE el pliegue no existe: el panel está oculto y la opción queda en False para que
        ' el exporter tome siempre la rama de FO4.
        Options.FoldFaceOverlays = PanelOverlays.Visible AndAlso RadioWithOverlays.Checked
        Options.WriteSkinTone = CheckWriteSkinTone.Checked
        Options.IncludeHelperShapes = CheckExportHelperShapes.Checked
        ' Igual que FoldFaceOverlays: se exige que el grupo esté VISIBLE (o sea, FO4) además del tilde,
        ' así el juego equivocado no puede colar la opción por un estado viejo del control. La geometría
        ' NO entra: el nodo se escribe con skin y sin skin.
        Options.AddLoadScreenNode = GroupLoadScreen.Visible AndAlso CheckAddLoadScreenNode.Checked
        ' Se manda lo que el usuario TIENE A LA VISTA, no un recálculo: así lo que se escribe en el NIF y
        ' lo que mostró el diálogo no pueden divergir.
        If Options.AddLoadScreenNode Then
            Options.LoadScreenNodePlacement = New LoadScreenNodePlacement With {
                .Position = New System.Numerics.Vector3(CSng(NumPosX.Value), CSng(NumPosY.Value), CSng(NumPosZ.Value)),
                .RotationDegrees = New System.Numerics.Vector3(CSng(NumRotX.Value), CSng(NumRotY.Value), CSng(NumRotZ.Value)),
                .Scale = CSng(NumScale.Value)
            }
        End If
    End Sub

End Class
