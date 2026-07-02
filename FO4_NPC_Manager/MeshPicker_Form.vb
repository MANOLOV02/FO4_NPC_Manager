Imports FO4_Base_Library

''' <summary>
''' Modal .nif picker with a LIVE PREVIEW of the selected mesh. Drop-in replacement for
''' <see cref="DictionaryFilePicker_Form"/> on the ARMA/ARMO mesh selectors: same ctor shape
''' (keys + rootPrefix + exts + initialKey) and same <see cref="SelectedKey"/> result contract,
''' but Panel2 of a SplitContainer hosts a standalone GL <see cref="PreviewControl"/> that renders
''' the chosen NIF on selection-change.
'''
''' Mirrors Wardrobe_Manager's <c>Create_from_Nif_Form</c> proven pattern: the
''' <see cref="DictionaryPicker_Control"/> lives in Panel1; the PreviewControl is created in
''' <c>Shown</c> (so its OpenGL context spins up only when the form is visible), docked Fill into a
''' DEDICATED panel inside Panel2, and torn down explicitly on FormClosing
''' (BeginTeardown → Clean → Dispose). The render path is the lib's simple primitive:
''' <c>Nifcontent_Class_Manolo.Load_Manolo(bytes)</c> → <c>NifRenderableShape.FromNif(nif)</c> →
''' <c>PreviewControl.RenderShapes(shapes, Nothing)</c> — no skeleton/pose/material setup is needed
''' for a bare standalone mesh (RenderShapes nulls the resolvers internally).
''' </summary>
Public Class MeshPicker_Form

    ''' <summary>The GL host for the live preview. Created in <c>Shown</c> (NOT ctor/Designer) and
    ''' docked Fill into the dedicated <see cref="PreviewHostPanel"/>; nulled on FormClosing.</summary>
    Private WithEvents _preview As PreviewControl = Nothing

    Sub New()
        ' Esta llamada es exigida por el diseñador.
        InitializeComponent()
    End Sub

    ''' <summary>Same constructor shape as <see cref="DictionaryFilePicker_Form"/>: pre-filtered keys
    ''' + the root prefix + allowed extensions + the initially-selected key.</summary>
    Public Sub New(keys As List(Of String), rootPrefix As String, allowedExts As HashSet(Of String), initialKey As String)
        InitializeComponent()
        ArgumentNullException.ThrowIfNull(keys)
        ArgumentNullException.ThrowIfNull(allowedExts)
        DictionaryPicker_Control1.Initialize(keys, rootPrefix, allowedExts)
        DictionaryPicker_Control1.Preselect(initialKey)
    End Sub

    ''' <summary>The dictionary key the user picked (full key, INCLUDING the root prefix), or Nothing.
    ''' Same contract as <c>DictionaryFilePicker_Form.DictionaryPicker_Control1.SelectedKey</c>; the
    ''' caller strips the Meshes\ prefix as before.</summary>
    Public ReadOnly Property SelectedKey As String
        Get
            Return DictionaryPicker_Control1.SelectedKey
        End Get
    End Property

    ' =====================================================================
    ' Preview host lifecycle — mirror Create_from_Nif_Form / EditBody_Form
    ' =====================================================================

    Private Sub MeshPicker_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        ' SplitterDistance is set here (not in the Designer) to avoid the InvalidOperationException
        ' that fires when it's assigned before the SplitContainer has its final size.
        Try
            SplitMain.SplitterDistance = CInt(SplitMain.Width * 0.45)
        Catch
        End Try

        If _preview Is Nothing OrElse _preview.IsDisposed Then
            _preview = New PreviewControl() With {.Dock = DockStyle.Fill}
            PreviewHostPanel.Controls.Add(_preview)
            _preview.BringToFront()
        End If

        ' Render whatever is preselected so the dialog doesn't open with an empty viewport.
        Dim k = DictionaryPicker_Control1.SelectedKey
        If Not String.IsNullOrEmpty(k) Then RenderMesh(k)
    End Sub

    Private Sub MeshPicker_Form_FormClosing(sender As Object, e As System.Windows.Forms.FormClosingEventArgs) Handles Me.FormClosing
        ' Quiesce render loop → control Clean/Dispose (same ordering as the editor hosts).
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            Try
                _preview.BeginTeardown()
            Catch
            End Try
            Try
                _preview.Clean()
            Catch
            End Try
            Try
                _preview.Dispose()
            Catch
            End Try
        End If
        _preview = Nothing
    End Sub

    ' =====================================================================
    ' Picker events (OK / Cancel via the control's own buttons; selection → preview)
    ' =====================================================================

    Private Sub DictionaryPicker_Control1_OkClicked() Handles DictionaryPicker_Control1.OkClicked
        ' OK requires a real selection.
        If String.IsNullOrEmpty(DictionaryPicker_Control1.SelectedKey) Then Return
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Sub DictionaryPicker_Control1_CancelClicked() Handles DictionaryPicker_Control1.CancelClicked
        Me.DialogResult = DialogResult.Cancel
        Me.Close()
    End Sub

    Private Sub DictionaryPicker_Control1_SelectionChanged(Key As String) Handles DictionaryPicker_Control1.SelectionChanged
        If String.IsNullOrEmpty(Key) Then
            ' Cleared selection → empty viewport.
            If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
                Try
                    _preview.RenderShapes(New List(Of IRenderableShape)(), Nothing)
                Catch
                End Try
            End If
            Return
        End If
        RenderMesh(Key)
    End Sub

    ''' <summary>Resolve the mesh bytes for <paramref name="key"/>, load the NIF and render its shapes
    ''' standalone (no pose). Wrapped so an unreadable/odd NIF never crashes the dialog — it just shows
    ''' nothing.</summary>
    Private Sub RenderMesh(key As String)
        If _preview Is Nothing OrElse _preview.IsDisposed Then Return
        Try
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(key, loc) OrElse loc Is Nothing Then
                _preview.RenderShapes(New List(Of IRenderableShape)(), Nothing)
                Return
            End If

            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length = 0 Then
                _preview.RenderShapes(New List(Of IRenderableShape)(), Nothing)
                Return
            End If

            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)

            Dim shapes = NifRenderableShape.FromNif(nif)
            _preview.RenderShapes(shapes.Cast(Of IRenderableShape)(), Nothing)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[MESH-PICKER] preview render failed for '{key}': {ex.GetType().Name}: {ex.Message}")
            Try
                _preview.RenderShapes(New List(Of IRenderableShape)(), Nothing)
            Catch
            End Try
        End Try
    End Sub

End Class
