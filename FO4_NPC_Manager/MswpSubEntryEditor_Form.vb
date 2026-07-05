Imports System.Globalization
Imports FO4_Base_Library

''' <summary>Modal editor for a SINGLE MSWP substitution (Original / Replacement / optional Color Remap),
''' opened from the Material Swap editor's substitutions grid (Add / Edit / double-click a row). Replaces the
''' OLD inline-editable GridSubs (combo Original cell + Replacement text cell + free-typed combo entry) so the
''' grid can be pure read-only — killing the reentrant crash the inline combo edits caused.
'''
''' The Original combo is pre-populated with THIS GENDER'S mesh NIF materials (BaseMaterials) exactly as the
''' old grid combo was; it stays a DropDown (editable) so a swap whose Original isn't referenced by the mesh
''' can still be authored (the old free-text fallback). Replacement is typed or picked via the library
''' <see cref="DictionaryFilePicker_Form"/> (Materials\ + {.bgsm,.bgem}). On OK a fresh
''' <see cref="MSWP_Substitution"/> is produced (deep-copied out); the caller reads it on DialogResult.OK.</summary>
Public Class MswpSubEntryEditor_Form

    ''' <summary>The edited substitution, valid only after <c>DialogResult.OK</c>. Fresh — the caller owns it.</summary>
    Public ReadOnly Property ResultSub As MSWP_Substitution
        Get
            Return _result
        End Get
    End Property
    Private _result As MSWP_Substitution

    ''' <param name="meshMaterials">Material paths the gender mesh references — the Original combo items.</param>
    ''' <param name="sub">The substitution to edit. DEEP-COPIED in (never aliased); Nothing starts empty.</param>
    Public Sub New(meshMaterials As IEnumerable(Of String), sub_ As MSWP_Substitution)
        InitializeComponent()

        If meshMaterials IsNot Nothing Then
            For Each p In meshMaterials
                If Not String.IsNullOrEmpty(p) AndAlso Not ComboOriginal.Items.Contains(p) Then ComboOriginal.Items.Add(p)
            Next
        End If

        Dim src = If(sub_, New MSWP_Substitution())
        ' Show the current Original even when it isn't one of the mesh's materials (out-of-list authoring).
        Dim orig = If(src.OriginalMaterial, "")
        If orig.Length > 0 AndAlso Not ComboOriginal.Items.Contains(orig) Then ComboOriginal.Items.Add(orig)
        ComboOriginal.Text = orig
        TextBoxReplacement.Text = If(src.ReplacementMaterial, "")

        ' Color remap: a checkbox marks it present/absent; the 0–1 slider (4 decimals) holds the index.
        CheckRemap.Checked = src.HasColorRemapIndex
        SliderRemap.Value = If(src.HasColorRemapIndex, Math.Max(0.0, Math.Min(1.0, CDbl(src.ColorRemapIndex))), 0.0)
        SliderRemap.Enabled = CheckRemap.Checked

        AddHandler CheckRemap.CheckedChanged, Sub()
                                                  SliderRemap.Enabled = CheckRemap.Checked
                                                  UpdateRemapGradientUi()
                                              End Sub
        AddHandler SliderRemap.ValueChanged, Sub() PicRemapGradient.Invalidate()   ' only the marker moves
        AddHandler TextBoxReplacement.TextChanged, Sub() UpdateRemapGradientUi()     ' palette source changed
        AddHandler ComboOriginal.TextChanged, Sub() UpdateRemapGradientUi()
        AddHandler PicRemapGradient.Paint, AddressOf OnRemapGradientPaint
        AddHandler Me.FormClosed, Sub() _gradientBmp?.Dispose()
        AddHandler ButtonBrowseReplacement.Click, AddressOf OnBrowseReplacement
        AddHandler ButtonOk.Click, AddressOf OnOk
        UpdateRemapGradientUi()
    End Sub

    Private _gradientBmp As Bitmap
    Private _gradientSourcePath As String = Nothing

    ''' <summary>The material whose color-remap PALETTE the index maps into: the Replacement when set, else the
    ''' Original (SNAM is optional — a swap may only change the remap of the original material).</summary>
    Private Function RemapSourceMaterial() As String
        Dim repl = TextBoxReplacement.Text.Trim()
        Return If(repl.Length > 0, repl, ComboOriginal.Text.Trim())
    End Function

    ''' <summary>Show the source material's greyscale palette in the gradient box (ALWAYS — the palette is visible
    ''' whether or not the remap is enabled, so the user can preview it), reloading the bitmap when the source
    ''' material changes. The marker is redrawn in <see cref="OnRemapGradientPaint"/>.</summary>
    Private Sub UpdateRemapGradientUi()
        Dim src = RemapSourceMaterial()
        If Not String.Equals(src, _gradientSourcePath, StringComparison.OrdinalIgnoreCase) Then
            _gradientBmp?.Dispose()
            _gradientBmp = LoadGreyscaleBitmap(src)
            _gradientSourcePath = src
            PicRemapGradient.Image = _gradientBmp
        End If
        PicRemapGradient.Invalidate()
    End Sub

    ''' <summary>Load the material's greyscale-to-palette texture as a horizontal gradient bitmap (rotated so the
    ''' remap axis runs along the width, mirroring Wardrobe Manager). Nothing when the material has no palette
    ''' texture / can't be loaded — the box then shows "no palette".</summary>
    Private Shared Function LoadGreyscaleBitmap(matPath As String) As Bitmap
        Try
            If String.IsNullOrWhiteSpace(matPath) Then Return Nothing
            Dim mat = MaterialResolver.TryLoadMaterialFromDictionary(matPath, Nothing, Nothing, Nothing)
            If mat Is Nothing OrElse String.IsNullOrEmpty(mat.GreyscaleTexture) Then Return Nothing
            Dim bytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(mat.GreyscaleTexture))
            If bytes Is Nothing Then Return Nothing
            Dim bmp = DirectXDDSLoader.CreateBitmapFromDDS(bytes)
            If bmp Is Nothing Then Return Nothing
            bmp.RotateFlip(RotateFlipType.Rotate270FlipNone)
            Return bmp
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>Draw the vertical marker line at the current remap index (0–1 → x across the gradient); when the
    ''' material has no palette texture, a small hint instead of a blank box.</summary>
    Private Sub OnRemapGradientPaint(sender As Object, e As PaintEventArgs)
        Dim w = PicRemapGradient.ClientSize.Width
        Dim h = PicRemapGradient.ClientSize.Height
        If _gradientBmp Is Nothing Then
            TextRenderer.DrawText(e.Graphics, "no palette", PicRemapGradient.Font, New Point(2, 2), Color.DimGray)
            Return
        End If
        Dim idx = Math.Max(0.0, Math.Min(1.0, SliderRemap.Value))
        Dim x = CInt(Math.Round(idx * (w - 1)))
        ' Marker at the index — red when the remap is enabled (present), gray when it's off (preview only).
        Using pen As New Pen(If(CheckRemap.Checked, Color.Red, Color.Gray), 2.0F)
            e.Graphics.DrawLine(pen, x, 0, x, h)
        End Using
    End Sub

    ''' <summary>Pick a Materials\ file (loose+BA2, ext-filtered) via the library tree picker into Replacement.
    ''' Opens positioned on the current Replacement; when there's none yet, on the ORIGINAL material's folder
    ''' (a swap usually replaces a material with one in the same directory).</summary>
    Private Sub OnBrowseReplacement(sender As Object, e As EventArgs)
        Dim current = TextBoxReplacement.Text.Trim()
        If current.Length = 0 Then
            ' No Replacement yet → open at the Original's folder, normalized to the picker's key format (Materials\).
            Dim orig = ComboOriginal.Text.Trim()
            If orig.Length > 0 Then current = MaterialsPrefix & FO4UnifiedMaterial_Class.CorrectMaterialPath(orig).StripPrefix(MaterialsPrefix)
        End If
        Dim exts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".bgsm", ".bgem"}
        Dim keys = FilesDictionary_class.GetFilteredKeys(MaterialsPrefix, exts)
        Using dlg As New DictionaryFilePicker_Form(keys, MaterialsPrefix, exts, current)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Dim sel = dlg.DictionaryPicker_Control1.SelectedKey
                If Not String.IsNullOrEmpty(sel) Then TextBoxReplacement.Text = sel
            End If
        End Using
    End Sub

    ''' <summary>Build the result substitution. An entirely empty row (no Original AND no Replacement) is
    ''' rejected so the grid never gains a content-less row.</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        Dim orig = ComboOriginal.Text.Trim()
        Dim repl = TextBoxReplacement.Text.Trim()
        If orig.Length = 0 AndAlso repl.Length = 0 Then
            MessageBox.Show(Me, "Enter an Original and/or Replacement material.", "Material Substitution",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If

        Dim built As New MSWP_Substitution With {.OriginalMaterial = orig, .ReplacementMaterial = repl}
        If CheckRemap.Checked Then
            built.HasColorRemapIndex = True
            built.ColorRemapIndex = CSng(SliderRemap.Value)
        End If

        _result = built
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
