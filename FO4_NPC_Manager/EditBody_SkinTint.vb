Imports System.Diagnostics
Imports FO4_Base_Library

''' <summary>Lógica del tab "Skin Tint Adjustment" del editor de cuerpo (los controles viven en
''' EditBody_Form.Designer.vb): ajusta el SKIN TONE del cuerpo (QNAM) con cuatro offsets — R/G/B y una
''' intensidad — para el caso en que el cuerpo y la cara vienen de mods distintos y el tono no matchea con
''' las reglas del motor.
'''
''' <para><b>Qué es cada cosa.</b> El ORIGEN es un píxel de la CARA: se le latchea el COLOR, y ese color no
''' se mueve nunca — el ajuste no toca la resolución que consume la cara (ver
''' <see cref="NpcMaterialResolver.ResolveNpcBodySkinToneColor"/>). El DESTINO es un píxel del CUERPO: se le
''' latchea la POSICIÓN, porque su color es justamente lo que el ajuste mueve y hay que re-muestrearlo en
''' cada iteración.</para>
'''
''' <para><b>Los dos píxeles son WYSIWYG</b> — el color final tal como se ve, con luz, sombra, tonemap y el
''' encode a display ya aplicados (Shader_Class: <c>tonemap()</c> + <c>pow(1/2.2)</c>). Por eso el auto-calc
''' es un LAZO CERRADO —aplicar, re-renderizar, re-muestrear— y no una inversión analítica: invertir la
''' cadena exigiría deshacer el tonemap (que no es separable por canal) y la iluminación del punto, y daría
''' un número lindo y equivocado. El lazo no reimplementa NI UN espacio: deja que el pipeline real calcule y
''' sólo lee el resultado.</para>
'''
''' <para><b>Game-aware.</b> Los cuatro valores son deltas canónicos en [-1..1] (<see cref="SkinToneQnamOffset"/>);
''' la UI muestra R/G/B en bytes y la intensidad en porcentaje. Dónde entra cada uno lo decide el resolver de
''' cada juego, no este formulario: en FO4 la intensidad ES el alpha del QNAM (la opacidad del soft-light del
''' cuerpo) y en SSE —donde el QNAM no tiene alpha— se PLIEGA dentro del color con el seed y la convención que
''' resuelve la config.</para></summary>
Partial Public Class EditBody_Form

    ' ===== Presupuesto de la búsqueda. Ninguna es un límite del dato: son el techo del lazo. =====
    ''' <summary>Lado de la ventana que se promedia al muestrear. 1 píxel solo queda a merced de un brillo
    ''' especular o del dithering del encode; 3x3 promedia sin cruzar bordes de la silueta.</summary>
    Private Const SkinTintSampleBox As Integer = 3
    Private Const SkinTintMaxEvals As Integer = 260
    Private Const SkinTintMaxSeconds As Double = 12.0R
    ''' <summary>Mejora mínima (en la métrica de error) para aceptar un candidato. Sin epsilon el descenso
    ''' acepta ruido del muestreo y no termina nunca.</summary>
    Private Const SkinTintEpsilon As Double = 0.5R

    Private Enum SkinTintPickTarget
        None = 0
        Source = 1
        Target = 2
    End Enum

    Private _stPick As SkinTintPickTarget = SkinTintPickTarget.None
    Private _stSourceColor As Color = Color.Empty
    Private _stSourcePoint As Point = Point.Empty
    Private _stHasSource As Boolean = False
    Private _stTargetPoint As Point = Point.Empty
    Private _stHasTarget As Boolean = False
    Private _stLastTargetColor As Color = Color.Empty
    ''' <summary>Firma de cámara+tamaño al momento de cada pick. Los picks son coordenadas de PANTALLA: si la
    ''' cámara se movió, el punto ya no apunta a lo mismo y el auto-calc estaría persiguiendo otro lugar.</summary>
    Private _stPickCameraSignature As String = ""
    Private _stBusy As Boolean = False

    ''' <summary>Lo único del tab que NO puede vivir en el Designer: el texto depende del juego, y
    ''' <c>InitializeComponent</c> sólo admite código declarativo plano (sin ramas). El default del Designer
    ''' es el de FO4; acá se reemplaza bajo Skyrim. Además siembra los sliders desde el overlay.</summary>
    Private Sub InitSkinTintTab()
        If _isSSE Then
            LabelSkinTintIntensityMeaning.Text = "Intensity is folded into the colour (Skyrim's QNAM has no alpha): it moves the skin-tone layer's interpolation."
        End If
        SyncSkinTintSlidersFromOverlay()
    End Sub

    ' =====================================================================
    ' Modelo <-> UI
    ' =====================================================================

    ''' <summary>El offset VIVO del overlay, creándolo si hace falta. Nothing sólo si no hay preset.</summary>
    Private Function EnsureSkinTintOffset() As SkinToneQnamOffset
        Dim p = Preset
        If p Is Nothing Then Return Nothing
        If p.SkinToneOffset Is Nothing Then p.SkinToneOffset = New SkinToneQnamOffset()
        Return p.SkinToneOffset
    End Function

    Private Sub SyncSkinTintSlidersFromOverlay()
        Dim p = Preset
        Dim off As SkinToneQnamOffset = Nothing
        If p IsNot Nothing Then off = p.SkinToneOffset
        Dim r As Double = 0.0R, g As Double = 0.0R, b As Double = 0.0R, i As Double = 0.0R
        If off IsNot Nothing Then
            r = off.RUi : g = off.GUi : b = off.BUi : i = off.IntensityUi
        End If
        Dim prev = _suspendEvents
        _suspendEvents = True
        Try
            SliderSkinTintR.Value = Math.Round(r)
            SliderSkinTintG.Value = Math.Round(g)
            SliderSkinTintB.Value = Math.Round(b)
            SliderSkinTintIntensity.Value = Math.Round(i)
        Finally
            _suspendEvents = prev
        End Try
    End Sub

    Private Sub WriteSkinTintOffsetFromSliders()
        Dim off = EnsureSkinTintOffset()
        If off Is Nothing Then Return
        off.RUi = CSng(SliderSkinTintR.Value)
        off.GUi = CSng(SliderSkinTintG.Value)
        off.BUi = CSng(SliderSkinTintB.Value)
        off.IntensityUi = CSng(SliderSkinTintIntensity.Value)
    End Sub

    ''' <summary>Aplica un offset al overlay y REPINTA por el camino barato: sólo se re-resuelve el tono del
    ''' cuerpo y se reescriben sus uniforms — no se recompone la cara ni se toca una textura, porque el ajuste
    ''' no entra por ahí. Devuelve False si el host todavía no tiene estado (preview sin render).</summary>
    Private Function ApplySkinTintOffsetLive(off As SkinToneQnamOffset, deferredRepaint As Boolean) As Boolean
        Dim p = Preset
        If p Is Nothing OrElse _editorHost Is Nothing OrElse _mainForm Is Nothing Then Return False
        p.SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(off)
        Dim ok As Boolean = _mainForm.RefreshBodySkinToneLive(p.SkinToneOffset, _editorHost)
        Dim ctl = _editorHost.PreviewCtl
        If ctl IsNot Nothing Then
            If deferredRepaint Then
                ctl.RefreshRender()
            Else
                ' El lazo del auto-calc no puede esperar a WM_PAINT: marca el frame como pendiente y deja que
                ' ReadPixelDisplay lo dibuje y lo presente sincrónicamente antes de leer.
                ctl.UpdateRequired = True
            End If
        End If
        Return ok
    End Function

    Private Sub OnSkinTintSliderChanged(sender As Object, e As EventArgs) _
        Handles SliderSkinTintR.ValueChanged, SliderSkinTintG.ValueChanged,
                SliderSkinTintB.ValueChanged, SliderSkinTintIntensity.ValueChanged
        If _suspendEvents OrElse _stBusy Then Return
        WriteSkinTintOffsetFromSliders()
        ApplySkinTintOffsetLive(EnsureSkinTintOffset(), deferredRepaint:=True)
        UpdateSkinTintStatus()
    End Sub

    Private Sub OnSkinTintSliderDragEnded(sender As Object, e As EventArgs) _
        Handles SliderSkinTintR.DragEnded, SliderSkinTintG.DragEnded,
                SliderSkinTintB.DragEnded, SliderSkinTintIntensity.DragEnded
        If _suspendEvents OrElse _stBusy Then Return
        ' Al soltar el slider (no en cada tick) se vuelve a leer el píxel de destino: el número que el usuario
        ' está persiguiendo tiene que ser el que ve, no el de hace tres arrastres.
        ResampleSkinTintTarget()
        UpdateSkinTintStatus()
    End Sub

    Private Sub OnSkinTintResetClick(sender As Object, e As EventArgs) Handles ButtonSkinTintReset.Click
        If _stBusy Then Return
        Dim off = EnsureSkinTintOffset()
        If off Is Nothing Then Return
        off.R = 0.0F : off.G = 0.0F : off.B = 0.0F : off.Intensity = 0.0F
        SyncSkinTintSlidersFromOverlay()
        ApplySkinTintOffsetLive(off, deferredRepaint:=True)
        ResampleSkinTintTarget()
        UpdateSkinTintStatus()
    End Sub

    ''' <summary>"Reset Section" del tab: vuelve al snapshot que el formulario tomó al abrirse (misma
    ''' semántica que el resto de las secciones), no a cero — el cero lo da el botón propio del tab.</summary>
    Private Sub ResetSkinTintSection()
        Dim p = Preset
        If p Is Nothing Then Return
        Dim prior As SkinToneQnamOffset = Nothing
        If _priorPreset IsNot Nothing Then prior = _priorPreset.SkinToneOffset
        p.SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(prior)
        SyncSkinTintSlidersFromOverlay()
        ApplySkinTintOffsetLive(p.SkinToneOffset, deferredRepaint:=True)
        ResampleSkinTintTarget()
        UpdateSkinTintStatus()
    End Sub

    ' =====================================================================
    ' Picker
    ' =====================================================================

    Private Sub OnSkinTintPickSourceClick(sender As Object, e As EventArgs) Handles ButtonSkinTintPickSource.Click
        ToggleSkinTintPick(SkinTintPickTarget.Source)
    End Sub

    Private Sub OnSkinTintPickTargetClick(sender As Object, e As EventArgs) Handles ButtonSkinTintPickTarget.Click
        ToggleSkinTintPick(SkinTintPickTarget.Target)
    End Sub

    ''' <summary>Arma (o desarma, si ya estaba armado el mismo) el modo picker del preview embebido. Es un
    ''' modo de UN disparo: el primer click muestrea y desarma. Ver <see cref="DisarmSkinTintPicker"/> para
    ''' las otras vías de salida.</summary>
    Private Sub ToggleSkinTintPick(what As SkinTintPickTarget)
        If _stBusy Then Return
        If _stPick = what Then
            DisarmSkinTintPicker()
            Return
        End If
        If EditPreviewControl Is Nothing OrElse EditPreviewControl.IsDisposed Then
            LabelSkinTintGate.Text = "The preview is not ready yet."
            Return
        End If
        _stPick = what
        EditPreviewControl.ColorPickMode = True
        UpdateSkinTintPickButtons()
        LabelSkinTintGate.Text = ""
        If what = SkinTintPickTarget.Source Then
            LabelSkinTintStatus.Text = "Click the FACE in the preview to take the source colour."
        Else
            LabelSkinTintStatus.Text = "Click the BODY in the preview to mark the point to match."
        End If
    End Sub

    ''' <summary>Apaga el modo picker SIEMPRE que se salga de él — por un pick, por cambiar de tab, por
    ''' cerrar el formulario o por apretar de nuevo el mismo botón. El preview es un control COMPARTIDO con
    ''' las otras apps: dejarlo en modo picker le robaría el botón izquierdo a la cámara.</summary>
    Private Sub DisarmSkinTintPicker()
        _stPick = SkinTintPickTarget.None
        If EditPreviewControl IsNot Nothing AndAlso Not EditPreviewControl.IsDisposed Then
            EditPreviewControl.ColorPickMode = False
        End If
        UpdateSkinTintPickButtons()
    End Sub

    Private Sub UpdateSkinTintPickButtons()
        If _stPick = SkinTintPickTarget.Source Then
            ButtonSkinTintPickSource.Text = "Click in the preview... (cancel)"
        Else
            ButtonSkinTintPickSource.Text = "Pick source (face)..."
        End If
        If _stPick = SkinTintPickTarget.Target Then
            ButtonSkinTintPickTarget.Text = "Click in the preview... (cancel)"
        Else
            ButtonSkinTintPickTarget.Text = "Pick target (body)..."
        End If
    End Sub

    Private Sub EditPreviewControl_ColorPicked(sender As Object, e As PreviewControl.ColorPickedEventArgs) Handles EditPreviewControl.ColorPicked
        Dim what = _stPick
        DisarmSkinTintPicker()
        If what = SkinTintPickTarget.None OrElse e Is Nothing Then Return
        If e.Color.IsEmpty Then
            LabelSkinTintStatus.Text = "Could not read that pixel (was the preview drawn?). Try again."
            Return
        End If

        If what = SkinTintPickTarget.Source Then
            _stSourceColor = e.Color
            _stSourcePoint = New Point(e.X, e.Y)
            _stHasSource = True
            PanelSkinTintSourceSwatch.BackColor = e.Color
            LabelSkinTintSource.Text = $"RGB({e.Color.R}, {e.Color.G}, {e.Color.B})  @ ({e.X}, {e.Y})"
        Else
            _stTargetPoint = New Point(e.X, e.Y)
            _stHasTarget = True
            _stLastTargetColor = e.Color
            PanelSkinTintTargetSwatch.BackColor = e.Color
            LabelSkinTintTarget.Text = $"position ({e.X}, {e.Y}) — now RGB({e.Color.R}, {e.Color.G}, {e.Color.B})"
        End If

        ' La firma se toma en CADA pick: los dos puntos tienen que pertenecer al mismo encuadre.
        _stPickCameraSignature = SkinTintCameraSignature()
        UpdateSkinTintStatus()
    End Sub

    ''' <summary>Re-muestrea el píxel de DESTINO y refresca su swatch/etiqueta. No-op si no hay destino
    ''' elegido o si el encuadre cambió (el punto ya no apunta a lo mismo).</summary>
    Private Sub ResampleSkinTintTarget()
        If Not _stHasTarget OrElse SkinTintFrameMoved() Then Return
        Dim ctl = EditPreviewControl
        If ctl Is Nothing OrElse ctl.IsDisposed Then Return
        Dim c = ctl.ReadPixelDisplay(_stTargetPoint.X, _stTargetPoint.Y, SkinTintSampleBox)
        If c.IsEmpty Then Return
        _stLastTargetColor = c
        PanelSkinTintTargetSwatch.BackColor = c
        Dim delta As String = ""
        If _stHasSource Then
            delta = $"  |  delta ({CInt(c.R) - CInt(_stSourceColor.R):+0;-0;0}, {CInt(c.G) - CInt(_stSourceColor.G):+0;-0;0}, {CInt(c.B) - CInt(_stSourceColor.B):+0;-0;0})"
        End If
        LabelSkinTintTarget.Text = $"position ({_stTargetPoint.X}, {_stTargetPoint.Y}) — now RGB({c.R}, {c.G}, {c.B}){delta}"
    End Sub

    ''' <summary>Firma del encuadre (cámara + tamaño del control). Si cambia, los píxeles elegidos dejan de
    ''' apuntar a lo que el usuario eligió — y el auto-calc estaría optimizando contra otro punto.</summary>
    Private Function SkinTintCameraSignature() As String
        Dim ctl = EditPreviewControl
        If ctl Is Nothing OrElse ctl.IsDisposed OrElse ctl.camera Is Nothing Then Return ""
        Dim c = ctl.camera
        Return String.Format(Globalization.CultureInfo.InvariantCulture,
                             "{0:F3}|{1:F3},{2:F3},{3:F3}|{4:F4},{5:F4},{6:F4}|{7}x{8}",
                             c.distance, c.FocusPosition.X, c.FocusPosition.Y, c.FocusPosition.Z,
                             c.Forward.X, c.Forward.Y, c.Forward.Z, ctl.Width, ctl.Height)
    End Function

    Private Function SkinTintFrameMoved() As Boolean
        If String.IsNullOrEmpty(_stPickCameraSignature) Then Return False
        Return Not String.Equals(_stPickCameraSignature, SkinTintCameraSignature(), StringComparison.Ordinal)
    End Function

    ' =====================================================================
    ' Auto-calc (lazo cerrado)
    ' =====================================================================

    Private Sub OnSkinTintAutoClick(sender As Object, e As EventArgs) Handles ButtonSkinTintAuto.Click
        If _stBusy Then Return
        If Not _stHasSource OrElse Not _stHasTarget Then
            LabelSkinTintGate.Text = "Pick a SOURCE pixel (face) and a TARGET pixel (body) first."
            Return
        End If
        If SkinTintFrameMoved() Then
            LabelSkinTintGate.Text = "The camera moved after the pixels were picked: pick them again (they are screen positions)."
            Return
        End If
        LabelSkinTintGate.Text = ""

        _stBusy = True
        Dim prevCursor = Me.Cursor
        Me.Cursor = Cursors.WaitCursor
        SetSkinTintControlsEnabled(False)
        Dim startOffset = SkinToneQnamOffset.CloneOrNothing(EnsureSkinTintOffset())
        Try
            Dim best As SkinToneQnamOffset = If(startOffset, New SkinToneQnamOffset())
            Dim bestErr As Double = EvaluateSkinTintCandidate(best)
            If bestErr < 0.0R Then
                LabelSkinTintGate.Text = "Could not sample the target point. Is it still on the body?"
                Return
            End If
            Dim startErr As Double = bestErr
            Dim evals As Integer = 1
            Dim sw = Stopwatch.StartNew()

            ' Descenso por coordenadas con paso decreciente. Los pasos son UNIDADES DE UI (bytes para R/G/B,
            ' porcentaje para la intensidad) porque es la grilla en la que el usuario después va a corregir a
            ' mano: terminar en un valor que el slider no puede representar sería mentirle.
            Dim rgbSteps() As Double = {32.0R, 16.0R, 8.0R, 4.0R, 2.0R, 1.0R}
            Dim intSteps() As Double = {16.0R, 8.0R, 4.0R, 2.0R, 1.0R, 1.0R}
            Dim aborted As Boolean = False

            For si As Integer = 0 To rgbSteps.Length - 1
                If aborted Then Exit For
                Dim improved As Boolean = True
                While improved
                    improved = False
                    For ch As Integer = 0 To 3
                        For dir As Integer = -1 To 1 Step 2
                            If evals >= SkinTintMaxEvals OrElse sw.Elapsed.TotalSeconds > SkinTintMaxSeconds Then
                                aborted = True
                                Exit For
                            End If
                            Dim stepUi As Double = If(ch = 3, intSteps(si), rgbSteps(si))
                            Dim cand = OffsetWithChannelDelta(best, ch, dir * stepUi)
                            If cand Is Nothing Then Continue For   ' el clamp lo dejó igual: no gastar un render
                            Dim err As Double = EvaluateSkinTintCandidate(cand)
                            evals += 1
                            If err >= 0.0R AndAlso err < bestErr - SkinTintEpsilon Then
                                best = cand
                                bestErr = err
                                improved = True
                            End If
                        Next
                        If aborted Then Exit For
                    Next
                End While
            Next

            ' Dejar aplicado el MEJOR (la última evaluación pudo haber sido un candidato peor).
            ApplySkinTintOffsetLive(best, deferredRepaint:=True)
            SyncSkinTintSlidersFromOverlay()
            ResampleSkinTintTarget()
            Dim residual = Math.Sqrt(bestErr / 3.0R)
            Dim startResidual = Math.Sqrt(startErr / 3.0R)
            Dim msg = $"Auto-calc: {evals} evaluations in {sw.Elapsed.TotalSeconds:F1}s. Residual {startResidual:F1} → {residual:F1} RGB levels per channel (0 = exact match)."
            If aborted Then msg &= " Stopped on budget."
            If residual > 12.0R Then
                msg &= " High residual: the two spots probably don't have comparable lighting — pick spots with similar orientation and shading."
            End If
            LabelSkinTintStatus.Text = msg
        Catch ex As Exception
            Logger.LogLazy(Function() $"[SKINTINT] auto-calc failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
            LabelSkinTintGate.Text = "Auto-calc failed: " & ex.Message
            ' Volver a lo que había: un fallo a mitad del lazo dejaría aplicado un candidato cualquiera.
            ApplySkinTintOffsetLive(startOffset, deferredRepaint:=True)
            SyncSkinTintSlidersFromOverlay()
        Finally
            _stBusy = False
            SetSkinTintControlsEnabled(True)
            Me.Cursor = prevCursor
        End Try
    End Sub

    ''' <summary>Clona <paramref name="src"/> con un canal movido (en unidades de UI) y clampeado al rango del
    ''' slider. Devuelve Nothing si el clamp lo dejó idéntico — así el lazo no gasta un render en un candidato
    ''' que ya evaluó.</summary>
    Private Function OffsetWithChannelDelta(src As SkinToneQnamOffset, channel As Integer, deltaUi As Double) As SkinToneQnamOffset
        Dim c = src.Clone()
        Select Case channel
            Case 0 : c.RUi = CSng(ClampUi(CDbl(src.RUi) + deltaUi, SkinToneQnamOffset.RgbUiScale))
            Case 1 : c.GUi = CSng(ClampUi(CDbl(src.GUi) + deltaUi, SkinToneQnamOffset.RgbUiScale))
            Case 2 : c.BUi = CSng(ClampUi(CDbl(src.BUi) + deltaUi, SkinToneQnamOffset.RgbUiScale))
            Case Else : c.IntensityUi = CSng(ClampUi(CDbl(src.IntensityUi) + deltaUi, SkinToneQnamOffset.IntensityUiScale))
        End Select
        If c.R = src.R AndAlso c.G = src.G AndAlso c.B = src.B AndAlso c.Intensity = src.Intensity Then Return Nothing
        Return c
    End Function

    Private Shared Function ClampUi(v As Double, limit As Double) As Double
        If v < -limit Then Return -limit
        If v > limit Then Return limit
        Return v
    End Function

    ''' <summary>Aplica el candidato, re-renderiza y devuelve el ERROR contra el color de origen: la distancia
    ''' cuadrática en el espacio en el que el usuario mira (display, post-tonemap). Negativo = no se pudo
    ''' muestrear. No hay inversión de nada acá — el pipeline calcula, nosotros leemos.</summary>
    Private Function EvaluateSkinTintCandidate(cand As SkinToneQnamOffset) As Double
        If Not ApplySkinTintOffsetLive(cand, deferredRepaint:=False) Then Return -1.0R
        Dim ctl = EditPreviewControl
        If ctl Is Nothing OrElse ctl.IsDisposed Then Return -1.0R
        Dim c = ctl.ReadPixelDisplay(_stTargetPoint.X, _stTargetPoint.Y, SkinTintSampleBox)
        If c.IsEmpty Then Return -1.0R
        _stLastTargetColor = c
        Dim dr As Double = CDbl(c.R) - CDbl(_stSourceColor.R)
        Dim dg As Double = CDbl(c.G) - CDbl(_stSourceColor.G)
        Dim db As Double = CDbl(c.B) - CDbl(_stSourceColor.B)
        Return dr * dr + dg * dg + db * db
    End Function

    ' =====================================================================
    ' Disponibilidad y estado
    ' =====================================================================

    ''' <summary>Habilita el tab sólo cuando el ajuste REALMENTE hace algo: hace falta que el skin tone del
    ''' cuerpo se derive (la raza tiene capa de skin-tone y resuelve), que es exactamente la condición bajo la
    ''' cual corre el soft-light del cuerpo. Un slider que no puede mover nada es peor que no tener slider.</summary>
    Friend Sub RefreshSkinTintAvailability()
        Dim tone As Nullable(Of Color) = Nothing
        If _mainForm IsNot Nothing AndAlso _editorHost IsNot Nothing Then
            tone = _mainForm.ResolveBaseSkinToneForHost(_editorHost)
        End If
        Dim available As Boolean = tone.HasValue
        SetSkinTintControlsEnabled(available)
        If Not available Then
            DisarmSkinTintPicker()
            LabelSkinTintGate.Text = "This NPC has no derivable skin tone (its race declares no skin-tone layer — synths, ghouls and robots don't have one), so QNAM does not tint the body and this adjustment would be inert."
            LabelSkinTintStatus.Text = ""
        Else
            LabelSkinTintGate.Text = ""
            UpdateSkinTintStatus(tone)
        End If
    End Sub

    Private Sub SetSkinTintControlsEnabled(enabled As Boolean)
        SliderSkinTintR.Enabled = enabled
        SliderSkinTintG.Enabled = enabled
        SliderSkinTintB.Enabled = enabled
        SliderSkinTintIntensity.Enabled = enabled
        ButtonSkinTintPickSource.Enabled = enabled
        ButtonSkinTintPickTarget.Enabled = enabled
        ButtonSkinTintReset.Enabled = enabled
        ButtonSkinTintAuto.Enabled = enabled AndAlso _stHasSource AndAlso _stHasTarget
    End Sub

    Private Sub UpdateSkinTintStatus(Optional baseTone As Nullable(Of Color) = Nothing)
        ButtonSkinTintAuto.Enabled = _stHasSource AndAlso _stHasTarget AndAlso Not _stBusy
        Dim effective As Nullable(Of Color) = Nothing
        If _mainForm IsNot Nothing AndAlso _editorHost IsNot Nothing Then
            effective = _mainForm.ResolveBodySkinToneForHost(_editorHost)
        End If
        Dim parts As New List(Of String)
        If baseTone.HasValue Then
            parts.Add($"QNAM base RGBA({baseTone.Value.R}, {baseTone.Value.G}, {baseTone.Value.B}, {baseTone.Value.A})")
        End If
        If effective.HasValue Then
            parts.Add($"effective RGBA({effective.Value.R}, {effective.Value.G}, {effective.Value.B}, {effective.Value.A})")
        End If
        If SkinTintFrameMoved() Then
            parts.Add("the camera moved since the last pick")
        End If
        LabelSkinTintStatus.Text = String.Join("   ·   ", parts)
    End Sub

    ' =====================================================================
    ' Enganches del formulario
    ' =====================================================================

    Private Sub SkinTintTabsBody_SelectedIndexChanged(sender As Object, e As EventArgs) Handles TabsBody.SelectedIndexChanged
        ' Salir del tab desarma el picker SIEMPRE (el modo no puede sobrevivir a que el usuario se vaya).
        If TabsBody.SelectedTab IsNot TabPageSkinTint Then
            DisarmSkinTintPicker()
        Else
            RefreshSkinTintAvailability()
        End If
    End Sub

    Private Sub SkinTintForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' Último cinturón: el control se destruye enseguida, pero el modo no puede quedar prendido si algún
        ' día el preview se reusara. Idempotente y a prueba de disposed.
        DisarmSkinTintPicker()
    End Sub
End Class
