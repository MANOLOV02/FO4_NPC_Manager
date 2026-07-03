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
        TextBoxRemap.Text = If(src.HasColorRemapIndex, src.ColorRemapIndex.ToString(CultureInfo.InvariantCulture), "")

        AddHandler ButtonBrowseReplacement.Click, AddressOf OnBrowseReplacement
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    ''' <summary>Pick a Materials\ file (loose+BA2, ext-filtered) via the library tree picker into Replacement.</summary>
    Private Sub OnBrowseReplacement(sender As Object, e As EventArgs)
        Dim current = TextBoxReplacement.Text.Trim()
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
        Dim remapText = TextBoxRemap.Text.Trim()
        Dim remapVal As Single
        If remapText.Length > 0 AndAlso Single.TryParse(remapText, NumberStyles.Float, CultureInfo.InvariantCulture, remapVal) Then
            built.HasColorRemapIndex = True
            built.ColorRemapIndex = remapVal
        End If

        _result = built
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
