Imports FO4_Base_Library

''' <summary>
''' Una tarjeta del editor de regiones óseas de la cara (FMRI/FMRS): los SIETE componentes de una región
''' —PosX/Y/Z, RotX/Y/Z y Scale— como filas de <c>[botón de reset | slider]</c> bajo los encabezados
''' Translation / Rotation / Scale.
''' <para>El layout es FIJO (la disposición FMRS es spec del binario, no dato del juego: Translation = 0..2,
''' Rotation = 3..5, Scale = un solo slider que mueve los tres ejes), así que vive entero en el Designer.
''' Lo único que varía por región es el rótulo, el tooltip y CUÁLES ejes están vivos, y eso lo pone
''' <see cref="Bind"/>. Se instancia una vez por región (N variable ⇒ población dinámica permitida por
''' 00-reglas-ui-y-vb §1).</para>
''' <para>⛔ Reemplaza a <c>EditFace_Form.BuildBoneCard</c>, que armaba esto por código con dos bucles
''' anidados. Con él se fue el camino de tarjeta MULTI-REGIÓN (<c>showSub</c> / <c>VariantLabel</c> / la
''' banda gris de separación): el colapso de regiones que comparten bone-set lo DESCARTÓ el usuario, y el
''' único call site pasaba siempre una lista de un solo miembro, así que era código muerto — no una
''' capacidad que se esté perdiendo.</para>
''' </summary>
Friend Class BoneRegionCard

    ''' <summary>Los 7 sliders en ORDEN FMRS, para devolvérselos al formulario tal como los espera
    ''' <c>_regionBars</c>. El orden es el del binario, no el visual.</summary>
    Private ReadOnly Property Barras As FO4_Base_Library.TinySliderTextBox()
        Get
            Return New FO4_Base_Library.TinySliderTextBox() {
                SliderPosX, SliderPosY, SliderPosZ, SliderRotX, SliderRotY, SliderRotZ, SliderScale}
        End Get
    End Property

    Private ReadOnly _botones As Button()

    Public Sub New()
        InitializeComponent()
        ' Paralelo a Barras: mismo orden FMRS, para poder recorrer eje por eje.
        _botones = New Button() {ButtonResetPosX, ButtonResetPosY, ButtonResetPosZ,
                                 ButtonResetRotX, ButtonResetRotY, ButtonResetRotZ, ButtonResetScale}
    End Sub

    ''' <summary>Ata la tarjeta a una región: rótulo, tooltips, qué ejes quedan habilitados y los handlers.
    ''' <para>Devuelve el array de 7 sliders en orden FMRS, o <c>Nothing</c> si la región no tiene NINGÚN eje
    ''' vivo — en ese caso la tarjeta no se muestra, igual que antes devolvía <c>Nothing</c> el builder.</para>
    ''' <para>Los ejes muertos se construyen igual pero quedan DESHABILITADOS y sin handlers, para que todas
    ''' las tarjetas midan lo mismo en vez de encogerse hasta sus ejes vivos.</para></summary>
    ''' <param name="onChanged">Recibe (ID de región, índice de componente FMRS 0..6).</param>
    ''' <param name="onDragEnded">El <c>DragEnded</c> del slider, para drenar la cola del throttle de render.</param>
    Friend Function Bind(rd As FacialBoneRegion,
                         onChanged As Action(Of UInteger, Integer),
                         onDragEnded As EventHandler) As FO4_Base_Library.TinySliderTextBox()
        If rd Is Nothing Then Return Nothing
        Dim live = RegionLiveComponents(rd)
        If Not live.Any(Function(x) x) Then Return Nothing

        GroupBoxCard.Text = rd.Name
        Dim boneList As String = String.Join(", ", rd.Bones.Select(Function(b) b.Bone))
        ToolTipCard.SetToolTip(GroupBoxCard, $"Bones: {boneList}  •  FMRI 0x{rd.ID:X8}")

        Dim slidersFmrs = Barras
        Dim regId As UInteger = rd.ID
        For ci = 0 To 6
            Dim componente As Integer = ci
            Dim barra = slidersFmrs(ci)
            Dim boton = _botones(ci)
            Dim isLive As Boolean = live(ci)
            barra.Enabled = isLive
            boton.Enabled = isLive
            If isLive Then
                AddHandler barra.ValueChanged, Sub(sender, e) onChanged(regId, componente)
                AddHandler barra.DragEnded, onDragEnded
                AddHandler boton.Click, Sub(sender, e) barra.Value = 0
            End If
            ToolTipCard.SetToolTip(boton, AxisName(ci) & If(isLive, " — reset", " — (not used by this region)"))
        Next
        Return slidersFmrs
    End Function

    ''' <summary>Cuáles de los 7 componentes FMRS (PosX/Y/Z, RotX/Y/Z, Scale) pueden producir un delta de
    ''' hueso no nulo. Delega la regla en <see cref="FaceBonePoseBuilder.IsFmrsAxisLive"/> para que el editor y
    ''' el camino render/bake compartan UNA convención: un eje está vivo si su Minima o su Maxima no es cero.
    ''' <para>⛔ Antes se comparaba Minima/Maxima contra los Defaults de la región, lo que contradice al motor:
    ''' el lerp FMRS nunca lee Defaults (RE de los dos binarios: reciben un struct de 18 floats
    ''' [Minima|Maxima] sin slot de Defaults y no tienen ni una resta). Las dos convenciones coinciden sólo
    ''' porque toda región vanilla trae Defaults = 0; una raza modeada con Defaults != 0 habría hecho que el
    ''' editor discrepara del render.</para>
    ''' <para>El componente 6 (Scale) mueve los tres ejes desde un solo valor, así que está vivo si cualquiera
    ''' de los tres tiene un extremo no nulo.</para></summary>
    Private Shared Function RegionLiveComponents(rd As FacialBoneRegion) As Boolean()
        Dim live(6) As Boolean
        For Each b In rd.Bones
            If FaceBonePoseBuilder.IsFmrsAxisLive(b.MinimaPosition.X, b.MaximaPosition.X) Then live(0) = True
            If FaceBonePoseBuilder.IsFmrsAxisLive(b.MinimaPosition.Y, b.MaximaPosition.Y) Then live(1) = True
            If FaceBonePoseBuilder.IsFmrsAxisLive(b.MinimaPosition.Z, b.MaximaPosition.Z) Then live(2) = True
            If FaceBonePoseBuilder.IsFmrsAxisLive(b.MinimaRotation.X, b.MaximaRotation.X) Then live(3) = True
            If FaceBonePoseBuilder.IsFmrsAxisLive(b.MinimaRotation.Y, b.MaximaRotation.Y) Then live(4) = True
            If FaceBonePoseBuilder.IsFmrsAxisLive(b.MinimaRotation.Z, b.MaximaRotation.Z) Then live(5) = True
            If FaceBonePoseBuilder.IsFmrsAxisLive(b.MinimaScale.X, b.MaximaScale.X) OrElse
               FaceBonePoseBuilder.IsFmrsAxisLive(b.MinimaScale.Y, b.MaximaScale.Y) OrElse
               FaceBonePoseBuilder.IsFmrsAxisLive(b.MinimaScale.Z, b.MaximaScale.Z) Then live(6) = True
        Next
        Return live
    End Function

    Private Shared Function AxisName(componentIdx As Integer) As String
        Select Case componentIdx
            Case 0 : Return "Position X"
            Case 1 : Return "Position Y"
            Case 2 : Return "Position Z"
            Case 3 : Return "Rotation X"
            Case 4 : Return "Rotation Y"
            Case 5 : Return "Rotation Z"
            Case Else : Return "Scale"
        End Select
    End Function

End Class
