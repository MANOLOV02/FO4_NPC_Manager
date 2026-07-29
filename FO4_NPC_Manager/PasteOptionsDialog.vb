
''' <summary>Modal dialog que permite al usuario elegir qué categorías del clipboard preset aplicar al NPC
''' receptor en Paste Look. Los checkboxes viven en <see cref="PresetCategoryPanel"/> — el MISMO control que
''' hostea el browser de presets LooksMenu/RaceMenu — así Paste y Load nunca ofrecen categorías distintas.
''' Las categorías NO tildadas preservan lo que el NPC receptor muestra hoy; el merge lo hace
''' <see cref="PresetCategoryFilter.BuildFiltered"/>.</summary>
Public Class PasteOptionsDialog

    ''' <param name="isSse">True when the current session is a Skyrim (SSE) game — the panel then collapses
    ''' the FO4-only categories (MRSV body regions, FMRS face bone regions, F4SE LM skin template) and
    ''' reveals the SSE-only ones (per-vertex sculpt, RaceMenu body scale).</param>
    ''' <param name="source">The preset being pasted. Drives the per-category amounts shown next to each
    ''' checkbox and greys out whatever the copied look doesn't carry.</param>
    Public Sub New(Optional isSse As Boolean = False,
                   Optional source As LooksmenuLoader.LooksmenuPreset = Nothing)
        InitializeComponent()
        CategoryPanel.ConfigureGame(isSse)
        CategoryPanel.SetPreset(source)
    End Sub

    ''' <summary>Shrink-wrap the dialog around its content: header + the panel's own preferred height + the
    ''' OK/Cancel row. Done at Load (not in the Designer) because the panel collapses the other game's
    ''' category rows in <see cref="PresetCategoryPanel.ConfigureGame"/>, so the height it needs is only known
    ''' once the game is known — and because by Load the form's font scaling has already been applied.
    ''' The frame is FixedDialog, so this is the size the user gets, with no dead space to leave behind.</summary>
    Private Sub PasteOptionsDialog_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Dim rowsAroundPanel As Integer = CInt(Root.RowStyles(0).Height + Root.RowStyles(2).Height)
        ClientSize = New Size(ClientSize.Width,
                              rowsAroundPanel + Root.Padding.Vertical + CategoryPanel.PreferredPanelHeight)
    End Sub

    ''' <summary>Snapshot the current checkbox states for <see cref="PresetCategoryFilter.BuildFiltered"/>.</summary>
    Public Function BuildOptions() As PresetCategoryOptions
        Return CategoryPanel.Options
    End Function
End Class
